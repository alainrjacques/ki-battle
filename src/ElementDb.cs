using System;
using Godot;

namespace KiBattle;

public enum Element { Fire = 0, Ice = 1, Water = 2, Lightning = 3, Darkness = 4, Poison = 5, Holy = 6, Light = 7 }

public enum BeamType { Single = 0, Twin = 1, Pinpoint = 2 }

/// <summary>The beam is always lit: Idle is the free base beam, Empowered is the
/// mana-draining channel (Space), Overdrive stacks on top (Shift).</summary>
public enum PowerTier { Idle = 0, Empowered = 1, Overdrive = 2 }

/// <summary>Visual identity of one element, fed into shaders, particles and HUD.</summary>
public readonly record struct ElementStyle(
    string DisplayName,
    Color Core,
    Color Glow,
    int StyleIndex,
    float NoiseScale,
    float ScrollSpeed,
    float WobbleAmp);

/// <summary>All gameplay numbers in one place for tuning. See docs/DESIGN-EightSouls.md.</summary>
public static class Tuning
{
    // Clash
    public const float ClashRate = 0.045f;      // k: units of clashX per second per net push
    public const float ClashStart = 0.5f;
    public const float LoseAt = 0.02f;          // for the side at 0; mirrored for side at 1
    public const float DesperationZone = 0.15f; // within this of your own edge
    public const float DesperationMult = 1.25f;

    // Pacing guarantees: fights must resolve. Escalation amplifies net push over time,
    // sudden death stops base mana regen, and the judge decides at the hard cap.
    public const float EscalationStart = 30f;
    public const float EscalationRatePerSec = 0.06f;
    public const float SuddenDeathStart = 75f;
    public const float MaxDuration = 120f;

    // Beam types: push multiplier / mana drain per second
    public static readonly float[] BeamPush = { 1.00f, 1.35f, 0.75f };   // Single, Twin, Pinpoint
    public static readonly float[] BeamDrain = { 12f, 20f, 7f };
    public const float PierceDamp = 0.5f;       // pinpoint damps soul passives and entrances (both sides)

    // Power tiers
    public const float IdlePushMult = 0.35f; // the always-on base beam's push

    // Mana
    public const float ManaMax = 100f;
    public const float ManaRegen = 5f;
    public const float ManaRegenExhausted = 10f;
    public const float OverdrivePush = 1.6f;
    public const float OverdriveDrainMult = 2.2f;
    public const float ExhaustDuration = 1.5f;

    // Switch timeline: press -> gather (windup, old element, full push, vulnerable)
    // -> reform (new element, half push, vulnerable) -> impact (entrance resolves).
    public const float GatherDuration = 0.25f;
    public const float ElementReformDuration = 0.65f;
    public const float BeamReformDuration = 0.40f;
    public const float ReformPush = 0.5f;

    // Switch economy
    public const float ElementSwitchCost = 7f;
    public const float LightSwitchCost = 6f;      // switching INTO Light
    public const float KeystoneReturnCost = 0f;   // coming home
    public const float ElementSwitchCooldown = 3.0f;
    public const float BeamSwitchCost = 6f;
    public const float BeamSwitchCooldown = 2.0f;
    public const float EchoLockout = 4.0f;        // re-entering a recently-left element: no entrance

    // Catalyst pip + impact modifiers
    public const float PipChargeTime = 7.0f;
    public const float UnchargedMult = 0.40f;     // pipless entrance: shove x this, no effect rider
    public const float ShatterMult = 1.5f;        // victim vulnerable at impact
    public const float ShatterGrace = 0.25f;      // still-staggered window after reform ends
    public const float BraceMult = 0.5f;          // victim overdriving at impact
    public const float MaxImpulse = 0.15f;        // hard cap on any single entrance impulse
    public const float ManaOpFloor = 10f;         // entrance mana ops never take a victim below this
    public const float ImpulseEscalationCap = 2f; // escalation multiplies impulses only up to here

    // Souls: shared
    public const float DominanceWindow = 1.0f;    // sustained net-push lead required for Ice/Darkness passives

    // Fire — Ignition
    public const float FlarePeriod = 2.5f;
    public const float FlareWindup = 0.5f;
    public const float FlareDuration = 0.6f;
    public const float FlareMult = 1.45f;
    public const float CombustShove = 0.08f;

    // Ice — Permafrost
    public const float FrostCdRate = 0.6f;        // enemy cooldown tick rate while Ice is dominant
    public const float GlaciateShove = 0.035f;
    public const float GlaciateCdAdd = 2.0f;      // added to the victim's currently-running cooldowns

    // Water — Flow
    public const float FlowDrainMult = 0.85f;
    public const float SpikeDamp = 0.35f;         // damps incoming flares/crits/shoves toward 1
    public const float RiptideShove = 0.035f;
    public const float RiptideManaBurn = 10f;
    public const float RiptideOverdriveLock = 2.0f;

