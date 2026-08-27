using System;
using System.Linq;
using Godot;

namespace KiBattle;

/// <summary>
/// Utility-scoring AI for the Eight Souls system. Ticks at 4 Hz with hysteresis.
/// Perceives the opponent's element/beam after a reaction delay, and their GATHER
/// windup after a (faster) gather delay — difficulty is reaction time, never psychic
/// reads. Hard braces incoming entrances and never panic-dodges; Normal sometimes
/// panic-dodges into a shatter — the learnable flaw.
/// </summary>
public partial class AIController : Node
{
    public Caster Target = null!;
    public Caster Opponent = null!;
    public BattleManager Battle = null!;
    public Difficulty Difficulty = Difficulty.Normal;
    public AiPersonality Personality = AiPersonality.Aggressor;

    private const float TickInterval = 0.25f;
    private const float Hysteresis = 0.15f;
    private const float StayBonus = 0.5f;
    private const float SwitchThreshold = 0.25f;

    private readonly Random _rng = new();
    private float _tickAccum;

    private enum Stance { Sustain, Overdrive, Rest }
    private Stance _stance = Stance.Sustain;

    // Delayed perception of the opponent's state.
    private Element _perceivedElement;
    private BeamType _perceivedBeam;
    private Element _pendingElement;
    private BeamType _pendingBeam;
    private float _elementNoticeTimer = -1f;
    private float _beamNoticeTimer = -1f;
    private bool _perceptionInit;

    // Gather perception: the incoming-entrance channel.
    private bool _sawGather;
    private float _gatherNoticeTimer = -1f;
    private float _braceDelay;
    private float _braceHold;
    private bool _panicDodged;
    private bool _recovering; // resting until mana recovers — the drain-rest rhythm

    private float ReactionDelay => Difficulty switch
    {
        Difficulty.Easy => 1.5f,
        Difficulty.Normal => 0.9f,
        _ => 0.5f,
    };

    private float GatherDelay => Difficulty switch
    {
        Difficulty.Easy => 0.90f,   // never defends in time — eats every entrance
        Difficulty.Normal => 0.55f, // defends the tail end, panic-dodges 15%
        _ => 0.30f,                 // braces reliably
    };

    private float MistakeChance => Difficulty switch
    {
        Difficulty.Easy => 0.25f,
        Difficulty.Normal => 0.10f,
        _ => 0.02f,
    };

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        UpdatePerception(dt);

        _tickAccum += dt;
        if (_tickAccum < TickInterval) return;
        _tickAccum = 0f;

        if (Battle.State != BattleState.Fighting)
        {
            Target.WantsChannel = false;
            Target.WantsOverdrive = false;
            return;
        }

