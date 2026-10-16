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
    public float BraceGrantRemaining => _braceGrant;

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
