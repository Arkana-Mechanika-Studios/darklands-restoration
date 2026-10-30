using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The battlefield builder's enemy records against the original program: each scenario in
/// Corpus/enemies.json is a chain of original calls (monster-type-lookup …, monsters-repair,
/// groups-repair) run from DARKLAND.EXE on the player's DARKLAND.ENM and DARKLAND.LST
/// (tools/oracle/build_enemy_corpus.py). Needs the game folder (DARKLANDS_DIR, or
/// the "game" key of local/paths.json); without it the test passes vacuously.
/// </summary>
public sealed class EnemyOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("enemies.json"));

    [Fact]
    public void KindsAndGroupsAreTheOriginals()
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
        foreach (var c in Data.Value.Cases("enemy_scenario"))
        {
            var s = c.Inputs;
            var label = $"scenario {n++}";
            var random = new MscRandom(s.GetProperty("state").GetUInt32());
            var forces = new BattlefieldForces(catalogue, items, tables, random);
            var levels = s.GetProperty("levels").GetInt32();
            var groupsAdded = false;
            foreach (var call in s.GetProperty("calls").EnumerateArray())
            {
                var args = call.GetProperty("args").EnumerateArray().Select(a => a.GetInt32()).ToArray();
                switch (call.GetProperty("call").GetString())
                {
                    case "R004+0x06AA":
                        Assert.True(call.GetProperty("ax").GetInt32() == forces.AddKind(args[0], args[1], args[2]), $"{label}: AddKind({string.Join(",", args)})");
                        break;
                    case "R004+0x122C":
                        if (!groupsAdded)
                        {
                            AddGroups(forces, s);
                            groupsAdded = true;
                        }
                        forces.FixKinds(args[0], args[1]);
                        break;
                    case "R004+0x1D90":
                        forces.FixGroups(args[0]);
                        break;
                }
            }
            var kinds = s.GetProperty("kinds");
            var groups = s.GetProperty("finalGroups");
            for (var level = 0; level < levels; level++)
            {
                var expected = kinds[level].EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.True(expected.Count == forces.Kinds[level].Count, $"{label}: kinds on level {level}");
                for (var k = 0; k < expected.Count; k++)
                {
                    var actual = Convert.ToHexString(EnemyRecords.Write(forces.Kinds[level][k])).ToLowerInvariant();
                    Assert.True(expected[k] == actual, $"{label}: level {level} kind {k}\n{expected[k]}\n{actual}");
                }
                var expectedGroups = groups[level].EnumerateArray().Select(e => e.GetString()!).ToList();
                Assert.True(expectedGroups.Count == forces.Groups[level].Count, $"{label}: groups on level {level}");
                for (var g = 0; g < expectedGroups.Count; g++)
                {
                    var actual = Convert.ToHexString(EnemyRecords.Write(forces.Groups[level][g])).ToLowerInvariant();
                    Assert.True(expectedGroups[g] == actual, $"{label}: level {level} group {g}\n{expectedGroups[g]}\n{actual}");
                }
            }
            Assert.True(s.GetProperty("holdrand").GetUInt32() == random.State, $"{label}: random state");
        }
    }

    private static void AddGroups(BattlefieldForces forces, System.Text.Json.JsonElement s)
    {
        var level = 0;
        foreach (var list in s.GetProperty("groups").EnumerateArray())
        {
            foreach (var hex in list.EnumerateArray())
            {
                forces.Groups[level].Add(EnemyRecords.ReadGroup(Convert.FromHexString(hex.GetString()!)));
            }
            level++;
        }
    }
}
