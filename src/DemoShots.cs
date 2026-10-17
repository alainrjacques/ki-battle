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
        Game.Instance.Difficulty = Difficulty.Easy; // coach hints visible in the shots
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
        // Timing bar: fresh state, catch the enemy mid element-switch with the
        // cursor sweeping between the shatter and brace zones.
        battle.ResetBattle();
        battle.LeftCaster.WantsChannel = true;
        battle.RightCaster.WantsChannel = true;
        await WaitSeconds(0.4f);
        bool switched = battle.RightCaster.TrySwitchElement(Element.Ice);
        GD.Print($"[shots] timing switch accepted={switched}");
        await WaitSeconds(0.5f);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_timing_bar.png");
        GD.Print("[shots] saved timing bar");

        // Power-tier contrast: left idles (base beam), right overdrives.
        battle.LeftCaster.DebugSetCombo(Element.Ice, BeamType.Single);
        battle.RightCaster.DebugSetCombo(Element.Fire, BeamType.Single);
        battle.LeftCaster.WantsChannel = false;
        battle.RightCaster.WantsOverdrive = true;
        await WaitSeconds(1.6f);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_tiers_idle_vs_overdrive.png");
        GD.Print("[shots] saved tier contrast");

        // --- Mouse cue layer (AC22). ScriptedMode means a live HUD with NO
        // PlayerController, so a clean run here is also the proof of the
        // null-guards (AC17). Three states: no window open, enemy mid-gather
        // (SHATTER marking), enemy entrance in flight (BRACE cue).
        battle.ResetBattle();
        battle.LeftCaster.DebugSetCombo(Element.Holy, BeamType.Single);
        battle.RightCaster.DebugSetCombo(Element.Ice, BeamType.Single);
        battle.LeftCaster.WantsChannel = true;
        battle.RightCaster.WantsChannel = true;
        battle.RightCaster.WantsOverdrive = false;
        await WaitSeconds(0.6f);

        // Hover a soul slot that is NOT the active one, so the screenshot separates the
        // hover state from the active-slot treatment (AC18).
        HoverAt(new Vector2(72 + 4 * 78 + 26, 1080 - 142)); // hover soul slot 5
        await WaitSeconds(0.3f);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_cue_idle.png");
        GD.Print("[shots] saved cue idle (no window open)");

        bool cueSwitch = battle.RightCaster.TrySwitchElement(Element.Water);
        GD.Print($"[shots] cue switch accepted={cueSwitch}");
        await WaitSeconds(0.14f); // inside their 0.25s gather: the shatter window
        GetViewport().GetTexture().GetImage().SavePng("user://shot_cue_shatter.png");
        GD.Print($"[shots] saved cue shatter (enemyGather={battle.RightCaster.GatherTimer:0.00})");

        HoverAt(new Vector2(960, 679)); // hover the BRACE cue itself
        await WaitSeconds(0.3f); // reform: entrance in flight
        GetViewport().GetTexture().GetImage().SavePng("user://shot_cue_brace.png");
        GD.Print($"[shots] saved cue brace (incoming={battle.RightCaster.IncomingEntrancePending})");

        // --- BRACE label states (X-2-6). These only exist when a PlayerController is
        // present, so attach one to the scripted battle and rebind the HUD: the whole
        // X-1-1/X-2-1 fix (BRACED / TOO EARLY / NO BRACE, blue vs amber drain bar) had
        // no screenshot coverage at all, and this project signs off visuals by looking.
        await WaitSeconds(2.8f); // previous entrance resolves + the 3 s switch cooldown clears
        var pc = new PlayerController { Target = battle.LeftCaster };
        battle.AddChild(pc);
        battle.Player = pc;
        battle.GetNode<Hud>("Hud").RebindController();
        pc.ToggleChannelLatch(); // keeps the left beam channelling, as the shots above did
        battle.LeftCaster.DebugSetCombo(Element.Holy, BeamType.Single);

        bool tooEarlySwitch = battle.RightCaster.TrySwitchElement(Element.Lightning);
        pc.GrantBrace(); // clicked the instant they press: 0.8 s grant, 0.90 s flight
        await WaitSeconds(0.12f);
        float etaEarly = PlayerController.ImpactEta(battle.RightCaster);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_cue_brace_too_early.png");
        GD.Print($"[shots] saved cue brace TOO EARLY (switch={tooEarlySwitch} eta={etaEarly:0.00} "
                 + $"predict={pc.PredictBrace(etaEarly)})");

        // Same entrance, re-clicked once the flight is short enough for the grant.
        await WaitSeconds(0.25f);
        pc.GrantBrace();
        await WaitSeconds(0.08f);
        float etaBraced = PlayerController.ImpactEta(battle.RightCaster);
        GetViewport().GetTexture().GetImage().SavePng("user://shot_cue_braced.png");
        GD.Print($"[shots] saved cue BRACED (eta={etaBraced:0.00} predict={pc.PredictBrace(etaBraced)})");

        GD.Print("[shots] done");
        GetTree().Quit(0);
    }

    /// <summary>Moves the OS cursor over a viewport-space point so the screenshot
    /// captures a hover state (AC18). The window is 1280x720 against a 1920x1080
    /// viewport, and Input.WarpMouse takes window pixels.</summary>
    private void HoverAt(Vector2 viewportPos)
    {
        float scale = GetWindow().Size.X / 1920f;
        var p = viewportPos * scale;
        Input.WarpMouse(p);
        // A warp alone does not always emit motion; synthesise one so the Control
        // actually enters its hover state before the capture.
        Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}
