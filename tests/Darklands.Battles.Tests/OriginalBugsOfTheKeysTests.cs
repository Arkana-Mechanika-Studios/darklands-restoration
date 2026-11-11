using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// Three original bugs reachable in play and reproduced behind <see cref="OriginalBehaviour"/> flags (faithful = the bug, the fix =
/// the behaviour the rewrite had before): the stairs key with nobody selected (R016+0x014E), Esc with nobody selected (R016+0x13D4)
/// and the stairs record's wall mark lost by level-change (R023+0x1618 / R024+0x05B4).
/// </summary>
public sealed class OriginalBugsOfTheKeysTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int StairsAt = 0x2FB8;

    private static TacticalBattle Start(OriginalBehaviour behaviour, int battlefield = 0x5E, int seed = 4545)
    {
        var (_, tactics) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(battlefield, seed, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), state => state.Behaviour = behaviour);
        tactics.PendingOrderMember = -1;
        if (tactics.Selected != -1)
        {
            tactics.CancelOrder();
        }
        if (tactics.Paused)
        {
            tactics.TogglePause();
        }
        return tactics;
    }

    /// <summary>R016+0x014E..0x0169: paused with the party split, the U key's stairs order copies entry [DS:A6B2] of two member tables; nobody selected: entry -1.</summary>
    [Theory]
    [InlineData(false, 0x5A)]
    [InlineData(true, 0x00)]
    public void TheStairsKeyWithoutSelectionCopiesTheEntryBeforeTheTables(bool fix, int expected)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start(new OriginalBehaviour { FixStairsKeyWithoutSelection = fix });
        // the party split over two levels: the second record's floor differs from the level shown
        var other = t.Battle.FloorShown == 0 ? 1 : 0;
        t.Battle.PartyFloor = other;
        t.TogglePause();
        Assert.True(t.Paused);
        t.Menus.Set(OrdersItem.UseStairs, 1);
        t.StairsBackBeforeTable = 0x5A;
        t.StairsExitBeforeTable = 0;
        t.UseKey(-1);
        Assert.Equal(expected, t.StairsExitBeforeTable);
        Assert.Equal(0x5A, t.StairsBackBeforeTable);
    }

    /// <summary>R016+0x13D4: with nobody selected the flee bit of D6EA lands on the pause byte DS:A695: 1 becomes 0x11 (D6EA = 1), where the tests of "paused" and "running" both refuse it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscWithNobodySelectedWritesTheFleeBitToThePauseByte(bool fix)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start(new OriginalBehaviour { FixEscapeWithoutSelectionPauseByte = fix });
        t.TogglePause();
        Assert.Equal(1, t.PauseByte);
        typeof(TacticalBattle).GetField("_fleeBefore", Any)!.SetValue(t, true);
        t.CancelOrder();
        if (fix)
        {
            Assert.Equal(1, t.PauseByte);
            Assert.True(t.Paused);
            return;
        }
        Assert.Equal(0x11, t.PauseByte);
        Assert.False(t.Paused);
        // not running either: the clock-gated parts of a tick stay frozen
        var before = t.Battle[t.Battle.Leader].Position;
        t.Battle.Clock = t.NextTickAt;
        t.Tick();
        Assert.Equal(before, t.Battle[t.Battle.Leader].Position);
        // Space takes the resume path (R016+0x074E: any value but 0), and the byte is 0 again
        t.TogglePause();
        Assert.Equal(0, t.PauseByte);
    }

    /// <summary>
    /// The screen with the pause byte 0x11: the clock stands (DS:07D3 still held by the pause, R016+0x075A .. 0x144D), the pointer is the order
    /// pointer (root 0000:00A0 tests == 0) and the Orders item Resume resumes (R016+0x1436 through root 09C0:1941, no test of the byte).
    /// </summary>
    [Fact]
    public void TheScreenTreatsThePauseByte0x11AsAPause()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), state => state.Behaviour = new OriginalBehaviour());
        t.PendingOrderMember = -1;
        if (t.Selected != -1)
        {
            t.CancelOrder();
        }
        if (t.PauseByte == 0)
        {
            t.TogglePause();
        }
        typeof(TacticalBattle).GetField("_fleeBefore", Any)!.SetValue(t, true);
        t.CancelOrder();
        Assert.Equal(0x11, t.PauseByte);
        var clock = t.Battle.Clock;
        typeof(Darklands.Presentation.Battles.BattleScreen).GetMethod("Advance", Any)!.Invoke(screen, [1000L]);
        Assert.Equal(clock, t.Battle.Clock);
        typeof(Darklands.Presentation.Battles.BattleScreen).GetMethod("Resume", Any, Type.EmptyTypes)!.Invoke(screen, []);
        Assert.Equal(0, t.PauseByte);
        typeof(Darklands.Presentation.Battles.BattleScreen).GetMethod("Advance", Any)!.Invoke(screen, [1000L]);
        Assert.NotEqual(clock, t.Battle.Clock);
    }

    /// <summary>With D6EA = 0 Esc leaves the pause byte alone, and the place actions' clear of bit 0x10 (R016+0x15BE) brings 0x11 back to 1.</summary>
    [Fact]
    public void EscWithoutAFleeBitKeepsThePauseByteAndAPlaceActionClearsTheBit()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start(new OriginalBehaviour());
        t.TogglePause();
        typeof(TacticalBattle).GetField("_fleeBefore", Any)!.SetValue(t, false);
        t.CancelOrder();
        Assert.Equal(1, t.PauseByte);
        t.PauseByte = 0x11;
        t.ChoosePlace(PlaceAction.Halt);
        Assert.Equal(1, t.PauseByte);
    }

    /// <summary>M with nobody selected (the Attack menu's Missile entry on): R016+0x156E has no test of DS:A6B2, so the order becomes 0x80 and
    /// 15BE clears bit 0x10 of the pause byte; with the fix the byte is left alone.</summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0x11)]
    public void MissileWithNobodySelectedTakesTheOrder(bool fixedByte, int pauseByte)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start(new OriginalBehaviour { FixEscapeWithoutSelectionPauseByte = fixedByte });
        Assert.Equal(-1, t.Selected);
        Assert.Equal(-1, t.OrderAction);
        t.PauseByte = 0x11;
        t.ChooseOrder(0x80);
        Assert.Equal(0x80, t.OrderAction);
        Assert.Equal(pauseByte, t.PauseByte);
        // W / F (action 1) are reached only with a selection and change nothing here
        t.ChooseOrder(1);
        Assert.Equal(0x80, t.OrderAction);
    }

    private static (TacticalBattle Tactics, int Record, int To, int Wall) Prepare(OriginalBehaviour behaviour)
    {
        var t = Start(behaviour, 0x0B, 4242);
        var level = t.Level;
        var to = (sbyte)level.GetByte(StairsAt + 6);
        const int wall = 1;
        level.SetByte(StairsAt + 7, wall);
        // the planted byte is the level file's too (a level left without DS:D4E4 is read back from its file)
        typeof(TacticalBattle).GetMethod("SaveLevelFiles", Any)!.Invoke(t, []);
        var spot = new MapPoint(level.GetWord(StairsAt) + level.GetByte(StairsAt + 4) / 2, level.GetWord(StairsAt + 2) + level.GetByte(StairsAt + 5) / 2);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (t.Battle[i].IsAble)
            {
                t.Battle[i].Position = spot;
            }
        }
        return (t, 0, to, wall);
    }

    private static void TakeFirstStairs(TacticalBattle t, bool changed)
    {
        t.LevelChanged = changed;
        var who = t.Battle.Leader;
        typeof(TacticalBattle).GetMethod("TakeExit", Any)!.Invoke(t, [0, who]);
    }

    /// <summary>R023+0x1618: the 0xFF written to the stairs record's +0x07 is saved by the level switch only when DS:D4E4 is set.</summary>
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 0xFF)]
    [InlineData(true, false, 0xFF)]
    public void TheWallMarkOfTheStairsIsLostWhenTheLevelWasNotChanged(bool fix, bool changed, int expected)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, to, wall) = Prepare(new OriginalBehaviour { FixStairsWallMarkLost = fix });
        Assert.Equal(wall, t.Level.GetByte(StairsAt + 7));
        var from = t.Battle.FloorShown;
        TakeFirstStairs(t, changed);
        // one member alone takes the stairs: the party stays, the level shown is the one left again (its switch back saved the new level)
        Assert.Equal(from, t.Battle.FloorShown);
        Assert.NotEqual(to, t.Battle.FloorShown);
        Assert.Equal(to, t.Battle[t.Battle.Leader].Floor);
        Assert.Equal(expected, t.LevelAt(from).GetByte(StairsAt + 7));
    }
    /// <summary>R024+0x059A reloads a level from its file at each switch to it; R024+0x05B4 saves the level left only when DS:D4E4 is set:
    /// any change made without it (a sprung trap's sign, a found trap) is lost by a switch away and back (split audit row 18).</summary>
    [Theory]
    [InlineData(false, 0x5A)]
    [InlineData(true, 0xA5)]
    public void AChangeWithoutTheLevelChangedFlagIsLostByASwitch(bool changed, int expected)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start(OriginalBehaviour.Faithful, 0x0B, 4242);
        Assert.True(t.LevelCount >= 2);
        var from = t.Battle.FloorShown;
        const int at = 0x33F0;
        t.Level.SetByte(at, 0x5A);
        typeof(TacticalBattle).GetMethod("SaveLevelFiles", Any)!.Invoke(t, []);
        t.Level.SetByte(at, 0xA5);
        t.LevelChanged = changed;
        t.SwitchLevel((from + 1) % t.LevelCount);
        t.SwitchLevel(from);
        Assert.Equal(expected, t.Level.GetByte(at));
    }
}
