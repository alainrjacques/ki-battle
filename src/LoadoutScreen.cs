using System;
using Godot;

namespace KiBattle;

/// <summary>
/// Pre-battle draft screen. Stub for now: uses the default loadout and a
/// counter-drafting AI, with a single button to start the fight.
/// The full 8-element pick UI arrives in the flow milestone.
/// </summary>
public partial class LoadoutScreen : Control
{
    public override void _Ready()
    {
        var button = GetNode<Button>("Center/Fight");
        button.Pressed += StartFight;
        button.GrabFocus();
    }

    private void StartFight()
    {
        var game = Game.Instance;
        game.PlayerLoadout = Loadout.Default();
        game.EnemyLoadout = AIController.Draft(
            game.PlayerLoadout.Elements, game.Difficulty, game.Personality, new Random());
        GetParent<Main>().StartBattle();
    }
}
