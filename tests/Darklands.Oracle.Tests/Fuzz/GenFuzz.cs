using System.Text.Json;
using System.Text.Json.Nodes;
using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Items;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests.Fuzz;

/// <summary>
/// Coverage-guided fuzzing of the C# battlefield generators (castle, mine, wild) for the original to confirm (opt-in; code/local/battlefuzz
/// genfuzz.py; DARKLANDS_GENFUZZ_PLAN and DARKLANDS_GENFUZZ_OUT). An input is a corpus scenario as the builders take it
/// (tools/oracle/build_{castle,mine,wild}_corpus.py, DL_SCENARIOS): its settings and a "force" map over the whole draw sequence
/// (gen_force.py; ScenarioDraws here). Kept: the scenarios whose C# run adds an edge (or an edge count bucket) the kind's runs so far
/// lacked, from the edge map of an instrumented Darklands.Core.dll (reset just before the build) or, without it, the run's outputs.
/// Only the kept scenarios go to the original (with coverage); agreement is then checked case by case (DARKLANDS_GENFUZZ_CHECK: the
/// corpus file the builder wrote for them, the corpus tests' own comparison), and only agreeing cases count, by the original's trace.
/// </summary>
public sealed class GenFuzz
{
    private static readonly int[] CastleTypes = [.. Enumerable.Range(0, 9), 0x27, 0x28, .. Enumerable.Range(0x7A, 5)];
    private static readonly int[] MineTypes = [.. Enumerable.Range(0x0A, 7), 0x66, 0x67, 0x68, 0x69, 0x6C, 0x6D, .. Enumerable.Range(0x7F, 11)];
    private static readonly int[] WildTypes = [.. Enumerable.Range(0x2E, 0x56 - 0x2E), 0x61, 0x62, 0x63, .. Enumerable.Range(0x6F, 0x7A - 0x6F)];
    private static readonly int[] Raws = [0, 1, 2, 3, 5, 8, 16, 100, 1000, 4096, 8192, 16383, 16384, 24576, 32000, 32767];

    private sealed record Data(EnemyCatalogue Catalogue, IReadOnlyList<ItemDefinition> Items, CombatTables Tables, PreparedMaps Maps);

