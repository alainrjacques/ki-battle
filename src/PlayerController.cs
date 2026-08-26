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

        if (Input.IsActionJustPressed("element_1")) Target.TrySwitchElement(0);
        if (Input.IsActionJustPressed("element_2")) Target.TrySwitchElement(1);
        if (Input.IsActionJustPressed("element_3")) Target.TrySwitchElement(2);

        if (Input.IsActionJustPressed("beam_single")) Target.TrySwitchBeam(BeamType.Single);
        if (Input.IsActionJustPressed("beam_twin")) Target.TrySwitchBeam(BeamType.Twin);
        if (Input.IsActionJustPressed("beam_pinpoint")) Target.TrySwitchBeam(BeamType.Pinpoint);
    }
}
