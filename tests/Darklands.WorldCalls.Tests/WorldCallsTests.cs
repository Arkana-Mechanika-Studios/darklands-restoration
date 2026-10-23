using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.WorldCalls.Tests;

/// <summary>
/// Single functions of the original outside battle against the rewrite, from the same saved game: tools/oracle/world_calls.py takes machine
/// states inside whole lockstep sessions, may edit their saved game (the original loads the edited bytes), calls ONE original function and
/// has the original's own writer make the saved game of the machine before and after. This test reads the rewrite's game from the "before"
/// bytes (the generator at the state the original had), makes the corresponding call (<see cref="Adapters"/>), writes the game back and
/// compares: AX, the generator's state, and every byte of the saved game the original or the rewrite changed. Set DARKLANDS_WORLD_CALLS to the
/// jobs file (world_calls.py export); DARKLANDS_WORLD_CALLS_OUT is the folder for results.tsv (every job) and excluded.txt (the jobs that do not
/// agree, "function|state#n" as the coverage lines name them). Without the jobs file (or the game) the test passes vacuously. Differences are
/// reported, not asserted: they are leads.
/// </summary>
public sealed class WorldCallsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>A call the rewrite cannot be asked (a state the game never has, state it does not carry): reported, not judged.</summary>
    internal sealed class SkipJob(string why) : Exception(why);

    internal sealed class Context(GameState state, WorldData data, WorldJob job)
    {
        public GameState State { get; } = state;

        public WorldData Data { get; } = data;

        public WorldJob Job { get; } = job;

        public int[] Args => Job.Args;

        /// <summary>Set by an adapter whose rewrite call did more than the original function (the loop's branch after a road encounter): the draws may be more, the events are not compared.</summary>
        public bool Lenient { get; set; }

        /// <summary>Differences an adapter found in what it compares itself (memory outside the saved game).</summary>
        public List<string> Notes { get; } = [];

        private Simulation.Overland.WorldMap? _map;

        /// <summary>The world map with the places of the game being called.</summary>
        public Simulation.Overland.WorldMap Map => _map ??= new Simulation.Overland.WorldMap(
            Content.Formats.Maps.WorldMapData.Decode(GameFolder.Files!.Read("DARKLAND.MAP")), State.Locations);

        /// <summary>Signed word argument i.</summary>
        public int S(int i) => (short)Args[i];

        /// <summary>Unsigned word argument i.</summary>
        public int U(int i) => Args[i] & 0xFFFF;

        /// <summary>Two words as one long (low word first).</summary>
        public uint L(int i) => (uint)(U(i) | U(i + 1) << 16);
    }

    [Fact]
    public void TheRewriteAgreesWithTheOriginalCallByCall()
    {
        if (GameFolder.Files is null || WorldJobs.Load() is not { } jobs)
        {
            return;
        }
        var data = WorldData.Load(GameFolder.Files);
        var only = Environment.GetEnvironmentVariable("DARKLANDS_WORLD_CALLS_ONLY");
        var rows = new List<(string Id, string Verdict, string Detail)>();
        foreach (var job in jobs.Jobs)
        {
            if (only is not null && !job.Id.StartsWith(only, StringComparison.Ordinal))
            {
                continue;
            }
            rows.Add((job.Id, "", ""));
            var (verdict, detail) = Run(job, data, only is not null);
            rows[^1] = (job.Id, verdict, detail);
        }
        var counts = rows.GroupBy(r => r.Verdict).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        output.WriteLine("world calls: " + string.Join(", ", counts));
        foreach (var r in rows.Where(r => r.Verdict is "DIFFER" or "EXCEPTION").Take(60))
        {
            output.WriteLine($"{r.Verdict}  {r.Id}  {r.Detail}");
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_WORLD_CALLS_OUT") is { Length: > 0 } outFolder)
        {
            Directory.CreateDirectory(outFolder);
            File.WriteAllLines(Path.Combine(outFolder, "results.tsv"), rows.Select(r => $"{r.Id}\t{r.Verdict}\t{r.Detail}"));
            File.WriteAllLines(Path.Combine(outFolder, "excluded.txt"), rows.Where(r => r.Verdict != "agree").Select(r => r.Id));
        }
    }

    private static (string, string) Run(WorldJob job, WorldData data, bool verbose)
    {
        GameState state;
        byte[] before = job.Before;
        var generator = new MscRandom(job.Random0);
        var counting = new CountingRandom(generator);
        try
        {
            state = OriginalSaves.Read(before, [], "dksave0.sav", data, null, counting).State;
        }
        catch (Exception e)
        {
            return ("GAP", "the saved game does not load: " + e.GetType().Name + " " + e.Message);
        }
        var start = OriginalSaves.Write(state).Sav;
        var seeds = new Queue<uint>(job.Orig.Seeds.Where(s => s[1] == 1).Select(s => (uint)s[0]));
        state.ClockSeed = () => seeds.Count > 0 ? seeds.Dequeue() : 0;
        if (!Adapters.Map.TryGetValue(job.Function, out var adapter))
        {
            return ("GAP", "no adapter for " + job.Function);
        }
        int? ax;
        var context = new Context(state, data, job);
        try
        {
            ax = adapter(context);
        }
        catch (SkipJob e)
        {
            return ("SKIP", e.Message);
        }
        catch (Exception e)
        {
            return ("EXCEPTION", e.GetType().Name + ": " + e.Message + (verbose ? "\n" + e.StackTrace : ""));
        }
        var after = OriginalSaves.Write(state).Sav;
        var o = job.Orig;
        var notes = new List<string>(context.Notes);
        if (ax is { } a && (a & 0xFFFF) != (o.Ax & 0xFFFF))
        {
            notes.Add($"AX original {o.Ax & 0xFFFF:X4} rewrite {a & 0xFFFF:X4}");
        }
        if (context.Lenient ? counting.Draws < o.Draws.Count : generator.State != o.Random || counting.Draws != o.Draws.Count)
        {
            notes.Add($"random original {o.Random:X8} rewrite {generator.State:X8} ({o.Draws.Count} draws, rewrite {counting.Draws})");
        }
        var origAfter = o.SavAfter is null ? before : WorldJobs.Unpack(o.SavAfter);
        SaveSections ob, oa, cb, ca;
        var which = "";
        try
        {
            which = "original before";
            ob = SaveSections.Cut(before);
            which = "original after";
            oa = SaveSections.Cut(origAfter);
            which = "rewrite before";
            cb = SaveSections.Cut(start);
            which = "rewrite after";
            ca = SaveSections.Cut(after);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
        {
            // a saved game that does not cut into records: the original's own writer's file with more events than its count, or the rewrite's with a different layout
            // the original's own writer leaves a short file when a party slot has status 0 (the Wild Hunt's victim; registered original bug): not judged
            return (which.StartsWith("original", StringComparison.Ordinal) ? "SKIP" : "DIFFER", "saved game does not cut: " + which + " (" + (which.StartsWith("original", StringComparison.Ordinal) ? origAfter.Length : after.Length) + " bytes)");
        }
        var changed = SaveSections.Differences(ob, oa);
        changed.UnionWith(SaveSections.Differences(cb, ca));
        var apart = SaveSections.Differences(oa, ca).Where(changed.Contains).Where(k => !(context.Lenient && k.Item1 == "events")).OrderBy(k => k.Item1).ThenBy(k => k.Item2).ThenBy(k => k.Item3).ToList();
        if (apart.Count > 0)
        {
            var k = apart[0];
            notes.Add($"saved game {k.Item1}[{k.Item2}]+0x{k.Item3:X}: original {Show(oa.At(k))} rewrite {Show(ca.At(k))} ({apart.Count} places; before {Show(ob.At(k))})");
        }
        return notes.Count == 0 ? ("agree", "") : ("DIFFER", string.Join("; ", notes));
    }

    /// <summary>The game's generator with a count of the draws.</summary>
    internal sealed class CountingRandom(MscRandom inner) : Darklands.Core.Randomness.IRandomSource
    {
        public int Draws { get; private set; }

        public int NextRaw()
        {
            Draws++;
            return inner.NextRaw();
        }

        public int Below(int n)
        {
            Draws++;
            return inner.Below(n);
        }

        public int Scaled(int n)
        {
            Draws++;
            return inner.Scaled(n);
        }

        public void Seed(uint seed) => inner.Seed(seed);
    }

    private static string Show(int? v) => v is { } b ? b.ToString("X2") : "none";
}
