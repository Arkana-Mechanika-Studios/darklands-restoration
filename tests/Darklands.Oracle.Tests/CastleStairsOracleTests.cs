using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The stair continuation R006+0x34B8 against the original, called directly on a crafted state (Corpus/castle-stairs.json, made by
/// local/batch/b32/c-fix/stairs_direct.py): the planner's call through thunk 09C0:1BE9 with AX = layout-room-list[level] - 1, the stub
/// root 09C0:052B from the original bytes. The level's first stair fails before the strip search, so the cut uses the slot the call
/// left (the castle corpus never reaches this). The work area, the counts and the random state afterwards must be the original's.
/// </summary>
public sealed class CastleStairsOracleTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void AStairThatFailsBeforeTheStripSearchIsTheOriginals()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "castle-stairs.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal("test_only_original_byte_execution", doc.RootElement.GetProperty("authority").GetString());
        var n = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var inputs = c.GetProperty("inputs");
            var after = c.GetProperty("after");
            var random = new MscRandom(inputs.GetProperty("holdrand").GetUInt32());
            var context = new BattlefieldContext { Enemies = null!, Items = [], Tables = null!, Maps = null!, Random = random };
            var g = new CastleGenerator(new GenerationState(context, new BattlefieldForces(null!, [], null!, random)));
            var w = (CastleWork)typeof(CastleGenerator).GetField("_w", Any)!.GetValue(g)!;
            var memory = (byte[])typeof(CastleWork).GetField("_memory", Any)!.GetValue(w)!;
            Unpack(inputs.GetProperty("work").GetString()!).CopyTo(memory, 0);
            typeof(CastleGenerator).GetField("_level", Any)!.SetValue(g, inputs.GetProperty("level").GetInt32());
            foreach (var (field, key) in Fields)
            {
                var a = (int[])typeof(CastleGenerator).GetField(field, Any)!.GetValue(g)!;
                var values = inputs.GetProperty(key).EnumerateArray().Select(e => e.GetInt32()).ToArray();
                values.CopyTo(a, 0);
            }

            typeof(CastleGenerator).GetMethod("ContinueStairs", Any)!.Invoke(g, []);

            var expected = Unpack(after.GetProperty("work").GetString()!);
            var differing = Enumerable.Range(0, expected.Length).Where(i => expected[i] != memory[i]).Take(8).Select(i => $"{i:X4}").ToList();
            Assert.True(differing.Count == 0, $"case {n}: work bytes differ at {string.Join(", ", differing)}");
            foreach (var (field, key) in Fields)
            {
                var a = (int[])typeof(CastleGenerator).GetField(field, Any)!.GetValue(g)!;
                Assert.Equal(after.GetProperty(key).EnumerateArray().Select(e => e.GetInt32()).ToArray(), a.Take(after.GetProperty(key).GetArrayLength()).ToArray());
            }
            Assert.Equal(after.GetProperty("holdrand").GetUInt32(), random.State);
            n++;
        }
        Assert.True(n > 0);
    }

    private static readonly (string Field, string Key)[] Fields =
        [("_roomCount", "rooms"), ("_lastKeepRoom", "lastKeep"), ("_stripCount", "strips"), ("_stairCount", "stairs"), ("_bounds", "bounds")];

    private static byte[] Unpack(string base64)
    {
        using var z = new ZLibStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var m = new MemoryStream();
        z.CopyTo(m);
        return m.ToArray();
    }
}
