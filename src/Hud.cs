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
    }

    private void BuildLoadoutStrip()
    {
        // All 8 souls, keys 1-8; the keystone slot gets a gold ring.
        for (int i = 0; i < 8; i++)
        {
            var style = ElementDb.Style((Element)i);
            var slot = new Control { Position = new Vector2(72 + i * 72, 1080 - 168), Size = new Vector2(52, 52) };

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

            _elementIcons[i] = slot;
            AddChild(slot);
        }

        // Beam types Q/W/E, to the right of the soul strip
        for (int i = 0; i < 3; i++)
        {
            var l = MakeLabel($"{"QWE"[i]}  {BeamNames[i]}", 16, new Color(0.85f, 0.85f, 0.95f));
            l.Position = new Vector2(700 + i * 140, 1080 - 164);
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
        UpdateCoach(dt);

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

            float cd = active ? 0f : _player.ElementCooldown / Tuning.ElementSwitchCooldown;
            _elementCooldown[i].Size = new Vector2(52, 52 * cd);
        }
        for (int i = 0; i < 3; i++)
        {
            bool active = (int)_player.Beam == i;
            _beamLabels[i].Modulate = active ? Colors.White : new Color(1, 1, 1, 0.45f);
        }
        _beamCooldown.Size = new Vector2(400 * _player.BeamCooldown / Tuning.BeamSwitchCooldown, 5);

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
        if (_enemy.GatherTimer > 0f || (_enemy.ReformTimer > 0f && _enemy.GatherTimer <= 0f))
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

        bool enemyIncoming = _enemy.GatherTimer > 0f || _enemy.ReformTimer > 0f;
        bool playerBusy = _player.GatherTimer > 0f || _player.ReformTimer > 0f;

        // 1. React to an incoming entrance: brace.
        if (enemyIncoming && _player.Mana > 25f)
            return (1, "INCOMING — hold SHIFT to BRACE (halves the hit)", danger);
        if (enemyIncoming)
            return (1, "INCOMING — too low on mana to brace, ride it out", danger);

        // 2. Their switch is your shatter window.
        if (playerBusy == false && _player.PipReady && _player.ElementCooldown <= 0f
            && (_enemy.GatherTimer > 0f || _enemy.SinceReformEnd < 0.8f))
            return (2, "SHATTER WINDOW — switch souls (1-8) to strike them mid-switch!", gold);

        // 3. Punish an exhausted enemy.
        if (_enemy.IsExhausted && _player.Mana > 15f)
            return (3, "ENEMY DRAINED — hold SPACE + SHIFT and shove!", gold);

        // 4-5. Telegraphed enemy spikes.
        if (_enemy.CurrentElement == Element.Fire && _enemy.IsEmpowered
            && _enemy.FlareClock > Tuning.FlarePeriod - Tuning.FlareWindup && _player.Mana > 25f)
            return (4, "FLARE COMING — hold SHIFT to brace through it", danger);
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
            return (9, "HOLD THE LINE — keep SPACE down, desperation gives +25%!", danger);

        // 10-11. Mana economy.
        if (_player.IsExhausted)
            return (10, "DRAINED! Next time release SPACE before the bar empties", danger);
        if (_player.Mana < 22f && _player.IsEmpowered && _battle.ClashX > 0.3f)
            return (11, "MANA LOW — release SPACE a moment and recover", calm);

        // 12. Idle pip nudge.
        if (_player.PipReady && !playerBusy && _player.ElementCooldown <= 0f && _player.SinceReformEnd > 6f)
            return (12, "REACTION CHARGED — switch souls (1-8) to unleash it", gold);

        return null;
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
