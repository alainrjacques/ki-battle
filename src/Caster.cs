using System;
using Godot;

namespace KiBattle;

/// <summary>
/// One duelist's battle state: mana, current element/beam, switching, exhaustion.
/// Driven by PlayerController or AIController through the same API; BattleManager
/// only calls Tick() and ComputePush().
/// </summary>
public partial class Caster : Node2D
{
    [Export] public int Side; // 0 = left (player side of the bar), 1 = right

    public Loadout Loadout;
    public int ElementSlot { get; private set; }
    public BeamType Beam { get; private set; }
    public float Mana { get; private set; } = Tuning.ManaMax;

    public bool WantsChannel;   // set by controller each frame
    public bool WantsOverdrive;

    public float ElementCooldown { get; private set; }
    public float BeamCooldown { get; private set; }
    public float ExhaustTimer { get; private set; }
    public float ReformTimer { get; private set; }

    public bool IsExhausted => ExhaustTimer > 0f;
    public bool IsChanneling => WantsChannel && !IsExhausted && Mana > 0f;
    public bool IsOverdriving => IsChanneling && WantsOverdrive;
    public Element CurrentElement => Loadout.Elements[ElementSlot];
    public ElementStyle Style => ElementDb.Style(CurrentElement);

    public event Action<Element>? ElementChanged;
    public event Action<BeamType>? BeamTypeChanged;
    public event Action? Exhausted;

    public void Setup(Loadout loadout)
    {
        Loadout = loadout;
        ElementSlot = 0;
        Beam = loadout.StartingBeam;
        Mana = Tuning.ManaMax;
        ElementCooldown = BeamCooldown = ExhaustTimer = ReformTimer = 0f;
        WantsChannel = WantsOverdrive = false;
    }

    public bool TrySwitchElement(int slot)
    {
        if (slot == ElementSlot || slot < 0 || slot >= Loadout.Elements.Length) return false;
        if (ElementCooldown > 0f || Mana < Tuning.ElementSwitchCost) return false;
        ElementSlot = slot;
        Mana -= Tuning.ElementSwitchCost;
        ElementCooldown = Tuning.ElementSwitchCooldown;
        ReformTimer = Tuning.ReformDuration;
        ElementChanged?.Invoke(CurrentElement);
        return true;
    }

    public bool TrySwitchBeam(BeamType type)
    {
        if (type == Beam) return false;
        if (BeamCooldown > 0f || Mana < Tuning.BeamSwitchCost) return false;
        Beam = type;
        Mana -= Tuning.BeamSwitchCost;
        BeamCooldown = Tuning.BeamSwitchCooldown;
        ReformTimer = Tuning.ReformDuration;
        BeamTypeChanged?.Invoke(Beam);
        return true;
    }

    /// <summary>Visual-check helper (F1 cycle): force any element/beam combo.</summary>
    public void DebugSetCombo(Element element, BeamType beam)
    {
        Loadout.Elements[ElementSlot] = element;
        Beam = beam;
        ElementChanged?.Invoke(element);
        BeamTypeChanged?.Invoke(beam);
    }

    /// <summary>Advance mana and timers by one physics step. Called by BattleManager.
    /// regenScale drops to 0 in sudden death (exhaust recovery is unaffected).</summary>
    public void Tick(float delta, float regenScale = 1f)
    {
        ElementCooldown = Mathf.Max(0f, ElementCooldown - delta);
        BeamCooldown = Mathf.Max(0f, BeamCooldown - delta);
        ReformTimer = Mathf.Max(0f, ReformTimer - delta);
        ExhaustTimer = Mathf.Max(0f, ExhaustTimer - delta);

        float regen = IsExhausted ? Tuning.ManaRegenExhausted : Tuning.ManaRegen * regenScale;
        float drain = IsChanneling
            ? Tuning.BeamDrain[(int)Beam] * (WantsOverdrive ? Tuning.OverdriveDrainMult : 1f)
            : 0f;
        Mana = Mathf.Clamp(Mana + (regen - drain) * delta, 0f, Tuning.ManaMax);

        if (Mana <= 0f && !IsExhausted)
        {
            ExhaustTimer = Tuning.ExhaustDuration;
            Exhausted?.Invoke();
        }
    }

    /// <summary>Current push force against the opponent. clashX is in this caster's frame
    /// (0 = own edge, 1 = opponent's edge) for the desperation bonus.</summary>
    public float ComputePush(Caster opponent, float ownSideClashX)
    {
        if (!IsChanneling) return 0f;

        bool anyPinpoint = Beam == BeamType.Pinpoint || opponent.Beam == BeamType.Pinpoint;
        float m = ElementDb.EffectiveMultiplier(CurrentElement, opponent.CurrentElement, anyPinpoint);
        float push = Tuning.BeamPush[(int)Beam] * m;
        if (IsOverdriving) push *= Tuning.OverdrivePush;
        if (ownSideClashX < Tuning.DesperationZone) push *= Tuning.DesperationMult;
        if (ReformTimer > 0f) push *= Tuning.ReformPush;
        return push;
    }
}
