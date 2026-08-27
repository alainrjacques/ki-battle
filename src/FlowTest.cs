using System;
using Godot;

namespace KiBattle;

/// <summary>
/// Headless flow verification (`-- --smoke-flow`): walks the loadout screen
/// programmatically, checks the battle starts with the chosen draft, prints
/// SMOKE-FLOW OK and exits.
/// </summary>
public partial class FlowTest : Node
{
    public override async void _Ready()
    {
        try
        {
            Engine.TimeScale = 8.0;
            var main = GetTree().Root.GetNode<Main>("Main");

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var loadout = main.GetNodeOrNull<LoadoutScreen>("LoadoutScreen")
                ?? throw new InvalidOperationException("LoadoutScreen not shown");

            loadout.DebugPickAndFight(Element.Darkness, BeamType.Twin, Difficulty.Hard);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var battle = main.GetNodeOrNull<BattleManager>("Battle")
                ?? throw new InvalidOperationException("Battle not started after FIGHT");

            if (Game.Instance.PlayerLoadout.Keystone != Element.Darkness ||
                Game.Instance.PlayerLoadout.StartingBeam != BeamType.Twin)
                throw new InvalidOperationException("player loadout not applied");
            if (battle.LeftCaster.CurrentElement != Element.Darkness)
                throw new InvalidOperationException("keystone is not the starting element");

            while (battle.State == BattleState.Intro)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (battle.State != BattleState.Fighting)
                throw new InvalidOperationException($"battle in state {battle.State}, expected Fighting");

            GD.Print("SMOKE-FLOW OK");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"SMOKE-FLOW FAIL: {e.Message}");
            GetTree().Quit(1);
        }
    }
}
