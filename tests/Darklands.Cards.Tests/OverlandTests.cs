using Darklands.Content.Formats.Maps;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;
using Darklands.Simulation.Overland;

namespace Darklands.Cards.Tests;

/// <summary>
/// The travel rules of the overland map: the step (R030+0x1300), the encounter tables of
/// overland_loop, the weights (2d44), the camp value (25be), the marker's borders (0c44) and the
/// season palette (2dd6). They run on a made-up map, so they need no game files.
/// </summary>
public sealed class OverlandTests
{
    /// <summary>A DARKLAND.MAP image: big-endian size, row offsets, then runs of up to 7 equal cells per row.</summary>
    private static WorldMap MapOf(int width, int height, Func<int, int, int> terrain) =>
        new(Data(width, height, terrain), []);

    private static WorldMapData Data(int width, int height, Func<int, int, int> terrain)
    {
        var rows = new List<byte[]>();
        for (var row = 0; row < height; row++)
        {
            var bytes = new List<byte>();
            for (var column = 0; column < width;)
            {
                var code = terrain(column, row);
                var run = 1;
                while (run < 7 && column + run < width && terrain(column + run, row) == code)
                {
                    run++;
                }
                bytes.Add((byte)(run << 5 | code));
                column += run;
            }
            rows.Add([.. bytes]);
        }
        var data = new List<byte> { (byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height };
        var offset = 4 + height * 4;
        foreach (var row in rows)
        {
            data.AddRange(BitConverter.GetBytes(offset));
            offset += row.Length;
        }
        foreach (var row in rows)
        {
            data.AddRange(row);
        }
        return WorldMapData.Decode(data.ToArray());
    }

    private static GameState State(uint seed = 1)
    {
        var state = new GameState(new MscRandom(seed))
        {
            Party = DefaultParty.Create(7),
            Date = new GameDate(12, 10, 5, 1402),
        };
        for (var i = 0; i < 414; i++)
        {
            // far away from everything the tests do, in the table's order
            state.Locations.Add(new Location { Index = i, X = 900 + i, Y = 900, Icon = i < Location.CityCount ? 0 : 8, ArrivalCard = 100 + i });
        }
        return state;
    }

    private static Location Place(int index, int x, int y, int icon = 0, int notice = 0, int danger = 0, int arrival = 0, int encounter = 0x62) =>
        new() { Index = index, X = x, Y = y, Icon = icon, NoticeRange = notice, Danger = danger, ArrivalCard = arrival == 0 ? 100 + index : arrival, EncounterCard = encounter };

    private static (int X, int Y) PixelOf(int column, int row) => (column * 16, row * 4 - 4);

    // ---- the step -------------------------------------------------------------------------

    [Fact]
    public void ForcedArrivalChoosesTheCityNearestTheOldPosition()
    {
        // The party stands near city 3; the step goes towards city 4. R030+0x1300 uses
        // party_x/party_y, set before the step, so city 3 is where the party is carried.
        var state = State();
        state.Locations[3] = Place(3, 10, 20);
        state.Locations[4] = Place(4, 60, 20);
        var map = MapOf(100, 60, (_, _) => Terrain.Plains);
        var travel = new OverlandTravel(state, map, kind => kind == 0x5D);
        state.MapPosition = new MapPosition(12, 20);
        travel.Begin();
        var (x, y) = PixelOf(58, 20);
        var result = travel.Step(x, y);
        Assert.Equal(StepOutcome.Arrived, result.Outcome);
        Assert.Equal(103, result.Card);
        Assert.Equal(3, state.CurrentLocation);
    }

    [Fact]
    public void ADragonQuestSortThatIsNotAKnownOneEndsTheChainWithoutAnyEncounter()
    {
        // sel == 1000 near the dragon's lair: `switch (thunk 09C0:1FE5())` has no default, so a sort
        // outside 0..3 leaves the if/else chain and nothing happens (it used to fall back to
        // the terrain table).
        for (uint seed = 1; seed <= 30; seed++)
        {
            var state = State(seed);
            state.Locations[7] = Place(7, 5, 5);
            state.CurrentLocation = 7;
            state.Events.Create(Who.Everybody, 7, 0x5F, EventKinds.Quest, 9, 0, 0, EventLog.Forever, 0, 4);
            state.Events.Create(Who.Everybody, 0, 0x5F, EventKinds.LocationFact, 1, 1, 0, EventLog.Forever, 0, 4);
            var map = MapOf(40, 40, (_, _) => Terrain.Road);
            state.MapPosition = new MapPosition(10, 10);
            var travel = new OverlandTravel(state, map, kind => false);
            travel.Begin();
            Assert.Equal(0, travel.RoadEncounter());
        }
    }

    [Fact]
    public void AnEventOfKind2AReplacesThePlacesOwnDangerInTheEncounterWeight()
    {
        var state = State();
        state.Locations[5] = Place(5, 10, 10, danger: 2);
        var map = MapOf(40, 40, (_, _) => Terrain.Plains);
        var travel = new OverlandTravel(state, map);
        Assert.Equal(3 + 2, travel.EncounterWeight(4, 5));      // marsh weighs 3, the place adds its own 2
        Assert.Equal(1 + 2, travel.EncounterWeight(0, 5));
        Assert.Equal(1, travel.EncounterWeight(0, -1));          // no place near: the terrain alone
        state.Events.Create(Who.Everybody, 5, 0, 0x2A, 0, 7, 0, EventLog.Forever, 0, 0);
        Assert.Equal(3 + 7, travel.EncounterWeight(4, 5));      // the event's 7 instead of the 2
    }

    [Fact]
    public void ANoticedPlaceShowsItsMapMessageOnceAndTheEventIsConsumed()
    {
        var state = State();
        state.Locations[9] = Place(9, 22, 21, notice: 3);
        state.Events.Create(Who.Everybody, 9, 0, 0x2B, 0, 5, 0, EventLog.Forever, 0, 0);
        var map = MapOf(60, 60, (_, _) => Terrain.Plains);
        state.MapPosition = new MapPosition(20, 20);
        var shown = new List<int>();
        var travel = new OverlandTravel(state, map, kind => false) { MapMessage = shown.Add };
        travel.Begin();
        var (x, y) = PixelOf(20, 20);
        travel.Step(x, y);
        Assert.Equal([5], shown);
        travel.Step(x + 1, y);
        Assert.Equal([5], shown);                                // the event was removed
        Assert.Equal(0, state.Events.ValueAt(0x2B, 9));
    }

    [Fact]
    public void ANoticedDragonLairPlaysItsCueEveryStep()
    {
        var state = State();
        state.Locations[9] = Place(9, 22, 21, icon: 15, notice: 3);
        var map = MapOf(60, 60, (_, _) => Terrain.Plains);
        state.MapPosition = new MapPosition(20, 20);
        var cues = new List<int>();
        var travel = new OverlandTravel(state, map, kind => false) { PlayCue = cues.Add };
        travel.Begin();
        var (x, y) = PixelOf(20, 20);
        travel.Step(x, y);
        travel.Step(x, y);
        Assert.Equal([OverlandTravel.NoticeCue, OverlandTravel.NoticeCue], cues);
    }

    [Fact]
    public void DeepWaterStopsTheStepUnlessAlreadyOnWater()
    {
        var state = State();
        var map = MapOf(60, 60, (column, _) => column >= 30 ? Terrain.Ocean : Terrain.Plains);
        state.MapPosition = new MapPosition(29, 20);
        var travel = new OverlandTravel(state, map, kind => false);
        travel.Begin();
        var (x, y) = PixelOf(30, 20);
        Assert.Equal(StepOutcome.Blocked, travel.Step(x, y).Outcome);
        Assert.Equal(29, state.MapPosition.X);                   // the position did not change
        // with a saint's travel blessing (event 0x4B) the water can be crossed
        var blessed = new OverlandTravel(state, map, kind => kind == 0x4B);
        blessed.Begin();
        Assert.NotEqual(StepOutcome.Blocked, blessed.Step(x, y).Outcome);
        // and a party already on the water goes on
        state.MapPosition = new MapPosition(30, 20);
        var onWater = new OverlandTravel(state, map, kind => false);
        onWater.Begin();
        var (x2, y2) = PixelOf(31, 20);
        Assert.NotEqual(StepOutcome.Blocked, onWater.Step(x2, y2).Outcome);
    }

    [Fact]
    public void AnAmbushInCampGivesTheCampEncounters()
    {
        var state = State();
        var map = MapOf(40, 40, (_, _) => Terrain.Plains);
        Assert.Null(new OverlandTravel(state, map, kind => false).AmbushInCamp());
        var ambushed = new OverlandTravel(state, map, kind => kind == 0x5F).AmbushInCamp();
        Assert.True(ambushed is 0x169 or 0x16A);
    }

    [Fact]
    public void AStepOnRoughTerrainTakesAnHourEverySoOftenAndTheHourPassesOnce()
    {
        // 2ac8: a marsh cell (4) costs 50 minutes; 1300 takes one hour off per step at most and
        // the rest carries over in DS:8672.
        var state = State();
        var map = MapOf(60, 60, (_, _) => 4);
        state.MapPosition = new MapPosition(20, 20);
        var hours = 0;
        var travel = new OverlandTravel(state, map, kind => false) { HourPassed = () => hours++ };
        travel.Begin();
        var (x, y) = PixelOf(20, 20);
        travel.Step(x, y);
        Assert.Equal(0, hours);                                  // 50
        travel.Step(x, y);
        Assert.Equal(1, hours);                                  // 100 -> one hour, 40 left
        travel.Step(x, y);
        Assert.Equal(2, hours);                                  // 90 -> one hour, 30 left (never two at once)
        travel.Step(x, y);
        Assert.Equal(3, hours);                                  // 80 -> 20 left
    }

    // ---- the map ---------------------------------------------------------------------------

    [Fact]
    public void ThePartyCellIsTheOneItsPixelsStandOn()
    {
        for (var row = 0; row < 40; row++)
        {
            for (var column = 0; column < 40; column++)
            {
                var (x, y) = PixelOf(column, row);
                Assert.Equal((column, row), WorldMap.CellAt(x, y));
            }
        }
    }

    [Fact]
    public void TheMarkersEdgePixelsReadTheEdgeCells()
    {
        // the marker is kept in the view's 11 .. 247 pixels (OverlandScreen.MoveOnce); the westernmost village of the game (column 3) can be reached: 11 pixels is column 0 or 1
        Assert.True(WorldMap.CellAt(11, 100).Column <= 1);
        Assert.True(WorldMap.CellAt(16 * 327 - 25, 100).Column <= 326);
        Assert.True(WorldMap.CellAt(16 * 327 - 25, 100).Column >= 325);
    }

    [Fact]
    public void ThePlaceIconsAreOnlyLaidForPlacesYouCanEnter()
    {
        var places = new[]
        {
            Place(0, 5, 5),                                      // a city: part of the map itself
            Place(92, 6, 6, icon: 8),                            // a village -> 0x1D
            Place(93, 7, 7, icon: 15),                           // a dragon lair: no icon
            Place(94, 8, 8, icon: 3, arrival: 0x62),             // closed (card 0x62): no icon
            Place(95, 9, 9, icon: 3),                            // 0x33
        };
        var map = new WorldMap(Data(30, 30, (_, _) => Terrain.Plains), places);
        Assert.Equal(Terrain.Plains, map.Cell(5, 5));
        Assert.Equal(0x1D, map.Cell(6, 6));
        Assert.Equal(Terrain.Plains, map.Cell(7, 7));
        Assert.Equal(Terrain.Plains, map.Cell(8, 8));
        Assert.Equal(0x33, map.Cell(9, 9));
        Assert.Equal(Terrain.Castle, map.TerrainAt(9, 9));        // icons count as castles when walking
    }

    [Fact]
    public void TheCampValueCountsMembersSettlementsAndTerrain()
    {
        // 25be: 3 per member; a settlement icon (0x1D) in the 10 x 10 block that starts at the party's cell
        // adds |distance - 6|; rough terrain takes off, a road or the heath adds
        Location Village(int index, int column, int row) => Place(index, column, row, icon: 8);
        var plain = Data(60, 60, (_, _) => Terrain.Plains);
        Assert.Equal(12, new WorldMap(plain, []).CampWatch(4, 20, 20));
        // a village 7 right and 2 down: i = 7 -> 2, j = 2 -> 3 capped to i -> 2; |2 - 6| = 4
        Assert.Equal(12 + 4, new WorldMap(plain, [Village(92, 27, 22)]).CampWatch(4, 20, 20));
        // one at the cell itself: i = 0 -> 5, j = 0 -> 5, res 5, |5 - 6| = 1
        Assert.Equal(12 + 1, new WorldMap(plain, [Village(92, 20, 20)]).CampWatch(4, 20, 20));
        // a settlement behind the party (left) is not in the block
        Assert.Equal(12, new WorldMap(plain, [Village(92, 15, 20)]).CampWatch(4, 20, 20));
        int Camp(int terrain) => MapOf(60, 60, (_, _) => terrain).CampWatch(4, 20, 20);
        Assert.Equal(12 - 1, Camp(5));
        Assert.Equal(12 - 2, Camp(16));
        Assert.Equal(12 - 3, Camp(17));
        Assert.Equal(12 - 5, Camp(22));
        Assert.Equal(12 - 6, Camp(23));
        Assert.Equal(12 + 1, Camp(24));
        Assert.Equal(12 + 2, Camp(8));
    }

    // ---- the season ----------------------------------------------------------------------------

    private sealed class Bytes : SeasonPalette.IPaletteBytes
    {
        private readonly int[] _values = new int[768];

        public int this[int offset]
        {
            get => _values[offset];
            set => _values[offset] = value;
        }
    }

    [Theory]
    [InlineData(0, 1, 15)]
    [InlineData(2, 20, 15)]
    [InlineData(11, 5, 15)]
    [InlineData(3, 1, 0x68)]
    [InlineData(4, 15, 0x68)]
    [InlineData(7, 31, 0x68)]
    [InlineData(8, 1, 0x68)]
    [InlineData(8, 6, 0x68)]
    [InlineData(8, 7, 6)]
    [InlineData(9, 1, 6)]
    [InlineData(10, 4, 6)]
    [InlineData(10, 5, 15)]
    public void TheMapBackgroundFollowsTheMonthAndDay(int month, int day, int expected)
    {
        Assert.Equal(expected, SeasonPalette.Apply(new Bytes(), month, day).Background);
    }

    [Fact]
    public void WinterRewritesThePaletteOnceAndSummerLeavesItAlone()
    {
        var palette = new Bytes();
        var first = SeasonPalette.Apply(palette, 0, 10);
        Assert.True(first.Changed);
        Assert.Equal(0x2E, palette[0xDE]);
        Assert.Equal(0x3F, palette[0x22D]);
        Assert.False(SeasonPalette.Apply(palette, 0, 10).Changed);   // already applied
        var summer = new Bytes();
        Assert.False(SeasonPalette.Apply(summer, 5, 10).Changed);
        Assert.Equal(0, summer[0xDE]);
    }

    [Fact]
    public void NovemberFadesInInStages()
    {
        var palette = new Bytes();
        SeasonPalette.Apply(palette, 10, 1);
        Assert.Equal(0x2E, palette[0xDE]);
        Assert.Equal(0, palette[0xED]);       // day > 2 not yet
        SeasonPalette.Apply(palette, 10, 3);
        Assert.Equal(0x2E, palette[0xED]);
        Assert.Equal(0, palette[0x13B]);      // day > 3 not yet
        SeasonPalette.Apply(palette, 10, 4);
        Assert.Equal(0x38, palette[0x13B]);
    }
}
