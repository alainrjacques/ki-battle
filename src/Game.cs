using Godot;

namespace KiBattle;

/// <summary>Autoload singleton: cross-scene state (loadouts, difficulty) and scene switching.</summary>
public partial class Game : Node
{
    public static Game Instance { get; private set; } = null!;

    public Loadout PlayerLoadout = Loadout.Default();
    public Loadout EnemyLoadout = Loadout.Default();
    public Difficulty Difficulty = Difficulty.Normal;
    public AiPersonality Personality = AiPersonality.Aggressor;

    public bool SmokeMode { get; private set; }
    public bool SmokeFlowMode { get; private set; }
    public bool ShotsMode { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        var args = OS.GetCmdlineUserArgs();
        foreach (string a in args)
        {
            if (a == "--smoke") SmokeMode = true;
            if (a == "--smoke-flow") SmokeFlowMode = true;
            if (a == "--shots") ShotsMode = true;
        }
        ElementDb.ValidateIdentities();
        GD.Print("boot ok");
    }
}

public enum Difficulty { Easy, Normal, Hard }

public enum AiPersonality { Aggressor, Miser, Trickster }

/// <summary>A side's pre-fight choice: the keystone soul (starting element, free
/// returns) and the starting beam type. All 8 elements are live in-fight.</summary>
public struct Loadout
{
    public Element Keystone;
    public BeamType StartingBeam;

    public static Loadout Default() => new()
    {
        Keystone = Element.Fire,
        StartingBeam = BeamType.Single,
    };
}
