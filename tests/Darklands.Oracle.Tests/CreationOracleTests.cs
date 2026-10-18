using System.Text.Json;
using Darklands.Content.Formats.Characters;
using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Items;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The character generator against the original program: every case of Corpus/chargen.json is
/// the original function run from DARKLAND.EXE with a generated character
/// (tools/oracle/build_chargen_corpus.py). The generator's tables are read from the player's
/// DARKLAND.EXE (DARKLANDS_EXE, or DARKLAND.EXE in the game folder); without it the test has nothing to compare.
/// </summary>
// orig: R037+0x691E, R037+0x7BCC, R037+0x7D8A, R037+0x80FE (R037) from the original bytes
public sealed class CreationOracleTests
{
    private const int Ds = 0x321A * 16;
    private const int Far = 0x2E38 * 16;

    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("chargen.json"));

    private static readonly Lazy<CreationTables?> Tables = new(() =>
    {
        var path = Darklands.Platform.LocalPaths.Get("exe", "DARKLANDS_EXE") ?? Path.Combine(Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "", "DARKLAND.EXE");
        return File.Exists(path) ? CreationDataDecoder.Decode(File.ReadAllBytes(path)).Tables : null;
    });

    [Theory]
    [InlineData("occupation_list")]
    [InlineData("serve_term")]
    [InlineData("career_events")]
    public void MatchesTheOriginalFunction(string function)
    {
        if (Tables.Value is not { } tables)
        {
            return;
        }
        var items = Items();
        var cases = Data.Value.Cases(function);
        Assert.NotEmpty(cases);
        var n = 0;
        foreach (var c in cases)
        {
            Check(function, c, tables, items, n++);
        }
    }

    [Fact]
    public void TheTablesAreReadFromTheExecutable()
    {
        if (Tables.Value is not { } t)
        {
            return;
        }
        Assert.Equal(6, t.Backgrounds.Count);
        Assert.Equal(37, t.Occupations.Count);
        Assert.Equal(("Nobility", 89), (t.Backgrounds[0].Name, t.Backgrounds[0].Experience));
        Assert.Equal(("Recruit", 18), (t.Occupations[0].Name, t.Occupations[0].Experience));
        Assert.Equal("Bandit", t.Occupations[36].Name);
        Assert.Equal([-6, -6, -4, -3, -2, -3], t.AgingPenalties[8]);
        // past the table (the preview from age 70): 2 bytes, then the character sheet's first box (x 0x40, y 1, w 0xC1, h 0x0A, colour 0xBE, flags 0x0C)
        Assert.Equal([0, 0, 0x40, 0, 1, 0], t.AgingPenalties[9]);
        Assert.Equal([-63, 0, 10, 0, -66, 12], t.AgingPenalties[10]);
        Assert.Equal([0, 0, 1, 0, -63, 10], t.AgingPenalties[11]);
        Assert.Equal((0x6C, 0x58, 0x92, 0x10), (t.Names.MaleNames.Count, t.Names.FemaleNames.Count, t.Names.Surnames.Count, t.Names.PatricianSurnames.Count));
    }

    private static List<ItemDefinition> Items()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Corpus", "chargen.json")));
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select((e, k) => new ItemDefinition(k, "", "", e[0].GetInt32(), 0, e[1].GetInt32(), e[2].GetInt32(), 0))
            .ToList();
    }

    private static void Check(string function, OracleCase c, CreationTables tables, List<ItemDefinition> items, int n)
    {
        var i = c.Inputs;
        var state = i.TryGetProperty("state", out var s) ? s.GetUInt32() : 1u;
        var random = new MscRandom(state);
        var creation = new CharacterCreation(tables, items, random, 0, () => 0);
        random.Seed(state);
        var label = $"{function} case {n}";
        switch (function)
        {
            case "occupation_list":
                {
                    SetCharacter(creation, i);
                    var list = OccupationEligibility.Available(creation);
                    var count = c.FinalInt16(Ds + 0x8C00, 0x5555);
                    Assert.True(list.Count == count, $"{label}: count {list.Count} vs {count}");
                    for (var k = 0; k < 20; k++)
                    {
                        var expected = c.FinalInt16(Ds + 0x8BD2 + 2 * k, unchecked((short)0xAAAA));
                        Assert.True((k < list.Count ? list[k] : -1) == expected, $"{label}: row {k}");
                    }
                    Assert.Equal(0, c.FinalInt16(Ds + 0x8BFC, 7));
                    break;
                }
            case "serve_term":
                {
                    SetCharacter(creation, i);
                    SetKnowledge(creation, i);
                    creation.Experience = Int(i, "points");
                    creation.TermExperience = 0x1234;
                    var adult = creation.Age >= 15;
                    creation.ServeTerm(Int(i, "occupation"));
                    var age = Int(i, "age");
                    Assert.True(creation.Age == c.FinalInt16(Ds + 0xE81C, (short)age), label + ": age");
                    for (var k = 0; k < 7; k++)
                    {
                        var before = (byte)i.GetProperty("attributes")[k].GetInt32();
                        Assert.True(creation.Attributes[k] == (sbyte)c.FinalByte(Ds + 0xE86E + k, before), $"{label}: attribute {k}");
                    }
                    for (var k = 0; k < 19; k++)
                    {
                        var before = (byte)i.GetProperty("skills")[k].GetInt32();
                        Assert.True(creation.Skills[k] == (sbyte)c.FinalByte(Ds + 0xE875 + k, before), $"{label}: skill {k}");
                    }
                    Assert.True(creation.LastOccupation == c.FinalInt16(Ds + 0x4428, (short)Int(i, "last")), label + ": last");
                    Assert.True(creation.PreviousOccupation == c.FinalInt16(Ds + 0x442A, (short)Int(i, "previous")), label + ": previous");
                    Assert.True(creation.ThirdLastOccupation == c.FinalInt16(Ds + 0x8C12, (short)Int(i, "third")), label + ": third");
                    for (var k = 0; k < 37; k++)
                    {
                        var before = (byte)i.GetProperty("taken")[k].GetInt32();
                        Assert.True(creation.HasBeen(k) == (c.FinalByte(Ds + 0x4402 + k, before) & 1) is 1, $"{label}: taken {k}");
                    }
                    Assert.True(creation.Experience == c.FinalInt16(Ds + 0x8BFA, (short)Int(i, "points")), label + ": points");
                    Assert.True(creation.TermExperience == c.FinalInt16(Ds + 0x8C48, 0x1234), label + ": term points");
                    CheckKnowledge(c, i, creation, label, adult);
                    break;
                }
            case "career_events":
                SetKnowledge(creation, i);
                creation.CareerEvents(Int(i, "occupation"));
                CheckKnowledge(c, i, creation, label, adult: true);
                break;
        }
    }

    private static void SetCharacter(CharacterCreation creation, JsonElement i)
    {
        creation.Age = Int(i, "age");
        for (var k = 0; k < 7; k++)
        {
            creation.Attributes[k] = i.GetProperty("attributes")[k].GetInt32();
        }
        for (var k = 0; k < 19; k++)
        {
            creation.Skills[k] = i.GetProperty("skills")[k].GetInt32();
        }
        for (var k = 0; k < 37; k++)
        {
            creation.SetTaken(k, i.GetProperty("taken")[k].GetInt32() != 0);
        }
        creation.LastOccupation = Int(i, "last");
        creation.PreviousOccupation = Int(i, "previous");
        creation.ThirdLastOccupation = Int(i, "third");
        creation.Background = Int(i, "background");
        creation.IsFemale = Int(i, "female") == 1;
    }

    /// <summary>Saint bits in the game's order (0x80 &gt;&gt; n % 8), formula bytes, an empty kit.</summary>
    private static void SetKnowledge(CharacterCreation creation, JsonElement i)
    {
        creation.Saints.Clear();
        creation.Saints.UnionWith(SaintsOf(Convert.FromHexString(i.GetProperty("saints").GetString()!)));
        Convert.FromHexString(i.GetProperty("formulas").GetString()!).CopyTo(creation.FormulaBytes, 0);
        creation.Kit.Clear();
    }

    private static IEnumerable<int> SaintsOf(byte[] bytes) =>
        Enumerable.Range(0, bytes.Length * 8).Where(s => (bytes[s / 8] & (0x80 >> (s % 8))) != 0);

    private static void CheckKnowledge(OracleCase c, JsonElement i, CharacterCreation creation, string label, bool adult)
    {
        var saints = Convert.FromHexString(i.GetProperty("saints").GetString()!);
        var after = Enumerable.Range(0, saints.Length).Select(k => c.FinalByte(Far + 0x3B1A + k, saints[k])).ToArray();
        Assert.True(SaintsOf(after).SequenceEqual(creation.Saints), label + ": saints");
        var formulas = Convert.FromHexString(i.GetProperty("formulas").GetString()!);
        for (var k = 0; k < formulas.Length; k++)
        {
            Assert.True(creation.FormulaBytes[k] == c.FinalByte(Far + 0x3B04 + k, formulas[k]), $"{label}: formula byte {k}");
        }
        var kitCount = (byte)Int(i, "kitCount");
        var count = c.FinalByte(Ds + 0xE888, kitCount);
        Assert.True((adult ? creation.Kit.Count : kitCount) == count, $"{label}: kit count {creation.Kit.Count} vs {count}");
        for (var k = 0; adult && k < creation.Kit.Count; k++)
        {
            var at = Far + 0x3984 + 6 * k;
            var stack = creation.Kit[k];
            Assert.True(stack.Code == c.FinalInt16(at, 0) && stack.Type == c.FinalByte(at + 2, 0) && stack.Quality == c.FinalByte(at + 3, 0)
                && stack.Count == c.FinalByte(at + 4, 0) && stack.Weight == c.FinalByte(at + 5, 0), $"{label}: kit entry {k}");
        }
        if (i.TryGetProperty("state", out var s))
        {
            Assert.True(((MscRandom)GetRandom(creation)).State == c.FinalUInt32(Ds + 0x7B20, s.GetUInt32()), label + ": generator");
        }
    }

    private static IRandomSource GetRandom(CharacterCreation creation) => creation.Random;

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
}
