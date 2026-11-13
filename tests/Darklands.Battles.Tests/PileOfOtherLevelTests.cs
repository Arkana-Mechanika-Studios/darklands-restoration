using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixPileOfOtherLevel (split audit row 29): the lone member falls on his level while the party's level is shown.
/// Faithful: fighter-dying R014+0x2324 puts his pile on the level shown (DS:11CA); fixed: on his own level.
/// </summary>
public sealed class PileOfOtherLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Lone = 1;

    private static (TacticalBattle T, int Party, int His, LootPile Pile) Fall(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.FloorShown;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Floor;
        Assert.NotEqual(party, his);
        Assert.Equal(party, t.Battle.FloorShown);
        var slot = (AnimationSlot)typeof(TacticalBattle).GetMethod("SetOf", Any)!.Invoke(t, [Lone])!;
        var before = t.LootPiles.Count;
        typeof(TacticalBattle).GetMethod("LeavePile", Any)!.Invoke(t, [t.Battle[Lone], slot]);
        Assert.Equal(before + 1, t.LootPiles.Count);
        return (t, party, his, t.LootPiles[^1]);
    }

    [Fact]
    public void FaithfulLeavesThePileOnTheShownLevel()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, party, _, pile) = Fall(OriginalBehaviour.Faithful);
        Assert.Equal(party, pile.Level);
    }

    [Fact]
    public void TheFixLeavesItOnHisOwnLevel()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, his, pile) = Fall(OriginalBehaviour.DataSafe);
        Assert.Equal(his, pile.Level);
        Assert.Equal(t.Battle[Lone].Position, pile.Position);
    }
}
