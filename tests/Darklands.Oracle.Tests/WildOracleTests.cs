using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The wilderness battlefield generator against the original program: each scenario in
/// Corpus/wild.json is the original wild_main run from DARKLAND.EXE on the player's IMAPS.CAT,
/// DARKLAND.ENM and DARKLAND.LST (tools/oracle/build_wild_corpus.py), with the terrain, month, lore
/// level and able party members of the scenario, and for the last scenarios the two debugging
/// globals forceMap and forceSide that are always -1 in the shipped game. Needs the game folder
/// (DARKLANDS_DIR, or the "game" key of local/paths.json); without it the test passes vacuously.
/// </summary>
public sealed class WildOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load(Environment.GetEnvironmentVariable("WILD_CORPUS") ?? "wild.json"));

    [Fact]
    public void WildernessBattlefieldsAreTheOriginals()
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
        foreach (var c in Data.Value.Cases("wild_main"))
        {
            var s = c.Inputs;
            var bf = s.GetProperty("bf").GetInt32();
            var forceMap = s.GetProperty("forceMap").GetInt32();
            var forceSide = s.GetProperty("forceSide").GetInt32();
            var label = $"scenario {n++} (bf 0x{bf:X2}, seed {s.GetProperty("seed").GetInt32()}, terrain {s.GetProperty("terrain").GetInt32()}, forceMap {forceMap}, forceSide {forceSide})";
            var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
            var lore = s.GetProperty("lore").GetInt32();
            var context = new BattlefieldContext
            {
                Enemies = catalogue, Items = items, Tables = tables, Maps = maps, Random = random,
                WorldTerrain = s.GetProperty("terrain").GetInt32(),
                Month = s.GetProperty("month").GetInt32(),
                AbleMembers = s.GetProperty("party").EnumerateArray().Count(e => e.GetInt32() == 1),
                LoreLevel = () => lore,
                ForceMap = forceMap,
                ForceSide = forceSide,
            };
            var forces = new BattlefieldForces(catalogue, items, tables, random);
            var state = new GenerationState(context, forces);
            var specs = s.GetProperty("specs").EnumerateArray()
                .Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())).ToArray();
            var levels = WildBattlefield.Build(state, bf, specs);
            Assert.True(s.GetProperty("levels").GetInt32() == levels, $"{label}: levels");
            Assert.True(state.SavedLevels.Count == 1 && state.SavedLevels.ContainsKey(0), $"{label}: saved levels");
            foreach (var saved in s.GetProperty("saved").EnumerateObject())
            {
                Assert.Equal("level0.flr", saved.Name);
                var expected = Convert.FromHexString(saved.Value.GetProperty("data").GetString()!);
                var actual = state.SavedLevels[0].Image;
                var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => expected[i] != actual[i], -1);
                Assert.True(first < 0, $"{label}: {saved.Name} differs first at +0x{first:X4} ({(first >= 0 ? expected[first] : 0):X2} vs {(first >= 0 ? actual[first] : 0):X2})");
            }
            var terrain = Convert.FromHexString(s.GetProperty("terrainDescription").GetString()!);
            var firstTerrain = Enumerable.Range(0, terrain.Length).FirstOrDefault(i => terrain[i] != state.Terrain.Bytes[i], -1);
            Assert.True(firstTerrain < 0, $"{label}: terrain description differs at +0x{firstTerrain:X2}");
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
            Assert.True(((flags & 0x40) != 0) == state.Timed, $"{label}: timed battle");
            Assert.False(state.Persistent, $"{label}: persistent");
        }
        Assert.True(n >= 40, "the corpus has at least 40 scenarios");
    }
}
