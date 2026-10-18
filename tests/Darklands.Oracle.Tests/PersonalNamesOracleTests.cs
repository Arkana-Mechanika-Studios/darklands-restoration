using System.Security.Cryptography;
using System.Text;
using Darklands.Content.Formats.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The name tables read from DARKLAND.EXE against the original program: every case of
/// Corpus/names.json is <c>random_name</c> or <c>name_to_slot</c> run from the EXE bytes
/// (tools/oracle/build_names_corpus.py); a name is compared by length and SHA-256.
/// Needs DARKLAND.EXE (DARKLANDS_EXE, or DARKLAND.EXE in the game folder); without it the tests have nothing to compare.
/// </summary>
// orig: random_name R029+0x5CC4 and name_to_slot R029+0x1FB8 from the original bytes
public sealed class PersonalNamesOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("names.json"));

    private static readonly Lazy<PersonalNameData?> Names = new(() =>
    {
        var path = Darklands.Platform.LocalPaths.Get("exe", "DARKLANDS_EXE") ?? Path.Combine(Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "", "DARKLAND.EXE");
        return File.Exists(path) ? PersonalNamesDecoder.Decode(File.ReadAllBytes(path)) : null;
    });

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.Latin1.GetBytes(text)));

    private static void AssertName(OracleCase c, string name)
    {
        Assert.Equal(c.Element.GetProperty("length").GetInt32(), name.Length);
        Assert.Equal(c.Element.GetProperty("sha256").GetString(), Sha(name));
    }

    [Fact]
    public void RandomNameMatchesTheOriginal()
    {
        if (Names.Value is not { } names)
        {
            return;
        }
        var cases = Data.Value.Cases("random_name");
        Assert.NotEmpty(cases);
        var drawn = 0;
        foreach (var c in cases)
        {
            var i = c.Inputs;
            var kind = i.GetProperty("kind").GetInt32();
            if (kind > 5)
            {
                // kinds above 5 copy nothing: the slot keeps what it held (empty here)
                AssertName(c, "");
                continue;
            }
            var name = RandomNames.Make(new MscRandom(0), names.People, i.GetProperty("party").GetInt32() << 16 >> 16,
                i.GetProperty("seed").GetInt32() << 16 >> 16, (NameKind)kind, (uint)i.GetProperty("clock").GetInt32());
            AssertName(c, name);
            drawn++;
        }
        Assert.True(drawn > 200);
    }

    [Fact]
    public void NameToSlotMatchesTheOriginalForTheDragonDwarfAndTitleTables()
    {
        if (Names.Value is not { } names)
        {
            return;
        }
        var cases = Data.Value.Cases("name_to_slot");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var i = c.Inputs;
            var index = i.GetProperty("index").GetInt32();
            var table = i.GetProperty("kind").GetInt32() switch { 6 => names.Dragons, 7 => names.Dwarfs, _ => names.Titles };
            AssertName(c, table[index]);
        }
    }
}
