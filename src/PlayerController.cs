using Godot;

namespace KiBattle;

/// <summary>
/// Translates input actions into Caster API calls each frame, and owns the mouse
/// layer's sustained-input contract: the CHANNEL latch, the arena hold-to-channel
/// flag, and the timed BRACE grant. Keyboard and mouse are OR'd — no key loses
/// function (D1/C2) and the mouse never binds the `channel`/`overdrive` actions,
/// so a click on a HUD cue can never leak into beam power (C2/D2.3).
/// </summary>
public partial class PlayerController : Node
{
    /// <summary>Channel+overdrive granted by one click of the BRACE cue (C4).
    /// Deliberately shorter than the 0.90 s entrance flight: a click fired too
    /// early expires before impact, so timing stays the skill. Lives with the
    /// input layer, not in the balance table (AC24).</summary>
    public const float BraceGrantDuration = 0.8f;

    public Caster Target = null!;

    /// <summary>Toggled by the HUD's CHANNEL pad; sustains WantsChannel with no key held.</summary>
    public bool ChannelLatch { get; private set; }

    /// <summary>Set while LMB is held on the arena background (HUD's arena Control).</summary>
    public bool ArenaHold;

    private float _braceGrant;
    private bool _keyChannel, _keyOverdrive;

    public bool BraceGrantActive => _braceGrant > 0f;

    /// <summary>Sim-seconds left on the running grant. Read by the HUD so the BRACE
    /// cue can tell the truth about whether the grant still reaches the pending
    /// impact (X-1-1).</summary>
    public float BraceGrantRemaining => _braceGrant;

    /// <summary>Sim-seconds until the thing a brace would soften actually lands: the
    /// enemy's pending entrance (gather left + the full reform) or, failing that, a
    /// Fire flare that is winding up. Negative when nothing is incoming. A pure
    /// function of the enemy's public timers, so it is machine-checkable without a
    /// HUD (see SmokeTest.BraceGrantCheck).</summary>
    public static float ImpactEta(Caster enemy)
    {
        if (enemy == null) return -1f;
        if (enemy.IncomingEntrancePending)
            return enemy.GatherTimer > 0f
                ? enemy.GatherTimer + Tuning.ElementReformDuration
                : enemy.ReformTimer;
        if (enemy.CurrentElement == Element.Fire && enemy.IsEmpowered && enemy.FlareActiveTimer <= 0f
            && enemy.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup)
            return Tuning.FlarePeriod - enemy.FlareClock;
        return -1f;
    }

    /// <summary>What the BRACE cue is allowed to say. One predicate, one truth: the HUD
    /// label is a pure function of this, so "BRACED" can never disagree with what the
    /// impact is about to do (X-2-1 / X-2-2).</summary>
    public enum BracePrediction
    {
        /// <summary>No overdrive source running: the resting "BRACE" prompt.</summary>
        None,
        /// <summary>A grant is live but expires before the hit lands (C4/AC14).</summary>
        TooEarly,
        /// <summary>Overdrive-locked, exhausted, or out of mana: a brace is impossible
        /// no matter how well the click is timed (AC15).</summary>
        Blocked,
        /// <summary>The caster will really be in PowerTier.Overdrive at impact.</summary>
        Braced,
    }

    /// <summary>One physics step of slack, in sim seconds, so a prediction never rides
    /// an exact tie whose outcome depends on node order inside the frame (F-2-1/X-2-7).</summary>
    private static float PredictMargin =>
        (float)(Engine.TimeScale / Mathf.Max(1, Engine.PhysicsTicksPerSecond));

