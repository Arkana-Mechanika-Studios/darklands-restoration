using System.Text;
using Darklands.Content;
using Darklands.Content.Formats.Characters;
using Darklands.Content.Formats.Executable;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests;

/// <summary>
/// The personal-name tables of DARKLAND.EXE (random_name, name_to_slot kinds 2, 6, 7, 8) and the
/// $Support words. They need the original game files (DARKLANDS_DIR, or the "game" key of local/paths.json) and are
/// skipped without them; the original-byte comparison is in the Oracle tests (names.json).
/// </summary>
public sealed class PersonalNamesTests
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

    [Fact]
    public void TheTablesHaveTheDocumentedCounts()
    {
        if (Exe() is not { } exe)
        {
            return;
        }
        var data = PersonalNamesDecoder.Decode(exe);
        Assert.Equal(0x6C, data.People.MaleNames.Count);
        Assert.Equal(0x58, data.People.FemaleNames.Count);
        Assert.Equal(0x92, data.People.Surnames.Count);
        Assert.Equal(0x10, data.People.PatricianSurnames.Count);
        Assert.Equal(16, data.Dragons.Count);
        Assert.Equal(38, data.Dwarfs.Count);
        Assert.Equal(31, data.Titles.Count);
        Assert.All(data.People.MaleNames.Concat(data.People.FemaleNames).Concat(data.People.Surnames).Concat(data.Dragons).Concat(data.Dwarfs).Concat(data.Titles),
            s => Assert.False(string.IsNullOrEmpty(s)));
        Assert.Equal(("Adalbert", "Wipo"), (data.People.MaleNames[0], data.People.MaleNames[^1]));
        Assert.Equal(("Adela", "Roxanne"), (data.People.FemaleNames[0], data.People.FemaleNames[^1]));
        Assert.Equal(("Alt", "of Zollern"), (data.People.Surnames[0], data.People.Surnames[^1]));
        Assert.Equal("Wagner", data.People.PatricianSurnames[^1]);
        Assert.Equal(("Enitharmon", "Baruch ophidious"), (data.Dragons[0], data.Dragons[^1]));
        Assert.Equal(("Alveig", "Volund"), (data.Dwarfs[0], data.Dwarfs[^1]));
        Assert.Equal(("goods merchant", "Medici", "Fugger"), (data.Titles[0], data.Titles[6], data.Titles[8]));
    }

    [Fact]
    public void RandomNameGivesTheSameNameForTheSameSeedAndTheGeneratorsOwnNameForTheSeed()
    {
        if (Context() is not { } c || Exe() is not { } exe)
        {
            return;
        }
        var tables = PersonalNamesDecoder.Decode(exe).People;
        for (var kind = 0; kind < 6; kind++)
        {
            c.RandomName(0x0C + 5, kind, 0);
            var first = c.Variables.Named[0];
            c.RandomName(0x0C + 5, kind, 1);
            Assert.Equal(first, c.Variables.Named[1]);
            Assert.False(string.IsNullOrEmpty(first));
            var expected = RandomNames.Make(new MscRandom(0), tables, c.State.Party.Seed, 0x0C + 5, (NameKind)kind, 0);
            Assert.Equal(expected, first);
        }
        c.RandomName(3, 0, 2);
        var other = c.Variables.Named[2];
        c.RandomName(4, 0, 2);
        Assert.NotEqual(other, c.Variables.Named[2]);
    }

    [Fact]
    public void WithoutTheTablesNamesStayEmpty()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var c = new CardContext(CardHarness.NewGame(1, 3), CardHarness.World!, new ScriptedScreen(-1));
        c.RandomName(1, 0, 0);
        Assert.Equal(" ", c.Variables.Named[0]);
    }

    [Fact]
    public void NameToSlotCopiesDragonDwarfAndTitleNames()
    {
        if (Context() is not { } c || Exe() is not { } exe)
        {
            return;
        }
        var data = PersonalNamesDecoder.Decode(exe);
        c.NameToSlot(6, 5, 1);
        Assert.Equal(data.Dragons[5], c.Variables.Named[1]);
        c.NameToSlot(7, 37, 0);
        Assert.Equal(data.Dwarfs[37], c.Variables.Named[0]);
        c.NameToSlot(8, 6, 2);
        Assert.Equal("Medici", c.Variables.Named[2]);
        c.NameToSlot(8, 31, 2);                 // past the table: the far pointer after it, as the original reads on
        Assert.Equal("a local hero", c.Variables.Named[2]);
        c.NameToSlot(9, 0, 2);                  // kinds above 8 copy nothing
        Assert.Equal("a local hero", c.Variables.Named[2]);
    }

    [Fact]
    public void NameToSlotKindTwoReadsTheFormulaNamesFromItemCode0x5F()
    {
        if (Context() is not { } c)
        {
            return;
        }
        var formulae = CardHarness.World!.Items.Formulae;
        c.NameToSlot(2, 0x66, 1);               // R089+0x0C08 passes the item codes 0x64..0x74
        Assert.Equal(formulae[0x66 - 0x5F].FullName, c.Variables.Named[1]);
        c.NameToSlot(2, 0x74, 1);
        Assert.Equal(formulae[0x74 - 0x5F].FullName, c.Variables.Named[1]);
        c.NameToSlot(2, 0x5F + formulae.Count, 1);   // the table goes on into the saint names
        Assert.Equal(CardHarness.World.Items.Saints[0].FullName, c.Variables.Named[1]);
    }

    [Fact]
    public void TheSupportWordsFollowThePointerTableAtOneBeforeTheEntryTheCardsIndex()
    {
        if (Exe() is not { } exe)
        {
            return;
        }
        // R136+0xB1EB is the middle of three far pointers at 290E:2B57
        var words = ProgramData.Load(exe).StringTable(0x290E, 0x2B57, 3);
        Assert.Equal(["oppose", "are undecided about", "support"], words);
    }

    [Fact]
    public void SupportVariablesExpandFromTheSidesMinusOneZeroAndOne()
    {
        if (Context() is not { } c)
        {
            return;
        }
        c.Support[0] = -1;
        c.Support[1] = 0;
        c.Support[2] = 1;
        c.Support[6] = 1;
        string Expand(string text) => Encoding.Latin1.GetString(TextVariables.Expand(Encoding.Latin1.GetBytes(text), c));
        Assert.Equal("They oppose it.", Expand("They $Support it."));
        Assert.Equal("They are undecided about it.", Expand("They $Support1 it."));
        Assert.Equal("They support it.", Expand("They $Support2 it."));
        Assert.Equal("They support it.", Expand("They $Support6 it."));
        Assert.Same(c.Support, c.Variables.Support);
    }
}
