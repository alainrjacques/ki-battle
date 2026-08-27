using System;
using Godot;

namespace KiBattle;

/// <summary>One entrance resolution, reported for FX/HUD/AI.</summary>
public readonly record struct ImpactReport(
    Element Soul, bool Charged, bool Shattered, bool Braced, float Impulse);

/// <summary>
/// One duelist's battle state: mana, soul (element), beam, the switch timeline
/// (gather -> reform -> impact), the Catalyst pip, and per-soul passive state.
/// Driven by PlayerController or AIController through the same API; BattleManager
/// calls Tick(), ComputePush() and resolves entrance impacts.
/// </summary>
public partial class Caster : Node2D
{
    [Export] public int Side; // 0 = left (player side of the bar), 1 = right

    public Element Keystone { get; private set; }
    public Element CurrentElement { get; private set; }
    public BeamType Beam { get; private set; }
    public float Mana { get; private set; } = Tuning.ManaMax;

    public bool WantsChannel;   // set by controller each frame
    public bool WantsOverdrive;

    // Timers (public reads drive HUD/AI/visuals)
    public float ElementCooldown { get; private set; }
    public float BeamCooldown { get; private set; }
    public float ExhaustTimer { get; private set; }
    public float GatherTimer { get; private set; }
    public float ReformTimer { get; private set; }
    public float SinceReformEnd { get; private set; } = 99f;

    // Catalyst pip
    public float PipCharge { get; private set; }
    public bool PipReady => PipCharge >= Tuning.PipChargeTime;

    // Incoming switch (public so the enemy HUD/AI can read the telegraph)
    public Element PendingElement { get; private set; }
    private bool _entrancePending;
    private bool _entranceCharged;
    private bool _impactReady;

    // Debuffs / buffs on this caster
    public float ConductTimer { get; private set; }        // drain x1.5
    public float OverdriveLockTimer { get; private set; }  // cannot overdrive
    public float PurgeImmunityTimer { get; private set; }  // no new debuffs, no leech
    public float EclipseTimer { get; private set; }        // this caster is CHANNELING a steal

    // Soul passive state
    public float FlareClock { get; private set; }
    public float FlareActiveTimer { get; private set; }
    public float CritCharge { get; private set; }
    public float CritActiveTimer { get; private set; }
    public bool CritArmed => CritCharge >= Tuning.CritChargeTime;
    public float StacksF { get; private set; }
    public int Stacks => (int)StacksF;

    // Set by BattleManager each tick
    public float DominanceTime;   // how long this side's net push has led
    public float CooldownRate = 1f; // < 1 while frostbitten by enemy Ice dominance

    private readonly float[] _echoTimer = new float[ElementDb.Count];
    private bool _wasOverdriving;

    public bool IsExhausted => ExhaustTimer > 0f;

    /// <summary>Vulnerable to shatter: mid-gather, mid-reform, or just-staggered.</summary>
    public bool IsVulnerable => GatherTimer > 0f || ReformTimer > 0f || SinceReformEnd < Tuning.ShatterGrace;

    /// <summary>Current power tier. The base beam is always on; Space empowers it.</summary>
    public PowerTier Tier =>
        IsExhausted ? PowerTier.Idle :
        WantsChannel && WantsOverdrive && Mana > 0f && OverdriveLockTimer <= 0f ? PowerTier.Overdrive :
        WantsChannel && Mana > 0f ? PowerTier.Empowered : PowerTier.Idle;

    public bool IsOverdriving => Tier == PowerTier.Overdrive;
    public bool IsEmpowered => Tier >= PowerTier.Empowered;
    public ElementStyle Style => ElementDb.Style(CurrentElement);

    public event Action<Element>? ElementChanged;
    public event Action<BeamType>? BeamTypeChanged;
    public event Action? Exhausted;
    public event Action<Element>? GatherStarted;

