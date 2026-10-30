using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The castle and keep battlefield generators against the original program: each scenario in
/// Corpus/castle.json is the original layout-battlefield-entry (R005, with the level planner R006 and the floor
/// builder R007) run from DARKLAND.EXE on the player's DARKLAND.ENM and DARKLAND.LST
/// (tools/oracle/build_castle_corpus.py). Needs the game folder (DARKLANDS_DIR, or the "game" key of local/paths.json);
/// without it the test passes vacuously.
/// </summary>
public sealed class CastleOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load(Environment.GetEnvironmentVariable("CASTLE_CORPUS") ?? "castle.json"));

    [Fact]
    public void CastleBattlefieldsAreTheOriginals()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        if (!GameFiles.IsGameFolder(folder))
        {
            return;
        }
        var files = new GameFiles(folder);
        var catalogue = EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM"));
        var items = WorldData.Load(files).ItemDefinitions;
        var tables = CombatTablesReader.Read(DarklandsExecutable.Load(files));
        var n = 0;
        var failures = new List<string>();
        foreach (var c in Data.Value.Cases("R005+0x0000"))
        {
            try
            {
                Compare(c.Inputs, n++, catalogue, items, tables);
            }
            catch (Exception ex) when (ex is Xunit.Sdk.XunitException or IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                failures.Add(ex.Message + (ex is Xunit.Sdk.XunitException ? string.Empty : "\n" + ex.StackTrace));
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} of {n} scenarios differ; the first:\n" + string.Join("\n", failures.Take(5)));
        Assert.True(n >= 30, $"the corpus has {n} scenarios");
    }

    private static void Compare(System.Text.Json.JsonElement s, int n, EnemyCatalogue catalogue, IReadOnlyList<Darklands.Core.Items.ItemDefinition> items, CombatTables tables)
    {
        var bf = s.GetProperty("bf").GetInt32();
        var label = $"scenario {n} (bf 0x{bf:X2}, seed {s.GetProperty("seed").GetInt32()})";
        var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
        var lore = s.GetProperty("lore").GetInt32();
        var context = new BattlefieldContext
        {
            Enemies = catalogue, Items = items, Tables = tables, Maps = new NoMaps(), Random = random,
            Location = s.GetProperty("location").GetInt32(),
            LoreLevel = () => lore,
            Memory = new BattlefieldMemory
            {
                LastLocation = s.GetProperty("lastLocation").GetInt32(), ClearedLevels = s.GetProperty("cleared").GetInt32(),
            },
        };
        var forces = new BattlefieldForces(catalogue, items, tables, random);
        var state = new GenerationState(context, forces);
        var specs = s.GetProperty("specs").EnumerateArray()
            .Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())).ToArray();
        var levels = CastleBattlefield.Build(state, bf, out var start, specs);
        Assert.True(s.GetProperty("levels").GetInt32() == levels, $"{label}: levels");
        Assert.True(s.GetProperty("start").GetInt32() == start, $"{label}: start level");
        Assert.True(s.GetProperty("saved").EnumerateObject().Count() == state.SavedLevels.Count, $"{label}: number of saved levels");
        for (var level = 0; level < levels; level++)
        {
            var kinds = s.GetProperty("kinds")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.True(kinds.Count == forces.Kinds[level].Count, $"{label}: kinds on level {level} ({kinds.Count} vs {forces.Kinds[level].Count})");
            for (var k = 0; k < kinds.Count; k++)
            {
                var actualKind = Convert.ToHexString(EnemyRecords.Write(forces.Kinds[level][k])).ToLowerInvariant();
                Assert.True(kinds[k] == actualKind, $"{label}: kind {level}/{k}\n{kinds[k]}\n{actualKind}");
            }
            var groups = s.GetProperty("groups")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.True(groups.Count == forces.Groups[level].Count, $"{label}: groups on level {level} ({groups.Count} vs {forces.Groups[level].Count}): " + string.Join(" ", forces.Groups[level].Select(g => $"({g.X},{g.Y}) {g.Kinds[0]}/{g.Counts[0]} {g.Kinds[1]}/{g.Counts[1]} {g.Kinds[2]}/{g.Counts[2]}")));
            for (var g = 0; g < groups.Count; g++)
            {
                var actual = Convert.ToHexString(EnemyRecords.Write(forces.Groups[level][g])).ToLowerInvariant();
                Assert.True(groups[g] == actual, $"{label}: group {level}/{g}\n{groups[g]}\n{actual}");
            }
        }
        foreach (var saved in s.GetProperty("saved").EnumerateObject())
        {
            var number = int.Parse(saved.Name["level".Length..^".flr".Length], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(state.SavedLevels.ContainsKey(number), $"{label}: {saved.Name} not saved");
            var expected = Inflate(Convert.FromBase64String(saved.Value.GetProperty("zlib").GetString()!));
            var actual = state.SavedLevels[number].Image;
            var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => expected[i] != actual[i], -1);
            Assert.True(first < 0, $"{label}: {saved.Name} differs first at +0x{first:X4} ({(first >= 0 ? expected[first] : 0):X2} vs {(first >= 0 ? actual[first] : 0):X2})");
        }
        var terrain = Convert.FromHexString(s.GetProperty("terrain").GetString()!);
        var firstTerrain = Enumerable.Range(0, terrain.Length).FirstOrDefault(i => terrain[i] != state.Terrain.Bytes[i], -1);
        Assert.True(firstTerrain < 0, $"{label}: terrain differs at +0x{firstTerrain:X2}");
        Assert.True(s.GetProperty("holdrand").GetUInt32() == random.State, $"{label}: random state");
        var flags = s.GetProperty("combatFlags").GetInt32();
        Assert.True(((flags & 1) != 0) == state.Persistent, $"{label}: persistent");
        Assert.True(((flags & 0x40) != 0) == state.Timed, $"{label}: timed");
        Assert.True(s.GetProperty("clearedAfter").GetInt32() == context.Memory.ClearedLevels, $"{label}: cleared levels");
        Assert.True(s.GetProperty("lastLocationAfter").GetInt32() == context.Memory.LastLocation, $"{label}: last location");
    }

    /// <summary>
    /// The original never returns for battlefield 0x7C with seed 49955 (the emulated original runs on until its instruction limit): the
    /// party's room is drawn until it differs from the gate room. The port gives up after 1000 draws.
    /// </summary>
    [Fact]
    public void ThePartyRoomDrawThatFreezesTheOriginalReturns()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        if (!GameFiles.IsGameFolder(folder))
        {
            return;
        }
        var files = new GameFiles(folder);
        var catalogue = EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM"));
        var items = WorldData.Load(files).ItemDefinitions;
        var tables = CombatTablesReader.Read(DarklandsExecutable.Load(files));
        var random = new MscRandom(49955);
        var context = new BattlefieldContext
        {
            Enemies = catalogue, Items = items, Tables = tables, Maps = new NoMaps(), Random = random, Location = 5, LoreLevel = () => 3,
            Memory = new BattlefieldMemory { LastLocation = 5, ClearedLevels = 28 },
        };
        var state = new GenerationState(context, new BattlefieldForces(catalogue, items, tables, random));
        var levels = CastleBattlefield.Build(state, 0x7C, out _, [(46, 4, 2), (60, 6, 8), (41, 5, 3)]);
        Assert.True(levels >= 1);
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var stream = new System.IO.Compression.ZLibStream(new MemoryStream(compressed), System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>The castle generators read no prepared maps.</summary>
    private sealed class NoMaps : IBattlefieldMaps
    {
        public BattlefieldLevel Load(string member) => throw new InvalidOperationException("the castle generators read no prepared maps");
    }
}
