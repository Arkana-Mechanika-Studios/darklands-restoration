using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixLeaderKeysOnOtherLevel (split audit row 3): the leader alone on another level, the view on the party's level (not
/// split for the keys), the battle running, U. Faithful: fight-keys R016+0x1182 sends him through the stairs record from the shown
/// level's place tests; fixed: nothing happens.
/// </summary>
public sealed class LeaderKeysOnOtherLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static (TacticalBattle T, int Party, int His, int Leader, MapPoint Before) Press(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.LevelShown;
        var leader = t.Battle.Leader;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, leader]);
        var his = t.Battle[leader].Level;
        Assert.NotEqual(party, his);
        Assert.Equal(party, t.Battle.LevelShown);
        Assert.Equal(party, t.Battle.PartyLevel);
        // he walks off the arrival spot on his level
        var c = t.Battle[leader];
        c.Position = new MapPoint(c.Position.X + 200, c.Position.Y + 100);
        var before = c.Position;
        t.PauseByte = 0;
        c.StairsToTake = 0;
        t.Menus.Set(OrdersItem.UseStairs, 1);
        typeof(TacticalBattle).GetProperty("MoveMode", Any)!.SetValue(t, 0);
        t.UseKey(-1);
        return (t, party, his, leader, before);
    }

    [Fact]
    public void FaithfulTakesTheShownLevelsStairs()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, his, leader, before) = Press(OriginalBehaviour.Faithful);
        // record 0 of the party's level leads to his level: he is placed again at its arrival spot, from wherever he stood
        Assert.Equal(his, t.Battle[leader].Level);
        Assert.NotEqual(before, t.Battle[leader].Position);
    }

    [Fact]
    public void TheFixDoesNothing()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, his, leader, before) = Press(OriginalBehaviour.DataSafe);
        Assert.Equal(his, t.Battle[leader].Level);
        Assert.Equal(before, t.Battle[leader].Position);
        Assert.Equal(party, t.Battle.LevelShown);
    }
}
