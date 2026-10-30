using System.Text;
using Darklands.Cards.Scripts;
using Darklands.Core;
using Darklands.Core.Economy;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Simulation;

namespace Darklands.Cards.Tests;

/// <summary>
/// The $name expansion of card texts (text-substitute / text-slot-expand in the card engine), the opening
/// card ($Party02) and the main loop's pass over a card. They need the original game files
/// (DARKLANDS_DIR, or the "game" key of local/paths.json) and are skipped without them.
/// </summary>
public sealed class TextAndOpeningTests
{
    private static CardContext? Context(int location = 3, uint seed = 1)
    {
        if (!CardHarness.Available)
        {
            return null;
        }
        var state = CardHarness.NewGame(seed, location);
        return new CardContext(state, CardHarness.World!, new ScriptedScreen(-1));
    }

    private static string Expand(CardContext c, string text) =>
        Encoding.Latin1.GetString(TextVariables.Expand(Encoding.Latin1.GetBytes(text), c));

    [Fact]
    public void ANameEndsAtTheFirstByteThatIsNotALetterOrDigit()
    {
        if (Context() is not { } c)
        {
            return;
        }
        c.Variables.Numbers[0] = 5;
        c.Variables.Numbers[1] = 6;
        Assert.Equal("n=5.", Expand(c, "n=$Number1."));
        Assert.Equal("n=5 and 6", Expand(c, "n=$Number1 and $Number2"));
        Assert.Equal("n=5", Expand(c, "n=$Number1"));              // a name at the very end is expanded too
        Assert.Equal("a$Number1b", Expand(c, "a$Number1b"));        // "Number1b" is not a name
    }

    [Fact]
    public void ADollarInsideANameDropsTheNameBeforeIt()
    {
        // text-substitute: a '$' always restarts the name buffer, so the first name is lost without a trace
        if (Context() is not { } c)
        {
            return;
        }
        c.Variables.Numbers[0] = 5;
        c.Variables.Numbers[1] = 6;
        Assert.Equal("6", Expand(c, "$Number1$Number2"));
        Assert.Equal("x6", Expand(c, "x$Number1$Number2"));
    }

    [Fact]
    public void UnknownNamesAndALoneDollarStayAsWritten()
    {
        if (Context() is not { } c)
        {
            return;
        }
        Assert.Equal("a $docks. b", Expand(c, "a $docks. b"));      // docks has no case in text-slot-expand
        Assert.Equal("$ x", Expand(c, "$ x"));                      // the empty name comes out as "$"
        Assert.Equal("$", Expand(c, "$$"));
        Assert.Equal("$nonsense", Expand(c, "$nonsense"));
    }

    [Fact]
    public void TheTextVariablesMatchTheirNamesExactly()
    {
        if (Context() is not { } c)
        {
            return;
        }
        c.Variables.Texts[0] = "abc";
        Assert.Equal("abc!", Expand(c, "$Text1!"));
        Assert.Equal("$Text01!", Expand(c, "$Text01!"));            // the old parse accepted "Text01"
        c.Variables.Money[0] = Money.From(0, 3, 2);
        Assert.Equal("3 groschen and 2 pfennigs", Expand(c, "$Money1"));
    }

    [Fact]
    public void InTheWildernessThePlaceNameIsTheLastCityLoaded()
    {
        // location-now < 0x5c is a signed compare: -1 takes the city branch and city-record still holds the last city
        if (Context(location: 3) is not { } c)
        {
            return;
        }
        var city = CardHarness.World!.Cities.Find(3)!.ShortName;
        Assert.Equal(city, Expand(c, "$PlaceName"));
        c.State.CurrentLocation = GameState.Wilderness;
        Assert.Equal(city, Expand(c, "$PlaceName"));
        c.State.CurrentLocation = 120;
        Assert.Equal(c.State.Locations[120].Name, Expand(c, "$PlaceName"));
    }

    [Fact]
    public void TheCityBuildingsComeFromTheCityRecord()
    {
        if (Context(location: 3) is not { } c)
        {
            return;
        }
        var city = CardHarness.World!.Cities.Find(3)!;
        Assert.Equal(city.InnName, Expand(c, "$Inn"));
        Assert.Equal(city.PoliticalCenterName, Expand(c, "$citySquare"));
        Assert.Equal(city.TownHallName, Expand(c, "$councilHall"));
        Assert.Equal(city.SlumName, Expand(c, "$hospital"));
        Assert.Equal(city.SlumName, Expand(c, "$slum"));            // both names read the same field
        Assert.Equal(city.LeaderName, Expand(c, "$CityLordName"));
        Assert.Equal(CardHarness.World!.CityDescription(3), Expand(c, "$PlaceDesc"));
    }

