using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixStairsRecordOfShownLevel (GitHub issue 3): the Stairs order of a member alone on another level, given with the
/// view back on the party's level. Faithful: use-exit takes record n of the level shown (R023+0x162B, no count test), whatever lies there;
/// fixed: his own level's record. A record naming a level the battle does not have is ignored, in either mode.
/// </summary>
public sealed class StairsRecordOfShownLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int StairsAt = 0x2FB8;
    private const int Who = 1;

    /// <summary>The Rotenburg-like split: member 1 alone through record 0 of the party's level, the view back on the party's level, his
    /// Stairs order set as the split menu sets it (exit = back).</summary>
    private static (TacticalBattle T, int PartyLevel, int HisLevel, int Back) Split(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(8, 3, 8), (2, 2, 4), (-1, 0, 0)]), state => state.Behaviour = behaviour);
        var party = t.Battle.LevelShown;
        Assert.True(t.LevelShown.GetByte(StairsAt - 2) >= 1);
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Who]);
        var c = t.Battle[Who];
        Assert.NotEqual(party, c.Level);
        Assert.Equal(party, t.Battle.LevelShown);
        c.StairsToTake = c.StairsBack;
        c.Place = PlaceAction.Stairs;
        return (t, party, c.Level, c.StairsBack);
    }

    /// <summary>Plants a record at slot n of the party's level: record 0's bytes with the given level number.</summary>
    private static void Plant(TacticalBattle t, int floor, int slot, int level)
    {
        var image = t.LevelAt(floor);
        for (var k = 0; k < 14; k++)
        {
            image.SetByte(StairsAt + 14 * slot + k, image.GetByte(StairsAt + k));
        }
        image.SetByte(StairsAt + 14 * slot + 6, level);
    }

    private static void Order(TacticalBattle t) => typeof(TacticalBattle).GetMethod("TakeStairsOrder", Any)!.Invoke(t, [Who]);

    [Fact]
    public void FaithfulTakesTheShownLevelsRecord()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, his, back) = Split(OriginalBehaviour.Faithful);
        // the shown (party's) level's slot of his record number leads to his own level: he goes nowhere else
        Plant(t, t.Battle.LevelShown, back, his);
        Order(t);
        Assert.Equal(his, t.Battle[Who].Level);
    }

    [Fact]
    public void TheFixTakesHisOwnLevelsRecord()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, his, back) = Split(OriginalBehaviour.DataSafe);
        Plant(t, t.Battle.LevelShown, back, his);
        var expected = (sbyte)t.LevelAt(his).GetByte(StairsAt + 14 * back + 6);
        Order(t);
        Assert.Equal(expected, t.Battle[Who].Level);
        Assert.NotEqual(his, t.Battle[Who].Level);
        // the view: the party's level again (shown before the order), not his old, empty level
        Assert.Equal(party, t.Battle.LevelShown);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-5)]
    public void ALevelTheBattleDoesNotHaveIsIgnored(int level)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, his, back) = Split(OriginalBehaviour.Faithful);
        Plant(t, t.Battle.LevelShown, back, level);
        Order(t);
        Assert.Equal(his, t.Battle[Who].Level);
    }
}
