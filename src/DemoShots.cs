using System;
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
        // hover state from the active-slot treatment (AC18). HoverSlot asserts the hover
        // registered; AssertBracketCaptured then reads it back out of the saved pixels.
        var hud = battle.GetNode<Hud>("Hud");
        await HoverSlot(hud, 4); // soul slot 5
        await WaitSeconds(0.3f);
        var idleImg = GetViewport().GetTexture().GetImage();
        idleImg.SavePng("user://shot_cue_idle.png");
        GD.Print("[shots] saved cue idle (no window open)");
        AssertBracketCaptured(idleImg, 4, "shot_cue_idle.png");

        bool cueSwitch = battle.RightCaster.TrySwitchElement(Element.Water);
        GD.Print($"[shots] cue switch accepted={cueSwitch}");
        await WaitSeconds(0.14f); // inside their 0.25s gather: the shatter window
        GetViewport().GetTexture().GetImage().SavePng("user://shot_cue_shatter.png");
        GD.Print($"[shots] saved cue shatter (enemyGather={battle.RightCaster.GatherTimer:0.00})");

        await WaitSeconds(0.3f); // reform: entrance in flight, so the cue exists to hover
        await HoverBraceCue(hud);
        var braceImg = GetViewport().GetTexture().GetImage();
        braceImg.SavePng("user://shot_cue_brace.png");
        GD.Print($"[shots] saved cue brace (incoming={battle.RightCaster.IncomingEntrancePending} "
                 + $"cueWidthPx={MeasureCueWidth(braceImg, hud):0.0})");

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

        if (_hoverFailures == 0)
        {
            GD.Print("[shots] done");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"[shots] SHOTS FAIL: {_hoverFailures} hover assertion(s) failed - the "
                        + "cue PNGs do not contain the treatment they are supposed to prove");
            GetTree().Quit(1);
        }
    }

    private int _hoverFailures;

    private Task HoverSlot(Hud hud, int slot) => Hover(hud, Hud.SlotCentre(slot),
        $"soul slot {slot + 1}", () => hud.HoveredSlot == slot, () => hud.DebugSetHover(slot, false));

    private Task HoverBraceCue(Hud hud) => Hover(hud, hud.BraceCueCentre,
        "BRACE cue", () => hud.HoveredBraceCue, () => hud.DebugSetHover(-1, true));

    /// <summary>Puts the pointer on a target and PROVES the hover registered before the caller
    /// saves a PNG. The real path goes first (warp the OS cursor, then a synthesised motion
    /// event) so the capture exercises the same code a player does - but Input.WarpMouse is a
    /// silent no-op on an unfocused window, which is how five cue PNGs were saved with no hover
    /// treatment in any of them while the harness reported success (X-3-2). So: try, verify,
    /// fall back to the HUD's seam and say so, verify again, and if neither path produces a
    /// hover, record a failure that makes the whole run exit non-zero.</summary>
    private async Task Hover(Hud hud, Vector2 canvasPos, string what, Func<bool> registered, Action force)
    {
        WarpTo(canvasPos);
        await WaitSeconds(0.15f);
        if (registered())
        {
            GD.Print($"[shots] hover {what}: registered via OS input at canvas {canvasPos}");
            return;
        }
        GD.Print($"[shots] hover {what}: WARN warp + synthesised motion did not register "
                 + $"(windowFocused={DisplayServer.WindowIsFocused()}); using the HUD seam");
        force();
        await WaitSeconds(0.15f);
        if (registered())
        {
            GD.Print($"[shots] hover {what}: registered via the HUD seam");
            return;
        }
        _hoverFailures++;
        GD.PrintErr($"[shots] FAIL hover {what} did not register by either path");
    }

    /// <summary>The window is 1280x720 against a 1920x1080 canvas (stretch keep), so window
    /// pixels are canvas pixels times the width ratio. WarpMouse takes window pixels.</summary>
    private void WarpTo(Vector2 canvasPos)
    {
        DisplayServer.WindowMoveToForeground();
        float scale = GetWindow().Size.X / 1920f;
        var p = canvasPos * scale;
        Input.WarpMouse(p);
        // A warp alone does not always emit motion; synthesise one so the Control actually
        // enters its hover state. Pushed at the window so it goes through the same GUI pick
        // a real event does, rather than only updating the Input singleton.
        GetWindow().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
    }

    /// <summary>Reads the hover bracket back out of the captured pixels. The state assert above
    /// proves the HUD believes it is hovering; this proves the treatment reached the PNG, which
    /// is the claim AC18 actually makes and the one that was false for three rounds.</summary>
    private void AssertBracketCaptured(Image img, int slot, string shot)
    {
        var bar = Hud.SlotHoverBarRect(slot);
        float s = img.GetWidth() / 1920f;
        int x0 = Mathf.Max(0, (int)(bar.Position.X * s));
        int x1 = Mathf.Min(img.GetWidth() - 1, (int)((bar.Position.X + bar.Size.X) * s));
        int y0 = Mathf.Max(0, (int)(bar.Position.Y * s) - 1);
        int y1 = Mathf.Min(img.GetHeight() - 1, (int)((bar.Position.Y + bar.Size.Y) * s) + 1);
        int longest = 0;
        for (int y = y0; y <= y1; y++)
        {
            int run = 0;
            for (int x = x0; x <= x1; x++)
            {
                var px = img.GetPixel(x, y);
                run = px.R > 0.75f && px.G > 0.75f && px.B > 0.75f ? run + 1 : 0;
                longest = Mathf.Max(longest, run);
            }
        }
        // The bar is SlotHitW-12 = 66 canvas px wide, i.e. 44 px at 720p. 5 px is a floor no
        // antialiased label stroke in that band reaches, and QA measured 0 with no hover.
        const int floorPx = 5;
        if (longest >= floorPx)
        {
            GD.Print($"[shots] bracket in {shot}: longest white run {longest}px in "
                     + $"x[{x0}..{x1}] y[{y0}..{y1}] (floor {floorPx})");
            return;
        }
        _hoverFailures++;
        GD.PrintErr($"[shots] FAIL no hover bracket in {shot}: longest white run {longest}px in "
                    + $"x[{x0}..{x1}] y[{y0}..{y1}], floor {floorPx}");
    }

    /// <summary>Width in screen pixels of the BRACE cue's orange border along its centre row.
    /// Printed, not asserted: this is the number that showed the cue sitting at scale 1.0 in
    /// both the hovered and the unhovered shot (240 * 0.667 = 160 px), so it is worth keeping
    /// on the record next to the hover state.</summary>
    private static float MeasureCueWidth(Image img, Hud hud)
    {
        float s = img.GetWidth() / 1920f;
        int y = Mathf.Clamp((int)(hud.BraceCueCentre.Y * s), 0, img.GetHeight() - 1);
        int first = -1, last = -1;
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var px = img.GetPixel(x, y);
            if (px.R <= 0.45f || px.R <= px.B * 1.4f) continue;
            if (first < 0) first = x;
            last = x;
        }
        return first < 0 ? 0f : last - first + 1;
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}
