using System;
using Godot;

namespace KiBattle;

public enum BattleState { Intro, Fighting, Ko, Result }

/// <summary>
/// Root of Battle.tscn. Owns the clash scalar, runs the battle state machine,
/// wires controllers onto the two casters, and drives all battle visuals.
/// </summary>
public partial class BattleManager : Node2D
{
    /// <summary>When true, no controllers are attached; an external harness drives the casters.</summary>
    public bool ScriptedMode;

    public BattleState State { get; private set; } = BattleState.Intro;
    public float ClashX { get; private set; } = Tuning.ClashStart;
    public float FightDuration { get; private set; }
    public int WinnerSide { get; private set; } = -1;

    public Caster LeftCaster = null!;
    public Caster RightCaster = null!;

    public event Action<int>? BattleEnded; // winner side
    public event Action<int, ImpactReport>? EntranceResolved; // attacker side, what landed

    private float _leadTimeLeft, _leadTimeRight;

    private float _introTimer = 1.5f;
    private Label? _debugLabel;

    // Visuals
    private Beam? _leftBeam, _rightBeam;
    private ClashPoint? _clash;
    private BattleCamera? _camera;
    private ColorRect? _flash;
    private float _lastPushLeft, _lastPushRight;
    private int _momentumSign;
    private bool _wasOverdrivingL, _wasOverdrivingR;
    private float _koTimer;
    private double _prevTimeScale = 1.0;
    private int _debugCombo;

