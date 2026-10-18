using System.IO.Compression;
using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The special battlefield generators against the original program: each scenario in Corpus/special.json
/// is the original generator run from DARKLAND.EXE on the player's IMAPS.CAT, LCASTLE, DARKLAND.ENM and
/// DARKLAND.LST (tools/oracle/build_special_corpus.py): the Templar fortress and the tombs (R011), the
/// dragon cave (R012) and Baphomet's citadel (R013). Needs the game folder (DARKLANDS_DIR, or
/// the "game" key of local/paths.json); without it the tests pass vacuously.
/// </summary>
public sealed class SpecialOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load(Environment.GetEnvironmentVariable("SPECIAL_CORPUS") ?? "special.json"));

    [Fact]
    public void TemplarFortressesAreTheOriginals() => Check("fortress");

    [Fact]
    public void TombsAreTheOriginals() => Check("tombs");

    [Fact]
    public void DragonCavesAreTheOriginals() => Check("dragon");

    [Fact]
    public void BaphometsCitadelRoomsAreTheOriginals() => Check("citadel");

    /// <summary>The builder sends every special battlefield type to its generator (and the loader's fix-ups run on all levels).</summary>
    [Fact]
    public void TheBuilderRoutesTheSpecialTypesToTheirGenerators()
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
        var maps = PreparedMaps.Load(files);
        var citadel = CitadelFiles.Load(files);
        int[] oneLevel = [0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x6E, 0x5F, 0x60, 0x64, 0x65];
        foreach (var bf in oneLevel.Append(0x5E))
        {
            var context = new BattlefieldContext
            {
                Enemies = catalogue, Items = items, Tables = tables, Maps = maps, Citadel = citadel, Random = new MscRandom(1), Location = 5,
            };
            (int Type, int Quality, int Count)[] groups = [(0x12, 2, 3), (0x22, 1, 2)];
            var field = BattlefieldBuilder.Build(context, bf, 4242, groups);
            Assert.Equal(bf == 0x5E ? 16 : 1, field.Levels.Count);
            // The dragon cave never marks its battlefield persistent.
            Assert.True(field.Persistent == (bf is not (0x60 or 0x64 or 0x65)), $"bf 0x{bf:X2} persistence");
            // Without LCASTLE the citadel is replaced by the empty field (one level).
            if (bf is >= 0x57 and <= 0x5D or 0x6E)
            {
                var bare = new BattlefieldContext
                {
                    Enemies = catalogue, Items = items, Tables = tables, Maps = maps, Random = new MscRandom(1), Location = 5,
                };
                Assert.Single(BattlefieldBuilder.Build(bare, bf, 4242, groups).Levels);
            }
        }
    }

    private static void Check(string kind)
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
        var maps = PreparedMaps.Load(files);
        var citadel = CitadelFiles.Load(files);
        var n = 0;
        var cases = Data.Value.Cases(kind);
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var s = c.Inputs;
            var bf = s.GetProperty("bf").GetInt32();
            var label = $"{kind} scenario {n++} (bf 0x{bf:X2}, seed {s.GetProperty("seed").GetInt32()})";
            var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
            var lore = s.GetProperty("lore").GetInt32();
            var context = new BattlefieldContext
            {
                Enemies = catalogue, Items = items, Tables = tables, Maps = maps, Citadel = citadel, Random = random,
                Location = s.GetProperty("location").GetInt32(),
                LoreLevel = () => lore,
                ForceMap = s.GetProperty("forceMap").GetInt32(),
                CourtProgress = s.GetProperty("courtProgress").GetInt32(),
                Memory = new BattlefieldMemory
                {
                    LastLocation = s.GetProperty("lastLocation").GetInt32(), ClearedLevels = s.GetProperty("cleared").GetInt32(),
                },
            };
            var forces = new BattlefieldForces(catalogue, items, tables, random);
            var state = new GenerationState(context, forces);
            var specs = s.GetProperty("specs").EnumerateArray()
                .Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())).ToArray();
            var start = 0;
            var levels = kind switch
            {
                "fortress" => FortressBattlefield.BuildFortress(state, bf, out start, specs),
                "tombs" => FortressBattlefield.BuildTombs(state, bf, specs),
                "dragon" => DragonCaveBattlefield.Build(state, bf, specs),
                _ => CitadelBattlefield.Build(state, bf, specs),
            };
            Assert.True(s.GetProperty("levels").GetInt32() == levels, $"{label}: levels");
            Assert.True(s.GetProperty("start").GetInt32() == start, $"{label}: start level");
            CompareLevels(label, s, state);
            var terrain = Convert.FromHexString(s.GetProperty("terrain").GetString()!);
            var firstTerrain = Enumerable.Range(0, terrain.Length).FirstOrDefault(i => terrain[i] != state.Terrain.Bytes[i], -1);
            Assert.True(firstTerrain < 0, $"{label}: terrain differs at +0x{firstTerrain:X2} ({(firstTerrain >= 0 ? terrain[firstTerrain] : 0):X2} vs {(firstTerrain >= 0 ? state.Terrain.Bytes[firstTerrain] : 0):X2})");
            CompareForces(label, s, levels, forces);
            Assert.True(s.GetProperty("holdrand").GetUInt32() == random.State, $"{label}: random state");
            var flags = s.GetProperty("combatFlags").GetInt32();
            Assert.True(((flags & 1) != 0) == state.Persistent, $"{label}: persistent");
            Assert.True(((flags & 0x40) != 0) == state.Timed, $"{label}: timed");
            Assert.True((ushort)s.GetProperty("clearedAfter").GetInt32() == (ushort)context.Memory.ClearedLevels, $"{label}: cleared levels");
            Assert.True(s.GetProperty("lastLocationAfter").GetInt32() == context.Memory.LastLocation, $"{label}: last location");
        }
    }

    private static void CompareLevels(string label, System.Text.Json.JsonElement s, GenerationState state)
    {
        var saved = s.GetProperty("saved").EnumerateObject().ToList();
        Assert.True(saved.Count == state.SavedLevels.Count, $"{label}: saved {saved.Count} levels, the C# {state.SavedLevels.Count}");
        foreach (var level in saved)
        {
            var number = int.Parse(level.Name["level".Length..^".flr".Length], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(state.SavedLevels.ContainsKey(number), $"{label}: {level.Name} not saved");
            var expected = Unpack(level.Value.GetProperty("zdata").GetString()!);
            var actual = state.SavedLevels[number].Image;
            Assert.True(expected.Length == actual.Length, $"{label}: {level.Name} length {expected.Length} vs {actual.Length}");
            var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => expected[i] != actual[i], -1);
            Assert.True(first < 0, $"{label}: {level.Name} differs first at +0x{first:X4} ({(first >= 0 ? expected[first] : 0):X2} vs {(first >= 0 ? actual[first] : 0):X2})");
        }
    }

    private static void CompareForces(string label, System.Text.Json.JsonElement s, int levels, BattlefieldForces forces)
    {
        for (var level = 0; level < levels; level++)
        {
            var kinds = s.GetProperty("kinds")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.True(kinds.Count == forces.Kinds[level].Count, $"{label}: kinds on level {level}: {kinds.Count} vs {forces.Kinds[level].Count}");
            for (var k = 0; k < kinds.Count; k++)
            {
                var actualKind = Convert.ToHexString(EnemyRecords.Write(forces.Kinds[level][k])).ToLowerInvariant();
                Assert.True(kinds[k] == actualKind, $"{label}: kind {level}/{k}\n{kinds[k]}\n{actualKind}");
            }
            var groups = s.GetProperty("groups")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.True(groups.Count == forces.Groups[level].Count, $"{label}: groups on level {level}: {groups.Count} vs {forces.Groups[level].Count}");
            for (var g = 0; g < groups.Count; g++)
            {
                var actual = Convert.ToHexString(EnemyRecords.Write(forces.Groups[level][g])).ToLowerInvariant();
                Assert.True(groups[g] == actual, $"{label}: group {level}/{g}\n{groups[g]}\n{actual}");
            }
        }
    }

    private static byte[] Unpack(string zdata)
    {
        using var stream = new ZLibStream(new MemoryStream(Convert.FromBase64String(zdata)), CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
