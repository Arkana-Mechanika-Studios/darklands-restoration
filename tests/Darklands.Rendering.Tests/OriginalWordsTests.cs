using System.Security.Cryptography;
using System.Text;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core.Items;
using Darklands.Core.Rules;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The original's word tables read from the player's EXE (vacuous without the game files). No game text is stored here: the words
/// are pinned by a hash of the literals 1.0 kept in Core and Cards (one hash a table), and every key is a known key of the
/// original-text extractor with the same text.
/// </summary>
public sealed class OriginalWordsTests
{
    private static readonly Lazy<OriginalWords?> Words = new(
        () => GameFolder.Files is { } files ? new OriginalWords(DarklandsExecutable.Load(files)) : null);

    private static readonly Lazy<StringTable?> Table = new(
        () => GameFolder.Files is { } files ? OriginalTextExtractor.Extract(files) : null);

    private static string Hash(IEnumerable<string> words) =>
        Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(string.Join('\n', words))));

    private static IEnumerable<string> All(OriginalWords words, WordTable table) =>
        Enumerable.Range(table.First, table.Count).Select(k => words.Text(table, k));

    [Fact]
    public void TheWordsAreTheOnesCoreAndTheCardsWroteBefore()
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        // SHA-256 of 1.0's literals joined by line feeds (PartyStanding, Trading, DayJobs, MemberRules, Formula, CampStay; the Cards copies
        // of the standing words and shop names were the same words)
        Assert.Equal("8D51990BFA51CDA638310CD58B0E0007662E9756B9EC6FCC5E75CBAB85F0BCCD", Hash(All(words, OriginalWords.FameTitles)));
        Assert.Equal("FFC30B88731C31374DC8430FF424B82F64216FB5E7381829D5C21158815A36FC", Hash(All(words, OriginalWords.StandingWords)));
        Assert.Equal("224C024972A2DF39089D9D2168CFACD32EA0310638CEDB471743EED428A90508", Hash(All(words, OriginalWords.Directions)));
        Assert.Equal("1B989A1C09722EEA7D76386D72967B053FB8155233F7DA5289D484D306C0EEC0",
            Hash(All(words, OriginalWords.TownSizes).Concat(All(words, OriginalWords.TownSizesTab))));
        Assert.Equal("75320887BAC80CD02AA7118EFA268D5086AA55587E550AAE8706BBB9B966B1EF", Hash(All(words, OriginalWords.ShopNames)));
        Assert.Equal("F0BB32EAD96D08D51B72683C1A632B673CFBD6DC18D532C687330B9482B9718A",
            Hash(DayJobs.All.Select(job => words.Text(OriginalWords.JobNames, job.NameEntry))));
        Assert.Equal("D4C28A250CD4E6906195CC3B1AD11C44C98A70C83428838598FFE47C7DE62208", Hash(All(words, OriginalWords.SkillNames)));
        Assert.Equal("A826F5568E1BF945DB18E383D85991944C44FAF0C172E11DD060FF89140A2C07",
            Hash(new[] { 0, 1, 2, 3, -1 }.Select(risk => words.Text(OriginalWords.RiskWords, new Formula(0, "", "", "", 0, risk, []).RiskWord))));
        CampDetail[] details =
        [
            new(CampDetailKind.Potions, 1), new(CampDetailKind.Potions, 2), new(CampDetailKind.Potions, 0), new(CampDetailKind.Fee, 12),
            new(CampDetailKind.Fee, 0), new(CampDetailKind.Job, DayJobs.DayLabourer), default,
        ];
        Assert.Equal("E526BCD9501D144DDF41595EDF8527EE08896554E8CD5209077AC4E1E5CBFED4", Hash(details.Select(words.Detail)));
    }

    [Fact]
    public void EveryKeyIsAKnownKeyOfTheExtractorWithTheSameText()
    {
        if (Words.Value is not { } words || Table.Value is not { } table)
        {
            return;
        }
        foreach (var t in OriginalWords.All)
        {
            for (var k = t.First; k < t.First + t.Count; k++)
            {
                var key = words.Key(t, k);
                var entry = table.Find(key);
                Assert.True(entry is not null, $"{t.Name}[{k}]: {key} is not in the extractor's table");
                Assert.Equal(StringOrigin.Known, entry.Origin);
                Assert.Equal(entry.Text, words.Text(t, k));
            }
        }
    }

    [Fact]
    public void TheKeysAreWhereTheProgramReadsTheWords()
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        Assert.Equal("exe:290E:07D3", words.Key(OriginalWords.FameTitles, 0));          // R136+0xA60B's first pointer
        Assert.Equal("exe:290E:0A1C", words.Key(OriginalWords.ShopNames, -3));          // the pointer before R136+0xA70F
        Assert.Equal("exe:290E:0A2C", words.Key(OriginalWords.ShopNames, 0));
        Assert.Equal("exe:290E:0CEB", words.Key(OriginalWords.JobNames, DayJobs.DayLabourer));
        Assert.Equal("exe:290E:0BF2", words.Key(OriginalWords.SkillNames, 0));         // the copy raise-skill-by and the camp read
        Assert.Equal("exe:DS:362C", words.Key(OriginalWords.TownSizes, 0));
        Assert.Equal("exe:DS:371F", words.Key(OriginalWords.TownSizesTab, 0));
        Assert.Equal("exe:DS:3309", words.Key(OriginalWords.RiskWords, 2));
        Assert.Equal("exe:DS:3D65", words.Key(OriginalWords.CampWords, 2));
    }

    [Fact]
    public void AnIndexOutsideATableIsAnError()
    {
        if (Words.Value is not { } words)
        {
            return;
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => words.Text(OriginalWords.FameTitles, 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => words.Text(OriginalWords.ShopNames, -4));
        Assert.Throws<ArgumentOutOfRangeException>(() => words.Text(OriginalWords.RiskWords, 3));
    }
}
