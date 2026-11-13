using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixLeaderStairsRecordOfShownLevel (GitHub issue 3): with the party split and the battle running, U sends the
/// leader through his stairs-back record number read on the level SHOWN (fight-keys R016+0x014E, use-exit R023+0x1618). With the view
/// on the lone member's floor (selected, then deselected) the leader takes a record of a floor he is not on (Faithful); fixed: his own
/// level's record. The lone member stays in both modes.
/// </summary>
public sealed class LeaderStairsRecordOfShownLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int StairsAt = 0x2FB8;
    private const int Who = 1;

    /// <summary>Member 1 alone through record 0 of the party's level, the view moved to his level, the battle running; the leader's
    /// stairs-back record number 0 (record 0 of the party's level leads to the lone member's level).</summary>
    private static (TacticalBattle T, int Leader, int PartyLevel, int HisLevel) Split(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(8, 3, 8), (2, 2, 4), (-1, 0, 0)]), state => state.Behaviour = behaviour);
        var party = t.Battle.LevelShown;
        Assert.True(t.LevelCount >= 3);
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Who]);
        var his = t.Battle[Who].Level;
        Assert.NotEqual(party, his);
        var leader = t.Battle.Leader;
        Assert.NotEqual(Who, leader);
        Assert.Equal(party, t.Battle[leader].Level);
        t.SwitchLevel(his);
        t.PauseByte = 0;
        t.Battle[leader].StairsBack = 0;
        // record 0 of the lone member's level (the level shown) names a third level
        var third = Enumerable.Range(0, t.LevelCount).First(l => l != party && l != his);
        t.LevelAt(his).SetByte(StairsAt + 6, third);
        return (t, leader, party, his);
    }

    [Fact]
    public void FaithfulTakesTheShownLevelsRecord()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, leader, party, his) = Split(OriginalBehaviour.Faithful);
        t.UseKey(-1);
        // the leader leaves by record 0 of the lone member's level: the third level, a floor nobody went to
        Assert.NotEqual(party, t.Battle[leader].Level);
        Assert.NotEqual(his, t.Battle[leader].Level);
        Assert.Equal(his, t.Battle[Who].Level);
    }

    [Fact]
    public void TheFixTakesHisOwnLevelsRecord()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, leader, party, his) = Split(OriginalBehaviour.DataSafe);
        var expected = (sbyte)t.LevelAt(party).GetByte(StairsAt + 6);
        Assert.Equal(his, expected);
        t.UseKey(-1);
        // record 0 of his own level: the stairs the lone member took, so the leader joins him
        Assert.Equal(expected, t.Battle[leader].Level);
        Assert.Equal(his, t.Battle[Who].Level);
        // the view: the level shown before U (the lone member's), not the leader's old level
        Assert.Equal(his, t.Battle.LevelShown);
    }

    [Fact]
    public void WithHisOwnLevelShownBothModesAgree()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var floors = new List<int>();
        foreach (var behaviour in new[] { OriginalBehaviour.Faithful, OriginalBehaviour.DataSafe })
        {
            var (t, _, _, _) = Split(behaviour);
            // the leader alone on the shown level instead: his own record is the one read in either mode
            t.Battle.Leader = Who;
            t.Battle[Who].StairsBack = 0;
            t.UseKey(-1);
            floors.Add(t.Battle[Who].Level);
        }
        Assert.Equal(floors[0], floors[1]);
    }
}