    public void Setup(Loadout loadout)
    {
        Keystone = loadout.Keystone;
        CurrentElement = loadout.Keystone;
        Beam = loadout.StartingBeam;
        Mana = Tuning.ManaMax;
        PipCharge = Tuning.PipChargeTime; // first pip lit at round start
        ElementCooldown = BeamCooldown = ExhaustTimer = GatherTimer = ReformTimer = 0f;
        SinceReformEnd = 99f;
        ConductTimer = OverdriveLockTimer = PurgeImmunityTimer = EclipseTimer = 0f;
        FlareClock = FlareActiveTimer = CritCharge = CritActiveTimer = StacksF = 0f;
        DominanceTime = 0f;
        CooldownRate = 1f;
        Array.Fill(_echoTimer, 0f);
        WantsChannel = WantsOverdrive = false;
        // Entrance-in-flight state must die with the old fight, or a battle that
        // ends mid-switch leaks a phantom (possibly charged) entrance into the next.
        _entrancePending = _entranceCharged = _impactReady = _wasOverdriving = false;
        PendingElement = CurrentElement;
    }

    /// <summary>True while a switch that will fire an entrance is in flight — the
    /// public telegraph. Beam reforms, keystone returns, and echo switches are NOT
    /// incoming entrances; HUD, coach, and AI must all read this, not ReformTimer.</summary>
    public bool IncomingEntrancePending => (GatherTimer > 0f || ReformTimer > 0f) && _entrancePending;
    public bool IncomingEntranceCharged => IncomingEntrancePending && _entranceCharged;

    public float SwitchCost(Element e) =>
        e == Keystone ? Tuning.KeystoneReturnCost :
        e == Element.Light ? Tuning.LightSwitchCost : Tuning.ElementSwitchCost;

    /// <summary>Would switching to e right now fire an entrance? (keystone returns and echoes never do)</summary>
    public bool EntranceEligible(Element e) => e != Keystone && _echoTimer[(int)e] <= 0f;

    public bool TrySwitchElement(Element e)
    {
        if (e == CurrentElement || GatherTimer > 0f || ReformTimer > 0f) return false;
        if (ElementCooldown > 0f || Mana < SwitchCost(e)) return false;

        Mana -= SwitchCost(e);
        ElementCooldown = Tuning.ElementSwitchCooldown;
        GatherTimer = Tuning.GatherDuration;
        PendingElement = e;
        _entrancePending = EntranceEligible(e);
        _entranceCharged = _entrancePending && PipReady;
        if (_entranceCharged) PipCharge = 0f;
        GatherStarted?.Invoke(e);
        return true;
    }

    public bool TrySwitchBeam(BeamType type)
    {
        if (type == Beam || GatherTimer > 0f) return false;
        if (BeamCooldown > 0f || Mana < Tuning.BeamSwitchCost) return false;
        Beam = type;
        Mana -= Tuning.BeamSwitchCost;
        BeamCooldown = Tuning.BeamSwitchCooldown;
        ReformTimer = Mathf.Max(ReformTimer, Tuning.BeamReformDuration);
        BeamTypeChanged?.Invoke(Beam);
        return true;
    }

    /// <summary>Visual-check helper (F1 cycle): force any element/beam combo.</summary>
    public void DebugSetCombo(Element element, BeamType beam)
    {
        CurrentElement = element;
        Beam = beam;
        ElementChanged?.Invoke(element);
        BeamTypeChanged?.Invoke(beam);
    }

    /// <summary>BattleManager polls this after Tick: true exactly once per landed entrance.</summary>
    public bool ConsumeImpact(out bool charged)
    {
        charged = _entranceCharged;
        if (!_impactReady) return false;
        _impactReady = false;
        _entrancePending = false;
        _entranceCharged = false;
        return true;
    }

