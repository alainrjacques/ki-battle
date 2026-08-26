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

            var picks = new[] { Element.Darkness, Element.Holy, Element.Ice };
            loadout.DebugPickAndFight(picks, BeamType.Twin, Difficulty.Hard);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var battle = main.GetNodeOrNull<BattleManager>("Battle")
                ?? throw new InvalidOperationException("Battle not started after FIGHT");

            if (!Equals(Game.Instance.PlayerLoadout.Elements[0], picks[0]) ||
                Game.Instance.PlayerLoadout.StartingBeam != BeamType.Twin)
                throw new InvalidOperationException("player loadout not applied");
            if (Game.Instance.EnemyLoadout.Elements.Length != 3)
                throw new InvalidOperationException("AI did not draft");

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
