using Godot;

namespace KiBattle;

/// <summary>
/// Battle HUD: clash meter, mana bars, loadout strip with cooldown overlays,
/// enemy telegraph card with matchup arrow, intro banner and result panel.
/// Everything is built in code; reads state from the parent BattleManager.
/// </summary>
public partial class Hud : CanvasLayer
{
    private BattleManager _battle = null!;
    private Caster _player = null!;
    private Caster _enemy = null!;

    // Clash meter
    private ColorRect _meterFillL = null!, _meterFillR = null!, _marker = null!;
    private const float MeterW = 900f, MeterH = 26f;

    // Mana bars
    private ColorRect _manaFillL = null!, _manaFillR = null!;
    private Label _exhaustL = null!, _exhaustR = null!;
    private const float ManaW = 420f, ManaH = 20f;

    // Soul strip (all 8 elements, keys 1-8)
    private readonly Control[] _elementIcons = new Control[8];
    private readonly ColorRect[] _elementCooldown = new ColorRect[8];
    private readonly Label[] _beamLabels = new Label[3];
    private ColorRect _beamCooldown = null!;

    // Catalyst pips
    private ColorRect _pipL = null!, _pipR = null!;

    // Enemy telegraph / threat card
    private PanelContainer _telegraph = null!;
    private ColorRect _teleDiamond = null!;
    private Label _teleElement = null!, _teleBeam = null!, _threat = null!;
    private float _teleFlash;
    private float _threatRefresh;

    // Player debuff line + entrance popup
    private Label _statusL = null!;
    private Label _popup = null!;
    private float _popupTime;

    // Coach (Easy difficulty): one prioritized what-to-do hint at a time
    private Label _coach = null!;
    private float _coachRefresh;
    private float _coachHold;
    private int _coachPriority = 99;

    // Timing bar (Easy): teaches WHEN to press during an enemy switch or flare
    private Control _timingBar = null!;
    private ColorRect _timingZoneShatter = null!, _timingZoneBrace = null!, _timingCursor = null!;
    private Label _timingLabelShatter = null!, _timingLabelBrace = null!;
    private bool _enemySwitchTimeline; // an element switch (gather seen), not a beam reform
    private const float TimingW = 360f, TimingH = 6f;

    // ---- Mouse layer (clickable actions + timed cues; all difficulties, C6) ----
    // Full-screen Control BEHIND the rest of the HUD: hold-LMB-to-channel lives
    // here, never on the `channel` action, so a cue click cannot leak into power
    // (C2/D2.3). Every other HUD Control is set click-through in ApplyMouseFilters.
    private Control _arena = null!;
    private PlayerController? _controller; // null in ScriptedMode (AC17)

    private Control _channelPad = null!;
    private ColorRect _channelPadFill = null!;
    private Label _channelPadState = null!;

    private readonly ColorRect[] _shatterMark = new ColorRect[8];
    private Label _shatterLabel = null!;

    private Control _braceCue = null!;
    private ColorRect _braceFill = null!;
    private Label _braceLabel = null!;

    private Label _pipCue = null!;

    private int _hoverSlot = -1, _hoverBeam = -1;
    private bool _hoverPad, _hoverBrace;
    private float _cuePulse;

    // Clash beam height: muzzles sit at caster y 900 - 103*1.25 scale.
    private const float ClashLeftX = 300f, ClashRightX = 1620f, ClashY = 771f;

    // Banners / result
    private Label _banner = null!;
    private PanelContainer _resultPanel = null!;
    private Label _resultTitle = null!;
    private bool _resultShown;
    private float _bannerTime;

    private static readonly string[] ElementAbbrev = { "FIR", "ICE", "WAT", "LIT", "DRK", "PSN", "HLY", "LUX" };
    private static readonly string[] BeamNames = { "SINGLE", "TWIN", "PINPOINT" };

    private bool _built;

    // Built on the first frame, not in _Ready: as a scene child this node readies
    // before BattleManager has set up the casters and loadouts.
    private void Build()
    {
        _battle = GetParent<BattleManager>();
        _player = _battle.LeftCaster;
        _enemy = _battle.RightCaster;
        _battle.GetNodeOrNull<Label>("DebugLabel")?.Hide();

        _controller = _battle.Player;

        BuildArenaInput();
        BuildClashMeter();
        BuildManaBars();
        BuildLoadoutStrip();
        BuildTelegraph();
        BuildBanner();
        BuildResultPanel();
        BuildMouseLayer();
        ApplyMouseFilters();

        _enemy.ElementChanged += _ => _teleFlash = 1f;
        _enemy.BeamTypeChanged += _ => _teleFlash = 1f;
        _enemy.GatherStarted += _ => _teleFlash = 1f;
        _battle.EntranceResolved += OnEntrance;
    }

    private void OnEntrance(int side, ImpactReport r)
    {
        var soul = ElementDb.Soul(r.Soul);
        string who = side == 0 ? "" : "ENEMY ";
        _popup.Text = $"{who}{soul.EntranceName}{(r.Shattered ? "  SHATTER!" : r.Braced ? "  braced" : "")}{(r.Charged ? "" : "  (weak)")}";
        _popup.AddThemeColorOverride("font_color", r.Shattered ? new Color(1f, 0.9f, 0.3f) : ElementDb.Style(r.Soul).Glow);
        _popupTime = 1.4f;
    }

    // ---------- construction ----------

    /// <summary>Black outline so cue text stays legible over beam/clash FX.</summary>
    private static Label Outlined(Label l)
    {
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        l.AddThemeConstantOverride("outline_size", 6);
        return l;
    }

