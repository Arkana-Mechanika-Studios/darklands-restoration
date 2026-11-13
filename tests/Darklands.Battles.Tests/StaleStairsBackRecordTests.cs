using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixStaleStairsBackRecord (split audit row 14): a member alone on another level whose stairs-back record number is
/// past his level's stairs count (written after a reunion, or never written). Faithful: the Stairs order takes that slot, whatever lies
/// there (here a planted copy of record 0 back to the party's level); fixed: the order does nothing.
/// </summary>
public sealed class StaleStairsBackRecordTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int CountAt = 0x2FB6, StairsAt = 0x2FB8, Lone = 1;

    private static (TacticalBattle T, int Party, int His) Order(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.LevelShown;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Level;
        Assert.NotEqual(party, his);
        t.SwitchLevel(his);
        var lv = t.LevelShown;
        int count = lv.GetByte(CountAt);
        for (var k = 0; k < 14; k++)
        {
            lv.SetByte(StairsAt + 14 * count + k, lv.GetByte(StairsAt + k));
        }
        lv.SetByte(StairsAt + 14 * count + 6, party);
        var c = t.Battle[Lone];
        c.StairsBack = count;
        c.StairsToTake = c.StairsBack;
        typeof(TacticalBattle).GetMethod("TakeStairsOrder", Any)!.Invoke(t, [Lone]);
        return (t, party, his);
    }

    [Fact]
    public void FaithfulTakesTheSlotPastTheCount()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, _) = Order(OriginalBehaviour.Faithful);
        Assert.Equal(party, t.Battle[Lone].Level);
    }

    [Fact]
    public void TheFixTakesNothing()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, his) = Order(OriginalBehaviour.DataSafe);
        Assert.Equal(his, t.Battle[Lone].Level);
    }
}
