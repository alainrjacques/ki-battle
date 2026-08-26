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
        ElementDb.ValidateTable();
        GD.Print("boot ok");
    }
}

public enum Difficulty { Easy, Normal, Hard }

public enum AiPersonality { Aggressor, Miser, Trickster }

/// <summary>A side's draft: 3 elements switchable in-fight, plus the starting beam type.</summary>
public struct Loadout
{
    public Element[] Elements;
    public BeamType StartingBeam;

    public static Loadout Default() => new()
    {
        Elements = new[] { Element.Fire, Element.Water, Element.Lightning },
        StartingBeam = BeamType.Single,
    };
}
