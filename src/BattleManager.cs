using System;
using Godot;

namespace KiBattle;

public enum BattleState { Intro, Fighting, Ko, Result }

/// <summary>
/// Root of Battle.tscn. Owns the clash scalar, runs the battle state machine,
/// wires controllers onto the two casters, and drives all battle visuals.
/// </summary>
public partial class BattleManager : Node2D
{
    /// <summary>When true, no controllers are attached; an external harness drives the casters.</summary>
    public bool ScriptedMode;

    public BattleState State { get; private set; } = BattleState.Intro;
    public float ClashX { get; private set; } = Tuning.ClashStart;
    public float FightDuration { get; private set; }
    public int WinnerSide { get; private set; } = -1;

    public Caster LeftCaster = null!;
    public Caster RightCaster = null!;

    public event Action<int>? BattleEnded; // winner side

    private float _introTimer = 1.5f;
    private Label? _debugLabel;

    // Visuals
    private Beam? _leftBeam, _rightBeam;
    private ClashPoint? _clash;
    private BattleCamera? _camera;
    private ColorRect? _flash;
    private float _lastPushLeft, _lastPushRight;
    private int _momentumSign;
    private bool _wasOverdrivingL, _wasOverdrivingR;
    private float _koTimer;
    private double _prevTimeScale = 1.0;
    private int _debugCombo;

