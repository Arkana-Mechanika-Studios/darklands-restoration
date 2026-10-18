using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The city battlefield generator against the original program: each scenario in
/// Corpus/city.json is the original city_build run from DARKLAND.EXE on the player's
/// IMAPS.CAT, DARKLAND.ENM and DARKLAND.LST (tools/oracle/build_city_corpus.py). Needs the game
/// folder (DARKLANDS_DIR, or the "game" key of local/paths.json); without it the test passes vacuously.
/// </summary>
public sealed class CityOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load(Environment.GetEnvironmentVariable("CITY_CORPUS") ?? "city.json"));

    [Fact]
    public void CityBattlefieldsAreTheOriginals()
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
        var n = 0;
        foreach (var c in Data.Value.Cases("city_build"))
        {
            var s = c.Inputs;
            var bf = s.GetProperty("bf").GetInt32();
            var label = $"scenario {n++} (bf 0x{bf:X2}, seed {s.GetProperty("seed").GetInt32()})";
            var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
            var context = new BattlefieldContext
            {
                Enemies = catalogue, Items = items, Tables = tables, Maps = maps, Random = random,
                Location = s.GetProperty("location").GetInt32(),
                Memory = new BattlefieldMemory
                {
                    LastLocation = s.GetProperty("lastLocation").GetInt32(), ClearedLevels = s.GetProperty("cleared").GetInt32(),
                },
            };
            var forces = new BattlefieldForces(catalogue, items, tables, random);
            var state = new GenerationState(context, forces);
            var specs = s.GetProperty("specs").EnumerateArray()
                .Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())).ToArray();
            var levels = CityBattlefield.Build(state, bf, out var start, specs);
            Assert.True(s.GetProperty("levels").GetInt32() == levels, $"{label}: levels");
            Assert.True(s.GetProperty("start").GetInt32() == start, $"{label}: start level");
            foreach (var saved in s.GetProperty("saved").EnumerateObject())
            {
                var number = int.Parse(saved.Name["level".Length..^".flr".Length], System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(state.SavedLevels.ContainsKey(number), $"{label}: {saved.Name} not saved");
                var expected = Convert.FromHexString(saved.Value.GetProperty("data").GetString()!);
                var actual = state.SavedLevels[number].Image;
                var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => expected[i] != actual[i], -1);
                Assert.True(first < 0, $"{label}: {saved.Name} differs first at +0x{first:X4} ({(first >= 0 ? expected[first] : 0):X2} vs {(first >= 0 ? actual[first] : 0):X2})");
            }
            var terrain = Convert.FromHexString(s.GetProperty("terrain").GetString()!);
            var firstTerrain = Enumerable.Range(0, terrain.Length).FirstOrDefault(i => terrain[i] != state.Terrain.Bytes[i], -1);
            Assert.True(firstTerrain < 0, $"{label}: terrain differs at +0x{firstTerrain:X2}");
            for (var level = 0; level < 2; level++)
            {
                var kinds = s.GetProperty("kinds")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.True(kinds.Count == forces.Kinds[level].Count, $"{label}: kinds on level {level}");
                for (var k = 0; k < kinds.Count; k++)
                {
                    var actualKind = Convert.ToHexString(EnemyRecords.Write(forces.Kinds[level][k])).ToLowerInvariant();
                    Assert.True(kinds[k] == actualKind, $"{label}: kind {level}/{k}\n{kinds[k]}\n{actualKind}");
                }
                var groups = s.GetProperty("groups")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.True(groups.Count == forces.Groups[level].Count, $"{label}: groups on level {level}");
                for (var g = 0; g < groups.Count; g++)
                {
                    var actual = Convert.ToHexString(EnemyRecords.Write(forces.Groups[level][g])).ToLowerInvariant();
                    Assert.True(groups[g] == actual, $"{label}: group {level}/{g}\n{groups[g]}\n{actual}");
                }
            }
            Assert.True(s.GetProperty("holdrand").GetUInt32() == random.State, $"{label}: random state");
            var flags = s.GetProperty("combatFlags").GetInt32();
            Assert.True(((flags & 1) != 0) == state.Persistent, $"{label}: persistent");
            Assert.True(((flags & 0x40) != 0) == state.Timed, $"{label}: timed");
            Assert.True(s.GetProperty("clearedAfter").GetInt32() == context.Memory.ClearedLevels, $"{label}: cleared levels");
            Assert.True(s.GetProperty("lastLocationAfter").GetInt32() == context.Memory.LastLocation, $"{label}: last location");
        }
    }
}