        Think();
    }

    private void UpdatePerception(float dt)
    {
        if (!_perceptionInit)
        {
            _perceivedElement = _pendingElement = Opponent.CurrentElement;
            _perceivedBeam = _pendingBeam = Opponent.Beam;
            _perceptionInit = true;
        }

        if (Opponent.CurrentElement != _pendingElement)
        {
            _pendingElement = Opponent.CurrentElement;
            _elementNoticeTimer = ReactionDelay;
        }
        if (_elementNoticeTimer >= 0f && (_elementNoticeTimer -= dt) < 0f)
            _perceivedElement = _pendingElement;

        if (Opponent.Beam != _pendingBeam)
        {
            _pendingBeam = Opponent.Beam;
            _beamNoticeTimer = ReactionDelay;
        }
        if (_beamNoticeTimer >= 0f && (_beamNoticeTimer -= dt) < 0f)
            _perceivedBeam = _pendingBeam;

        // Gather channel: notice the windup, then defend until the impact passes.
        bool incoming = Opponent.GatherTimer > 0f || Opponent.ReformTimer > 0f;
        if (Opponent.GatherTimer > 0f && !_sawGather)
        {
            _sawGather = true;
            _panicDodged = false;
            _gatherNoticeTimer = GatherDelay;
        }
        if (!incoming)
        {
            _sawGather = false;
            _gatherNoticeTimer = -1f;
        }
        if (_gatherNoticeTimer >= 0f && (_gatherNoticeTimer -= dt) < 0f && incoming)
            OnPerceivedIncoming();

        // A scheduled brace engages late — overdrive only through the impact
        // window itself, like the timing bar teaches — not from perception on.
        if (_braceDelay > 0f && (_braceDelay -= dt) <= 0f)
            _braceHold = 0.55f;
        _braceHold = Mathf.Max(0f, _braceHold - dt);
    }

    /// <summary>The AI has just perceived an incoming entrance. Hard always braces
    /// a charged one (and never dodges — it knows the vulnerability trap); Normal
    /// defends the tail end ~40% of the time and panic-dodges 15%; Easy eats it.</summary>
    private void OnPerceivedIncoming()
    {
        _gatherNoticeTimer = -1f;
        // Only a charged entrance is worth the overdrive mana a brace costs;
        // uncharged shoves and pipless switches are noise.
        bool worthDefending = Opponent.IncomingEntranceCharged;
        bool canBrace = worthDefending && Target.Mana > 35f;

        if (canBrace && (Difficulty == Difficulty.Hard
                         || (Difficulty == Difficulty.Normal && _rng.NextDouble() < 0.4)))
        {
            // Schedule a LATE brace: overdriving from perception to impact costs
            // ~3x what the halved shove is worth; only the impact window pays.
            float timeToImpact = Opponent.GatherTimer + Opponent.ReformTimer;
            _braceDelay = Mathf.Max(0.001f, timeToImpact - 0.4f);
        }
        else if (Difficulty == Difficulty.Normal && !_panicDodged && _rng.NextDouble() < 0.15)
        {
            // The learnable flaw: a late dodge that eats the shatter.
            _panicDodged = true;
            TrySwitchScored(force: true);
        }
    }

    private void Think()
    {
        float ownX = Target.Side == 0 ? Battle.ClashX : 1f - Battle.ClashX;
        float losing = (0.5f - ownX) / 0.5f; // 1 = clash at our door, -1 = almost won
        float manaRatio = Target.Mana / Tuning.ManaMax;
        float oppMana = Opponent.Mana / Tuning.ManaMax;
        bool punishWindow = Opponent.IsExhausted || Opponent.ReformTimer > 0f;

        // --- Continuous stance ---
        float sustain = 0.5f + (punishWindow ? 0.3f : 0f);
        float overdrive = (punishWindow ? 1.2f : 0f)
                          + 0.9f * MathF.Max(losing - 0.3f, 0f)
                          + 2.0f * MathF.Max(-losing - 0.4f, 0f) * MathF.Max(0.5f - oppMana, 0f)
                          + 0.2f * (manaRatio - 0.5f)
                          - (manaRatio < 0.25f ? 2.0f : 0f);
        bool suddenDeath = Battle.FightDuration > Tuning.SuddenDeathStart;
        float rest = 3.0f * (0.42f - manaRatio)
              - (punishWindow ? 2.0f : 0f)
              - 1.5f * MathF.Max(losing - 0.5f, 0f)
              - 1.0f * MathF.Max(-losing - 0.4f, 0f)
              - (suddenDeath ? 0.4f : 0f);

        if (Personality == AiPersonality.Aggressor) overdrive += 0.3f;
        if (Personality == AiPersonality.Miser) rest += 0.3f;
        // Riding a soul whose payoff needs Empowered+ biases away from resting.
        if (Target.CurrentElement is Element.Fire or Element.Lightning or Element.Poison) rest -= 0.3f;

        var scores = new (Stance stance, float score)[]
        {
            (Stance.Sustain, sustain), (Stance.Overdrive, overdrive), (Stance.Rest, rest),
        };

        if (_rng.NextDouble() < MistakeChance)
        {
            _stance = scores[_rng.Next(scores.Length)].stance;
        }
        else
        {
            var best = scores.MaxBy(s => s.score);
            float current = scores.First(s => s.stance == _stance).score;
            if (best.score > current + Hysteresis) _stance = best.stance;
        }

        // Mana discipline — the honest difficulty lever now that no passive
        // multiplier decides fights: never channel into exhaustion without a
        // reason. Easy forgets this most of the time; Hard never does. This
        // gate outranks punish windows: chasing a reforming enemy on an empty
        // tank is exactly the mistake it exists to prevent.
        // Easy's flaw is exhausting itself: its per-tick chance to notice the
        // empty tank is so low it usually faceplants first, opening the punish
        // windows Hard's zeal converts.
        float discipline = Difficulty switch
        {
            Difficulty.Easy => 0.04f, // usually faceplants into exhaustion first
            Difficulty.Normal => 0.75f,
            _ => 1f,
        };
        // Recovery is for when the clash is safely away — resting while losing
        // ground trades territory for mana at a losing rate.
        bool desperate = losing > 0.3f
                         || (Opponent.IsExhausted && oppMana < 0.2f && Target.Mana > 10f);
        if (_recovering && (Target.Mana > 28f || desperate)) _recovering = false;
        if (!_recovering && Target.Mana < 16f && !desperate && _rng.NextDouble() < discipline)
            _recovering = true;
        if (_recovering) _stance = Stance.Rest;

        // Exhaust punish: the decisive window. A sputtering enemy takes full
        // overdrive push with zero resistance — spend into it. Zeal is the other
        // half of the difficulty lever: Hard never misses the window.
        float punishZeal = Difficulty switch
        {
            Difficulty.Easy => 0.3f,
            Difficulty.Normal => 0.7f,
            _ => 1f,
        };
        if (Opponent.IsExhausted && Target.Mana > 15f && _rng.NextDouble() < punishZeal)
        {
            _recovering = false;
            // Full overdrive only on a real reserve — burning the last drops on a
            // punish just trades places in the exhaust cycle.
            _stance = Target.Mana > 40f ? Stance.Overdrive : Stance.Sustain;
        }

        // A brace it can no longer afford gets dropped rather than exhausting into it.
        if (_braceHold > 0f && Target.Mana < 12f) _braceHold = 0f;

        Target.WantsChannel = _stance != Stance.Rest;
        Target.WantsOverdrive = _stance == Stance.Overdrive || _braceHold > 0f;
        if (_braceHold > 0f) Target.WantsChannel = true;

        // Easy AI often just doesn't consider switching at all.
        if (Difficulty == Difficulty.Easy && _rng.NextDouble() < 0.30) return;

        TrySwitchScored(force: false);
        TrySwitchBeamScored(losing, manaRatio, oppMana, punishWindow);
    }

    /// <summary>Scores all 8 souls against the fight state and switches if one clearly
    /// beats the current one. force = panic dodge (ignores thresholds, still pays costs).</summary>
    private void TrySwitchScored(bool force)
    {
        if (Target.GatherTimer > 0f || Target.ReformTimer > 0f || Target.ElementCooldown > 0f) return;

        float ownX = Target.Side == 0 ? Battle.ClashX : 1f - Battle.ClashX;
        float losing = (0.5f - ownX) / 0.5f;
        float manaRatio = Target.Mana / Tuning.ManaMax;
        float oppMana = Opponent.Mana / Tuning.ManaMax;
        float switchBias = Personality == AiPersonality.Trickster ? 0.2f : 0f;

        // Pip context: a lit pip makes entering somewhere valuable; an enemy
        // mid-switch is a shatter target worth spending on.
        bool oppVulnerable = Opponent.GatherTimer > 0f || Opponent.ReformTimer > 0f;
        float pipBonus = Target.PipReady ? 0.3f : -0.2f;
        // NO reactive shatter hunting: an entrance needs 0.9s of flight, so a
        // switch made after perceiving their gather lands AFTER their
        // vulnerability ends — while making US vulnerable exactly when their
        // entrance arrives. While their fire is in flight, switching is suicide.
        float incomingPenalty = Opponent.IncomingEntrancePending ? -0.6f : 0f;

        float best = force ? float.MinValue : SwitchThreshold;
        Element bestSoul = Target.CurrentElement;

        for (int i = 0; i < ElementDb.Count; i++)
        {
            var e = (Element)i;
            if (e == Target.CurrentElement) continue;
            if (Target.Mana < Target.SwitchCost(e)) continue;

            float fit = e switch
            {
                Element.Holy => 1.2f * MathF.Max(0.35f - manaRatio, 0f) * 3f
                                + 0.8f * MathF.Max(losing - 0.3f, 0f)
                                + (Target.ConductTimer > 0f || Target.OverdriveLockTimer > 0f ? 0.5f : 0f),
                Element.Water => 1.5f * MathF.Max(0.4f - manaRatio, 0f)
                                 + (_perceivedElement is Element.Fire or Element.Lightning ? 0.35f : 0f),
                Element.Ice => 0.6f * MathF.Max(-losing - 0.2f, 0f),
                Element.Darkness => 0.5f * MathF.Max(-losing - 0.2f, 0f) + 0.6f * MathF.Max(0.5f - oppMana, 0f),
                Element.Fire => 0.35f + 0.25f * manaRatio - 0.3f * MathF.Max(losing, 0f),
                Element.Lightning => 0.3f + (oppVulnerable ? 0.3f : 0f) + 0.2f * manaRatio,
                Element.Poison => 0.5f * (1f - MathF.Abs(losing)) * manaRatio,
                Element.Light => 0.25f + 0.4f * MathF.Max(1f - Target.PipCharge / Tuning.PipChargeTime - 0.3f, 0f),
                _ => 0f,
            };

            float score = fit + switchBias + incomingPenalty
                        + (Target.EntranceEligible(e) ? pipBonus : -0.3f)
                        + (e == Target.Keystone ? 0.1f : 0f);
            if (score > best) { best = score; bestSoul = e; }
        }

        // Staying has value: soul commitment (especially Poison stacks) plus hysteresis.
        float stay = force ? float.MinValue : StayBonus
            + (Target.CurrentElement == Element.Poison ? 0.05f * Target.Stacks : 0f);
        if (best > stay && bestSoul != Target.CurrentElement)
            Target.TrySwitchElement(bestSoul);
    }

    private void TrySwitchBeamScored(float losing, float manaRatio, float oppMana, bool punishWindow)
    {
        if (Target.BeamCooldown > 0f || Target.Mana < Tuning.BeamSwitchCost) return;

        float switchBias = Personality == AiPersonality.Trickster ? 0.3f : 0f;
        float twin = (punishWindow ? 1.0f : 0f)
                     + 2.0f * MathF.Max(-losing - 0.4f, 0f) * MathF.Max(0.5f - oppMana, 0f)
                     - 0.3f + switchBias + (Personality == AiPersonality.Aggressor ? 0.2f : 0f)
                     - (manaRatio < 0.35f ? 1.5f : 0f);
        // Pinpoint turns the souls down: pick it to survive an enemy soul snowball
        // (stacked Poison, dominant Darkness) or to stretch a thin mana pool.
        bool enemySnowball = (Opponent.CurrentElement == Element.Poison && Opponent.Stacks >= 4)
                             || (Opponent.CurrentElement == Element.Darkness && Opponent.DominanceTime > 2f);
        float pinpoint = (enemySnowball ? 0.8f : 0f) + (manaRatio < 0.35f ? 0.7f : 0f)
                         - 0.3f + switchBias + (Personality == AiPersonality.Miser ? 0.3f : 0f);
        float single = Target.Beam == BeamType.Twin && !punishWindow ? 0.2f : -0.3f;

        var beams = new (BeamType type, float score)[]
        {
            (BeamType.Twin, twin), (BeamType.Pinpoint, pinpoint), (BeamType.Single, single),
        };
        var bestBeam = beams.Where(b => b.type != Target.Beam).MaxBy(b => b.score);
        if (bestBeam.score > SwitchThreshold) Target.TrySwitchBeam(bestBeam.type);
    }

    /// <summary>Picks the AI's keystone soul and starting beam from its personality.</summary>
    public static Loadout Draft(Difficulty difficulty, AiPersonality personality, Random rng)
    {
        Element[] pool = personality switch
        {
            AiPersonality.Aggressor => new[] { Element.Fire, Element.Lightning, Element.Holy },
            AiPersonality.Miser => new[] { Element.Water, Element.Darkness, Element.Poison },
            _ => new[] { Element.Light, Element.Ice, Element.Water },
        };
        var beam = personality switch
        {
            AiPersonality.Aggressor => BeamType.Twin,
            AiPersonality.Miser => BeamType.Pinpoint,
            _ => BeamType.Single,
        };
        return new Loadout { Keystone = pool[rng.Next(pool.Length)], StartingBeam = beam };
    }
}
