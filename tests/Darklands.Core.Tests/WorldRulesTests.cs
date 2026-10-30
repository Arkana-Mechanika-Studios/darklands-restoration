using Darklands.Core.Events;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Core.Tests;

public sealed class WorldRulesTests
{
    private static GameState WithCity()
    {
        var s = TestGame.Create(location: 5);
        var bytes = ((TestData)s.Data).CityBytes;
        bytes[0x40] = 6;                         // size
        bytes[0x56] = 40;                        // ordinal
        bytes[0x58] = 1;                         // city type
        bytes[0x5A] = 3;                         // the other type word
        bytes[0x5E] = 0x14;                      // fortress, cathedral
        bytes[0x5F] = 0x08;                      // docks
        for (var k = 0x62; k <= 0x6A; k++)
        {
            bytes[k] = (byte)(k - 0x60);         // shop qualities 2..10
        }
        bytes[0x6C] = 160;                       // festival day
        return s;
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 3)]     // merchant (0x63)
    [InlineData(4, 2)]     // blacksmith (0x62)
    [InlineData(6, 7)]     // bowyer (0x67)
    [InlineData(11, 6)]    // the unknown shop (0x66)
    [InlineData(10, 9)]
    [InlineData(9, 10)]
    [InlineData(15, 4)]    // fortress bit
    [InlineData(29, 0x10)] // cathedral bit
    [InlineData(18, 0x800)]
    [InlineData(24, 0)]
    [InlineData(19, 0)]    // no case: 0
    [InlineData(42, 42)]   // past the table: the code itself
    [InlineData(40, 1)]
    [InlineData(41, 3)]
    public void LocationQueriesReadTheCurrentCityRecord(int what, int expected)
    {
        var s = WithCity();
        Assert.Equal(expected, LocationRules.Query(s, 200, what));   // whatever location is asked about
    }

    [Fact]
    public void ReputationAndItsBand()
    {
        var s = WithCity();
        s.Locations[7].Reputation = -40;
        Assert.Equal(-40, LocationRules.Query(s, 7, 0));
        Assert.Equal(4, LocationRules.Query(s, 7, 1));
        Assert.Equal([5, 4, 3, 2, 1, 0], new[] { -75, -41, -10, 10, 50, 51 }.Select(LocationRules.ReputationBand));
    }

    [Fact]
    public void SeedSizeAndFestivalDependOnWhereThePartyIs()
    {
        var s = WithCity();
        Assert.Equal(40 + s.Party.Seed, LocationRules.Query(s, 5, 33));
        s.Date = s.Date with { Month = 5 };                   // day 151 of the year: within 14 of 160
        Assert.Equal(1, LocationRules.Query(s, 5, 35));
        s.CurrentLocation = 150;
        Assert.Equal(2, LocationRules.Query(s, 5, 2));
        Assert.Equal(150 + s.Party.Seed, LocationRules.Query(s, 5, 33));
        s.CurrentLocation = GameState.Wilderness;
        s.MapPosition = new MapPosition(10, 20);
        Assert.Equal(1, LocationRules.Query(s, 5, 2));
        Assert.Equal(30 + s.Party.Seed, LocationRules.Query(s, 5, 33));
    }

    [Fact]
    public void SettingShopsSixAndElevenSwapsThem()
    {
        var s = WithCity();
        LocationRules.Set(s, 5, 6, 50);
        Assert.Equal(50, LocationRules.Query(s, 5, 11));
        Assert.Equal(7, LocationRules.Query(s, 5, 6));

        s.Behaviour.FixShopSetSwap = true;
        LocationRules.Set(s, 5, 6, 60);
        Assert.Equal(60, LocationRules.Query(s, 5, 6));
    }

    [Fact]
    public void RecordingAValueOverwritesEveryOccupiedSlot()
    {
        var s = WithCity();
        LocationRules.Set(s, 9, 37, 11);
        LocationRules.Set(s, 9, 37, 22);
        Assert.Equal([22, 22, 0], s.Locations[9].Recorded);
        LocationRules.Set(s, 9, 37, 33);
        Assert.Equal([33, 33, 33], s.Locations[9].Recorded);
        LocationRules.Set(s, 9, 37, 44);                       // full: nothing changes
        Assert.Equal(33, LocationRules.Query(s, 9, 37));
    }

    [Fact]
    public void LocalValuesFlagsAndMark()
    {
        var s = WithCity();
        LocationRules.Set(s, 9, 13, -3);
        LocationRules.Set(s, 9, 32, 0x81);
        s.Locations[9].Mark = 12;
        Assert.Equal(-3, LocationRules.Query(s, 9, 13));
        Assert.Equal(25, LocationRules.Query(s, 9, 12));
        Assert.Equal(-127, LocationRules.Query(s, 9, 32));     // a signed byte
        Assert.Equal(12, LocationRules.Query(s, 9, 38));
    }

    [Fact]
    public void HostilityIgnoresUnrestBecauseOfSwappedArguments()
    {
        var s = WithCity();
        s.Locations[5].Reputation = -20;
        s.Events.CreateHere(EventKinds.Unrest, 100);
        Assert.False(LocationRules.Hostile(s));
        s.Behaviour.FixHostileUnrestTest = true;
        Assert.True(LocationRules.Hostile(s));
        s.Behaviour.FixHostileUnrestTest = false;
        s.Events.CreateHere(EventKinds.Hostile, 100);
        Assert.True(LocationRules.Hostile(s));
    }

    [Fact]
    public void ReputationChangesStayInRange()
    {
        var s = WithCity();
        for (var k = 0; k < 200; k++)
        {
            var r = LocationRules.AdjustReputation(s, 5, 10, 30);
            Assert.InRange(r, -99, 99);
        }
        Assert.True(s.Locations[5].Reputation > 50);
    }

    [Fact]
    public void FindingPlacesOnTheMap()
    {
        var s = TestGame.Create();
        Assert.Equal(7, LocationRules.Distance(0, 0, 6, 9));   // dx 6, dy 3: 6 + 3/2
        Assert.Equal(99, LocationRules.NearestOfType(s, s.Locations[100].X, s.Locations[100].Y, 8));   // 99 and 101 tie: the first wins
        s.Locations[99].Flags = Location.DestroyedFlag;
        Assert.Equal(101, LocationRules.NearestOfType(s, s.Locations[100].X, s.Locations[100].Y, 8));
        Assert.Equal(1, LocationRules.NearestCity(s, 0, 0));      // city 0 stands there
    }

    [Fact]
    public void ACitysPoliticsAreSetUpAndDriftDaily()
    {
        var s = WithCity();
        CityPolitics.SetUpCity(s, 5);
        Assert.True(s.Events.QuestAt(CityPolitics.Group, 5));
        var minus = s.Events.FactValue(2, 1, 5);
        var plus = s.Events.FactValue(2, 2, 5);
        Assert.Equal(0xFEFF, (minus | plus) & 0xFFFF);                  // every bit but 0x100 has a side
        Assert.Equal(0, minus & plus);
        Assert.Equal(15, Enumerable.Range(0, 16).Count(b => CityPolitics.SideOf(s, (short)(1 << b)) != 0));

        TimePassage.Advance(s, 24);
        Assert.Equal(1, s.Events.All().Count(x => x.Event.Kind == EventKinds.LocationFact && x.Event.Type == 0xC));
    }

    [Fact]
    public void SidesAndTheirStrength()
    {
        var s = WithCity();
        // In a city without side facts, the "and not" update creates side 1's fact with
        // ~bit (the event-advance quirk): every other bit goes to side 1.
        CityPolitics.SetSide(s, 0x1, -1);
        Assert.Equal(-1, CityPolitics.SideOf(s, 0x1));
        Assert.Equal(1, CityPolitics.SideOf(s, 0x2));

        CityPolitics.SetSide(s, 0x2, 0);
        CityPolitics.SetSide(s, 0x4, -1);
        Assert.Equal(0, CityPolitics.SideOf(s, 0x2));
        Assert.Equal(-1, CityPolitics.SideOf(s, 0x4));
        // of the 11 counted bits: side -1 holds 2, side 1 holds 8, one is neutral
        Assert.Equal((2 * 2 + 1) * 2, CityPolitics.Strength(s, -1));
        Assert.Equal((8 * 2 + 1) * 2, CityPolitics.Strength(s, 1));
        Assert.Equal(34 * 100 / 10, CityPolitics.Percent(s, 1));
    }
}
