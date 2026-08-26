using System;
using Godot;

namespace KiBattle;

public enum BattleState { Intro, Fighting, Ko, Result }

/// <summary>
/// Root of Battle.tscn. Owns the clash scalar, runs the battle state machine,
/// and wires controllers onto the two casters.
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

    public override void _Ready()
    {
        LeftCaster = GetNode<Caster>("PlayerCaster");
        RightCaster = GetNode<Caster>("EnemyCaster");
        _debugLabel = GetNodeOrNull<Label>("DebugLabel");

        LeftCaster.Setup(Game.Instance.PlayerLoadout);
        RightCaster.Setup(Game.Instance.EnemyLoadout);

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
        float escalation = 1f + Mathf.Max(0f, FightDuration - Tuning.EscalationStart) * Tuning.EscalationRatePerSec;
        ClashX += Tuning.ClashRate * escalation * (pushLeft - pushRight) * dt;
        ClashX = Mathf.Clamp(ClashX, 0f, 1f);

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
        State = BattleState.Ko;
        LeftCaster.WantsChannel = false;
        RightCaster.WantsChannel = false;
        BattleEnded?.Invoke(winnerSide);
        // KO presentation (slow-mo, flash, result panel) arrives with the visuals milestone.
        State = BattleState.Result;
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
