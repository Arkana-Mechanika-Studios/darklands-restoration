using System.Text.Json.Nodes;
using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests.Fuzz;

/// <summary>
/// Coverage-guided fuzzing of whole C# fights for the fight oracle to confirm (opt-in; code/local/battlefuzz fightfuzz.py;
/// DARKLANDS_FIGHTFUZZ_PLAN and DARKLANDS_FIGHTFUZZ_OUT). An input is what tools/oracle/battle_oracle.py plays in both programs: the
/// battle's 12 argument words (mode 0, battlefield, seed, three enemy groups (type, quality, count)) with the party of the tester's saved
/// game (DKSAVE0) and no orders. The C# fight runs in process, started from the save as BattleCallsTests' natural battles are, for the
/// plan's clock ticks; the signal is the edge map of an instrumented Darklands.Core.dll (reset before the battle starts: the generator,
/// the placement and the fight), else the fight's end. Kept: fights adding an edge (or an edge count bucket) the runs so far lacked; they
/// are played by battle_oracle (the full comparison, C# headless against the original) and only agreeing fights count.
/// </summary>
public sealed class BattleFuzz
{
    // the battlefield types battle_oracle's fights and the corpus builders use: city, wilderness, mines, castles, special
    private static readonly int[] Battlefields =
    [
        .. Enumerable.Range(0x00, 0x11), .. Enumerable.Range(0x11, 0x2E - 0x11), .. Enumerable.Range(0x2E, 0x56 - 0x2E), 0x56, 0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C,
        0x5D, 0x5E, 0x5F, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, .. Enumerable.Range(0x6F, 0x8A - 0x6F),
    ];

