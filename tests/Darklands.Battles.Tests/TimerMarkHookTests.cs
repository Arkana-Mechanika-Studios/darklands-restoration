using Darklands.Cards;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>
/// The oracle's hooks for where the original's timer callbacks landed (tools/oracle/timer_marks.py, lockstep.py --timer-marks): the
/// endurance regain reads the clock anew for each member (R014+0x45CF), trigger-check reads it at its end for the next check
/// (R014+0x459E). The hooks are null in play; a callback given there moves only what is read after it.
/// </summary>
// the hooks are static, and every battle screen clears them at each pass (BattleScreen's loop): another test's battle on another thread
//   could clear this test's hook between its set and its call; so these tests run alone, after the parallel ones (Collection below)
//   and a call from another thread is still ignored
[Collection(TimerMarkHookTests.Alone)]
public sealed class TimerMarkHookTests
{
    /// <summary>The collection that runs by itself: no other test of the assembly runs alongside it.</summary>
    public const string Alone = "TimerMarkHooks";

    private static readonly int[] Delays = [300, 900, 1800, 2700];

    [Fact]
    public void ACallbackInsideTheRegainsLoopGivesTheLaterMembersTheLaterClock()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (_, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]));
        var b = tactics.Battle;
        b.RealCombat = false;
        b.Clock = 100;
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(CharacterStatus.Able, b[i].Status);
            b[i].Endurance = 1;
            b[i].RecoverAt = 0;
        }
        var clock = b.Clock;
        var read = new List<int>();
        try
        {
            // gen-carry-11: one callback between member 0's read and member 1's
            var me = Environment.CurrentManagedThreadId;
            TacticalBattle.RegainRead = slot =>
            {
                if (Environment.CurrentManagedThreadId != me)
                {
                    return;
                }
                read.Add(slot);
                if (slot == 1)
                {
                    b.Clock++;
                }
            };
            Assert.False(tactics.IterateHead());
        }
        finally
        {
            TacticalBattle.RegainRead = null;
        }
        Assert.Equal([0, 1, 2, 3], read.Take(4));
        Assert.Contains(b[0].RecoverAt - clock, Delays);
        for (var i = 1; i < 4; i++)
        {
            Assert.Contains(b[i].RecoverAt - (clock + 1), Delays);
        }
    }

    [Fact]
    public void ACallbackDuringTriggerCheckMovesTheNextCheck()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (_, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]));
        var b = tactics.Battle;
        b.Clock = 100;
        tactics.ActivationsAt = 0;
        var clock = b.Clock;
        try
        {
            // gen-carry-12: the check entered at 40756 set DS:DA4F from the clock 40757 at its end
            var me = Environment.CurrentManagedThreadId;
            TacticalBattle.ClockPhase = phase =>
            {
                if (phase == -8 && Environment.CurrentManagedThreadId == me)
                {
                    b.Clock++;
                }
            };
            tactics.IterateRest();
        }
        finally
        {
            TacticalBattle.ClockPhase = null;
        }
        Assert.Equal((ushort)(clock + 1 + 120), tactics.ActivationsAt);
    }
}

/// <summary>The timer-mark hook tests' collection, run with no other test of the assembly at the same time.</summary>
[CollectionDefinition(TimerMarkHookTests.Alone, DisableParallelization = true)]
public sealed class TimerMarkHookCollection
{
}
