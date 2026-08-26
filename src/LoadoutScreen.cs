using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KiBattle;

/// <summary>
/// Pre-battle draft: pick 3 of 8 elements (order = slots 1/2/3), a starting beam,
/// and AI difficulty. The AI counter-drafts on FIGHT. UI is built in code.
/// </summary>
public partial class LoadoutScreen : Control
{
    private readonly List<Element> _picked = new();
    private BeamType _beam = BeamType.Single;
    private Difficulty _difficulty = Difficulty.Normal;

    private readonly Dictionary<Element, Button> _elementButtons = new();
    private readonly Dictionary<Element, Label> _orderLabels = new();
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

        box.AddChild(MakeLabel("Draft 3 elements — order becomes keys 1 / 2 / 3", 24, new Color(1, 1, 1, 0.7f)));

        // Element grid, 4 x 2
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        box.AddChild(grid);

        foreach (Element e in Enum.GetValues<Element>())
        {
            var style = ElementDb.Style(e);
            var b = new Button
            {
                Text = style.DisplayName,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(190, 64),
            };
            b.AddThemeFontSizeOverride("font_size", 26);
            b.Modulate = style.Glow.Lightened(0.25f);
            b.Toggled += pressed => OnElementToggled(e, pressed);
            grid.AddChild(b);
            _elementButtons[e] = b;

            var order = MakeLabel("", 20, Colors.White);
            order.Position = new Vector2(8, 2);
            b.AddChild(order);
            _orderLabels[e] = order;
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

    private void OnElementToggled(Element e, bool pressed)
    {
        if (pressed)
        {
            if (_picked.Count >= 3)
            {
                _elementButtons[e].SetPressedNoSignal(false);
                return;
            }
            _picked.Add(e);
        }
        else
        {
            _picked.Remove(e);
        }
        RefreshOrderLabels();
        _fight.Disabled = _picked.Count != 3;
    }

    private void RefreshOrderLabels()
    {
        foreach (var (e, label) in _orderLabels)
        {
            int idx = _picked.IndexOf(e);
            label.Text = idx >= 0 ? $"{idx + 1}" : "";
        }
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
        if (_picked.Count != 3) return;
        var game = Game.Instance;
        var rng = new Random();
        game.Difficulty = _difficulty;
        game.Personality = (AiPersonality)rng.Next(3);
        game.PlayerLoadout = new Loadout { Elements = _picked.ToArray(), StartingBeam = _beam };
        game.EnemyLoadout = AIController.Draft(game.PlayerLoadout.Elements, _difficulty, game.Personality, rng);
        GetParent<Main>().StartBattle();
    }

    /// <summary>Flow-test hook: make the same choices a player would, then fight.</summary>
    public void DebugPickAndFight(Element[] elements, BeamType beam, Difficulty difficulty)
    {
        foreach (var e in elements) OnElementToggled(e, true);
        SelectBeam(beam);
        SelectDifficulty(difficulty);
        StartFight();
    }
}