    /// <summary>One fight with a progress line per clock tick (OUT/one.log) and, from tick DARKLANDS_FIGHTFUZZ_TRACE_FROM on, every draw with
    /// its callers (OUT/trace.log, at most 200,000 lines): where a fight that does not return loops (opt-in, DARKLANDS_FIGHTFUZZ_ONE).</summary>
    [Fact]
    public void TraceOneFight()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_FIGHTFUZZ_ONE") is not { Length: > 0 } one
            || Environment.GetEnvironmentVariable("DARKLANDS_FIGHTFUZZ_OUT") is not { Length: > 0 } outDir || GameFolder.Files is not { } files)
        {
            return;
        }
        Directory.CreateDirectory(outDir);
        var from = int.Parse(Environment.GetEnvironmentVariable("DARKLANDS_FIGHTFUZZ_TRACE_FROM") ?? "1000000", System.Globalization.CultureInfo.InvariantCulture);
        var v = one.Split(',').Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var progress = new StreamWriter(Path.Combine(outDir, "one.log")) { AutoFlush = true };
        using var trace = new StreamWriter(Path.Combine(outDir, "trace.log")) { AutoFlush = true };
        var lines = 0;
        var save = LoadSave(files);
        var state = Darklands.Persistence.OriginalSaves.Read(save.Sav, save.Bsv, "dksave0.sav", WorldData.Load(files)).State;
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var screen = new BattleScreen(ui, new BattleAssets(files), state, new BattleScreens(ui, () => state));
        try
        {
            progress.WriteLine("start");
            screen.Start(new BattleRequest(v[1], v[2], [(v[3], v[4], v[5]), (v[6], v[7], v[8]), (v[9], v[10], v[11])], Mode: v[0]));
            var tactics = screen.Tactics!;
            progress.WriteLine("started");
            for (var t = 0; t < 3000; t++)
            {
                progress.WriteLine($"tick {t} clock {tactics.Battle.Clock}");
                if (t == from)
                {
                    Darklands.Core.Randomness.MscRandom.Trace = s =>
                    {
                        if (lines++ < 200_000)
                        {
                            trace.WriteLine(s);
                        }
                    };
                }
                tactics.Battle.Clock++;
                if (tactics.Iterate())
                {
                    progress.WriteLine($"ended {tactics.Battle.Result}");
                    break;
                }
                if (t % 30 == 0)
                {
                    screen.Draw();
                    if (tactics.Paused)
                    {
                        tactics.TogglePause();
                    }
                }
            }
        }
        finally
        {
            Darklands.Core.Randomness.MscRandom.Trace = null;
        }
    }

    [Fact]
    public void FuzzForTheOracle()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_FIGHTFUZZ_PLAN") is not { Length: > 0 } planFile
            || Environment.GetEnvironmentVariable("DARKLANDS_FIGHTFUZZ_OUT") is not { Length: > 0 } outDir)
        {
            return;
        }
        var edges = EdgeMap.Attach();
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var plan = JsonNode.Parse(File.ReadAllText(planFile))!.AsObject();
        var seconds = plan["seconds"]!.GetValue<double>();
        var maxKeep = plan["maxKeep"]!.GetValue<int>();
        var ticks = plan["ticks"]!.GetValue<int>();
        var enemyTypes = plan["enemyTypes"]?.GetValue<int>() ?? 0x46;
        var rng = new Random(plan["seed"]!.GetValue<int>());
        Directory.CreateDirectory(outDir);
        using var kept = new StreamWriter(Path.Combine(outDir, "kept.jsonl"), append: true);
        using var log = new StreamWriter(Path.Combine(outDir, "fuzz.log"), append: true);
        log.WriteLine($"edge map {(edges is null ? "none (ends)" : "SharpFuzz")}");
        var seen = new HashSet<long>();
        var tried = new HashSet<string>();
        var corpus = new List<int[]>();
        var save = LoadSave(files);
        var current = Path.Combine(outDir, "current.txt");
        foreach (var skip in plan["skip"]?.AsArray() ?? [])
        {
            tried.Add(skip!.GetValue<string>());
        }
        foreach (var s in plan["seeds"]!.AsArray())
        {
            var request = s!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
            tried.Add(string.Join(",", request));
            var (features, _, _) = RunOne(files, save, request, ticks, edges);
            seen.UnionWith(features);
            corpus.Add(request);
        }
        log.WriteLine($"{corpus.Count} seeds, {seen.Count} features");
        log.Flush();
        var started = System.Diagnostics.Stopwatch.StartNew();
        int runs = 0, keptCount = 0, errors = 0;
        while (corpus.Count > 0 && started.Elapsed.TotalSeconds < seconds && keptCount < maxKeep)
        {
            var child = Mutate(corpus[rng.Next(corpus.Count)], rng, enemyTypes);
            if (!tried.Add(string.Join(",", child)))
            {
                continue;
            }
            // the fight about to run (a fight that never returns is found here by the driver's watchdog: fightfuzz.py, hangs.jsonl)
            File.WriteAllText(current, string.Join(",", child));
            var (features, end, error) = RunOne(files, save, child, ticks, edges);
            runs++;
            var fresh = features.Count(f => !seen.Contains(f));
            if (fresh == 0)
            {
                continue;
            }
            seen.UnionWith(features);
            corpus.Add(child);
            keptCount++;
            errors += error is null ? 0 : 1;
            kept.WriteLine(new JsonObject
            {
                ["request"] = new JsonArray([.. child.Select(v => (JsonNode)v)]), ["fresh"] = fresh, ["end"] = end, ["error"] = error,
            }.ToJsonString());
            kept.Flush();
            if (keptCount % 10 == 0)
            {
                log.WriteLine($"{started.Elapsed.TotalSeconds:F0} s, {runs} runs, {keptCount} kept, {seen.Count} features");
                log.Flush();
            }
        }
        log.WriteLine($"done {started.Elapsed.TotalSeconds:F0} s, {runs} runs, {keptCount} kept ({errors} with an exception), {seen.Count} features");
    }

    private static (byte[] Sav, byte[] Bsv) LoadSave(GameFiles files)
    {
        var folder = Path.Combine(LocalPaths.Get("game", "DARKLANDS_DIR")!, "SAVES");
        var sav = File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.SAV"));
        var bsv = File.Exists(Path.Combine(folder, "DKSAVE0.BSV")) ? File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.BSV")) : [];
        return (sav, bsv);
    }

    /// <summary>One whole C# fight from the save: its features, how it ended, the exception (if any).</summary>
    private static (List<long> Features, string End, string? Error) RunOne(GameFiles files, (byte[] Sav, byte[] Bsv) save, int[] v, int ticks, EdgeMap? edges)
    {
        var features = new List<long>();
        string? error = null;
        var end = "running";
        var world = WorldData.Load(files);
        var state = Darklands.Persistence.OriginalSaves.Read(save.Sav, save.Bsv, "dksave0.sav", world).State;
        var request = new BattleRequest(v[1], v[2], [(v[3], v[4], v[5]), (v[6], v[7], v[8]), (v[9], v[10], v[11])], Mode: v[0]);
        edges?.Reset();
        try
        {
            var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
            var screens = new BattleScreens(ui, () => state);
            var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
            screen.Start(request);
            var tactics = screen.Tactics!;
            for (var t = 0; t < ticks; t++)
            {
                tactics.Battle.Clock++;
                if (tactics.Iterate())
                {
                    end = $"ended {tactics.Battle.Result}";
                    break;
                }
                if (t % 30 == 0)
                {
                    screen.Draw();
                    if (tactics.Paused)
                    {
                        tactics.TogglePause();
                    }
                }
            }
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
            end = "exception " + e.GetType().Name;
        }
        if (edges is null || !edges.Collect(features))
        {
            features.Add(end.GetHashCode(StringComparison.Ordinal));
        }
        if (error is not null)
        {
            features.Add(-1 - (error.GetHashCode(StringComparison.Ordinal) & 0xFFFF));
        }
        return (features, end, error);
    }

    /// <summary>1..3 changes: the battlefield, the seed, a group's type / quality / count, a group taken away or added.</summary>
    internal static int[] Mutate(int[] parent, Random rng, int enemyTypes)
    {
        var c = (int[])parent.Clone();
        var n = 1 + rng.Next(3);
        for (var i = 0; i < n; i++)
        {
            var g = 3 + 3 * rng.Next(3);
            switch (rng.Next(7))
            {
                case 0:
                    c[1] = Battlefields[rng.Next(Battlefields.Length)];
                    break;
                case 1:
                case 2:
                    c[2] = rng.Next(0x8000);
                    break;
                case 3:
                    c[g] = rng.Next(enemyTypes);
                    c[g + 1] = c[g + 1] is >= 0 and <= 6 ? c[g + 1] : 2;
                    c[g + 2] = c[g + 2] is >= 1 and <= 8 ? c[g + 2] : 2;
                    break;
                case 4:
                    if (c[g] >= 0)
                    {
                        c[g + 1] = rng.Next(7);
                    }
                    break;
                case 5:
                    if (c[g] >= 0)
                    {
                        c[g + 2] = 1 + rng.Next(8);
                    }
                    break;
                default:
                    if (g > 3)
                    {
                        (c[g], c[g + 1], c[g + 2]) = c[g] >= 0 ? (-1, 0, 0) : (rng.Next(enemyTypes), rng.Next(7), 1 + rng.Next(4));
                    }
                    break;
            }
        }
        return c;
    }
}
