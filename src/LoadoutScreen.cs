using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KiBattle;

/// <summary>
/// Pre-battle SOUL SELECT: pick one of 8 souls as your keystone (starting element,
/// free returns home), a starting beam, and AI difficulty. All 8 souls are live
/// in-fight on keys 1-8; each button doubles as the teaching surface for its soul.
/// </summary>
public partial class LoadoutScreen : Control
{
    private Element? _keystone;
    private BeamType _beam = BeamType.Single;
    private Difficulty _difficulty = Difficulty.Normal;

    private readonly Dictionary<Element, Button> _elementButtons = new();
    private Button[] _beamButtons = null!;
    private Button[] _diffButtons = null!;
    private Button _fight = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 26);
        center.AddChild(box);

        var title = MakeLabel("KI BATTLE", 72, new Color(0.8f, 0.9f, 1f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        box.AddChild(MakeLabel("Choose your keystone soul — all 8 are yours in battle (keys 1-8), home is free", 24, new Color(1, 1, 1, 0.7f)));

        // Soul grid, 4 x 2; every button teaches its soul's passive and entrance.
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        box.AddChild(grid);

        foreach (Element e in Enum.GetValues<Element>())
        {
            var style = ElementDb.Style(e);
            var soul = ElementDb.Soul(e);
            var b = new Button
            {
                ToggleMode = true,
                CustomMinimumSize = new Vector2(260, 104),
            };
            b.Modulate = style.Glow.Lightened(0.25f);
            b.Toggled += pressed => OnKeystoneToggled(e, pressed);
            grid.AddChild(b);
            _elementButtons[e] = b;

            var stack = new VBoxContainer();
            stack.SetAnchorsPreset(LayoutPreset.FullRect);
            stack.OffsetLeft = 12; stack.OffsetTop = 6;
            stack.MouseFilter = MouseFilterEnum.Ignore;
            b.AddChild(stack);
            var name = MakeLabel($"{style.DisplayName} — {soul.PassiveName}", 22, Colors.White);
            var teach1 = MakeLabel(soul.TeachPassive, 14, new Color(1, 1, 1, 0.75f));
            var teach2 = MakeLabel($"{soul.EntranceName}: {soul.TeachEntrance}", 14, new Color(1, 1, 1, 0.6f));
            stack.AddChild(name);
            stack.AddChild(teach1);
            stack.AddChild(teach2);
        }

        // Beam selector
        box.AddChild(MakeLabel("Starting beam", 24, new Color(1, 1, 1, 0.7f)));
        var beamRow = new HBoxContainer();
        beamRow.AddThemeConstantOverride("separation", 14);
        box.AddChild(beamRow);
        _beamButtons = new[] { "Single", "Twin", "Pinpoint" }
            .Select((name, i) =>
            {
                var b = new Button { Text = name, ToggleMode = true, CustomMinimumSize = new Vector2(150, 48) };
                b.Toggled += on => { if (on) SelectBeam((BeamType)i); };
                beamRow.AddChild(b);
                return b;
            }).ToArray();
        _beamButtons[0].ButtonPressed = true;

        // Difficulty selector
        box.AddChild(MakeLabel("Opponent", 24, new Color(1, 1, 1, 0.7f)));
        var diffRow = new HBoxContainer();
        diffRow.AddThemeConstantOverride("separation", 14);
        box.AddChild(diffRow);
        _diffButtons = new[] { "Easy", "Normal", "Hard" }
            .Select((name, i) =>
            {
                var b = new Button { Text = name, ToggleMode = true, CustomMinimumSize = new Vector2(150, 48) };
                b.Toggled += on => { if (on) SelectDifficulty((Difficulty)i); };
                diffRow.AddChild(b);
                return b;
            }).ToArray();
        _diffButtons[1].ButtonPressed = true;

        _fight = new Button { Text = "FIGHT", Disabled = true, CustomMinimumSize = new Vector2(260, 70) };
        _fight.AddThemeFontSizeOverride("font_size", 36);
        _fight.Pressed += StartFight;
        box.AddChild(_fight);
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private void OnKeystoneToggled(Element e, bool pressed)
    {
        if (pressed)
        {
            _keystone = e;
            foreach (var (other, button) in _elementButtons)
                if (other != e) button.SetPressedNoSignal(false);
        }
        else if (_keystone == e)
        {
            _keystone = null;
        }
        _fight.Disabled = _keystone == null;
    }

    private void SelectBeam(BeamType t)
    {
        _beam = t;
        for (int i = 0; i < 3; i++) _beamButtons[i].SetPressedNoSignal(i == (int)t);
    }

    private void SelectDifficulty(Difficulty d)
    {
        _difficulty = d;
        for (int i = 0; i < 3; i++) _diffButtons[i].SetPressedNoSignal(i == (int)d);
    }

    private void StartFight()
    {
        if (_keystone == null) return;
        var game = Game.Instance;
        var rng = new Random();
        game.Difficulty = _difficulty;
        game.Personality = (AiPersonality)rng.Next(3);
        game.PlayerLoadout = new Loadout { Keystone = _keystone.Value, StartingBeam = _beam };
        game.EnemyLoadout = AIController.Draft(_difficulty, game.Personality, rng);
        GetParent<Main>().StartBattle();
    }

    /// <summary>Flow-test hook: make the same choices a player would, then fight.</summary>
    public void DebugPickAndFight(Element keystone, BeamType beam, Difficulty difficulty)
    {
        OnKeystoneToggled(keystone, true);
        SelectBeam(beam);
        SelectDifficulty(difficulty);
        StartFight();
    }
}