    [Fact]
    public void FuzzForTheOracle()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_GENFUZZ_OUT") is not { Length: > 0 } outDir)
        {
            return;
        }
        var edges = EdgeMap.Attach();
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        if (!GameFiles.IsGameFolder(folder))
        {
            return;
        }
        var files = new GameFiles(folder);
        var data = new Data(EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM")), WorldData.Load(files).ItemDefinitions,
            CombatTablesReader.Read(DarklandsExecutable.Load(files)), PreparedMaps.Load(files));
        Directory.CreateDirectory(outDir);
        if (Environment.GetEnvironmentVariable("DARKLANDS_GENFUZZ_CHECK") is { Length: > 0 } check)
        {
            Check(Environment.GetEnvironmentVariable("DARKLANDS_GENFUZZ_KIND") ?? "castle", check, data, outDir);
            return;
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_GENFUZZ_PLAN") is not { Length: > 0 } planFile)
        {
            return;
        }
        var plan = JsonNode.Parse(File.ReadAllText(planFile))!.AsObject();
        var kind = plan["kind"]!.GetValue<string>();
        var seconds = plan["seconds"]!.GetValue<double>();
        var maxKeep = plan["maxKeep"]!.GetValue<int>();
        var maxDraws = plan["maxDraws"]?.GetValue<int>() ?? 20000;
        // "edgesOnly": an edge counts once, whatever its count (the buckets make nearly every new seed "new"; the original is the dear side)
        var edgesOnly = plan["edgesOnly"]?.GetValue<bool>() ?? false;
        var rng = new Random(plan["seed"]!.GetValue<int>());
        using var kept = new StreamWriter(Path.Combine(outDir, "kept.jsonl"), append: true);
        using var log = new StreamWriter(Path.Combine(outDir, "fuzz.log"), append: true);
        log.WriteLine($"{kind}: edge map {(edges is null ? "none (outputs)" : "SharpFuzz")}");
        ScenarioDraws.Counting = true;
        try
        {
            var seen = new HashSet<long>();
            var tried = new HashSet<string>();
            var corpus = new List<(JsonObject Scenario, int Draws)>();
            foreach (var s in plan["seeds"]!.AsArray())
            {
                var sc = s!.AsObject().DeepClone().AsObject();
                var (features, draws, _) = RunOne(kind, sc, data, edges, edgesOnly);
                tried.Add(sc.ToJsonString());
                seen.UnionWith(features);
                corpus.Add((sc, draws));
            }
            log.WriteLine($"{kind}: {corpus.Count} seeds, {seen.Count} features");
            var started = System.Diagnostics.Stopwatch.StartNew();
            int runs = 0, keptCount = 0, dup = 0, errors = 0, tooLong = 0;
            while (corpus.Count > 0 && started.Elapsed.TotalSeconds < seconds && keptCount < maxKeep)
            {
                var (parent, parentDraws) = corpus[rng.Next(corpus.Count)];
                var child = Mutate(kind, parent, parentDraws, rng);
                var key = child.ToJsonString();
                if (!tried.Add(key))
                {
                    dup++;
                    continue;
                }
                var (features, draws, error) = RunOne(kind, child, data, edges, edgesOnly);
                runs++;
                if (draws > maxDraws)
                {
                    tooLong++;
                    continue;            // the C# gives up where the original loops on (castle 0x7C party room); not a scenario to send
                }
                var fresh = features.Count(f => !seen.Contains(f));
                if (fresh == 0)
                {
                    continue;
                }
                seen.UnionWith(features);
                corpus.Add((child, draws));
                keptCount++;
                errors += error is null ? 0 : 1;
                var line = new JsonObject { ["scenario"] = child.DeepClone(), ["fresh"] = fresh, ["draws"] = draws, ["error"] = error };
                kept.WriteLine(line.ToJsonString());
                if (keptCount % 20 == 0)
                {
                    kept.Flush();
                    log.WriteLine($"{kind}: {started.Elapsed.TotalSeconds:F0} s, {runs} runs, {keptCount} kept, {seen.Count} features");
                    log.Flush();
                }
            }
            log.WriteLine($"{kind}: done {started.Elapsed.TotalSeconds:F0} s, {runs} runs, {keptCount} kept ({errors} with an exception), {dup} duplicates, "
                + $"{tooLong} over {maxDraws} draws, {seen.Count} features");
        }
        finally
        {
            ScenarioDraws.Counting = false;
        }
    }