    [Theory]
    [InlineData(0, 0, 0, -30, 0)]     // north
    [InlineData(0, 0, 30, -60, 1)]    // north-east
    [InlineData(0, 0, 40, 0, 2)]      // east
    [InlineData(0, 0, 30, 60, 3)]     // south-east
    [InlineData(0, 0, 0, 60, 4)]      // south
    [InlineData(0, 0, -30, 60, 5)]    // south-west
    [InlineData(0, 0, -40, 0, 6)]     // west
    [InlineData(0, 0, -30, -60, 7)]   // north-west
    [InlineData(0, 0, 40, -60, 2)]    // a side that is at least twice the other wins: dx / 2 >= dy
    [InlineData(5, 5, 5, 5, 6)]       // the same place: x2 <= x1 and y2 <= y1, dx / 2 >= dy -> west
    public void TheCompassDirectionFollowsTheOriginalsRule(int x1, int y1, int x2, int y2, int expected)
    {
        Assert.Equal(expected, Darklands.Core.Rules.PartyStanding.Direction(x1, y1, x2, y2));
    }

    // ---- the opening card -----------------------------------------------------------------------

    [Fact]
    public void TheOpeningCardSetsTheStartingPurseDateAndEvents()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        // purse-assign(..., roll-below(3) + 8, roll-below(3) + 0xa): the arguments are drawn right to left; then day and month
        var twin = new MscRandom(((MscRandom)state.Random).State);
        var pfennigs = twin.Below(3) + 10;
        var groschen = twin.Below(3) + 8;
        var day = twin.Below(6) + 24;
        var month = twin.Below(4) + 4;
        var (next, screen) = CardHarness.Run(new PartyAtTheInn(), state, option: 2);
        Assert.Equal(Money.From(0, groschen, pfennigs), state.Party.Purse);
        Assert.Equal((9, day, month), (state.Date.Hour, (int)state.Date.Day, (int)state.Date.Month));
        Assert.Equal(9, state.Locations[3].Reputation);
        Assert.True(state.Events.Started(0x63), "event-here-test(0x63, 0x2d0) is part of the card");
        Assert.True(state.Events.QuestExists(Quests.Sabbat), "open-quest-exists(1) is false -> the sabbat quest is started");
        Assert.Equal(6, next);                                        // option 2 ("No risk") goes to card 6
        Assert.Equal(
            [OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled],
            screen.LastOptions.Take(4));
        Assert.All(screen.LastOptions.Skip(4), option => Assert.Equal(OptionState.Hidden, option));   // the dormant options 4..9 are never offered
    }

    [Fact]
    public void TheOpeningCardOptionsLeadWhereTheOriginalSends()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        foreach (var (option, card) in new[] { (0, 0x1D), (1, 0x0C), (2, 0x06), (3, 0x09) })
        {
            var state = CardHarness.NewGame(seed: 5, location: 3);
            var (next, _) = CardHarness.Run(new PartyAtTheInn(), state, option);
            Assert.Equal(card, next);
        }
    }

    [Fact]
    public void TheOpeningCardNeedsNoStartingLocationToRun()
    {
        // the original writes into locations[location-now] unchecked; here the wilderness just skips the reputation
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        state.CurrentLocation = GameState.Wilderness;
        var (next, _) = CardHarness.Run(new PartyAtTheInn(), state, option: 0);
        Assert.Equal(0x1D, next);
    }

    // ---- the main loop ----------------------------------------------------------------------------

    [Fact]
    public void RunningACardMovesTheGameOnAndRemembersTheCardBefore()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        state.CurrentCard = NewGame.FirstCard;
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(1));
        var next = new CardDispatcher().RunCard(c);
        Assert.Equal(0x0C, next);                                     // the second option waits an hour and goes to the map
        Assert.Equal(0x0C, state.CurrentCard);
        Assert.Equal(NewGame.FirstCard, state.PreviousCard);
    }

    [Theory]
    [InlineData(-2, true)]
    [InlineData(-6, true)]
    [InlineData(-1, false)]
    [InlineData(-7, false)]
    [InlineData(0x0C, false)]
    public void TheBattleCodesAreMinusTwoToMinusSix(int card, bool battle)
    {
        Assert.Equal(battle, CardDispatcher.IsBattle(card));
    }

    [Fact]
    public void ANewGameStartsAtAnInnOfOneOfTheFirstNinetyCities()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        for (uint seed = 1; seed < 40; seed++)
        {
            var state = NewGame.Quickstart(CardHarness.World!, seed);
            Assert.True(state.CurrentLocation is >= 0 and < NewGame.StartingCities);
            Assert.Equal(0x29, state.CurrentCard);
            Assert.Equal(414, state.Locations.Count);
            Assert.Equal(4, state.Party.Members.Count);
        }
    }
}