    /// <summary>Answers the only question the cue actually cares about: will this caster
    /// be in <see cref="PowerTier.Overdrive"/> when the incoming hit resolves in
    /// `impactEta` sim-seconds? That is what BattleManager reads (`victim.IsOverdriving`),
    /// so every precondition Caster.Tier has must be projected forward, not just the
    /// grant clock: the overdrive lock (which bracing a Water entrance *gives* you for
    /// 2 s, longer than the next flight), the exhaust timer, and mana - the forced
    /// overdrive drain of the grant itself is what empties the bar on a low-mana click.
    /// Held SPACE+SHIFT counts as a source exactly like the grant, so the keyboard and
    /// mixed-input player is described truthfully too (X-2-2).
    /// Pure function of public timers, so --smoke can check it against the real impact.</summary>
    public BracePrediction PredictBrace(float impactEta)
    {
        if (Target == null || impactEta < 0f) return BracePrediction.None;

        float margin = PredictMargin;
        // A held key is assumed still held at impact - that is the player's own doing.
        bool heldSource = _keyChannel && _keyOverdrive;
        bool grantSource = _braceGrant > impactEta + margin;

        // Seconds of overdrive demanded before the hit: the whole flight when the keys
        // are held, otherwise whatever the grant covers - a live one, or the fresh 0.8 s
        // grant a click right now would start (so the cue can warn before the click).
        float grantWindow = _braceGrant > 0f ? _braceGrant : BraceGrantDuration;
        float odSeconds = Mathf.Min(heldSource ? impactEta : grantWindow, impactEta);

        if (Target.OverdriveLockTimer > 0f && Target.OverdriveLockTimer + margin > impactEta)
            return BracePrediction.Blocked;
        if (Target.ExhaustTimer > 0f && Target.ExhaustTimer + margin > impactEta)
            return BracePrediction.Blocked;

        // Mana projection. No drain while exhausted (Tier is Idle then, Caster.Tick), and
        // net flow is monotonically negative under overdrive, so testing the end point is
        // testing the minimum. regenScale (0.5 in sudden death) is ignored: that makes the
        // projection optimistic by at most ~2.5 mana over a full grant.
        float exhaustLeft = Mathf.Min(Target.ExhaustTimer, odSeconds);
        float mana = Mathf.Min(Tuning.ManaMax, Target.Mana + Tuning.ManaRegenExhausted * exhaustLeft);
        mana += (RegenPerSecond() - OverdriveDrainPerSecond()) * Mathf.Max(0f, odSeconds - exhaustLeft);
        if (mana <= 0f) return BracePrediction.Blocked;

        if (heldSource || grantSource) return BracePrediction.Braced;
        return _braceGrant > 0f ? BracePrediction.TooEarly : BracePrediction.None;
    }

    /// <summary>The one boolean the BRACE label and --smoke share: braced at impact.</summary>
    public bool WillBeBracedAt(float impactEta) => PredictBrace(impactEta) == BracePrediction.Braced;

    private float OverdriveDrainPerSecond()
    {
        float d = Tuning.BeamDrain[(int)Target.Beam] * Tuning.OverdriveDrainMult;
        if (Target.CurrentElement == Element.Water) d *= Tuning.FlowDrainMult;
        if (Target.ConductTimer > 0f) d *= Tuning.ConductDrainMult;
        return d;
    }

    private float RegenPerSecond() =>
        Tuning.ManaRegen + (Target.CurrentElement == Element.Holy ? Tuning.HolyRegen : 0f);

    public void ToggleChannelLatch() => ChannelLatch = !ChannelLatch;

    /// <summary>Starts (or restarts) the 0.8 s channel+overdrive grant from a BRACE click.</summary>
    public void GrantBrace() => _braceGrant = BraceGrantDuration;

    /// <summary>Clears every sustained input. Called on ResetBattle so a rematch
    /// never starts mid-channel or mid-grant (AC9).</summary>
    public void ResetInputState()
    {
        ChannelLatch = false;
        ArenaHold = false;
        _braceGrant = 0f;
        _keyChannel = _keyOverdrive = false;
        if (Target != null) Target.WantsChannel = Target.WantsOverdrive = false;
    }

    public override void _Process(double delta)
    {
        // Key polling stays on the render frame, exactly as before: just-pressed
        // edges are frame-accurate there (AC3).
        _keyChannel = Input.IsActionPressed("channel");
        _keyOverdrive = Input.IsActionPressed("overdrive");
        Apply();

        for (int i = 0; i < ElementDb.Count; i++)
        {
            if (Input.IsActionJustPressed($"element_{i + 1}"))
                Target.TrySwitchElement((Element)i);
        }

        if (Input.IsActionJustPressed("beam_single")) Target.TrySwitchBeam(BeamType.Single);
        if (Input.IsActionJustPressed("beam_twin")) Target.TrySwitchBeam(BeamType.Twin);
        if (Input.IsActionJustPressed("beam_pinpoint")) Target.TrySwitchBeam(BeamType.Pinpoint);
    }

    /// <summary>The grant is a gameplay timer, so it burns down in sim time next to
    /// Caster.Tick rather than on the render frame — a 0.8 s grant must expire
    /// before a 0.90 s entrance flight regardless of frame rate or TimeScale (C4).</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_braceGrant > 0f) _braceGrant = Mathf.Max(0f, _braceGrant - (float)delta);
        Apply();
    }

    private void Apply()
    {
        // Safety net for a release delivered outside the arena Control: raw state
        // can only ever *clear* the hold here, never set it, so no click leaks in.
        if (ArenaHold && !Input.IsMouseButtonPressed(MouseButton.Left)) ArenaHold = false;

        bool grant = _braceGrant > 0f;
        Target.WantsChannel = _keyChannel || ChannelLatch || ArenaHold || grant;
        Target.WantsOverdrive = _keyOverdrive || grant;
    }
}