    /// <summary>Advance mana, timers, and soul passives by one physics step.</summary>
    public void Tick(float delta, float regenScale = 1f)
    {
        bool holy = CurrentElement == Element.Holy;

        // Pip
        float pipRate = CurrentElement == Element.Light ? Tuning.LightPipRate : 1f;
        PipCharge = Mathf.Min(Tuning.PipChargeTime, PipCharge + delta * pipRate);

        for (int i = 0; i < _echoTimer.Length; i++)
            _echoTimer[i] = Mathf.Max(0f, _echoTimer[i] - delta);

        // Cooldowns tick slower while frostbitten
        ElementCooldown = Mathf.Max(0f, ElementCooldown - delta * CooldownRate);
        BeamCooldown = Mathf.Max(0f, BeamCooldown - delta * CooldownRate);
        ExhaustTimer = Mathf.Max(0f, ExhaustTimer - delta);

        // Switch timeline: gather -> reform -> impact
        if (GatherTimer > 0f)
        {
            GatherTimer -= delta;
            if (GatherTimer <= 0f)
            {
                GatherTimer = 0f;
                _echoTimer[(int)CurrentElement] = Tuning.EchoLockout;
                CurrentElement = PendingElement;
                ReformTimer = Tuning.ElementReformDuration;
                ElementChanged?.Invoke(CurrentElement);
            }
        }
        else if (ReformTimer > 0f)
        {
            ReformTimer -= delta;
            if (ReformTimer <= 0f)
            {
                ReformTimer = 0f;
                SinceReformEnd = 0f;
                if (_entrancePending) _impactReady = true;
            }
        }
        else
        {
            SinceReformEnd += delta;
        }

        // Debuffs
        ConductTimer = Mathf.Max(0f, ConductTimer - delta);
        OverdriveLockTimer = Mathf.Max(0f, OverdriveLockTimer - delta);
        PurgeImmunityTimer = Mathf.Max(0f, PurgeImmunityTimer - delta);
        if (EclipseTimer > 0f && (GatherTimer > 0f || ReformTimer > 0f || IsExhausted))
            EclipseTimer = 0f; // channel broken
        else
            EclipseTimer = Mathf.Max(0f, EclipseTimer - delta);

        // Fire — Ignition
        if (CurrentElement == Element.Fire && IsEmpowered)
        {
            if (IsOverdriving && !_wasOverdriving)
                FlareClock = Mathf.Max(FlareClock, Tuning.FlarePeriod - Tuning.FlareWindup);
            FlareClock += delta;
            if (FlareClock >= Tuning.FlarePeriod)
            {
                FlareClock = 0f;
                FlareActiveTimer = Tuning.FlareDuration;
            }
        }
        else if (CurrentElement != Element.Fire)
        {
            FlareClock = 0f;
        }
        FlareActiveTimer = Mathf.Max(0f, FlareActiveTimer - delta);
        _wasOverdriving = IsOverdriving;

        // Lightning — Overload (charging; firing is decided by BattleManager)
        if (CurrentElement == Element.Lightning && IsEmpowered)
            CritCharge += delta;
        else if (CurrentElement != Element.Lightning)
            CritCharge = 0f;
        CritActiveTimer = Mathf.Max(0f, CritActiveTimer - delta);

        // Poison — Corrosion
        if (CurrentElement == Element.Poison)
        {
            if (IsEmpowered) StacksF = Mathf.Min(Tuning.StackMax, StacksF + delta);
        }
        else
        {
            StacksF = Mathf.Max(0f, StacksF - Tuning.StackDecay * delta);
        }

        // Mana
        float regen = IsExhausted
            ? Tuning.ManaRegenExhausted
            : (Tuning.ManaRegen + (holy ? Tuning.HolyRegen : 0f)) * regenScale;
        float drain = Tier switch
        {
            PowerTier.Overdrive => Tuning.BeamDrain[(int)Beam] * Tuning.OverdriveDrainMult,
            PowerTier.Empowered => Tuning.BeamDrain[(int)Beam],
            _ => 0f, // the base beam is free
        };
        if (drain > 0f)
        {
            if (CurrentElement == Element.Water) drain *= Tuning.FlowDrainMult;
            if (ConductTimer > 0f) drain *= Tuning.ConductDrainMult;
        }
        Mana = Mathf.Clamp(Mana + (regen - drain) * delta, 0f, Tuning.ManaMax);

        if (Mana <= 0f && !IsExhausted)
        {
            ExhaustTimer = Tuning.ExhaustDuration * (holy ? Tuning.HolyExhaustMult : 1f);
            Exhausted?.Invoke();
        }
    }