    private static Label MakeLabel(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    private void BuildClashMeter()
    {
        var back = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            Position = new Vector2(960 - MeterW / 2 - 4, 36),
            Size = new Vector2(MeterW + 8, MeterH + 8),
        };
        AddChild(back);

        _meterFillL = new ColorRect { Position = new Vector2(960 - MeterW / 2, 40), Size = new Vector2(MeterW / 2, MeterH) };
        _meterFillR = new ColorRect { Position = new Vector2(960, 40), Size = new Vector2(MeterW / 2, MeterH) };
        _marker = new ColorRect { Color = Colors.White, Size = new Vector2(6, MeterH + 12) };
        AddChild(_meterFillL);
        AddChild(_meterFillR);
        AddChild(_marker);
    }

    private void BuildManaBars()
    {
        AddChild(new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            Position = new Vector2(56, 1080 - 64),
            Size = new Vector2(ManaW + 8, ManaH + 8),
        });
        _manaFillL = new ColorRect { Color = new Color(0.35f, 0.75f, 1f), Position = new Vector2(60, 1080 - 60), Size = new Vector2(ManaW, ManaH) };
        AddChild(_manaFillL);
        _exhaustL = MakeLabel("EXHAUSTED", 22, new Color(1f, 0.35f, 0.3f));
        _exhaustL.Position = new Vector2(60, 1080 - 96);
        _exhaustL.Visible = false;
        AddChild(_exhaustL);

