using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KiBattle;

/// <summary>
/// Utility-scoring AI. Ticks at 4 Hz with hysteresis; perceives the opponent's
/// element/beam only after a difficulty-based reaction delay.
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
    private const float SwitchThreshold = 0.2f;

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

    private float ReactionDelay => Difficulty switch
    {
        Difficulty.Easy => 1.5f,
        Difficulty.Normal => 0.9f,
        _ => 0.5f,
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
    }

    private void Think()
    {
        // Situation, from this side's frame: ownX = 0 at own edge.
        float ownX = Target.Side == 0 ? Battle.ClashX : 1f - Battle.ClashX;
        float losing = (0.5f - ownX) / 0.5f; // 1 = clash at our door, -1 = we've almost won
        float manaRatio = Target.Mana / Tuning.ManaMax;

        bool anyPinpoint = Target.Beam == BeamType.Pinpoint || _perceivedBeam == BeamType.Pinpoint;
        float curM = ElementDb.EffectiveMultiplier(Target.CurrentElement, _perceivedElement, anyPinpoint);
        float matchupAdv = (curM - 1f) / (Tuning.Advantage - 1f); // roughly -1..1

        // --- Continuous stance ---
        // Overdrive and twin are mana-inefficient: worth it only in punish windows
        // (opponent exhausted/reforming) or to finish an opponent too poor to answer.
        float oppMana = Opponent.Mana / Tuning.ManaMax;
        bool punishWindow = Opponent.IsExhausted || Opponent.ReformTimer > 0f;

        float sustain = 0.5f + 0.3f * matchupAdv + (punishWindow ? 0.3f : 0f);
        float overdrive = (punishWindow ? 1.2f : 0f)
                          + 0.9f * MathF.Max(losing - 0.3f, 0f)
                          + 2.0f * MathF.Max(-losing - 0.4f, 0f) * MathF.Max(0.5f - oppMana, 0f)
                          + 0.2f * (manaRatio - 0.5f)
                          - (manaRatio < 0.25f ? 2.0f : 0f);
        // Rest pays only while regen exists, and never through a punish window,
        // a losing clash, or a finishable opponent.
        bool suddenDeath = Battle.FightDuration > Tuning.SuddenDeathStart;
        float rest = suddenDeath ? -1f
            : 2.2f * (0.35f - manaRatio)
              - (punishWindow ? 2.0f : 0f)
              - 1.5f * MathF.Max(losing - 0.5f, 0f)
              - 1.0f * MathF.Max(-losing - 0.4f, 0f);

        if (Personality == AiPersonality.Aggressor) overdrive += 0.3f;
        if (Personality == AiPersonality.Miser) rest += 0.3f;

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

        Target.WantsChannel = _stance != Stance.Rest;
        Target.WantsOverdrive = _stance == Stance.Overdrive;

        // Easy AI often just doesn't consider switching at all.
        if (Difficulty == Difficulty.Easy && _rng.NextDouble() < 0.30) return;

        float switchBias = Personality == AiPersonality.Trickster ? 0.3f : 0f;

        // --- Element switch ---
        if (Target.ElementCooldown <= 0f && Target.Mana >= Tuning.ElementSwitchCost)
        {
            int bestSlot = -1;
            float bestScore = SwitchThreshold;
            for (int slot = 0; slot < Target.Loadout.Elements.Length; slot++)
            {
                if (slot == Target.ElementSlot) continue;
                float newM = ElementDb.EffectiveMultiplier(Target.Loadout.Elements[slot], _perceivedElement, anyPinpoint);
                float score = 0.7f * ((newM - curM) / (Tuning.Advantage - 1f)) - 0.25f + switchBias;
                if (score > bestScore) { bestScore = score; bestSlot = slot; }
            }
            if (bestSlot >= 0) Target.TrySwitchElement(bestSlot);
        }

        // --- Beam switch ---
        if (Target.BeamCooldown <= 0f && Target.Mana >= Tuning.BeamSwitchCost)
        {
            bool countered = matchupAdv < -0.5f;
            bool hasCounterSlot = Target.Loadout.Elements
                .Where((e, i) => i != Target.ElementSlot)
                .Any(e => ElementDb.Multiplier(e, _perceivedElement) > 1f);

            float twin = (punishWindow ? 1.0f : 0f)
                         + 0.6f * MathF.Max(matchupAdv, 0f) * MathF.Max(-losing - 0.3f, 0f)
                         + 2.0f * MathF.Max(-losing - 0.4f, 0f) * MathF.Max(0.5f - oppMana, 0f)
                         - 0.3f + switchBias + (Personality == AiPersonality.Aggressor ? 0.2f : 0f)
                         - (manaRatio < 0.35f ? 1.5f : 0f);
            float pinpoint = ((countered && !hasCounterSlot) || manaRatio < 0.35f ? 0.7f : 0f)
                             - 0.3f + switchBias + (Personality == AiPersonality.Miser ? 0.3f : 0f);
            // Single is the economy home base: return to it once the punish window closes.
            float single = Target.Beam == BeamType.Twin && !punishWindow ? 0.5f - 0.3f : -0.3f;

            var beams = new (BeamType type, float score)[]
            {
                (BeamType.Twin, twin), (BeamType.Pinpoint, pinpoint), (BeamType.Single, single),
            };
            var bestBeam = beams.Where(b => b.type != Target.Beam).MaxBy(b => b.score);
            if (bestBeam.score > SwitchThreshold) Target.TrySwitchBeam(bestBeam.type);
        }
    }

    /// <summary>Drafts an AI loadout in response to the player's locked elements.</summary>
    public static Loadout Draft(Element[] playerElements, Difficulty difficulty, AiPersonality personality, Random rng)
    {
        var all = Enum.GetValues<Element>().ToList();
        var picked = new List<Element>();

        float CounterValue(Element e) => playerElements.Average(p => ElementDb.Multiplier(e, p));

        switch (difficulty)
        {
            case Difficulty.Hard:
                picked = all.OrderByDescending(CounterValue).Take(3).ToList();
                break;
            case Difficulty.Normal:
                var bestCounter = all.MaxBy(CounterValue);
                picked.Add(bestCounter);
                picked.AddRange(all.Where(e => e != bestCounter).OrderBy(_ => rng.Next()).Take(2));
                break;
            default:
                picked = all.OrderBy(_ => rng.Next()).Take(3).ToList();
                break;
        }

        var beam = personality switch
        {
            AiPersonality.Aggressor => BeamType.Twin,
            AiPersonality.Miser => BeamType.Pinpoint,
            _ => BeamType.Single,
        };
        return new Loadout { Elements = picked.ToArray(), StartingBeam = beam };
    }
}