    /// <summary>BattleManager fires the armed crit when the window is right.</summary>
    public void TryFireCrit(bool opponentVulnerable)
    {
        if (CurrentElement != Element.Lightning || !CritArmed) return;
        if (opponentVulnerable || CritCharge >= Tuning.CritSelfFireTime)
        {
            CritCharge = 0f;
            CritActiveTimer = Tuning.CritDuration;
        }
    }

    // --- helpers used by entrance/leech resolution ---
    public void BurnMana(float amount)
    {
        // The floor stops burns, it never refills: a victim already below it is untouched.
        if (Mana > Tuning.ManaOpFloor)
            Mana = Mathf.Max(Tuning.ManaOpFloor, Mana - amount);
    }

    public void GainMana(float amount) => Mana = Mathf.Min(Tuning.ManaMax, Mana + amount);

    public void ApplyConduct(float duration)
    {
        if (PurgeImmunityTimer <= 0f) ConductTimer = Mathf.Max(ConductTimer, duration);
    }

    public void ApplyOverdriveLock(float duration)
    {
        if (PurgeImmunityTimer <= 0f) OverdriveLockTimer = Mathf.Max(OverdriveLockTimer, duration);
    }

    public void ApplyGlaciate(float add)
    {
        if (PurgeImmunityTimer > 0f) return;
        if (ElementCooldown > 0f) ElementCooldown += add;
        if (BeamCooldown > 0f) BeamCooldown += add;
    }

    public void StartEclipse(float duration) => EclipseTimer = duration;
    public void BreakEclipse() => EclipseTimer = 0f;

    public void Purge(float immunity)
    {
        ConductTimer = 0f;
        OverdriveLockTimer = 0f;
        PurgeImmunityTimer = immunity;
    }

    public void AddStacks(int n) => StacksF = Mathf.Min(Tuning.StackMax, StacksF + n);
    public void RefundPip(float seconds) => PipCharge = Mathf.Min(Tuning.PipChargeTime, PipCharge + seconds);

    /// <summary>Current push force against the opponent. clashX is in this caster's frame
    /// (0 = own edge, 1 = opponent's edge) for the desperation bonus.</summary>
    public float ComputePush(Caster opponent, float ownSideClashX)
    {
        if (IsExhausted) return 0f; // the beam sputters out: the punish window

        float tierMult = Tier switch
        {
            PowerTier.Overdrive => Tuning.OverdrivePush,
            PowerTier.Empowered => 1f,
            _ => Tuning.IdlePushMult,
        };
        float push = tierMult * Tuning.BeamPush[(int)Beam];

        // Soul multipliers. Pinpoint (either side) damps them toward 1; a Water
        // opponent damps discrete spikes (flares, crits) but not sustained bonuses.
        bool anyPinpoint = Beam == BeamType.Pinpoint || opponent.Beam == BeamType.Pinpoint;
        float spikeDamp = (anyPinpoint ? Tuning.PierceDamp : 1f)
                        * (opponent.CurrentElement == Element.Water ? 1f - Tuning.SpikeDamp : 1f);
        if (FlareActiveTimer > 0f) push *= 1f + (Tuning.FlareMult - 1f) * spikeDamp;
        if (CritActiveTimer > 0f) push *= 1f + (Tuning.CritMult - 1f) * spikeDamp;
        if (CurrentElement == Element.Poison && IsEmpowered && Stacks > 0)
            push *= 1f + Tuning.StackBonus * Stacks * (anyPinpoint ? Tuning.PierceDamp : 1f);

        if (ownSideClashX < Tuning.DesperationZone) push *= Tuning.DesperationMult;
        if (ReformTimer > 0f) push *= Tuning.ReformPush;
        return push;
    }
}