        AddChild(new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            Position = new Vector2(1920 - 64 - ManaW, 1080 - 64),
            Size = new Vector2(ManaW + 8, ManaH + 8),
        });
        _manaFillR = new ColorRect { Color = new Color(1f, 0.5f, 0.4f), Position = new Vector2(1920 - 60 - ManaW, 1080 - 60), Size = new Vector2(ManaW, ManaH) };
        AddChild(_manaFillR);
        _exhaustR = MakeLabel("EXHAUSTED", 22, new Color(1f, 0.35f, 0.3f));
        _exhaustR.Position = new Vector2(1920 - 60 - 130, 1080 - 96);
        _exhaustR.Visible = false;
        AddChild(_exhaustR);

        // Catalyst pips: a diamond socket beside each mana bar. Both are public reads.
        _pipL = new ColorRect { Size = new Vector2(22, 22), Position = new Vector2(60 + ManaW + 30, 1080 - 58), Rotation = Mathf.Pi / 4 };
        _pipR = new ColorRect { Size = new Vector2(22, 22), Position = new Vector2(1920 - 60 - ManaW - 24, 1080 - 58), Rotation = Mathf.Pi / 4 };
        AddChild(_pipL);
        AddChild(_pipR);

        // Player debuff line ("CONDUCTED", "OVERDRIVE LOCKED"...)
        _statusL = MakeLabel("", 20, new Color(1f, 0.6f, 0.9f));
        _statusL.Position = new Vector2(200, 1080 - 96);
        AddChild(_statusL);

        // Entrance popup, center screen
        _popup = MakeLabel("", 40);
        _popup.HorizontalAlignment = HorizontalAlignment.Center;
        _popup.AnchorLeft = 0; _popup.AnchorRight = 1;
        _popup.Position = new Vector2(0, 250);
        _popup.Visible = false;
        AddChild(_popup);

        // Coach line: above the soul strip, outlined for readability over FX.
        _coach = MakeLabel("", 27);
        _coach.HorizontalAlignment = HorizontalAlignment.Center;
        _coach.AnchorLeft = 0; _coach.AnchorRight = 1;
        _coach.Position = new Vector2(0, 1080 - 236);
        _coach.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _coach.AddThemeConstantOverride("outline_size", 7);
        _coach.Visible = false;
        AddChild(_coach);

        // Timing bar under the coach line: ----|gold|----|red|-- with a sweeping
        // cursor. Gold = switch now (shatter), red = hold SHIFT (brace at impact).
        _timingBar = new Control { Position = new Vector2(960 - TimingW / 2, 1080 - 196), Visible = false };
        _timingBar.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.7f), Position = new Vector2(-4, -4), Size = new Vector2(TimingW + 8, TimingH + 8) });
        _timingBar.AddChild(new ColorRect { Color = new Color(0.35f, 0.35f, 0.4f), Size = new Vector2(TimingW, TimingH) });
        _timingZoneShatter = new ColorRect { Color = new Color(1f, 0.85f, 0.35f, 0.95f), Size = new Vector2(0, TimingH) };
        _timingZoneBrace = new ColorRect { Color = new Color(1f, 0.45f, 0.3f, 0.95f), Size = new Vector2(0, TimingH) };
        _timingBar.AddChild(_timingZoneShatter);
        _timingBar.AddChild(_timingZoneBrace);
        _timingCursor = new ColorRect { Color = Colors.White, Size = new Vector2(4, TimingH + 12), Position = new Vector2(0, -6) };
        _timingBar.AddChild(_timingCursor);
        _timingLabelShatter = MakeLabel("SHATTER", 13, new Color(1f, 0.85f, 0.35f));
        _timingLabelBrace = MakeLabel("BRACE", 13, new Color(1f, 0.45f, 0.3f));
        _timingLabelShatter.Position = new Vector2(0, TimingH + 6);
        _timingBar.AddChild(_timingLabelShatter);
        _timingBar.AddChild(_timingLabelBrace);
        AddChild(_timingBar);
    }

    private void BuildLoadoutStrip()
    {
        // All 8 souls, keys 1-8; the keystone slot gets a gold ring. Each slot is
        // also a click target (AC1) and carries its SHATTER marking (AC10).
        for (int i = 0; i < 8; i++)
        {
            int idx = i;
            var style = ElementDb.Style((Element)i);
            var slot = new Control
            {
                Position = new Vector2(72 + i * 72, 1080 - 168),
                Size = new Vector2(52, 52),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };

            // SHATTER halo, behind everything: a rotated rect's diamond is centred
            // at pos + (0, size/sqrt2), so the offset keeps it concentric with the icon.
            _shatterMark[i] = new ColorRect
            {
                Color = new Color(1f, 0.85f, 0.35f, 0.85f),
                Size = new Vector2(50, 50),
                Position = new Vector2(26, 24.04f - 35.36f),
                Rotation = Mathf.Pi / 4,
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            slot.AddChild(_shatterMark[i]);

            if ((Element)i == _player.Keystone)
            {
                var ring = new ColorRect
                {
                    Color = new Color(1f, 0.85f, 0.35f),
                    Size = new Vector2(42, 42),
                    Position = new Vector2(26, -4),
                    Rotation = Mathf.Pi / 4,
                };
                slot.AddChild(ring);
            }
            var diamond = new ColorRect
            {
                Color = style.Glow,
                Size = new Vector2(34, 34),
                Position = new Vector2(26, 0),
                Rotation = Mathf.Pi / 4,
            };
            slot.AddChild(diamond);

            // The digit stays on the icon: a clickable must still name its key (AC18).
            var key = MakeLabel($"{i + 1}", 15, new Color(1, 1, 1, 0.8f));
            key.Position = new Vector2(-4, -8);
            slot.AddChild(key);

            var name = MakeLabel(ElementAbbrev[i], 12);
            name.Position = new Vector2(8, 50);
            slot.AddChild(name);

            _elementCooldown[i] = new ColorRect
            {
                Color = new Color(0, 0, 0, 0.65f),
                Size = new Vector2(52, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            slot.AddChild(_elementCooldown[i]);

            slot.GuiInput += e => OnSlotInput(idx, e);
            slot.MouseEntered += () => _hoverSlot = idx;
            slot.MouseExited += () => { if (_hoverSlot == idx) _hoverSlot = -1; };

            _elementIcons[i] = slot;
            AddChild(slot);
        }

        // Beam types Q/W/E, to the right of the soul strip; clickable too (AC2).
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var l = MakeLabel($"{"QWE"[i]}  {BeamNames[i]}", 16, new Color(0.85f, 0.85f, 0.95f));
            l.Position = new Vector2(700 + i * 140, 1080 - 164);
            l.Size = new Vector2(126, 28);
            l.MouseFilter = Control.MouseFilterEnum.Stop;
            l.GuiInput += e => OnBeamInput(idx, e);
            l.MouseEntered += () => _hoverBeam = idx;
            l.MouseExited += () => { if (_hoverBeam == idx) _hoverBeam = -1; };
            _beamLabels[i] = l;
            AddChild(l);
        }
        _beamCooldown = new ColorRect
        {
            Color = new Color(0.15f, 0.15f, 0.2f, 0.9f),
            Position = new Vector2(700, 1080 - 138),
            Size = new Vector2(0, 5),
        };
        AddChild(_beamCooldown);

        // SHATTER banner over the strip, left of the Easy timing bar (x >= 780).
        _shatterLabel = MakeLabel("SHATTER WINDOW — CLICK A GLOWING SOUL (1-8)", 19, new Color(1f, 0.85f, 0.35f));
        _shatterLabel.Position = new Vector2(76, 1080 - 200);
        _shatterLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _shatterLabel.AddThemeConstantOverride("outline_size", 7);
        _shatterLabel.Visible = false;
        AddChild(_shatterLabel);
    }

    private void BuildTelegraph()
    {
        _telegraph = new PanelContainer { Position = new Vector2(1920 - 360, 90) };
        var box = new VBoxContainer();
        _telegraph.AddChild(box);

        box.AddChild(MakeLabel("ENEMY", 18, new Color(1, 1, 1, 0.6f)));

        var row = new HBoxContainer();
        box.AddChild(row);
        var holder = new Control { CustomMinimumSize = new Vector2(70, 60) };
        _teleDiamond = new ColorRect { Size = new Vector2(42, 42), Position = new Vector2(34, 6), Rotation = Mathf.Pi / 4 };
        holder.AddChild(_teleDiamond);
        row.AddChild(holder);
        var stack = new VBoxContainer();
        _teleElement = MakeLabel("FIRE", 30);
        _teleBeam = MakeLabel("SINGLE", 20, new Color(1, 1, 1, 0.75f));
        stack.AddChild(_teleElement);
        stack.AddChild(_teleBeam);
        row.AddChild(stack);

        _threat = MakeLabel("", 24);
        box.AddChild(_threat);

        AddChild(_telegraph);
    }

    private void BuildBanner()
    {
        _banner = MakeLabel("READY...", 84, Colors.White);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AnchorLeft = 0; _banner.AnchorRight = 1;
        _banner.Position = new Vector2(0, 380);
        AddChild(_banner);
    }

    private void BuildResultPanel()
    {
        _resultPanel = new PanelContainer
        {
            Position = new Vector2(760, 380),
            CustomMinimumSize = new Vector2(400, 260),
            Visible = false,
        };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        _resultPanel.AddChild(box);

        _resultTitle = MakeLabel("VICTORY", 56);
        _resultTitle.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_resultTitle);

        var rematch = new Button { Text = "Rematch  (Enter)" };
        rematch.Pressed += () => MainNode()?.StartBattle();
        box.AddChild(rematch);

        var loadout = new Button { Text = "Change Loadout" };
        loadout.Pressed += () => MainNode()?.ShowLoadout();
        box.AddChild(loadout);

        var quit = new Button { Text = "Quit" };
        quit.Pressed += () => GetTree().Quit();
        box.AddChild(quit);

        AddChild(_resultPanel);
    }

    // ---------- mouse layer ----------

    /// <summary>Full-screen hold-LMB-to-channel surface, added first so it sits
    /// BEHIND every other HUD control (D2.3). `Background` in Battle.tscn has
    /// mouse_filter = 2 and deliberately is not reused for this.</summary>
    private void BuildArenaInput()
    {
        _arena = new Control
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _arena.GuiInput += OnArenaInput;
        AddChild(_arena);
    }

    private void BuildMouseLayer()
    {
        // CHANNEL latch pad: the primary mouse affordance for the one continuous
        // input (C3). Names its key, per AC18.
        _channelPad = new Control
        {
            Position = new Vector2(1150, 1080 - 176),
            Size = new Vector2(190, 62),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _channelPadFill = new ColorRect
        {
            Color = new Color(0.1f, 0.18f, 0.26f, 0.85f),
            Size = new Vector2(190, 62),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _channelPad.AddChild(_channelPadFill);
        var padTitle = Outlined(MakeLabel("CHANNEL", 22, Colors.White));
        padTitle.Position = new Vector2(10, 2);
        _channelPad.AddChild(padTitle);
        _channelPadState = Outlined(MakeLabel("OFF - click / hold SPACE", 14, new Color(1, 1, 1, 0.92f)));
        _channelPadState.Position = new Vector2(10, 34);
        _channelPad.AddChild(_channelPadState);
        _channelPad.GuiInput += OnChannelPadInput;
        _channelPad.MouseEntered += () => _hoverPad = true;
        _channelPad.MouseExited += () => _hoverPad = false;
        AddChild(_channelPad);

        // BRACE cue: appears only while a real entrance is in flight (or a Fire
        // flare is winding up) and expires with it (D3).
        _braceCue = new Control
        {
            Position = new Vector2(960 - 120, ClashY - 130),
            Size = new Vector2(240, 76),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _braceCue.AddChild(new ColorRect
        {
            Color = new Color(1f, 0.5f, 0.35f, 0.95f),
            Position = new Vector2(-4, -4),
            Size = new Vector2(248, 84),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _braceFill = new ColorRect
        {
            Color = new Color(0.32f, 0.07f, 0.05f, 0.97f),
            Size = new Vector2(240, 76),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _braceCue.AddChild(_braceFill);
        _braceLabel = Outlined(MakeLabel("BRACE", 38, new Color(1f, 0.86f, 0.7f)));
        _braceLabel.Position = new Vector2(14, -2);
        _braceCue.AddChild(_braceLabel);
        var braceKey = Outlined(MakeLabel("click  /  hold SHIFT", 17, new Color(1, 1, 1, 0.95f)));
        braceKey.Position = new Vector2(16, 48);
        _braceCue.AddChild(braceKey);
        _braceCue.GuiInput += OnBraceInput;
        _braceCue.MouseEntered += () => _hoverBrace = true;
        _braceCue.MouseExited += () => _hoverBrace = false;
        AddChild(_braceCue);

        // PIP cue: informational pulse beside the existing socket (D3).
        _pipCue = Outlined(MakeLabel("PIP READY - SWITCH SOULS", 17, new Color(1f, 0.85f, 0.3f)));
        _pipCue.Position = new Vector2(60 + ManaW + 62, 1080 - 58);
        _pipCue.Visible = false;
        AddChild(_pipCue);
    }

    /// <summary>Everything the HUD draws is click-through except the declared
    /// clickables and the result panel. Without this, any ColorRect (MouseFilter
    /// defaults to Stop) would swallow arena holds and cue clicks alike.</summary>
    private void ApplyMouseFilters()
    {
        foreach (Node child in GetChildren()) SetFilterRecursive(child, Control.MouseFilterEnum.Ignore);

        _arena.MouseFilter = Control.MouseFilterEnum.Stop;
        _channelPad.MouseFilter = Control.MouseFilterEnum.Stop;
        _braceCue.MouseFilter = Control.MouseFilterEnum.Stop;
        foreach (var slot in _elementIcons) slot.MouseFilter = Control.MouseFilterEnum.Stop;
        foreach (var label in _beamLabels) label.MouseFilter = Control.MouseFilterEnum.Stop;

        // The result panel keeps its buttons live (Rematch / Loadout / Quit).
        SetFilterRecursive(_resultPanel, Control.MouseFilterEnum.Stop);
    }

    private static void SetFilterRecursive(Node node, Control.MouseFilterEnum filter)
    {
        if (node is Control c) c.MouseFilter = filter;
        foreach (Node child in node.GetChildren()) SetFilterRecursive(child, filter);
    }

    /// <summary>Clicks only do something during an actual fight, and never while
    /// the result panel is up (AC17).</summary>
    private bool Interactive => _battle.State == BattleState.Fighting && !_resultPanel.Visible;

    private static bool IsLeftPress(InputEvent e) =>
        e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };

    private void OnArenaInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        _arena.AcceptEvent();
        if (_controller == null) return; // ScriptedMode: no controller (AC17)
        _controller.ArenaHold = mb.Pressed && Interactive;
    }

    private void OnSlotInput(int i, InputEvent e)
    {
        if (!IsLeftPress(e)) return;
        _elementIcons[i].AcceptEvent();
        if (!Interactive) return;
        // Same call the key makes: every cost/cooldown/gather guard still applies.
        _player.TrySwitchElement((Element)i);
    }

    private void OnBeamInput(int i, InputEvent e)
    {
        if (!IsLeftPress(e)) return;
        _beamLabels[i].AcceptEvent();
        if (!Interactive) return;
        _player.TrySwitchBeam((BeamType)i);
    }

    private void OnChannelPadInput(InputEvent e)
    {
        if (!IsLeftPress(e)) return;
        _channelPad.AcceptEvent();
        if (!Interactive || _controller == null) return;
        _controller.ToggleChannelLatch();
    }

    private void OnBraceInput(InputEvent e)
    {
        if (!IsLeftPress(e)) return;
        _braceCue.AcceptEvent();
        if (!Interactive || _controller == null) return;
        _controller.GrantBrace();
    }

    /// <summary>Drives the cue layer: appear when the gameplay window opens, expire
    /// when it closes, and stay inert outside Fighting. Shown at every difficulty
    /// (C6) - this is geometry, not tutoring. Runs after UpdateLoadoutStrip so the
    /// hover states win over the resting scale/modulate.</summary>
    private void UpdateMouseLayer(float dt)
    {
        _cuePulse += dt;
        float pulse = 0.65f + 0.35f * Mathf.Sin(_cuePulse * 9f);
        bool live = Interactive;

        if (!live && _controller != null) _controller.ArenaHold = false;
        _arena.MouseFilter = live ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;

        // Clickables are hidden and inert outside Fighting (AC17).
        for (int i = 0; i < 8; i++) _elementIcons[i].Visible = live;
        for (int i = 0; i < 3; i++) _beamLabels[i].Visible = live;
        _beamCooldown.Visible = live;
        _channelPad.Visible = live;

        bool playerBusy = _player.GatherTimer > 0f || _player.ReformTimer > 0f;

        // --- SHATTER marking (AC10/AC11): only while the enemy is gathering and
        // the player could actually answer it. Marks only; it never picks a soul (C5).
        bool canSwitch = live && !playerBusy && _player.ElementCooldown <= 0f;
        bool window = canSwitch && _enemy.GatherTimer > 0f;
        int marked = 0;
        for (int i = 0; i < 8; i++)
        {
            var e = (Element)i;
            bool mark = window && e != _player.CurrentElement
                        && _player.EntranceEligible(e) && _player.Mana >= _player.SwitchCost(e);
            _shatterMark[i].Visible = mark;
            if (mark)
            {
                _shatterMark[i].Color = new Color(1f, 0.88f, 0.4f, 0.7f + 0.3f * pulse);
                // UpdateLoadoutStrip dims idle slots to 50% alpha; a marked slot is
                // a live choice, so it comes back to full and lifts slightly.
                _elementIcons[i].Modulate = Colors.White;
                _elementIcons[i].Scale = Vector2.One * 1.05f;
                marked++;
            }
        }
        _shatterLabel.Visible = marked > 0;
        if (marked > 0) _shatterLabel.Modulate = new Color(1, 1, 1, 0.6f + 0.4f * pulse);

        // --- BRACE cue (AC12): reads IncomingEntrancePending, never ReformTimer.
        // The Fire flare windup is the one non-entrance window that earns a brace (D3).
        bool flareWindup = _enemy.CurrentElement == Element.Fire && _enemy.IsEmpowered
                           && _enemy.FlareActiveTimer <= 0f
                           && _enemy.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup;
        bool braceWindow = live && (_enemy.IncomingEntrancePending || flareWindup);
        _braceCue.Visible = braceWindow;
        if (braceWindow)
        {
            float cx = Mathf.Lerp(ClashLeftX, ClashRightX, _battle.ClashX);
            _braceCue.Position = new Vector2(Mathf.Clamp(cx - 120f, 20f, 1920f - 260f), ClashY - 130f);
            bool granted = _controller?.BraceGrantActive == true;
            _braceLabel.Text = granted ? "BRACED" : "BRACE";
            _braceFill.Color = granted
                ? new Color(0.1f, 0.3f, 0.42f, 0.9f)
                : new Color(0.3f + 0.25f * pulse, 0.07f, 0.05f, 0.9f);
            _braceCue.Scale = Vector2.One * (_hoverBrace ? 1.1f : 1f);
            _braceCue.Modulate = _hoverBrace ? new Color(1.35f, 1.35f, 1.35f) : Colors.White;
        }

        // --- PIP cue: informational only.
        _pipCue.Visible = live && _player.PipReady && !playerBusy;
        if (_pipCue.Visible) _pipCue.Modulate = new Color(1, 1, 1, 0.55f + 0.45f * pulse);

        // --- CHANNEL pad state: visibly distinct on/off (AC7) + hover (AC18).
        bool latched = _controller?.ChannelLatch == true;
        bool channelling = _player.WantsChannel;
        _channelPadFill.Color = latched
            ? new Color(0.2f, 0.62f, 0.85f, 0.92f)
            : channelling
                ? new Color(0.14f, 0.34f, 0.46f, 0.88f)
                : new Color(0.1f, 0.14f, 0.2f, 0.85f);
        _channelPadState.Text = latched ? "ON - click to release" : "OFF - click / hold SPACE";
        _channelPad.Modulate = _hoverPad ? new Color(1.35f, 1.35f, 1.35f) : Colors.White;
        _channelPad.Scale = Vector2.One * (_hoverPad ? 1.05f : 1f);

        // --- Hover feedback on the strip and beam labels (AC18).
        if (live && _hoverSlot >= 0)
        {
            _elementIcons[_hoverSlot].Scale = Vector2.One * 1.35f;
            _elementIcons[_hoverSlot].Modulate = new Color(1.4f, 1.4f, 1.4f);
        }
        if (live && _hoverBeam >= 0)
            _beamLabels[_hoverBeam].Modulate = new Color(1.5f, 1.5f, 1.5f);
    }

    private Main? MainNode() => GetTree().Root.GetNodeOrNull<Main>("Main");

    // ---------- per-frame ----------

    public override void _Process(double delta)
    {
        if (!_built)
        {
            // Headless: never build or subscribe. BattleManager queue-frees this
            // node, but a zero-physics-step first frame can still run _Process
            // once — subscribing events here would leave freed Labels wired to
            // the (suite-shared) battle and throw on the next entrance.
            if (DisplayServer.GetName() == "headless")
            {
                SetProcess(false);
                return;
            }
            Build();
            _built = true;
        }
        float dt = (float)delta;
        UpdateClashMeter();
        UpdateManaBars();
        UpdateLoadoutStrip();
        UpdateTelegraph(dt);
        UpdateBannerAndResult(dt);
        UpdateCoach(dt);
        UpdateTimingBar();
        UpdateMouseLayer(dt);

        if (_popupTime > 0f)
        {
            _popupTime -= dt;
            _popup.Visible = true;
            _popup.Modulate = new Color(1, 1, 1, Mathf.Clamp(_popupTime / 0.5f, 0f, 1f));
        }
        else
        {
            _popup.Visible = false;
        }
    }

    private void UpdateClashMeter()
    {
        float x = _battle.ClashX;
        var pGlow = _player.Style.Glow;
        var eGlow = _enemy.Style.Glow;
        float left = 960 - MeterW / 2;
        float split = left + MeterW * x;

        _meterFillL.Position = new Vector2(left, 40);
        _meterFillL.Size = new Vector2(MeterW * x, MeterH);
        _meterFillL.Color = new Color(pGlow.R, pGlow.G, pGlow.B, 0.85f);
        _meterFillR.Position = new Vector2(split, 40);
        _meterFillR.Size = new Vector2(MeterW * (1 - x), MeterH);
        _meterFillR.Color = new Color(eGlow.R, eGlow.G, eGlow.B, 0.85f);
        _marker.Position = new Vector2(split - 3, 34);

        bool danger = x < Tuning.DesperationZone || x > 1 - Tuning.DesperationZone;
        float pulse = danger ? 0.6f + 0.4f * Mathf.Sin((float)Time.GetTicksMsec() / 70f) : 1f;
        _marker.Color = new Color(1, 1, 1, pulse);
    }

    private void UpdateManaBars()
    {
        _manaFillL.Size = new Vector2(ManaW * _player.Mana / Tuning.ManaMax, ManaH);
        _manaFillR.Size = new Vector2(ManaW * _enemy.Mana / Tuning.ManaMax, ManaH);
        _manaFillL.Color = _player.IsExhausted ? new Color(1f, 0.3f, 0.25f) : new Color(0.35f, 0.75f, 1f);
        _manaFillR.Color = _enemy.IsExhausted ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.5f, 0.4f);
        _exhaustL.Visible = _player.IsExhausted;
        _exhaustR.Visible = _enemy.IsExhausted;
    }

    private void UpdateLoadoutStrip()
    {
        Element pending = _player.GatherTimer > 0f ? _player.PendingElement : _player.CurrentElement;
        for (int i = 0; i < 8; i++)
        {
            bool active = (Element)i == _player.CurrentElement;
            bool incoming = _player.GatherTimer > 0f && (Element)i == pending;
            _elementIcons[i].Scale = Vector2.One * (active || incoming ? 1.2f : 0.9f);
            _elementIcons[i].Modulate = active || incoming ? Colors.White : new Color(1, 1, 1, 0.5f);

            // Clamp: Glaciate pushes live cooldowns past the base constant.
            float cd = active ? 0f : Mathf.Min(1f, _player.ElementCooldown / Tuning.ElementSwitchCooldown);
            _elementCooldown[i].Size = new Vector2(52, 52 * cd);
        }
        for (int i = 0; i < 3; i++)
        {
            bool active = (int)_player.Beam == i;
            _beamLabels[i].Modulate = active ? Colors.White : new Color(1, 1, 1, 0.45f);
        }
        _beamCooldown.Size = new Vector2(400 * Mathf.Min(1f, _player.BeamCooldown / Tuning.BeamSwitchCooldown), 5);

        UpdatePip(_pipL, _player);
        UpdatePip(_pipR, _enemy);

        _statusL.Text = _player.ConductTimer > 0f ? "CONDUCTED!"
            : _player.OverdriveLockTimer > 0f ? "OVERDRIVE LOCKED"
            : _player.PurgeImmunityTimer > 0f ? "IMMUNE"
            : "";
    }

    private static void UpdatePip(ColorRect pip, Caster caster)
    {
        float t = caster.PipCharge / Tuning.PipChargeTime;
        pip.Color = caster.PipReady
            ? new Color(1f, 0.85f, 0.3f, 0.75f + 0.25f * Mathf.Sin((float)Time.GetTicksMsec() / 90f))
            : new Color(0.35f + 0.4f * t, 0.32f + 0.3f * t, 0.25f, 0.8f);
    }

    private Element _lastTeleElement = (Element)(-1);
    private BeamType _lastTeleBeam = (BeamType)(-1);

    private void UpdateTelegraph(float dt)
    {
        // Text and theme overrides only change on a switch; don't rebuild them at 60 fps.
        if (_enemy.CurrentElement != _lastTeleElement || _enemy.Beam != _lastTeleBeam)
        {
            _lastTeleElement = _enemy.CurrentElement;
            _lastTeleBeam = _enemy.Beam;
            var style = _enemy.Style;
            _teleDiamond.Color = style.Glow;
            _teleElement.Text = style.DisplayName.ToUpper();
            _teleBeam.Text = BeamNames[(int)_enemy.Beam];
        }

        _teleFlash = Mathf.Max(0f, _teleFlash - dt * 2.5f);
        _telegraph.Modulate = new Color(1 + _teleFlash * 2f, 1 + _teleFlash * 2f, 1 + _teleFlash * 2f);

        // Threat line: the single read that carries the timing game. Rebuilt at
        // most 10x/s — it contains live countdowns.
        _threatRefresh -= dt;
        if (_threatRefresh > 0f) return;
        _threatRefresh = 0.1f;

        string threat;
        Color color;
        if (_enemy.IncomingEntrancePending)
        {
            var incoming = ElementDb.Style(_enemy.GatherTimer > 0f ? _enemy.PendingElement : _enemy.CurrentElement);
            threat = $"!! INCOMING: {incoming.DisplayName.ToUpper()}";
            color = new Color(1f, 0.55f, 0.25f);
        }
        else
        {
            (threat, color) = _enemy.CurrentElement switch
            {
                Element.Fire when _enemy.FlareActiveTimer > 0f => ("FLARING!", new Color(1f, 0.5f, 0.2f)),
                Element.Fire when _enemy.IsEmpowered && _enemy.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup
                    => ($"FLARE IN {Tuning.FlarePeriod - _enemy.FlareClock:0.0}s", new Color(1f, 0.7f, 0.3f)),
                Element.Lightning when _enemy.CritActiveTimer > 0f => ("CRIT!", new Color(0.8f, 0.95f, 1f)),
                Element.Lightning when _enemy.CritArmed => ("CRIT ARMED", new Color(0.7f, 0.9f, 1f)),
                Element.Poison when _enemy.Stacks > 0 => ($"{_enemy.Stacks} STACKS +{_enemy.Stacks * 2}%", new Color(0.6f, 0.95f, 0.3f)),
                Element.Darkness when _enemy.DominanceTime >= Tuning.DominanceWindow => ("LEECHING YOU", new Color(0.8f, 0.4f, 1f)),
                Element.Ice when _enemy.DominanceTime >= Tuning.DominanceWindow => ("FROSTING YOUR COOLDOWNS", new Color(0.5f, 0.85f, 1f)),
                _ => ("", Colors.White),
            };
            if (_enemy.PipReady) threat = threat.Length > 0 ? threat + "  ◆ PIP" : "◆ PIP CHARGED";
        }
        if (threat != _threat.Text)
        {
            _threat.Text = threat;
            _threat.AddThemeColorOverride("font_color", color);
        }
    }

    /// <summary>Easy-difficulty coach: reads the fight state and says what to press.
    /// One hint at a time; lower priority number preempts, and a shown hint holds
    /// for a moment so the line doesn't flicker between conditions.</summary>
    private void UpdateCoach(float dt)
    {
        if (Game.Instance.Difficulty != Difficulty.Easy || _battle.State != BattleState.Fighting)
        {
            _coach.Visible = false;
            _coachPriority = 99;
            return;
        }

        _coachHold -= dt;
        _coachRefresh -= dt;
        if (_coachRefresh > 0f) return;
        _coachRefresh = 0.25f;

        (int prio, string text, Color color)? hint = FindHint();
        if (hint == null)
        {
            if (_coachHold <= 0f)
            {
                _coach.Visible = false;
                _coachPriority = 99;
            }
            return;
        }

        var (prio, text, color) = hint.Value;
        if (prio > _coachPriority && _coachHold > 0f) return; // don't preempt with weaker advice
        if (text != _coach.Text)
        {
            _coach.Text = text;
            _coach.AddThemeColorOverride("font_color", color);
        }
        _coach.Visible = true;
        _coachPriority = prio;
        _coachHold = 1.6f;
    }

    private (int, string, Color)? FindHint()
    {
        var danger = new Color(1f, 0.45f, 0.35f);
        var gold = new Color(1f, 0.85f, 0.35f);
        var calm = new Color(0.55f, 0.85f, 1f);

        bool enemyIncoming = _enemy.IncomingEntrancePending;
        bool playerBusy = _player.GatherTimer > 0f || _player.ReformTimer > 0f;

        // 1. React to an incoming entrance: brace. (Beam reforms, keystone
        // returns and echo switches fire no entrance — never cry wolf on them.)
        if (enemyIncoming && _player.Mana > 25f)
            return (1, "INCOMING — click BRACE, or hold SPACE + SHIFT (halves the hit)", danger);
        if (enemyIncoming)
            return (1, "INCOMING — too low on mana to brace, ride it out", danger);

        // 2. Their switch is your shatter window — but ONLY during their gather:
        // your entrance needs 0.9s of flight, so any later press lands too late.
        if (playerBusy == false && _player.PipReady && _player.ElementCooldown <= 0f
            && _enemy.GatherTimer > 0f)
            return (2, "SHATTER WINDOW — click a glowing soul (or keys 1-8) to strike them mid-switch!", gold);

        // 3. Punish an exhausted enemy.
        if (_enemy.IsExhausted && _player.Mana > 15f)
            return (3, "ENEMY DRAINED — CHANNEL on (SPACE) + SHIFT and shove!", gold);

        // 4-5. Telegraphed enemy spikes.
        if (_enemy.CurrentElement == Element.Fire && _enemy.IsEmpowered
            && _enemy.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup && _player.Mana > 25f)
            return (4, "FLARE COMING — click BRACE, or hold SHIFT, to brace through it", danger);
        if (_enemy.CurrentElement == Element.Lightning && _enemy.CritArmed)
            return (5, "CRIT ARMED — don't switch now, switching triggers it", danger);

        // 6-8. Ongoing soul pressure and its answers.
        if (_player.ConductTimer > 0f)
            return (6, "CONDUCTED — your drain is doubled! HOLY (7) purges it", danger);
        if (_enemy.CurrentElement == Element.Darkness && _enemy.DominanceTime >= Tuning.DominanceWindow)
            return (7, "THEY'RE LEECHING YOUR MANA — push back, or HOLY (7) blocks it", danger);
        if (_enemy.CurrentElement == Element.Poison && _enemy.Stacks >= 4)
            return (8, "POISON STACKING UP — press E: pinpoint mutes their soul", calm);

        // 9. Back to the wall.
        if (_battle.ClashX < 0.18f)
            return (9, "HOLD THE LINE — keep CHANNEL on (SPACE), desperation gives +25%!", danger);

        // 10-11. Mana economy.
        if (_player.IsExhausted)
            return (10, "DRAINED! Next time drop CHANNEL (SPACE) before the bar empties", danger);
        if (_player.Mana < 22f && _player.IsEmpowered && _battle.ClashX > 0.3f)
            return (11, "MANA LOW — drop CHANNEL (SPACE) a moment and recover", calm);

        // 12. Idle pip nudge.
        if (_player.PipReady && !playerBusy && _player.ElementCooldown <= 0f && _player.SinceReformEnd > 6f)
            return (12, "REACTION CHARGED — click a soul (or keys 1-8) to unleash it", gold);

        return null;
    }

    /// <summary>The when-to-press lesson: during an enemy element switch the bar
    /// sweeps from their press (t=0) to their impact (t=0.9s). Gold opening zone =
    /// counter-switch now and your entrance shatters them; red closing zone = hold
    /// SHIFT so you are braced at the moment of impact. Also reused for Fire's
    /// flare windup (brace zone only). Easy difficulty only, like the coach.</summary>
    private void UpdateTimingBar()
    {
        if (Game.Instance.Difficulty != Difficulty.Easy || _battle.State != BattleState.Fighting)
        {
            _timingBar.Visible = false;
            _enemySwitchTimeline = false;
            return;
        }

        // Track whether the current enemy reform came from an element switch
        // (a gather precedes it) — beam reforms get no bar: nothing is incoming.
        if (_enemy.GatherTimer > 0f) _enemySwitchTimeline = true;
        else if (_enemy.ReformTimer <= 0f) _enemySwitchTimeline = false;

        float total, elapsed, shatterEnd, braceStart;
        if (_enemySwitchTimeline)
        {
            total = Tuning.GatherDuration + Tuning.ElementReformDuration;
            float remaining = _enemy.GatherTimer > 0f
                ? _enemy.GatherTimer + Tuning.ElementReformDuration
                : _enemy.ReformTimer;
            elapsed = total - remaining;
            shatterEnd = Tuning.GatherDuration / total; // press inside their gather
            // Brace only matters when a real entrance is coming — keystone
            // returns and echo switches land nothing worth halving.
            braceStart = _enemy.IncomingEntrancePending ? 0.55f : -1f;
        }
        else if (_enemy.CurrentElement == Element.Fire && _enemy.IsEmpowered
                 && _enemy.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup)
        {
            total = Tuning.FlareWindup;
            elapsed = total - (Tuning.FlarePeriod - _enemy.FlareClock);
            shatterEnd = -1f;
            braceStart = 0.25f;
        }
        else
        {
            _timingBar.Visible = false;
            return;
        }

        _timingBar.Visible = true;
        bool hasShatter = shatterEnd > 0f;
        _timingZoneShatter.Visible = _timingLabelShatter.Visible = hasShatter;
        if (hasShatter)
        {
            _timingZoneShatter.Size = new Vector2(TimingW * shatterEnd, TimingH);
            _timingLabelShatter.Position = new Vector2(0, TimingH + 6);
        }
        bool hasBrace = braceStart >= 0f;
        _timingZoneBrace.Visible = _timingLabelBrace.Visible = hasBrace;
        if (hasBrace)
        {
            _timingZoneBrace.Position = new Vector2(TimingW * braceStart, 0);
            _timingZoneBrace.Size = new Vector2(TimingW * (1f - braceStart), TimingH);
            _timingLabelBrace.Position = new Vector2(TimingW * braceStart, TimingH + 6);
        }
        _timingCursor.Position = new Vector2(TimingW * Mathf.Clamp(elapsed / total, 0f, 1f) - 2, -6);
    }

    private void UpdateBannerAndResult(float dt)
    {
        _bannerTime += dt;
        switch (_battle.State)
        {
            case BattleState.Intro:
                _banner.Visible = true;
                _banner.Text = "READY...";
                _bannerTime = 0f; // CLASH! gets its full window once Fighting starts
                break;
            case BattleState.Fighting when _bannerTime < 2.3f:
                _banner.Text = "CLASH!";
                _banner.Modulate = new Color(1, 1, 1, Mathf.Clamp(2.3f - _bannerTime, 0f, 1f));
                break;
            case BattleState.Result when !_resultShown:
                _resultShown = true;
                _banner.Visible = false;
                _resultTitle.Text = _battle.WinnerSide == 0 ? "VICTORY" : "DEFEAT";
                _resultTitle.AddThemeColorOverride("font_color",
                    _battle.WinnerSide == 0 ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.4f, 0.35f));
                _resultPanel.Visible = true;
                (_resultPanel.GetChild(0).GetChild(1) as Button)?.GrabFocus();
                break;
            default:
                _banner.Visible = _battle.State == BattleState.Fighting && _bannerTime < 2.3f;
                break;
        }
    }
}
