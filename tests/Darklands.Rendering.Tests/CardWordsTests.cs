using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The rest of L-4: the words the cards and the screens took from tables of their own (months, bells, lord titles, support words,
/// patrons) and the card scripts' literal fills now come from the EXE by key. Each gives the bytes of the literal it replaced.
/// </summary>
public sealed class CardWordsTests
{
    private static readonly Lazy<OriginalWords?> Words = new(
        () => GameFolder.Files is { } files ? new OriginalWords(DarklandsExecutable.Load(files)) : null);

    private static readonly System.Text.Encoding Latin1 = System.Text.Encoding.Latin1;

    // the literals of 1.0 (TextVariables, CardContext.PatronNames), in the game's own bytes (\u001f its a-umlaut, { o-umlaut, | u-umlaut)
    public static TheoryData<string, string[]> Tables() => new()
    {
        { "month-names", ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"] },
        { "bell-names", ["Latins", "Prime", "Terce", "Sexts", "Nones", "Vespers", "Compline", "Matins"] },
        { "lord-titles-1", ["Vogt", "Erbvogt", "Obervogt", "Burggraf", "Richter", "Landhofmeister"] },
        { "lord-titles-2", ["alte Herr", "\u001fltere Herren", "Frager", "Losunger", "alte Losunger", "Oberste Hauptm\u001fnn", "Schultheiss", "Sch{ff", "B|rgermeister"] },
        { "support-words", ["oppose", "are undecided about", "support"] },
        { "patron-names", ["goods merchant", "blacksmith", "artificier", "cathedral", "foreign trader", "pharmacist", "Medici", "Hanse", "Fugger", "village", "City Lord", "castle"] },
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public void ATableGivesTheBytesOfThe10Literals(string name, string[] literal)
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        var table = OriginalWords.All.Single(t => t.Name == name);
        Assert.Equal(literal.Length, table.Count);
        for (var k = 0; k < literal.Length; k++)
        {
            Assert.Equal(Latin1.GetBytes(literal[k]), GameTextCodec.Encode(words.Text(table, table.First + k)));
        }
    }

    // the card scripts' fills: key, the literal it replaced
    public static TheoryData<string, string> Fills() => new()
    {
        { "exe:DS:586E", "Foreign Trader" }, { "exe:DS:629F", "Foreign Trader" }, { "exe:DS:58A4", "Arms Outfitter" },
        { "exe:DS:5C27", "Arms Outfitter" }, { "exe:DS:5A57", "Mine Giftshop" }, { "exe:DS:5FCF", "Village Stable" },
        { "exe:DS:58BD", "Poor" }, { "exe:DS:58C2", "Modest" }, { "exe:DS:58C9", "Good" }, { "exe:DS:58CE", "Very Good" },
        { "exe:DS:58D8", "Excellent" }, { "exe:DS:5D9F", "today" }, { "exe:DS:5DA5", "tomorrow" }, { "exe:DS:5DAE", "two days hence" },
        { "exe:DS:5DBD", "three days hence" }, { "exe:DS:5C5D", "Weapon Skills" }, { "exe:DS:5CC3", "Potions" },
        { "exe:DS:5CCB", "Alchemical Components" }, { "exe:DS:652F", "touch the " }, { "exe:DS:653A", "turn the stone" },
        { "exe:DS:6549", "heart" }, { "exe:DS:6511", "gold coins" },
        { "exe:DS:6709", "Reinhard of OGronigen" }, { "exe:DS:5B8F", "Deposit how many Florins?" }, { "exe:DS:5BBD", "Deposit how many Florins?" }, { "exe:290E:049A", "You need a missile weapon" },
        { "exe:DS:68AF", "spring" }, { "exe:DS:68B6", "lake" }, { "exe:DS:68BB", "cave" }, { "exe:DS:68C0", "shrine" },
        { "exe:DS:68C7", "mound" }, { "exe:DS:68CD", "pagan altar" }, { "exe:DS:68D9", "spring" }, { "exe:DS:68E0", "lake" },
        { "exe:DS:68E5", "cave" }, { "exe:DS:68EA", "shrine" }, { "exe:DS:68F1", "mound" }, { "exe:DS:68F7", "pagan altar" },
    };

    [Theory]
    [MemberData(nameof(Fills))]
    public void AFillKeyGivesTheWordsOfItsLiteral(string key, string literal)
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        Assert.Equal(literal, words.Exe(key));
    }

    [Fact]
    public void TheQuickstartNamesAreTheLiteralsOf10()
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        Darklands.Core.Characters.QuickstartName[] literals =
        [
            new("Gretchen Wilburg", "Gretch"), new("Gunther Langer", "Gunther"), new("Hans Muller", "Hans"), new("Ebhard of Achdorf", "Ebhard"),
        ];
        Assert.Equal(literals, words.Quickstart());
    }

    [Fact]
    public void AStringThatIsNoKeyIsRefused()
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        Assert.Null(words.ExeOrNull("Foreign Trader"));
        Assert.Throws<ArgumentException>(() => words.Exe("exe:DS:58"));
    }
}