    public override void _Ready()
    {
        LeftCaster = GetNode<Caster>("PlayerCaster");
        RightCaster = GetNode<Caster>("EnemyCaster");
        _debugLabel = GetNodeOrNull<Label>("DebugLabel");

        // The headless dummy renderer crashes intermittently on particles/lights;
        // visuals are meaningless there, so drop every visual node instead.
        if (DisplayServer.GetName() == "headless")
        {
            foreach (string n in new[] { "PlayerBeam", "EnemyBeam", "Clash", "Background", "WorldEnvironment", "BattleCamera", "Hud" })
                GetNodeOrNull(n)?.QueueFree();
            GetNodeOrNull("PlayerCaster/Visual")?.QueueFree();
            GetNodeOrNull("EnemyCaster/Visual")?.QueueFree();
        }
        else
        {
            _leftBeam = GetNodeOrNull<Beam>("PlayerBeam");
            _rightBeam = GetNodeOrNull<Beam>("EnemyBeam");
            _clash = GetNodeOrNull<ClashPoint>("Clash");
            _camera = GetNodeOrNull<BattleCamera>("BattleCamera");

            var flashLayer = new CanvasLayer { Layer = 90 };
            _flash = new ColorRect
            {
                Color = new Color(1, 1, 1, 0),
                AnchorRight = 1, AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            flashLayer.AddChild(_flash);
            AddChild(flashLayer);
        }

        LeftCaster.Setup(Game.Instance.PlayerLoadout);
        RightCaster.Setup(Game.Instance.EnemyLoadout);

        LeftCaster.Exhausted += () => _camera?.AddTrauma(0.4f);
        RightCaster.Exhausted += () => _camera?.AddTrauma(0.4f);

        if (!ScriptedMode)
        {
            if (Game.Instance.SmokeMode)
            {
                AttachAi(LeftCaster, RightCaster);
            }
            else
            {
                var pc = new PlayerController();
                pc.Target = LeftCaster;
                AddChild(pc);
            }
            AttachAi(RightCaster, LeftCaster);
        }

        if (ScriptedMode || Game.Instance.SmokeMode) _introTimer = 0f; // no ceremony in tests
    }

    private void AttachAi(Caster target, Caster opponent)
    {
        var ai = new AIController
        {
            Target = target,
            Opponent = opponent,
            Battle = this,
            Difficulty = Game.Instance.Difficulty,
            Personality = Game.Instance.Personality,
        };
        AddChild(ai);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        switch (State)
        {
            case BattleState.Intro:
                _introTimer -= dt;
                if (_introTimer <= 0f) State = BattleState.Fighting;
                break;

            case BattleState.Fighting:
                StepFight(dt);
                break;

            case BattleState.Ko:
                _koTimer -= dt;
                if (_koTimer <= 0f)
                {
                    Engine.TimeScale = _prevTimeScale;
                    State = BattleState.Result;
                }
                break;

            case BattleState.Result:
                break;
        }
        UpdateDebugLabel();
    }

    private void StepFight(float dt)
    {
        FightDuration += dt;
        float regenScale = FightDuration > Tuning.SuddenDeathStart ? 0.5f : 1f;

        // Frost: an Ice soul that has held dominance slows the enemy's cooldowns.
        LeftCaster.CooldownRate = FrostRateAgainst(RightCaster, LeftCaster, _leadTimeRight);
        RightCaster.CooldownRate = FrostRateAgainst(LeftCaster, RightCaster, _leadTimeLeft);

        LeftCaster.Tick(dt, regenScale);
        RightCaster.Tick(dt, regenScale);

        // Escalation ramps gently after 30s and hard after sudden death: late leads
        // snowball, so the fight is decided by play, not by the judge's coin flip.
        float escalation = 1f
            + Mathf.Max(0f, Mathf.Min(FightDuration, Tuning.SuddenDeathStart) - Tuning.EscalationStart) * Tuning.EscalationRatePerSec
            + Mathf.Max(0f, FightDuration - Tuning.SuddenDeathStart) * Tuning.EscalationRatePerSec * 3f;

        // Deterministic Lightning crits: fire into the enemy's vulnerability window.
        LeftCaster.TryFireCrit(CritWindowOpen(RightCaster));
        RightCaster.TryFireCrit(CritWindowOpen(LeftCaster));

        // Entrance impacts land before push so the shove and its FX line up.
        if (LeftCaster.ConsumeImpact(out bool chargedL))
            ResolveEntrance(LeftCaster, RightCaster, chargedL, escalation, +1f);
        if (RightCaster.ConsumeImpact(out bool chargedR))
            ResolveEntrance(RightCaster, LeftCaster, chargedR, escalation, -1f);

        float pushLeft = LeftCaster.ComputePush(RightCaster, ClashX);
        float pushRight = RightCaster.ComputePush(LeftCaster, 1f - ClashX);
        _lastPushLeft = pushLeft;
        _lastPushRight = pushRight;

        // Dominance timers feed Ice frost and Darkness leech.
        const float eps = 0.02f;
        _leadTimeLeft = pushLeft > pushRight + eps ? _leadTimeLeft + dt : 0f;
        _leadTimeRight = pushRight > pushLeft + eps ? _leadTimeRight + dt : 0f;
        LeftCaster.DominanceTime = _leadTimeLeft;
        RightCaster.DominanceTime = _leadTimeRight;

        TickSustainedDrains(LeftCaster, RightCaster, _leadTimeLeft, dt);
        TickSustainedDrains(RightCaster, LeftCaster, _leadTimeRight, dt);

        ClashX += Tuning.ClashRate * escalation * (pushLeft - pushRight) * dt;
        ClashX = Mathf.Clamp(ClashX, 0f, 1f);

        // Momentum-flip and overdrive-engage shakes.
        int sign = Math.Sign(pushLeft - pushRight);
        if (sign != 0 && _momentumSign != 0 && sign != _momentumSign) _camera?.AddTrauma(0.3f);
        if (sign != 0) _momentumSign = sign;
        if (LeftCaster.IsOverdriving && !_wasOverdrivingL) _camera?.AddTrauma(0.2f);
        if (RightCaster.IsOverdriving && !_wasOverdrivingR) _camera?.AddTrauma(0.2f);
        _wasOverdrivingL = LeftCaster.IsOverdriving;
        _wasOverdrivingR = RightCaster.IsOverdriving;

        if (ClashX >= 1f - Tuning.LoseAt) EndBattle(0);
        else if (ClashX <= Tuning.LoseAt) EndBattle(1);
        else if (FightDuration >= Tuning.MaxDuration) EndBattle(JudgeDecision());
    }

    /// <summary>Reinitializes for a fresh fight in the SAME scene instance, reading
    /// loadouts from Game. Test harnesses use this instead of scene churn — repeated
    /// instantiate/QueueFree cycles crash the headless dummy renderer intermittently.</summary>
    public void ResetBattle()
    {
        State = BattleState.Intro;
        ClashX = Tuning.ClashStart;
        FightDuration = 0f;
        WinnerSide = -1;
        _introTimer = ScriptedMode || Game.Instance.SmokeMode ? 0f : 1.5f;
        _leadTimeLeft = _leadTimeRight = 0f;
        _momentumSign = 0;
        _koTimer = 0f;
        LeftCaster.Setup(Game.Instance.PlayerLoadout);
        RightCaster.Setup(Game.Instance.EnemyLoadout);
    }

    /// <summary>Cooldown tick rate imposed on `victim` by an Ice `holder` with sustained dominance.</summary>
    private static float FrostRateAgainst(Caster holder, Caster victim, float holderLead)
    {
        if (holder.CurrentElement != Element.Ice || holderLead < Tuning.DominanceWindow) return 1f;
        bool anyPinpoint = holder.Beam == BeamType.Pinpoint || victim.Beam == BeamType.Pinpoint;
        float slow = 1f - Tuning.FrostCdRate; // 0.4 of the tick rate removed
        return 1f - slow * (anyPinpoint ? Tuning.PierceDamp : 1f);
    }

    /// <summary>Lightning crits release into gathers, reforms, and the stagger after them.</summary>
    private static bool CritWindowOpen(Caster victim) =>
        victim.GatherTimer > 0f || victim.ReformTimer > 0f || victim.SinceReformEnd < Tuning.CritWindowAfterReform;

    /// <summary>Darkness leech and Eclipse channel: the sustained mana streams.</summary>
    private static void TickSustainedDrains(Caster taker, Caster victim, float takerLead, float dt)
    {
        bool blocked = victim.IsExhausted || victim.PurgeImmunityTimer > 0f || victim.Mana <= Tuning.ManaOpFloor;

        if (taker.CurrentElement == Element.Darkness && takerLead >= Tuning.DominanceWindow && !blocked)
        {
            bool anyPinpoint = taker.Beam == BeamType.Pinpoint || victim.Beam == BeamType.Pinpoint;
            float amount = Tuning.LeechRate * (anyPinpoint ? Tuning.PierceDamp : 1f) * dt;
            amount = Mathf.Min(amount, victim.Mana - Tuning.ManaOpFloor);
            victim.BurnMana(amount);
            taker.GainMana(amount);
        }

        if (taker.EclipseTimer > 0f && !blocked)
        {
            float amount = Mathf.Min(Tuning.EclipseStealRate * dt, victim.Mana - Tuning.ManaOpFloor);
            victim.BurnMana(amount);
            taker.GainMana(amount);
        }
    }

    /// <summary>Resolves one landed entrance: impulse to the clash plus the soul's rider.</summary>
    private void ResolveEntrance(Caster attacker, Caster victim, bool charged, float escalation, float direction)
    {
        Element soul = attacker.CurrentElement;
        bool shattered = victim.IsVulnerable;
        bool braced = victim.IsOverdriving;
        bool anyPinpoint = attacker.Beam == BeamType.Pinpoint || victim.Beam == BeamType.Pinpoint;

        float shove = soul switch
        {
            Element.Fire => Tuning.CombustShove,
            Element.Ice => Tuning.GlaciateShove,
            Element.Water => Tuning.RiptideShove,
            Element.Lightning => Tuning.ConductShove,
            Element.Holy => Tuning.PurgeShove,
            Element.Light => Tuning.FlashShove,
            _ => 0f, // Darkness and Poison enter quietly
        };

        float mods = (shattered ? Tuning.ShatterMult : 1f)
                   * (braced ? Tuning.BraceMult : 1f)
                   * (anyPinpoint ? Tuning.PierceDamp : 1f)
                   * (victim.CurrentElement == Element.Water ? 1f - Tuning.SpikeDamp : 1f);

        float impulse = shove * (charged ? 1f : Tuning.UnchargedMult) * mods
                      * Mathf.Min(escalation, Tuning.ImpulseEscalationCap);
        impulse = Mathf.Min(impulse, Tuning.MaxImpulse) * direction;
        ClashX = Mathf.Clamp(ClashX + impulse, 0f, 1f);

        // Effect riders only land charged; magnitudes/durations scale with mods
        // (Conduct: duration only, rate is fixed — a shattered conduct lasts longer).
        if (charged)
        {
            switch (soul)
            {
                case Element.Ice:
                    victim.ApplyGlaciate(Tuning.GlaciateCdAdd * mods);
                    break;
                case Element.Water:
                    if (!victim.IsExhausted && victim.PurgeImmunityTimer <= 0f)
                        victim.BurnMana(Tuning.RiptideManaBurn * mods);
                    if (braced) victim.ApplyOverdriveLock(Tuning.RiptideOverdriveLock);
                    break;
                case Element.Lightning:
                    victim.ApplyConduct(Tuning.ConductDuration * mods);
                    break;
                case Element.Darkness:
                    attacker.StartEclipse(Tuning.EclipseDuration * mods);
                    break;
                case Element.Poison:
                    attacker.AddStacks((int)MathF.Round(Tuning.EnvenomStacks * mods));
                    break;
                case Element.Holy:
                    attacker.Purge(Tuning.PurgeImmunity * mods);
                    victim.BreakEclipse();
                    break;
                case Element.Light:
                    attacker.RefundPip(Tuning.FlashPipRefund * mods);
                    break;
            }
        }

        var report = new ImpactReport(soul, charged, shattered, braced, Mathf.Abs(impulse));
        EntranceResolved?.Invoke(attacker.Side, report);
        _camera?.AddTrauma(shattered ? 0.5f : charged ? 0.35f : 0.15f);
    }

    /// <summary>Hard-cap tiebreak: whoever holds clash territory wins; mana breaks a dead tie.</summary>
    private int JudgeDecision()
    {
        if (Mathf.Abs(ClashX - 0.5f) > 0.001f) return ClashX > 0.5f ? 0 : 1;
        return LeftCaster.Mana >= RightCaster.Mana ? 0 : 1;
    }

    private void EndBattle(int winnerSide)
    {
        WinnerSide = winnerSide;
        LeftCaster.WantsChannel = false;
        RightCaster.WantsChannel = false;
        BattleEnded?.Invoke(winnerSide);

        if (ScriptedMode || Game.Instance.SmokeMode)
        {
            State = BattleState.Result;
            return;
        }

        // KO ceremony: slow-mo, white flash, loser dissolves, winner beam surges.
        State = BattleState.Ko;
        _prevTimeScale = Engine.TimeScale;
        Engine.TimeScale = 0.3;
        _koTimer = 0.9f;
        _camera?.AddTrauma(1.0f);
        if (_flash != null) _flash.Color = new Color(1, 1, 1, 0.85f);
        var loser = winnerSide == 0 ? RightCaster : LeftCaster;
        loser.GetNodeOrNull<CasterVisual>("Visual")?.StartDissolve();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateBeams(dt);

        if (_flash != null && _flash.Color.A > 0f)
            _flash.Color = new Color(1, 1, 1, Mathf.Max(0f, _flash.Color.A - dt * 1.2f));

        if (!ScriptedMode && Input.IsActionJustPressed("debug_cycle"))
        {
            _debugCombo = (_debugCombo + 1) % 24;
            LeftCaster.DebugSetCombo((Element)(_debugCombo % 8), (BeamType)(_debugCombo / 8));
        }
    }

    private void UpdateBeams(float dt)
    {
        if (_leftBeam == null || _rightBeam == null) return;

        Vector2 muzzleL = LeftCaster.ToGlobal(CasterVisual.MuzzleLocal);
        Vector2 muzzleR = RightCaster.ToGlobal(CasterVisual.MuzzleLocal);
        Vector2 clashPos = muzzleL.Lerp(muzzleR, ClashX);

        _leftBeam.UpdateBeam(LeftCaster, muzzleL, clashPos, BeamIntensity(LeftCaster, 0), dt);
        _rightBeam.UpdateBeam(RightCaster, muzzleR, clashPos, BeamIntensity(RightCaster, 1), dt);

        if (_clash != null)
        {
            _clash.GlobalPosition = clashPos;
            float combined = (_leftBeam.DisplayIntensity + _rightBeam.DisplayIntensity) * 0.4f;
            float totalPush = _lastPushLeft + _lastPushRight;
            float ratio = totalPush > 0.001f ? _lastPushRight / totalPush : 0.5f;
            _clash.UpdateClash(LeftCaster.Style.Glow, RightCaster.Style.Glow, combined, ratio, dt);
        }

        // Constant low rumble while both beams are pushing hard (idle beams stay calm).
        if (State == BattleState.Fighting && _leftBeam.DisplayIntensity > 0.6f && _rightBeam.DisplayIntensity > 0.6f)
            _camera?.AddTrauma(0.35f * dt * (_leftBeam.DisplayIntensity + _rightBeam.DisplayIntensity) * 0.5f);
    }

    private float BeamIntensity(Caster caster, int side)
    {
        if (State is BattleState.Ko or BattleState.Result)
            return WinnerSide == side ? 2.2f : 0f;
        float reform = caster.ReformTimer > 0f
            ? Mathf.Lerp(1f, 0.3f, caster.ReformTimer / Tuning.ElementReformDuration)
            : 1f;
        // The beam is always lit: a sputtering wisp when exhausted, a base glow at
        // idle, full when empowered, blazing on overdrive. The gather windup and
        // active flares/crits brighten it — the visual tells of the soul system.
        float tier = caster.IsExhausted ? 0.12f : caster.Tier switch
        {
            PowerTier.Overdrive => 1.6f,
            PowerTier.Empowered => 1.0f,
            _ => 0.45f,
        };
        if (caster.GatherTimer > 0f) tier *= 1.35f;
        if (caster.FlareActiveTimer > 0f || caster.CritActiveTimer > 0f) tier *= 1.3f;
        else if (caster.CurrentElement == Element.Fire && caster.IsEmpowered
                 && caster.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup)
            tier *= 1.15f; // flare windup: the readable telegraph
        return tier * reform;
    }

    private void UpdateDebugLabel()
    {
        if (_debugLabel == null || !_debugLabel.Visible) return; // hidden by the HUD in normal play
        _debugLabel.Text =
            $"state={State} clash={ClashX:0.000} t={FightDuration:0.0}s\n" +
            $"L: {LeftCaster.CurrentElement}/{LeftCaster.Beam} mana={LeftCaster.Mana:0} {(LeftCaster.IsExhausted ? "EXH" : "")}\n" +
            $"R: {RightCaster.CurrentElement}/{RightCaster.Beam} mana={RightCaster.Mana:0} {(RightCaster.IsExhausted ? "EXH" : "")}" +
            (WinnerSide >= 0 ? $"\nWINNER: {(WinnerSide == 0 ? "LEFT" : "RIGHT")}" : "");
    }
}
