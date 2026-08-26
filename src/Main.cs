using Godot;

namespace KiBattle;

/// <summary>Root scene: swaps between LoadoutScreen and Battle.</summary>
public partial class Main : Node
{
    private Node? _current;

    public override void _Ready()
    {
        if (Game.Instance.SmokeMode)
        {
            // Headless verification: straight into an AI-vs-AI battle.
            var smoke = new SmokeTest();
            AddChild(smoke);
            return;
        }
        ShowLoadout();
    }

    public void ShowLoadout()
    {
        SwapTo(GD.Load<PackedScene>("res://scenes/LoadoutScreen.tscn").Instantiate());
    }

    public void StartBattle()
    {
        SwapTo(GD.Load<PackedScene>("res://scenes/Battle.tscn").Instantiate());
    }

    private void SwapTo(Node scene)
    {
        _current?.QueueFree();
        _current = scene;
        AddChild(scene);
    }
}
