using System.Reflection;
using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// use-exit (R023+0x1618) copies stairs record n of the level shown with no test of n against the level's stairs count (+0x2FB6,
/// R023+0x162B..0x164A). GitHub issue 3: a member alone on another level, Use Stairs, then another member selected (the view back on
/// the party's level) and Space: his record number is read on the party's level, past its count, and he still goes (lockstep
/// castle-rotenburg-split-reselect, the original's use-exit(2, 3) at clock 9283 with DS:11CA = DS:C623).
/// </summary>
public sealed class StairsRecordPastCountTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int StairsCountAt = 0x2FB6, StairsAt = 0x2FB8;

    [Fact]
    public void ARecordPastTheLevelsStairsCountIsTaken()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x7C, 3434, [(8, 3, 8), (2, 2, 4), (-1, 0, 0)]));
        var level = t.Level;
        var count = level.GetByte(StairsCountAt);
        Assert.True(count >= 1);
        // the slot just past the count gets a copy of record 0 (to another level)
        for (var k = 0; k < 14; k++)
        {
            level.SetByte(StairsAt + 14 * count + k, level.GetByte(StairsAt + k));
        }
        var to = (sbyte)level.GetByte(StairsAt + 6);
        var who = t.Battle.Leader;
        Assert.NotEqual(to, t.Battle[who].Floor);
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [count, who]);
        Assert.Equal(to, t.Battle[who].Floor);
    }
}
