using System;
using System.Threading.Tasks;
using Godot;

namespace KiBattle;

/// <summary>
/// Headless verification harness (run with `-- --smoke`). Spawns scripted and
/// AI-vs-AI battles at high time scale, asserts gameplay invariants, prints
/// SMOKE OK / SMOKE FAIL and exits with a matching code.
/// </summary>
public partial class SmokeTest : Node
{
    public override async void _Ready()
    {
        try
        {
            Engine.TimeScale = 8.0;
            await RateCheck();
            await ExhaustCheck();
            await AiBattleCheck();
            await HardVsEasyCheck();
            GD.Print("SMOKE OK");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"SMOKE FAIL: {e.Message}\n{e.StackTrace}");
            GetTree().Quit(1);
        }
    }

    private static Loadout MakeLoadout(Element first, BeamType beam = BeamType.Single) => new()
    {
        Elements = new[] { first, (Element)(((int)first + 1) % 8), (Element)(((int)first + 2) % 8) },
        StartingBeam = beam,
    };

    private async Task<BattleManager> SpawnBattle(Loadout left, Loadout right)
    {
        Game.Instance.PlayerLoadout = left;
        Game.Instance.EnemyLoadout = right;
        var battle = GD.Load<PackedScene>("res://scenes/Battle.tscn").Instantiate<BattleManager>();
        battle.ScriptedMode = true;
        AddChild(battle);
        await NextPhysicsFrame();
        return battle;
    }

    private SignalAwaiter NextPhysicsFrame() => ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

    /// <summary>Waits until the battle's game-time clock advances by the given seconds
    /// (TimeScale multiplies physics delta, so frame counting is not sim time).</summary>
    private async Task WaitGameSeconds(BattleManager battle, float seconds)
    {
        float until = battle.FightDuration + seconds;
        while (battle.FightDuration < until && battle.State == BattleState.Fighting)
            await NextPhysicsFrame();
    }

    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new InvalidOperationException(message);
    }

    /// <summary>Fire vs Ice, singles, both channeling: clash must drift toward the enemy at the analytic rate.</summary>
    private async Task RateCheck()
    {
        var battle = await SpawnBattle(MakeLoadout(Element.Fire), MakeLoadout(Element.Ice));
        battle.LeftCaster.WantsChannel = true;
        battle.RightCaster.WantsChannel = true;

        while (battle.State != BattleState.Fighting) await NextPhysicsFrame();
        float startClash = battle.ClashX;
        float startTime = battle.FightDuration;
        await WaitGameSeconds(battle, 5f);

        float elapsed = battle.FightDuration - startTime;
        Assert(elapsed > 0.1f, "battle ended before the rate could be measured");
        float rate = (battle.ClashX - startClash) / elapsed;
        float expected = Tuning.ClashRate * (Tuning.Advantage - Tuning.Disadvantage);
        GD.Print($"[smoke] rate check: rate={rate:0.00000}/s expected={expected:0.00000}/s over {elapsed:0.00}s");
        Assert(Mathf.Abs(rate - expected) <= expected * 0.10f,
            $"clash rate off: {rate:0.00000}/s, expected {expected:0.00000}/s ±10%");

        battle.QueueFree();
        await NextPhysicsFrame();
    }

    /// <summary>Overdriven twin beam must drain to zero and trigger exhaustion.</summary>
    private async Task ExhaustCheck()
    {
        var battle = await SpawnBattle(MakeLoadout(Element.Holy, BeamType.Twin), MakeLoadout(Element.Holy));
        bool exhausted = false;
        battle.LeftCaster.Exhausted += () => exhausted = true;
        battle.LeftCaster.WantsChannel = true;
        battle.LeftCaster.WantsOverdrive = true;

        // Drain 44/s vs regen 5/s → empty at ~2.6 s. Allow 4 s of game time.
        await WaitGameSeconds(battle, 4f);
        GD.Print($"[smoke] exhaust check: exhausted={exhausted} mana={battle.LeftCaster.Mana:0.0}");
        Assert(exhausted, "caster did not exhaust after overdriven twin drain");

        battle.QueueFree();
        await NextPhysicsFrame();
    }

    /// <summary>A full AI-vs-AI battle must end with a winner in a sane duration.</summary>
    private async Task AiBattleCheck()
    {
        var (winner, duration) = await RunAiBattle(Difficulty.Hard, Difficulty.Hard);
        GD.Print($"[smoke] ai battle: winner={winner} duration={duration:0.0}s");
        Assert(winner is 0 or 1, "AI battle produced no winner");
        Assert(duration >= 5f && duration <= Tuning.MaxDuration + 1f,
            $"AI battle duration {duration:0.0}s outside 5–{Tuning.MaxDuration}s");
    }

    /// <summary>Hard AI (with counter-draft) must beat Easy AI at least 8 of 10 fights.</summary>
    private async Task HardVsEasyCheck()
    {
        int hardWins = 0;
        for (int i = 0; i < 10; i++)
        {
            // Right side is Hard and counter-drafts the Easy side's random loadout.
            var (winner, duration) = await RunAiBattle(Difficulty.Easy, Difficulty.Hard);
            GD.Print($"[smoke]   fight {i + 1}: winner={winner} duration={duration:0.0}s");
            if (winner == 1) hardWins++;
        }
        GD.Print($"[smoke] hard vs easy: {hardWins}/10");
        Assert(hardWins >= 8, $"Hard AI won only {hardWins}/10 vs Easy");
    }

    private async Task<(int winner, float duration)> RunAiBattle(Difficulty leftDiff, Difficulty rightDiff)
    {
        var rng = new Random();
        var leftLoadout = AIController.Draft(
            new[] { Element.Fire, Element.Water, Element.Holy }, Difficulty.Easy, AiPersonality.Trickster, rng);
        var rightLoadout = AIController.Draft(leftLoadout.Elements, rightDiff, AiPersonality.Aggressor, rng);

        var battle = await SpawnBattle(leftLoadout, rightLoadout);
        AttachAi(battle, battle.LeftCaster, battle.RightCaster, leftDiff);
        AttachAi(battle, battle.RightCaster, battle.LeftCaster, rightDiff);

        // Diagnostics: track what each side actually did.
        double mulL = 0, mulR = 0, pushL = 0, pushR = 0, odL = 0, odR = 0;
        int samples = 0, exhL = 0, exhR = 0;
        battle.LeftCaster.Exhausted += () => exhL++;
        battle.RightCaster.Exhausted += () => exhR++;

        const float maxGameSeconds = 180f;
        while (battle.State != BattleState.Result && battle.FightDuration < maxGameSeconds)
        {
            await NextPhysicsFrame();
            if (battle.State != BattleState.Fighting) continue;
            var l = battle.LeftCaster;
            var r = battle.RightCaster;
            bool pin = l.Beam == BeamType.Pinpoint || r.Beam == BeamType.Pinpoint;
            mulL += ElementDb.EffectiveMultiplier(l.CurrentElement, r.CurrentElement, pin);
            mulR += ElementDb.EffectiveMultiplier(r.CurrentElement, l.CurrentElement, pin);
            pushL += l.ComputePush(r, battle.ClashX);
            pushR += r.ComputePush(l, 1f - battle.ClashX);
            if (l.IsOverdriving) odL++;
            if (r.IsOverdriving) odR++;
            samples++;
        }
        if (samples > 0)
            GD.Print($"[smoke]     L: mul={mulL / samples:0.00} push={pushL / samples:0.00} od={odL / samples:0.00} exh={exhL}  " +
                     $"R: mul={mulR / samples:0.00} push={pushR / samples:0.00} od={odR / samples:0.00} exh={exhR}  clash={battle.ClashX:0.00}");

        // Timeout = no winner (-1); callers decide whether that is fatal.
        var result = (battle.State == BattleState.Result ? battle.WinnerSide : -1, battle.FightDuration);
        battle.QueueFree();
        await NextPhysicsFrame();
        return result;
    }

    private static void AttachAi(BattleManager battle, Caster target, Caster opponent, Difficulty difficulty)
    {
        battle.AddChild(new AIController
        {
            Target = target,
            Opponent = opponent,
            Battle = battle,
            Difficulty = difficulty,
            Personality = AiPersonality.Aggressor,
        });
    }
}