    public override void _Ready()
    {
        LeftCaster = GetNode<Caster>("PlayerCaster");
        RightCaster = GetNode<Caster>("EnemyCaster");
        _debugLabel = GetNodeOrNull<Label>("DebugLabel");

        // The headless dummy renderer crashes intermittently on particles/lights;
        // visuals are meaningless there, so drop every visual node instead.
        if (DisplayServer.GetName() == "headless")
        {
            foreach (string n in new[] { "PlayerBeam", "EnemyBeam", "Clash", "Background", "WorldEnvironment", "BattleCamera", "Hud" })
                GetNodeOrNull(n)?.QueueFree();
            GetNodeOrNull("PlayerCaster/Visual")?.QueueFree();
            GetNodeOrNull("EnemyCaster/Visual")?.QueueFree();
        }
        else
        {
            _leftBeam = GetNodeOrNull<Beam>("PlayerBeam");
            _rightBeam = GetNodeOrNull<Beam>("EnemyBeam");
            _clash = GetNodeOrNull<ClashPoint>("Clash");
            _camera = GetNodeOrNull<BattleCamera>("BattleCamera");

            var flashLayer = new CanvasLayer { Layer = 90 };
            _flash = new ColorRect
            {
                Color = new Color(1, 1, 1, 0),
                AnchorRight = 1, AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            flashLayer.AddChild(_flash);
            AddChild(flashLayer);
        }

        LeftCaster.Setup(Game.Instance.PlayerLoadout);
        RightCaster.Setup(Game.Instance.EnemyLoadout);

        LeftCaster.Exhausted += () => _camera?.AddTrauma(0.4f);
        RightCaster.Exhausted += () => _camera?.AddTrauma(0.4f);

        if (!ScriptedMode)
        {
            if (Game.Instance.SmokeMode)
            {
                AttachAi(LeftCaster, RightCaster);
            }
            else
            {
                var pc = new PlayerController();
                pc.Target = LeftCaster;
                AddChild(pc);
            }
            AttachAi(RightCaster, LeftCaster);
        }

        if (ScriptedMode || Game.Instance.SmokeMode) _introTimer = 0f; // no ceremony in tests
    }

    private void AttachAi(Caster target, Caster opponent)
    {
        var ai = new AIController
        {
            Target = target,
            Opponent = opponent,
            Battle = this,
            Difficulty = Game.Instance.Difficulty,
            Personality = Game.Instance.Personality,
        };
        AddChild(ai);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        switch (State)
        {
            case BattleState.Intro:
                _introTimer -= dt;
                if (_introTimer <= 0f) State = BattleState.Fighting;
                break;

            case BattleState.Fighting:
                StepFight(dt);
                break;

            case BattleState.Ko:
                _koTimer -= dt;
                if (_koTimer <= 0f)
                {
                    Engine.TimeScale = _prevTimeScale;
                    State = BattleState.Result;
                }
                break;

            case BattleState.Result:
                break;
        }
        UpdateDebugLabel();
    }

    private void StepFight(float dt)
    {
        FightDuration += dt;
        float regenScale = FightDuration > Tuning.SuddenDeathStart ? 0f : 1f;
        LeftCaster.Tick(dt, regenScale);
        RightCaster.Tick(dt, regenScale);

        float pushLeft = LeftCaster.ComputePush(RightCaster, ClashX);
        float pushRight = RightCaster.ComputePush(LeftCaster, 1f - ClashX);
        _lastPushLeft = pushLeft;
        _lastPushRight = pushRight;

        float escalation = 1f + Mathf.Max(0f, FightDuration - Tuning.EscalationStart) * Tuning.EscalationRatePerSec;
        ClashX += Tuning.ClashRate * escalation * (pushLeft - pushRight) * dt;
        ClashX = Mathf.Clamp(ClashX, 0f, 1f);

        // Momentum-flip and overdrive-engage shakes.
        int sign = Math.Sign(pushLeft - pushRight);
        if (sign != 0 && _momentumSign != 0 && sign != _momentumSign) _camera?.AddTrauma(0.3f);
        if (sign != 0) _momentumSign = sign;
        if (LeftCaster.IsOverdriving && !_wasOverdrivingL) _camera?.AddTrauma(0.2f);
        if (RightCaster.IsOverdriving && !_wasOverdrivingR) _camera?.AddTrauma(0.2f);
        _wasOverdrivingL = LeftCaster.IsOverdriving;
        _wasOverdrivingR = RightCaster.IsOverdriving;

        if (ClashX >= 1f - Tuning.LoseAt) EndBattle(0);
        else if (ClashX <= Tuning.LoseAt) EndBattle(1);
        else if (FightDuration >= Tuning.MaxDuration) EndBattle(JudgeDecision());
    }

    /// <summary>Hard-cap tiebreak: whoever holds clash territory wins; mana breaks a dead tie.</summary>
    private int JudgeDecision()
    {
        if (Mathf.Abs(ClashX - 0.5f) > 0.001f) return ClashX > 0.5f ? 0 : 1;
        return LeftCaster.Mana >= RightCaster.Mana ? 0 : 1;
    }

    private void EndBattle(int winnerSide)
    {
        WinnerSide = winnerSide;
        LeftCaster.WantsChannel = false;
        RightCaster.WantsChannel = false;
        BattleEnded?.Invoke(winnerSide);

        if (ScriptedMode || Game.Instance.SmokeMode)
        {
            State = BattleState.Result;
            return;
        }

        // KO ceremony: slow-mo, white flash, loser dissolves, winner beam surges.
        State = BattleState.Ko;
        _prevTimeScale = Engine.TimeScale;
        Engine.TimeScale = 0.3;
        _koTimer = 0.9f;
        _camera?.AddTrauma(1.0f);
        if (_flash != null) _flash.Color = new Color(1, 1, 1, 0.85f);
        var loser = winnerSide == 0 ? RightCaster : LeftCaster;
        loser.GetNodeOrNull<CasterVisual>("Visual")?.StartDissolve();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateBeams(dt);

        if (_flash != null && _flash.Color.A > 0f)
            _flash.Color = new Color(1, 1, 1, Mathf.Max(0f, _flash.Color.A - dt * 1.2f));

        if (!ScriptedMode && Input.IsActionJustPressed("debug_cycle"))
        {
            _debugCombo = (_debugCombo + 1) % 24;
            LeftCaster.DebugSetCombo((Element)(_debugCombo % 8), (BeamType)(_debugCombo / 8));
        }
    }

    private void UpdateBeams(float dt)
    {
        if (_leftBeam == null || _rightBeam == null) return;

        Vector2 muzzleL = LeftCaster.ToGlobal(CasterVisual.MuzzleLocal);
        Vector2 muzzleR = RightCaster.ToGlobal(CasterVisual.MuzzleLocal);
        Vector2 clashPos = muzzleL.Lerp(muzzleR, ClashX);

        _leftBeam.UpdateBeam(LeftCaster, muzzleL, clashPos, BeamIntensity(LeftCaster, 0), dt);
        _rightBeam.UpdateBeam(RightCaster, muzzleR, clashPos, BeamIntensity(RightCaster, 1), dt);

        if (_clash != null)
        {
            _clash.GlobalPosition = clashPos;
            float combined = (_leftBeam.DisplayIntensity + _rightBeam.DisplayIntensity) * 0.4f;
            float totalPush = _lastPushLeft + _lastPushRight;
            float ratio = totalPush > 0.001f ? _lastPushRight / totalPush : 0.5f;
            _clash.UpdateClash(LeftCaster.Style.Glow, RightCaster.Style.Glow, combined, ratio, dt);
        }

        // Constant low rumble while both beams are pushing hard (idle beams stay calm).
        if (State == BattleState.Fighting && _leftBeam.DisplayIntensity > 0.6f && _rightBeam.DisplayIntensity > 0.6f)
            _camera?.AddTrauma(0.35f * dt * (_leftBeam.DisplayIntensity + _rightBeam.DisplayIntensity) * 0.5f);
    }

    private float BeamIntensity(Caster caster, int side)
    {
        if (State is BattleState.Ko or BattleState.Result)
            return WinnerSide == side ? 2.2f : 0f;
        float reform = caster.ReformTimer > 0f
            ? Mathf.Lerp(1f, 0.3f, caster.ReformTimer / Tuning.ReformDuration)
            : 1f;
        // The beam is always lit: a sputtering wisp when exhausted, a base glow at
        // idle, full when empowered, blazing on overdrive.
        float tier = caster.IsExhausted ? 0.12f : caster.Tier switch
        {
            PowerTier.Overdrive => 1.6f,
            PowerTier.Empowered => 1.0f,
            _ => 0.45f,
        };
        return tier * reform;
    }

    private void UpdateDebugLabel()
    {
        if (_debugLabel == null) return;
        _debugLabel.Text =
            $"state={State} clash={ClashX:0.000} t={FightDuration:0.0}s\n" +
            $"L: {LeftCaster.CurrentElement}/{LeftCaster.Beam} mana={LeftCaster.Mana:0} {(LeftCaster.IsExhausted ? "EXH" : "")}\n" +
            $"R: {RightCaster.CurrentElement}/{RightCaster.Beam} mana={RightCaster.Mana:0} {(RightCaster.IsExhausted ? "EXH" : "")}" +
            (WinnerSide >= 0 ? $"\nWINNER: {(WinnerSide == 0 ? "LEFT" : "RIGHT")}" : "");
    }
}