    /// <summary>Every case of a corpus file the builder wrote, compared by the corpus test's own code: OUT/check.jsonl, one line per case.</summary>
    private static void Check(string kind, string corpusFile, Data data, string outDir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(corpusFile));
        using var output = new StreamWriter(Path.Combine(outDir, "check.jsonl"), append: false);
        var n = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var s = c.GetProperty("inputs");
            string? failure;
            try
            {
                failure = kind switch
                {
                    "castle" => Run(() => CastleOracleTests.Compare(s, n, data.Catalogue, data.Items, data.Tables)),
                    "wild" => Run(() => WildOracleTests.Compare(s, n, data.Catalogue, data.Items, data.Tables, data.Maps)),
                    _ => MineOracleTests.RunScenario(s, s.GetProperty("bf").GetInt32(), $"scenario {n}", data.Catalogue, data.Items, data.Tables, data.Maps),
                };
            }
            catch (Exception e)
            {
                failure = $"{e.GetType().Name}: {e.Message}";
            }
            var index = s.TryGetProperty("index", out var i) ? i.GetInt32() : n;
            output.WriteLine(new JsonObject { ["n"] = n, ["index"] = index, ["agree"] = failure is null, ["message"] = failure }.ToJsonString());
            n++;
        }
    }

    private static string? Run(Action compare)
    {
        try
        {
            compare();
            return null;
        }
        catch (Xunit.Sdk.XunitException e)
        {
            return e.Message;
        }
    }

    /// <summary>One C# build: its features, its draws, the exception it ended with (if any).</summary>
    private static (List<long> Features, int Draws, string? Error) RunOne(string kind, JsonObject scenario, Data data, EdgeMap? edges, bool edgesOnly)
    {
        using var doc = JsonDocument.Parse(scenario.ToJsonString());
        var s = doc.RootElement;
        var features = new List<long>();
        string? error = null;
        string summary;
        edges?.Reset();
        try
        {
            summary = kind switch
            {
                "castle" => BuildCastle(s, data),
                "wild" => BuildWild(s, data),
                _ => MineOracleTests.BuildState(s, s.GetProperty("bf").GetInt32(), data.Catalogue, data.Items, data.Tables, data.Maps).ToString("X8"),
            };
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
            summary = e.GetType().Name + " " + (e.StackTrace?.Split('\n').FirstOrDefault() ?? "");
        }
        var draws = ScenarioDraws.LastCount;
        if (edgesOnly && edges is not null)
        {
            var plain = new List<long>();
            if (edges.Collect(plain))
            {
                features.AddRange(plain.Select(f => f >> 3 << 3).Distinct());
                if (error is not null)
                {
                    features.Add(-1 - (error.Split(':')[0].GetHashCode(StringComparison.Ordinal) & 0xFFFF));
                }
                return (features, draws, error);
            }
        }
        if (edges is null || !edges.Collect(features))
        {
            features.Add(summary.GetHashCode(StringComparison.Ordinal));
        }
        if (error is not null)
        {
            features.Add(-1 - (error.Split(':')[0].GetHashCode(StringComparison.Ordinal) & 0xFFFF));
        }
        return (features, draws, error);
    }

    private static (int, int, int)[] Specs(JsonElement s) =>
        [.. s.GetProperty("specs").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32()))];

    private static string BuildCastle(JsonElement s, Data data)
    {
        var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
        using var forcedDraws = ScenarioDraws.Begin(random, s);
        var lore = s.GetProperty("lore").GetInt32();
        var context = new BattlefieldContext
        {
            Enemies = data.Catalogue, Items = data.Items, Tables = data.Tables, Maps = data.Maps, Random = random,
            Location = s.GetProperty("location").GetInt32(),
            LoreLevel = () => lore,
            Memory = new BattlefieldMemory
            {
                LastLocation = s.GetProperty("lastLocation").GetInt32(), ClearedLevels = s.GetProperty("cleared").GetInt32(),
            },
        };
        var state = new GenerationState(context, new BattlefieldForces(data.Catalogue, data.Items, data.Tables, random));
        var levels = CastleBattlefield.Build(state, s.GetProperty("bf").GetInt32(), out var start, Specs(s));
        return $"{levels} {start} {random.State:X8}";
    }

    private static string BuildWild(JsonElement s, Data data)
    {
        var random = new MscRandom((uint)s.GetProperty("seed").GetInt32());
        using var forcedDraws = ScenarioDraws.Begin(random, s);
        var lore = s.GetProperty("lore").GetInt32();
        var context = new BattlefieldContext
        {
            Enemies = data.Catalogue, Items = data.Items, Tables = data.Tables, Maps = data.Maps, Random = random,
            WorldTerrain = s.GetProperty("terrain").GetInt32(),
            Month = s.GetProperty("month").GetInt32(),
            AbleMembers = s.GetProperty("party").EnumerateArray().Count(e => e.GetInt32() == 1),
            LoreLevel = () => lore,
            ForceMap = s.GetProperty("forceMap").GetInt32(),
            ForceSide = s.GetProperty("forceSide").GetInt32(),
        };
        var state = new GenerationState(context, new BattlefieldForces(data.Catalogue, data.Items, data.Tables, random));
        var levels = WildBattlefield.Build(state, s.GetProperty("bf").GetInt32(), Specs(s));
        return $"{levels} {random.State:X8}";
    }

    // ------------------------------------------------------------------------------------------------ mutation
    /// <summary>1..4 changes: a setting of the scenario (within what the builders' plans give), a spec, a forced draw (k up to the parent's
    /// draw count, a boundary or random raw value) or a forced draw taken away.</summary>
    internal static JsonObject Mutate(string kind, JsonObject parent, int parentDraws, Random rng)
    {
        var child = parent.DeepClone().AsObject();
        var n = 1 + rng.Next(4);
        for (var i = 0; i < n; i++)
        {
            var r = rng.NextDouble();
            if (r < 0.45)
            {
                Force(child, parentDraws, rng);
            }
            else if (r < 0.55 && child["force"] is JsonObject { Count: > 0 } force)
            {
                force.Remove(force.ElementAt(rng.Next(force.Count)).Key);
            }
            else if (r < 0.75)
            {
                Spec(kind, child, rng);
            }
            else
            {
                Setting(kind, child, rng);
            }
        }
        return child;
    }

    private static void Force(JsonObject s, int draws, Random rng)
    {
        if (s["force"] is not JsonObject force)
        {
            force = [];
            s["force"] = force;
        }
        var k = rng.Next(Math.Max(1, draws + 1));
        if (rng.NextDouble() < 0.3 && draws > 0)
        {
            k = Math.Max(0, draws - 1 - rng.Next(Math.Min(draws, 16)));       // the last draws: the compares after them
        }
        var raw = rng.NextDouble() < 0.6 ? Raws[rng.Next(Raws.Length)] : rng.Next(32768);
        force[k.ToString(System.Globalization.CultureInfo.InvariantCulture)] = raw;
    }

    private static void Spec(string kind, JsonObject s, Random rng)
    {
        var specs = s["specs"]!.AsArray();
        var i = rng.Next(specs.Count);
        var spec = specs[i]!.AsArray();
        var bf = s["bf"]!.GetValue<int>();
        var section = kind == "mine" && i == 2 && bf is >= 0x82 and <= 0x86;
        switch (rng.Next(4))
        {
            case 0:
                spec[0] = section ? rng.Next(-1, 21) : rng.NextDouble() < 0.15 ? -1 : rng.NextDouble() < 0.5 ? rng.Next(0x52)
                    : new[] { 0x12, 0x03, 0x22, 0x24, 0x26, 0x23, 0x16, 0x50, 0x4F, 0x2A }[rng.Next(10)];
                if (spec[0]!.GetValue<int>() == -1 && !section)
                {
                    spec[1] = 0;
                    spec[2] = 0;
                }
                break;
            case 1:
                spec[1] = section ? rng.Next(8) : rng.Next(7);
                break;
            default:
                spec[2] = rng.Next(kind == "mine" ? 21 : 10);
                break;
        }
    }

    private static void Setting(string kind, JsonObject s, Random rng)
    {
        var pick = rng.Next(kind switch { "wild" => 8, "mine" => 8, _ => 6 });
        switch (pick)
        {
            case 0:
                var types = kind switch { "castle" => CastleTypes, "mine" => MineTypes, _ => WildTypes };
                s["bf"] = types[rng.Next(types.Length)];
                return;
            case 1:
                s["seed"] = rng.NextDouble() < 0.5 ? rng.Next(0x10000) : (s["seed"]!.GetValue<int>() + rng.Next(-16, 17)) & 0xFFFF;
                return;
            case 2:
                s["lore"] = rng.Next(1, 11);
                return;
        }
        if (kind == "wild")
        {
            switch (pick)
            {
                case 3:
                    s["terrain"] = rng.Next(30);
                    return;
                case 4:
                    s["month"] = rng.Next(12);
                    return;
                case 5:
                    var party = new JsonArray();
                    var able = 0;
                    for (var m = 0; m < 5; m++)
                    {
                        var st = new[] { 1, 1, 0, 2, 3 }[rng.Next(5)];
                        able += st == 1 ? 1 : 0;
                        party.Add(st);
                    }
                    s["party"] = party;
                    s["ableMembers"] = able;
                    return;
                case 6:
                    s["forceMap"] = rng.NextDouble() < 0.5 ? -1 : rng.Next(1, 22);
                    return;
                default:
                    s["forceSide"] = rng.NextDouble() < 0.4 ? -1 : rng.Next(0, 6);
                    return;
            }
        }
        switch (pick)
        {
            case 3:
                s["cleared"] = kind == "castle" ? new[] { 0, 0, 1, 2, 3, 6, 31, 0x12, rng.Next(0x8000) }[rng.Next(9)]
                    : new[] { 0, 0, 0x7FFF, rng.Next(0x8000) }[rng.Next(4)];
                return;
            case 4:
                s["location"] = kind == "castle" ? new[] { 5, 7 }[rng.Next(2)] : new[] { 5, 7, -1 }[rng.Next(3)];
                return;
            case 5:
                s["lastLocation"] = new[] { -1, 5, 7, 9, s["location"]!.GetValue<int>() }[rng.Next(5)];
                return;
            case 6:
                s["forceMap"] = new[] { -1, -1, 0, rng.Next(1, 22) }[rng.Next(4)];
                return;
            default:
                if (rng.NextDouble() < 0.5)
                {
                    s["forceSide"] = new[] { -1, rng.Next(0, 7) }[rng.Next(2)];
                }
                else
                {
                    s["debugSwitch"] = s["debugSwitch"] is null ? 1 : 0;
                }
                return;
        }
    }
}
