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

    // Loadout strip
    private readonly Control[] _elementIcons = new Control[3];
    private readonly ColorRect[] _elementCooldown = new ColorRect[3];
    private readonly Label[] _beamLabels = new Label[3];
    private ColorRect _beamCooldown = null!;

    // Enemy telegraph
    private PanelContainer _telegraph = null!;
    private ColorRect _teleDiamond = null!;
    private Label _teleElement = null!, _teleBeam = null!, _matchup = null!;
    private float _teleFlash;

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

        BuildClashMeter();
        BuildManaBars();
        BuildLoadoutStrip();
        BuildTelegraph();
        BuildBanner();
        BuildResultPanel();

        _enemy.ElementChanged += _ => _teleFlash = 1f;
        _enemy.BeamTypeChanged += _ => _teleFlash = 1f;
    }

    // ---------- construction ----------

    private static Label MakeLabel(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text };
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
    }

    private void BuildLoadoutStrip()
    {
        // Element slots 1/2/3
        for (int i = 0; i < 3; i++)
        {
            var style = ElementDb.Style(_player.Loadout.Elements[i]);
            var slot = new Control { Position = new Vector2(80 + i * 92, 1080 - 175), Size = new Vector2(64, 64) };

            var diamond = new ColorRect
            {
                Color = style.Glow,
                Size = new Vector2(44, 44),
                Position = new Vector2(32, 0),
                Rotation = Mathf.Pi / 4,
            };
            slot.AddChild(diamond);

            var key = MakeLabel($"{i + 1}", 18, new Color(1, 1, 1, 0.8f));
            key.Position = new Vector2(-2, -6);
            slot.AddChild(key);

            var name = MakeLabel(ElementAbbrev[(int)_player.Loadout.Elements[i]], 16);
            name.Position = new Vector2(12, 64);
            slot.AddChild(name);

            _elementCooldown[i] = new ColorRect
            {
                Color = new Color(0, 0, 0, 0.65f),
                Size = new Vector2(64, 0),
                Position = new Vector2(0, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            slot.AddChild(_elementCooldown[i]);

            _elementIcons[i] = slot;
            AddChild(slot);
        }

        // Beam types Q/W/E, to the right of the element diamonds
        for (int i = 0; i < 3; i++)
        {
            var l = MakeLabel($"{"QWE"[i]}  {BeamNames[i]}", 17, new Color(0.85f, 0.85f, 0.95f));
            l.Position = new Vector2(420 + i * 150, 1080 - 168);
            _beamLabels[i] = l;
            AddChild(l);
        }
        _beamCooldown = new ColorRect
        {
            Color = new Color(0.15f, 0.15f, 0.2f, 0.9f),
            Position = new Vector2(420, 1080 - 138),
            Size = new Vector2(0, 5),
        };
        AddChild(_beamCooldown);
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

        _matchup = MakeLabel("● EVEN", 24);
        box.AddChild(_matchup);

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

    private Main? MainNode() => GetTree().Root.GetNodeOrNull<Main>("Main");

    // ---------- per-frame ----------

    public override void _Process(double delta)
    {
        if (!_built)
        {
            Build();
            _built = true;
        }
        float dt = (float)delta;
        UpdateClashMeter();
        UpdateManaBars();
        UpdateLoadoutStrip();
        UpdateTelegraph(dt);
        UpdateBannerAndResult(dt);
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
        for (int i = 0; i < 3; i++)
        {
            bool active = i == _player.ElementSlot;
            _elementIcons[i].Scale = Vector2.One * (active ? 1.18f : 0.95f);
            _elementIcons[i].Modulate = active ? Colors.White : new Color(1, 1, 1, 0.55f);

            float cd = active ? 0f : _player.ElementCooldown / Tuning.ElementSwitchCooldown;
            _elementCooldown[i].Size = new Vector2(64, 64 * cd);
        }
        for (int i = 0; i < 3; i++)
        {
            bool active = (int)_player.Beam == i;
            _beamLabels[i].Modulate = active ? Colors.White : new Color(1, 1, 1, 0.45f);
        }
        _beamCooldown.Size = new Vector2(440 * _player.BeamCooldown / Tuning.BeamSwitchCooldown, 5);
    }

    private Element _lastTeleElement = (Element)(-1);
    private BeamType _lastTeleBeam = (BeamType)(-1);
    private int _lastMatchState = -99;

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

        bool anyPinpoint = _player.Beam == BeamType.Pinpoint || _enemy.Beam == BeamType.Pinpoint;
        float m = ElementDb.EffectiveMultiplier(_player.CurrentElement, _enemy.CurrentElement, anyPinpoint);
        int state = m > 1.05f ? 1 : m < 0.95f ? -1 : 0;
        if (state == _lastMatchState) return;
        _lastMatchState = state;
        switch (state)
        {
            case 1:
                _matchup.Text = "▲ ADVANTAGE";
                _matchup.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
                break;
            case -1:
                _matchup.Text = "▼ COUNTERED";
                _matchup.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.35f));
                break;
            default:
                _matchup.Text = "● EVEN";
                _matchup.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
                break;
        }
    }

    private void UpdateBannerAndResult(float dt)
    {
        _bannerTime += dt;
        switch (_battle.State)
        {
            case BattleState.Intro:
                _banner.Visible = true;
                _banner.Text = "READY...";
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
