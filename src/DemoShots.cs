using System.Threading.Tasks;
using Godot;

namespace KiBattle;

/// <summary>
/// Visual verification harness (run windowed with `-- --shots`): forces a series of
/// element/beam combos on a scripted battle, saves a screenshot of each to user://,
/// then quits. Used to eyeball the shader work without playing manually.
/// </summary>
public partial class DemoShots : Node
{
    private static readonly (Element l, BeamType lb, Element r, BeamType rb)[] Combos =
    {
        (Element.Fire, BeamType.Single, Element.Water, BeamType.Single),
        (Element.Lightning, BeamType.Single, Element.Lightning, BeamType.Pinpoint),
        (Element.Lightning, BeamType.Twin, Element.Darkness, BeamType.Twin),
        (Element.Ice, BeamType.Pinpoint, Element.Poison, BeamType.Single),
        (Element.Holy, BeamType.Single, Element.Light, BeamType.Twin),
        (Element.Darkness, BeamType.Single, Element.Holy, BeamType.Pinpoint),
        (Element.Poison, BeamType.Twin, Element.Ice, BeamType.Single),
    };

    public override async void _Ready()
    {
        // Loadout screen first
        var loadout = GD.Load<PackedScene>("res://scenes/LoadoutScreen.tscn").Instantiate();
        AddChild(loadout);
        await WaitSeconds(0.4f);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_loadout.png");
        GD.Print("[shots] saved loadout screen");
        loadout.QueueFree();

        Game.Instance.PlayerLoadout = Loadout.Default();
        Game.Instance.EnemyLoadout = Loadout.Default();
        var battle = GD.Load<PackedScene>("res://scenes/Battle.tscn").Instantiate<BattleManager>();
        battle.ScriptedMode = true;
        AddChild(battle);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        battle.LeftCaster.WantsChannel = true;
        battle.RightCaster.WantsChannel = true;

        int i = 0;
        foreach (var (l, lb, r, rb) in Combos)
        {
            battle.LeftCaster.DebugSetCombo(l, lb);
            battle.RightCaster.DebugSetCombo(r, rb);
            await WaitSeconds(1.6f);
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng($"user://shot_{i}_{l}{lb}_vs_{r}{rb}.png");
            GD.Print($"[shots] saved shot {i}");
            i++;
        }
        // Power-tier contrast: left idles (base beam), right overdrives.
        battle.LeftCaster.DebugSetCombo(Element.Ice, BeamType.Single);
        battle.RightCaster.DebugSetCombo(Element.Fire, BeamType.Single);
        battle.LeftCaster.WantsChannel = false;
        battle.RightCaster.WantsOverdrive = true;
        await WaitSeconds(1.6f);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_tiers_idle_vs_overdrive.png");
        GD.Print("[shots] saved tier contrast");

        GD.Print("[shots] done");
        GetTree().Quit(0);
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}
