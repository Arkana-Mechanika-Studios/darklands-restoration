using Darklands.Core;
using Darklands.Core.World;
using Darklands.Persistence;

namespace Darklands.Cards.Tests;

/// <summary>
/// A game saved in the wilderness and loaded again (GitHub issue 7): the load reads the city record at the city file's start, as the
/// original does (its seek to a negative offset fails), one byte off city 0's. Arriving in a city must then give that city's own record,
/// as the original's arrival read does: in Groningen (city 0) the main street showed no Stadtplatz and no market, a stray byte before
/// the name and the inn's name garbled, because that record was taken as city 0's and never read again.
/// </summary>
public sealed class WildernessLoadCityTests
{
    private static GameState LoadedInTheWilderness()
    {
        var state = CardHarness.NewGame(1);
        state.CurrentLocation = GameState.Wilderness;
        var (sav, bsv) = OriginalSaves.Write(state, "wilderness");
        var loaded = OriginalSaves.Read(sav, bsv, "DKSAVE0.SAV", CardHarness.World!, running: state).State;
        Assert.Equal(GameState.Wilderness, loaded.CurrentLocation);
        return loaded;
    }

    [Fact]
    public void ArrivingInACityAfterAWildernessLoadGivesThatCitysRecord()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var world = CardHarness.World!;
        for (var city = 0; city < Location.CityCount && city < world.CreateLocations().Count; city++)
        {
            var s = LoadedInTheWilderness();
            _ = s.City;                                   // the record the load read (the file's start), as the original keeps it
            s.CurrentLocation = city;                     // the arrival
            Assert.Equal(world.LoadCity(city).ToArray(), s.City.ToArray());
        }
    }

    [Fact]
    public void GroningensMainStreetAfterAWildernessLoadShowsItsPlaces()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var s = LoadedInTheWilderness();
        _ = s.City;
        s.CurrentLocation = 0;                            // Groningen, city 0
        var c = new CardContext(s, CardHarness.World!, new ScriptedScreen(-1));
        Assert.NotEqual(0, c.HereQuery(0x10));            // the Stadtplatz
        Assert.NotEqual(0, c.HereQuery(0x11));            // the market
        Assert.Equal(CardHarness.World!.CreateLocations()[0].Name, TextVariables.Value("PlaceName", c));
    }
}
