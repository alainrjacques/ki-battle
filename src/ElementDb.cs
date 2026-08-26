using System;
using Godot;

namespace KiBattle;

public enum Element { Fire = 0, Ice = 1, Water = 2, Lightning = 3, Darkness = 4, Poison = 5, Holy = 6, Light = 7 }

public enum BeamType { Single = 0, Twin = 1, Pinpoint = 2 }

/// <summary>Visual identity of one element, fed into shaders, particles and HUD.</summary>
public readonly record struct ElementStyle(
    string DisplayName,
    Color Core,
    Color Glow,
    int StyleIndex,
    float NoiseScale,
    float ScrollSpeed,
    float WobbleAmp);

/// <summary>All gameplay numbers in one place for tuning.</summary>
public static class Tuning
{
    // Element matchup multipliers
    public const float Advantage = 1.30f;
    public const float Neutral = 1.00f;
    public const float Disadvantage = 0.77f;

    // Clash
    public const float ClashRate = 0.045f;      // k: units of clashX per second per net push
    public const float ClashStart = 0.5f;
    public const float LoseAt = 0.02f;          // for the side at 0; mirrored for side at 1
    public const float DesperationZone = 0.15f; // within this of your own edge
    public const float DesperationMult = 1.25f;

    // Pacing guarantees: fights must resolve. Escalation amplifies net push over time,
    // sudden death stops base mana regen, and the judge decides at the hard cap.
    public const float EscalationStart = 30f;
    public const float EscalationRatePerSec = 0.04f;
    public const float SuddenDeathStart = 75f;
    public const float MaxDuration = 120f;

    // Beam types: push multiplier / mana drain per second
    public static readonly float[] BeamPush = { 1.00f, 1.35f, 0.75f };   // Single, Twin, Pinpoint
    public static readonly float[] BeamDrain = { 12f, 20f, 7f };
    public const float PierceDamp = 0.5f;       // pinpoint damps elemental multiplier toward neutral

    // Mana
    public const float ManaMax = 100f;
    public const float ManaRegen = 5f;
    public const float ManaRegenExhausted = 10f;
    public const float OverdrivePush = 1.6f;
    public const float OverdriveDrainMult = 2.2f;
    public const float ExhaustDuration = 1.5f;

    // Switching
    public const float ElementSwitchCost = 10f;
    public const float ElementSwitchCooldown = 3.0f;
    public const float BeamSwitchCost = 6f;
    public const float BeamSwitchCooldown = 2.0f;
    public const float ReformDuration = 0.4f;
    public const float ReformPush = 0.5f;
}

/// <summary>Static data: the 8x8 matchup table and per-element visual styles.</summary>
public static class ElementDb
{
    public const int Count = 8;

    // Rows = attacker, columns = defender. Each element beats exactly 3, loses to 3,
    // is neutral to 1 (+ its mirror). See plan for flavor rationale.
    private const float A = Tuning.Advantage, N = Tuning.Neutral, D = Tuning.Disadvantage;

    //                                          vs: Fire Ice Water Ligh Dark Pois Holy Light
    private static readonly float[,] Table =
    {
        /* Fire      */ { N,   A,  D,   D,   A,   A,   N,   D },
        /* Ice       */ { D,   N,  A,   A,   D,   A,   D,   N },
        /* Water     */ { A,   D,  N,   D,   N,   D,   A,   A },
        /* Lightning */ { A,   D,  A,   N,   A,   N,   D,   D },
        /* Darkness  */ { D,   A,  N,   D,   N,   A,   D,   A },
        /* Poison    */ { D,   D,  A,   N,   D,   N,   A,   A },
        /* Holy      */ { N,   A,  D,   A,   A,   D,   N,   D },
        /* Light     */ { A,   N,  D,   A,   D,   D,   A,   N },
    };

    public static float Multiplier(Element attacker, Element defender) =>
        Table[(int)attacker, (int)defender];

    /// <summary>Matchup multiplier including pinpoint pierce (either side using pinpoint damps it).</summary>
    public static float EffectiveMultiplier(Element attacker, Element defender, bool anyPinpoint)
    {
        float m = Multiplier(attacker, defender);
        return anyPinpoint ? 1f + (m - 1f) * Tuning.PierceDamp : m;
    }

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

    /// <summary>Asserts every row has exactly 3 advantages, 3 disadvantages, 2 neutrals, and the table is antisymmetric.</summary>
    public static void ValidateTable()
    {
        for (int r = 0; r < Count; r++)
        {
            int adv = 0, dis = 0, neu = 0;
            for (int c = 0; c < Count; c++)
            {
                float v = Table[r, c];
                if (v == A) adv++;
                else if (v == D) dis++;
                else neu++;

                float mirror = Table[c, r];
                bool consistent = (v == N && mirror == N) || (v == A && mirror == D) || (v == D && mirror == A);
                if (!consistent)
                    throw new InvalidOperationException($"Matchup table asymmetry at [{(Element)r} vs {(Element)c}]");
            }
            if (adv != 3 || dis != 3 || neu != 2)
                throw new InvalidOperationException($"Matchup row {(Element)r} unbalanced: {adv}A/{dis}D/{neu}N");
        }
    }
}
