using Godot;

namespace KiBattle;

/// <summary>Translates input actions into Caster API calls each frame.</summary>
public partial class PlayerController : Node
{
    public Caster Target = null!;

    public override void _Process(double delta)
    {
        Target.WantsChannel = Input.IsActionPressed("channel");
        Target.WantsOverdrive = Input.IsActionPressed("overdrive");

        for (int i = 0; i < ElementDb.Count; i++)
        {
            if (Input.IsActionJustPressed($"element_{i + 1}"))
                Target.TrySwitchElement((Element)i);
        }

        if (Input.IsActionJustPressed("beam_single")) Target.TrySwitchBeam(BeamType.Single);
        if (Input.IsActionJustPressed("beam_twin")) Target.TrySwitchBeam(BeamType.Twin);
        if (Input.IsActionJustPressed("beam_pinpoint")) Target.TrySwitchBeam(BeamType.Pinpoint);
    }
}
