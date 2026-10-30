using System.IO.Compression;
using System.Text.Json;
using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The mine battlefield generator against the original program: each scenario in Corpus/mine.json is the original mine-builder-entry run
/// from DARKLAND.EXE on the player's IMAPS.CAT, DARKLAND.ENM and DARKLAND.LST (tools/oracle/build_mine_corpus.py). Needs the game
/// folder (DARKLANDS_DIR, or the "game" key of local/paths.json); without it the test passes vacuously.
/// </summary>
/// <remarks>
/// Compared per scenario: the prepared maps asked for (in order), the number of levels, every saved level image, the tile description,
/// the kinds and groups of all sixteen levels, the final random state, the combat flags and the battle memory.
/// </remarks>
public sealed class MineOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load(Environment.GetEnvironmentVariable("MINE_CORPUS") ?? "mine.json"));

    /// <summary>Records which prepared maps the generator asks for, in order.</summary>
    private sealed class RecordingMaps(IBattlefieldMaps inner) : IBattlefieldMaps
    {
        public List<string> Names { get; } = [];

        public BattlefieldLevel Load(string member)
        {
            Names.Add(member.ToUpperInvariant());
            return inner.Load(member);
        }
    }

    [Fact]
    public void MineBattlefieldsAreTheOriginals()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        if (!GameFiles.IsGameFolder(folder))
        {
            return;
        }
        var files = new GameFiles(folder);
        var catalogue = EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM"));
        var items = WorldData.Load(files).ItemDefinitions;
        var executable = DarklandsExecutable.Load(files);
        var tables = CombatTablesReader.Read(executable);
        var maps = PreparedMaps.Load(files);
        var failures = new List<string>();
        var n = 0;
        foreach (var c in Data.Value.Cases("R009+0x0000"))
        {
            var s = c.Inputs;
            var bf = s.GetProperty("bf").GetInt32();
            var label = $"scenario {n++} (bf 0x{bf:X2}, seed {s.GetProperty("seed").GetInt32()}, spec3 {Compact(s.GetProperty("specs")[2])}, "
                + $"forceMap {s.GetProperty("forceMap")}, forceSide {s.GetProperty("forceSide")}, debug {Debug(s)})";
            try
            {
                var failure = RunScenario(s, bf, label, catalogue, items, tables, maps);
                if (failure is not null)
                {
                    failures.Add(failure);
                }
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                failures.Add($"{label}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} of {n} scenarios differ:\n" + string.Join("\n", failures.Take(25)));
        Assert.True(n >= 30, "the corpus has at least 30 scenarios");
    }

    private static string Compact(JsonElement e) => string.Join("/", e.EnumerateArray().Select(v => v.GetInt32()));

    private static bool Debug(JsonElement s) => s.TryGetProperty("debugSwitch", out var debug) && debug.GetInt32() != 0;

    private static string? RunScenario(JsonElement s, int bf, string label, EnemyCatalogue catalogue,
        IReadOnlyList<ItemDefinition> items, CombatTables tables, PreparedMaps maps)
    {
        var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
        var lore = s.GetProperty("lore").GetInt32();
        var recording = new RecordingMaps(maps);
        var context = new BattlefieldContext
        {
            Enemies = catalogue, Items = items, Tables = tables, Maps = recording, Random = random,
            Location = s.GetProperty("location").GetInt32(),
            LoreLevel = () => lore,
            ForceMap = s.GetProperty("forceMap").GetInt32(),
            ForceSide = s.GetProperty("forceSide").GetInt32(),
            MineDebugSwitch = Debug(s),
            Memory = new BattlefieldMemory
            {
                LastLocation = s.GetProperty("lastLocation").GetInt32(), ClearedLevels = s.GetProperty("cleared").GetInt32(),
            },
        };
        var forces = new BattlefieldForces(catalogue, items, tables, random);
        var state = new GenerationState(context, forces);
        var specs = s.GetProperty("specs").EnumerateArray()
            .Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())).ToArray();
        var levels = MineBattlefield.Build(state, bf, specs);
        var expectedReads = s.GetProperty("reads").EnumerateArray().Select(e => e.GetString()!).ToList();
        if (!expectedReads.SequenceEqual(recording.Names))
        {
            return $"{label}: maps read: expected {string.Join(",", expectedReads)} got {string.Join(",", recording.Names)}";
        }
        if (s.GetProperty("levels").GetInt32() != levels)
        {
            return $"{label}: levels {s.GetProperty("levels").GetInt32()} vs {levels}";
        }
        var expectedNames = s.GetProperty("saved").EnumerateObject().Select(p => p.Name).Order().ToList();
        var actualNames = state.SavedLevels.Keys.Select(k => $"level{k}.flr").Order().ToList();
        if (!expectedNames.SequenceEqual(actualNames))
        {
            return $"{label}: saved levels {string.Join(",", expectedNames)} vs {string.Join(",", actualNames)}";
        }
        return CompareLevels(s, state, label)
            ?? CompareTerrain(s, state, label)
            ?? CompareForces(s, forces, label)
            ?? CompareMemory(s, state, random, context, label);
    }

    private static string? CompareLevels(JsonElement s, GenerationState state, string label)
    {
        foreach (var saved in s.GetProperty("saved").EnumerateObject())
        {
            var number = int.Parse(saved.Name["level".Length..^".flr".Length], System.Globalization.CultureInfo.InvariantCulture);
            var expected = Inflate(saved.Value.GetProperty("deflated").GetString()!);
            var actual = state.SavedLevels[number].Image;
            var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => expected[i] != actual[i], -1);
            if (first >= 0)
            {
                return $"{label}: {saved.Name} differs first at +0x{first:X4} ({expected[first]:X2} vs {actual[first]:X2})";
            }
        }
        return null;
    }

    private static string? CompareTerrain(JsonElement s, GenerationState state, string label)
    {
        var terrain = Convert.FromHexString(s.GetProperty("terrain").GetString()!);
        var first = Enumerable.Range(0, terrain.Length).FirstOrDefault(i => terrain[i] != state.Terrain.Bytes[i], -1);
        return first >= 0 ? $"{label}: terrain differs at +0x{first:X2} ({terrain[first]:X2} vs {state.Terrain.Bytes[first]:X2})" : null;
    }

    private static string? CompareForces(JsonElement s, BattlefieldForces forces, string label)
    {
        for (var level = 0; level < BattlefieldForces.MaxLevels; level++)
        {
            var kinds = s.GetProperty("kinds")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
            if (kinds.Count != forces.Kinds[level].Count)
            {
                return $"{label}: kinds on level {level}: {kinds.Count} vs {forces.Kinds[level].Count}";
            }
            for (var k = 0; k < kinds.Count; k++)
            {
                var actualKind = Convert.ToHexString(EnemyRecords.Write(forces.Kinds[level][k])).ToLowerInvariant();
                if (kinds[k] != actualKind)
                {
                    return $"{label}: kind {level}/{k}\n{kinds[k]}\n{actualKind}";
                }
            }
            var groups = s.GetProperty("groups")[level].EnumerateArray().Select(e => e.GetString()!).ToList();
            if (groups.Count != forces.Groups[level].Count)
            {
                return $"{label}: groups on level {level}: {groups.Count} vs {forces.Groups[level].Count}";
            }
            for (var g = 0; g < groups.Count; g++)
            {
                var actual = Convert.ToHexString(EnemyRecords.Write(forces.Groups[level][g])).ToLowerInvariant();
                if (groups[g] != actual)
                {
                    return $"{label}: group {level}/{g}\n{groups[g]}\n{actual}";
                }
            }
        }
        return null;
    }

    private static string? CompareMemory(JsonElement s, GenerationState state, MscRandom random, BattlefieldContext context, string label)
    {
        if (s.GetProperty("holdrand").GetUInt32() != random.State)
        {
            return $"{label}: random state {s.GetProperty("holdrand").GetUInt32():X8} vs {random.State:X8}";
        }
        var flags = s.GetProperty("combatFlags").GetInt32();
        if (((flags & 1) != 0) != state.Persistent)
        {
            return $"{label}: persistent {(flags & 1) != 0} vs {state.Persistent}";
        }
        if (((flags & 0x40) != 0) != state.Timed)
        {
            return $"{label}: timed {(flags & 0x40) != 0} vs {state.Timed}";
        }
        if (s.GetProperty("clearedAfter").GetInt32() != context.Memory.ClearedLevels)
        {
            return $"{label}: cleared levels {s.GetProperty("clearedAfter").GetInt32()} vs {context.Memory.ClearedLevels}";
        }
        if (s.GetProperty("lastLocationAfter").GetInt32() != context.Memory.LastLocation)
        {
            return $"{label}: last location {s.GetProperty("lastLocationAfter").GetInt32()} vs {context.Memory.LastLocation}";
        }
        return null;
    }

    private static byte[] Inflate(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