    // Lightning — Overload (deterministic crit)
    public const float CritChargeTime = 5.0f;     // Empowered+ seconds to arm
    public const float CritSelfFireTime = 6.0f;   // fires unaided at this charge
    public const float CritMult = 1.8f;
    public const float CritDuration = 0.35f;
    public const float CritWindowAfterReform = 1.5f;
    public const float ConductShove = 0.05f;
    public const float ConductDrainMult = 1.5f;
    public const float ConductDuration = 3.0f;

    // Darkness — Devour
    public const float LeechRate = 3.0f;
    public const float EclipseStealRate = 4.0f;   // mana/s over the channel
    public const float EclipseDuration = 2.0f;

    // Poison — Corrosion
    public const int StackMax = 8;
    public const float StackBonus = 0.02f;        // push per stack, only while Poison Empowered+
    public const float StackDecay = 1.0f;         // stacks/s once you leave Poison
    public const int EnvenomStacks = 3;

    // Holy — Sanctuary
    public const float HolyRegen = 3.0f;
    public const float HolyExhaustMult = 0.5f;
    public const float PurgeShove = 0.05f;
    public const float PurgeImmunity = 2.0f;

    // Light — Velocity
    public const float LightPipRate = 1.35f;
    public const float FlashShove = 0.035f;
    public const float FlashPipRefund = 3.0f;     // seconds of pip charge returned
}

/// <summary>Static data: per-element visual styles and soul display metadata.</summary>
public static class ElementDb
{
    public const int Count = 8;

    public static readonly ElementStyle[] Styles =
    {
        new("Fire",      new Color("fff3c0"), new Color("ff5a00"), 0, 6.0f, 2.2f, 0.10f),
        new("Ice",       new Color("f0fcff"), new Color("57c8ff"), 1, 5.0f, 0.6f, 0.05f),
        new("Water",     new Color("e6fbff"), new Color("1e90ff"), 2, 4.0f, 1.4f, 0.14f),
        new("Lightning", new Color("ffffff"), new Color("9be8ff"), 3, 8.0f, 3.0f, 0.16f),
        new("Darkness",  new Color("1a0b2e"), new Color("b14cf0"), 4, 3.5f, 0.8f, 0.12f),
        new("Poison",    new Color("eaffc0"), new Color("7adc1e"), 5, 4.5f, 1.0f, 0.13f),
        new("Holy",      new Color("fff7d6"), new Color("ffc94d"), 6, 2.0f, 1.2f, 0.03f),
        new("Light",     new Color("ffffff"), new Color("c0e8ff"), 7, 2.0f, 2.6f, 0.00f),
    };

    public static ElementStyle Style(Element e) => Styles[(int)e];

    /// <summary>HUD/teaching text for one soul: passive and entrance names + one-liners.</summary>
    public readonly record struct SoulInfo(string PassiveName, string EntranceName, string TeachPassive, string TeachEntrance);

    public static readonly SoulInfo[] Souls =
    {
        new("IGNITION", "COMBUST", "Flares x1.45 every 2.5s while empowered", "Enters with a heavy shove"),
        new("PERMAFROST", "GLACIATE", "Dominance slows enemy cooldowns", "Adds 2s to enemy cooldowns"),
        new("FLOW", "RIPTIDE", "Cheaper drain, damps enemy spikes", "Burns 10 enemy mana, locks overdrive"),
        new("OVERLOAD", "CONDUCT", "Arms a x1.8 crit every 5s of channel", "Enemy drain x1.5 for 3s"),
        new("DEVOUR", "ECLIPSE", "Dominance leeches 3 mana/s", "Channel-steals 8 enemy mana"),
        new("CORROSION", "ENVENOM", "+2% push per stack while committed", "+3 stacks instantly"),
        new("SANCTUARY", "PURGE", "+3 mana/s, half exhaust", "Cleanses debuffs, 2s immunity"),
        new("VELOCITY", "FLASH", "Pip charges x1.35, cheap entry", "Refunds 3s of pip charge"),
    };

    public static SoulInfo Soul(Element e) => Souls[(int)e];

    /// <summary>Boot-time sanity asserts on the soul constants.</summary>
    public static void ValidateIdentities()
    {
        if (Styles.Length != Count || Souls.Length != Count)
            throw new InvalidOperationException("Soul/style tables must cover all 8 elements");
        foreach (float shove in new[] { Tuning.CombustShove, Tuning.GlaciateShove, Tuning.RiptideShove, Tuning.ConductShove, Tuning.PurgeShove, Tuning.FlashShove })
            if (shove < 0f || shove > 0.1f)
                throw new InvalidOperationException($"Entrance shove {shove} outside sane range 0..0.1");
        if (Tuning.GatherDuration + Tuning.ElementReformDuration <= Tuning.ShatterGrace)
            throw new InvalidOperationException("Reaction-shatter-impossible invariant violated");
    }
}
