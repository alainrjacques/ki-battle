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

    /// <summary>`-- --ai-bench`: run the AI strength matrix instead of the smoke suite.</summary>
    public bool AiBenchMode { get; private set; }

    /// <summary>Fights per matchup in --ai-bench (`--bench-fights=N`).</summary>
    public int BenchFights = 30;

    /// <summary>`--bench-quick`: only the Hard-vs-Easy pair, for fast iteration.</summary>
    public bool BenchQuick;

    public override void _Ready()
    {
        Instance = this;
        var args = OS.GetCmdlineUserArgs();
        foreach (string a in args)
        {
            if (a == "--smoke") SmokeMode = true;
            if (a == "--smoke-flow") SmokeFlowMode = true;
            if (a == "--shots") ShotsMode = true;
            if (a == "--ai-bench") AiBenchMode = true;
            if (a == "--bench-quick") BenchQuick = true;
            if (a.StartsWith("--bench-fights=") && int.TryParse(a.Substring(15), out int bf) && bf > 0)
                BenchFights = bf;
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
