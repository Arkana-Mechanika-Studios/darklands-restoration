using Darklands.Cards.Scripts;
using Darklands.Content;
using Darklands.Content.Formats.Characters;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>
/// GitHub issue 5 (ycsqwan, 2026-11-27): the names the cards put into their text slots go through the pack. The docks' destinations
/// ("boat to $NamedOneName") were filled from the location's record, so a translated line read "船至Kassel"; the foreign traders' and
/// the merchants' titles and the random personal names (the hermit's, the strangers') were read from the EXE's tables as they are.
/// Without a pack each is the original's. They need the original game files and are skipped without them.
/// </summary>
public sealed class PackPlaceNamesTests
{
    private static byte[]? Exe()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? new GameFiles(folder).Read("DARKLAND.EXE") : null;
    }

    // a city with a port (city record word 0x4A, the docks' first destination)
    private static (GameState State, int Port)? PortCity()
    {
        for (var city = 0; city < Darklands.Core.World.Location.CityCount; city++)
        {
            var state = CardHarness.NewGame(1, city);
            if (state.City.Word(0x4A) is var port and not -1)
            {
                return (state, port);
            }
        }
        return null;
    }

    [Fact]
    public void TheDocksNameTheirDestinationsInThePacksWords()
    {
        if (!CardHarness.Available || PortCity() is not { } found)
        {
            return;
        }
        var (state, port) = found;
        var english = state.Locations[port].Name;
        Assert.NotEqual("", english);

        // option 6: back to the main street (the menu is shown once, with the destinations in their slots)
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(6)) { PersonalNames = CardHarness.PersonalNames };
        new Docks().Run(c);
        Assert.Equal(english, c.Variables.Named[0]);

        (state, port) = PortCity()!.Value;
        c = new CardContext(state, CardHarness.World!, new ScriptedScreen(6))
        {
            PersonalNames = CardHarness.PersonalNames,
            Translation = key => key == DataNames.Location(port) ? "港口" : null,
        };
        new Docks().Run(c);
    }

    [Fact]
    public void ThePersonalNameTablesWordTablesAreTheDecodersTables()
    {
        if (!CardHarness.Available || Exe() is not { } exe)
        {
            return;
        }
        var people = PersonalNamesDecoder.Decode(exe).People;
        var words = CardHarness.World!.Words;
        Assert.Equal(people.MaleNames, Enumerable.Range(0, people.MaleNames.Count).Select(i => words.Text(OriginalWords.MenFirstNames, i)));
        Assert.Equal(people.FemaleNames, Enumerable.Range(0, people.FemaleNames.Count).Select(i => words.Text(OriginalWords.WomenFirstNames, i)));
        Assert.Equal(people.Surnames, Enumerable.Range(0, people.Surnames.Count).Select(i => words.Text(OriginalWords.Surnames, i)));
        Assert.Equal(people.PatricianSurnames, Enumerable.Range(0, people.PatricianSurnames.Count).Select(i => words.Text(OriginalWords.MerchantFamilies, i)));
    }

    [Fact]
    public void TheTradersTitlesAndTheRandomNamesAreInThePacksWords()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(1, 3);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(-1)) { PersonalNames = CardHarness.PersonalNames };
        var words = CardHarness.World!.Words;
        var title = c.TableName(NameTable.Titles, 4, "");
        c.RandomName(7, 0, 1);
        var english = c.Variables.Named[1];
        Assert.Equal(c.PersonalNames.Name(NameTable.Titles, 4), title);

        // the pack's words for every entry of the two tables the name is drawn from, and for the title
        var keys = new Dictionary<string, string> { [words.Key(OriginalWords.TitleNames, 4)] = "外国商人" };
        for (var i = 0; i < OriginalWords.MenFirstNames.Count; i++)
        {
            keys[words.Key(OriginalWords.MenFirstNames, i)] = $"名{i}";
        }
        for (var i = 0; i < OriginalWords.Surnames.Count; i++)
        {
            keys[words.Key(OriginalWords.Surnames, i)] = $"姓{i}";
        }
        c.Translation = key => keys.GetValueOrDefault(key);
        Assert.Equal("外国商人", c.TableName(NameTable.Titles, 4, ""));
        c.RandomName(7, 0, 1);
        // the same draw (the same first name and surname), in the pack's words
        var parts = english.Split(' ', 2);
        var first = Enumerable.Range(0, c.PersonalNames.Count(NameTable.MenFirstNames)).First(i => c.PersonalNames.Name(NameTable.MenFirstNames, i) == parts[0]);
        var last = Enumerable.Range(0, c.PersonalNames.Count(NameTable.Surnames)).First(i => c.PersonalNames.Name(NameTable.Surnames, i) == parts[1]);
        Assert.Equal($"名{first} 姓{last}", c.Variables.Named[1]);
    }
}
