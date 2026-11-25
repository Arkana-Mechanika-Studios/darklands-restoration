using Darklands.Content;
using Darklands.Content.Formats.Characters;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>
/// The cards' words a translation pack reaches since the untranslated-text audit (1.2): the dragons', dwarfs' and titles' names,
/// the pronouns, the shop and guild words of the EXE. Without a pack each is the original's. They need the original game files
/// and are skipped without them.
/// </summary>
public sealed class PackRoutesTests
{
    private static byte[]? Exe()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? new GameFiles(folder).Read("DARKLAND.EXE") : null;
    }

    private static CardContext? Context(int location = 3, uint seed = 1)
    {
        if (!CardHarness.Available)
        {
            return null;
        }
        var state = CardHarness.NewGame(seed, location);
        return new CardContext(state, CardHarness.World!, new ScriptedScreen(-1)) { PersonalNames = CardHarness.PersonalNames };
    }

    private static string Expand(CardContext c, string text) =>
        GameTextCodec.Decode(TextVariables.Expand(GameTextCodec.Encode(text), c));

    [Fact]
    public void TheNameTablesWordTablesAreTheDecodersTables()
    {
        if (Context() is not { } c || Exe() is not { } exe)
        {
            return;
        }
        var data = PersonalNamesDecoder.Decode(exe);
        var words = CardHarness.World!.Words;
        Assert.Equal(data.Dragons, Enumerable.Range(0, data.Dragons.Count).Select(i => words.Text(OriginalWords.DragonNames, i)));
        Assert.Equal(data.Dwarfs, Enumerable.Range(0, data.Dwarfs.Count).Select(i => words.Text(OriginalWords.DwarfNames, i)));
        Assert.Equal(data.Titles, Enumerable.Range(0, data.Titles.Count).Select(i => words.Text(OriginalWords.TitleNames, i)));
    }

    [Fact]
    public void APackNamesTheDragonsDwarfsAndTitles()
    {
        if (Context() is not { } c || Exe() is not { } exe)
        {
            return;
        }
        var data = PersonalNamesDecoder.Decode(exe);
        var words = CardHarness.World!.Words;
        var keys = new Dictionary<string, string>
        {
            [words.Key(OriginalWords.DragonNames, 5)] = "Drache",
            [words.Key(OriginalWords.DwarfNames, 37)] = "Zwerg",
            [words.Key(OriginalWords.TitleNames, 6)] = "Titel",
        };
        c.Translation = key => keys.GetValueOrDefault(key);
        c.NameToSlot(6, 5, 1);
        Assert.Equal("Drache", c.Variables.Named[1]);
        c.NameToSlot(7, 37, 0);
        Assert.Equal("Zwerg", c.Variables.Named[0]);
        c.NameToSlot(8, 6, 2);
        Assert.Equal("Titel", c.Variables.Named[2]);
        // an entry the pack lacks: the table's; past the table: the far pointer after it, as the original reads on
        c.NameToSlot(6, 4, 1);
        Assert.Equal(data.Dragons[4], c.Variables.Named[1]);
        c.NameToSlot(8, 31, 2);
        Assert.Equal("a local hero", c.Variables.Named[2]);

        c.Translation = null;
        c.NameToSlot(6, 5, 1);
        Assert.Equal(data.Dragons[5], c.Variables.Named[1]);
    }

    [Fact]
    public void APacksPronounVariablesAnswerTheGamesPronouns()
    {
        if (Context() is not { } c)
        {
            return;
        }
        var he = Expand(c, "$he");
        var him = Expand(c, "$him");
        Assert.Contains(he, new[] { "he", "she" });

        c.PackVariable = (name, female) => name switch
        {
            "he" => female ? "sie" : "er",
            "His" => female ? "Ihr" : "Sein",
            _ => null,
        };
        var female = TextVariables.ChosenIsFemale(c);
        Assert.Equal(female ? "sie" : "er", Expand(c, "$he"));
        Assert.Equal(female ? "Ihr" : "Sein", Expand(c, "$His"));
        // a pronoun the pack lacks: the game's
        Assert.Equal(him, Expand(c, "$him"));

        c.PackVariable = null;
        Assert.Equal(he, Expand(c, "$he"));
    }

    [Theory]
    [InlineData("exe:DS:587D", "herbalist")]
    [InlineData("exe:DS:5887", "Pawnshop")]
    [InlineData("exe:DS:5915", "Stablemaster")]
    [InlineData("exe:DS:5A8D", "University")]
    [InlineData("exe:DS:5E47", "rebel")]
    [InlineData("exe:DS:5E4D", "conservative")]
    public void TheCardsWordsOfTheExeAreItsStringsAndAPacksWhenItHasThem(string key, string english)
    {
        if (Context() is not { } c)
        {
            return;
        }
        // the strings the scripts wrote out before (the same words), now read from the EXE by key
        Assert.Equal(english, c.ExeText(key));
        c.Translation = k => k == key ? "übersetzt" : null;
        Assert.Equal("übersetzt", c.ExeText(key));
    }

    [Fact]
    public void APackNamesTheItemsSaintsAndFormulaeTheCardsCopy()
    {
        if (Context() is not { } c)
        {
            return;
        }
        var world = CardHarness.World!;
        c.NameToSlot(0, 1, 0);
        Assert.Equal(world.ItemName(1), c.Variables.Named[0]);
        c.Translation = key => key == DataNames.Item(1) ? "Schwert" : key == DataNames.Formula(2) ? "Trank" : null;
        c.NameToSlot(0, 1, 0);
        Assert.Equal("Schwert", c.Variables.Named[0]);
        Assert.Equal("Trank", c.FormulaNameShown(2));
        Assert.Equal(world.ItemName(2), c.ItemNameShown(2));
    }
}
