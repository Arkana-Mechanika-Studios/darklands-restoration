using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// Gameplay fixes of the Fixed rule set that had no test of their fixed path (rules set A3): each test shows the original's behaviour
/// with the flag off and the fixed one with the flag on, on the same situation.
/// </summary>
public sealed class FixedRulesBothPathsTests
{
    /// <summary>FixRecordedListShift: place-assign 37 overwrites every occupied slot; fixed, the list shifts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingAValueOverwritesOrShiftsTheList(bool fixedGame)
    {
        var s = TestGame.Create(location: 5);
        s.Behaviour.FixRecordedListShift = fixedGame;
        LocationRules.Set(s, 9, 37, 11);
        LocationRules.Set(s, 9, 37, 22);
        Assert.Equal(fixedGame ? [22, 11, 0] : [22, 22, 0], s.Locations[9].Recorded);
        LocationRules.Set(s, 9, 37, 33);
        Assert.Equal(fixedGame ? [33, 22, 11] : [33, 33, 33], s.Locations[9].Recorded);
        LocationRules.Set(s, 9, 37, 44);                       // full: nothing changes either way
        Assert.Equal(fixedGame ? [33, 22, 11] : [33, 33, 33], s.Locations[9].Recorded);
    }

    /// <summary>FixItemQualityByFlags: item-grade-flags tests item 0's kind bits for every stack; fixed, each stack's own.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBestQualityByKindTestsItemZerosOrEachStacksBits(bool fixedGame)
    {
        var s = TestGame.Create();
        s.Behaviour.FixItemQualityByFlags = fixedGame;
        var gretchen = s.Party.Slots[0];
        Assert.All(gretchen.Pack, st => Assert.Equal(25, st.Quality));    // her three stacks: kind bits 0, quality 25
        gretchen.Pack.Add(new ItemStack(3, 0, 40, 1, 0));                 // a hand weapon (kind bits 1), quality 40
        // item 0's bits are 0: the original answers 0 for kind 1 and the best of everything for kind 0
        Assert.Equal(fixedGame ? 40 : 0, Inventory.BestQualityByFlags(s, 0, 0x1));
        Assert.Equal(fixedGame ? 25 : 40, Inventory.BestQualityByFlags(s, 0, 0));
    }

    /// <summary>FixKeepItemsOffByOne: item-keep-percent starts one past the last stack (an empty pack's count becomes −1); fixed, at the last stack.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepingItemsStartsPastOrAtTheLastStack(bool fixedGame)
    {
        var s = TestGame.Create();
        s.Behaviour.FixKeepItemsOffByOne = fixedGame;
        var gunther = s.Party.Slots[1];
        gunther.Pack.Clear();
        Inventory.KeepEach(s, 1, -1);                          // every roll fails
        Assert.Equal(fixedGame ? 0 : -1, gunther.Pack.Stacks);
        Inventory.Add(s, 1, 60);
        // the original writes the next item to entry -1 (the member before's last entry); fixed, it is Gunther's
        Assert.Equal(fixedGame ? 1 : 0, gunther.Pack.Count);
        Assert.Equal(fixedGame ? 0 : 60, s.Party.Slots[0].Pack[63].Code);

        // a full drop: the original's first roll is for the entry past the last; fixed, every stack goes and the count is 0
        var gretchen = s.Party.Slots[0];
        var stacks = gretchen.Pack.Stacks;
        Assert.True(stacks > 0);
        Inventory.KeepEach(s, 0, -1);
        Assert.Equal(fixedGame ? 0 : -1, gretchen.Pack.Stacks);
    }

    /// <summary>FixBattleSeed: battle-driver gives a location's battle the party seed twice; fixed, once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALocationsBattleSeedHasThePartySeedTwiceOrOnce(bool fixedGame)
    {
        var s = TestGame.Create(location: 5);
        s.Behaviour.FixBattleSeed = fixedGame;
        s.Party.Seed = 1000;
        Assert.Equal(fixedGame ? 5 + 1000 : 5 + 1000 + 1000, PartyRules.BattleSeed(s, -2));
        // a given seed gets the party seed once either way
        Assert.Equal(77 + 1000, PartyRules.BattleSeed(s, 77));
    }

    /// <summary>FixFameCost: the fame cost sets the fame to cost − fame; fixed, fame − cost.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFameCostLeavesCostMinusFameOrFameMinusCost(bool fixedGame)
    {
        var s = TestGame.Create();
        s.Behaviour.FixFameCost = fixedGame;
        PartyQueries.Set(s, 4, 331);
        var cost = 15 * (331 / 200);                           // size 4: 15 x max(1, fame / 200)
        Assert.Equal(fixedGame ? 331 - cost : cost - 331, Practice.ChargeFame(s, 4));
        Assert.Equal(fixedGame ? 331 - cost : cost - 331, s.Party.Fame);
    }
}
