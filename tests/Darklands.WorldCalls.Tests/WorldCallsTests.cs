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

        /// <summary>
        /// Set by an adapter whose rewrite call did more than the original function: the road encounter's card, which overland_loop R030+0x02C0
        /// chooses (its sel == 1000 branch) right after 0c44 returns 1300's 1000, with no draw in between (0c44 returns at once). The rewrite's
        /// first draws must then be the original's exactly (the generator's state after as many draws as the original made is the original's);
        /// the draws after them are the loop's.
        /// </summary>
        public bool Lenient { get; set; }

        /// <summary>DARKLANDS_WORLD_CALLS_STRICT=1: the adapters do not normalise or relax anything (the travel step's road-encounter comparison by the draw prefix is off).</summary>
        public static bool Strict { get; } = Environment.GetEnvironmentVariable("DARKLANDS_WORLD_CALLS_STRICT") == "1";

        /// <summary>Which lenient comparisons the job relied on (shown as the detail of an agreeing job).</summary>
        public List<string> Tags { get; } = [];

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
            var (verdict, detail) = Run(job, jobs.States, data, only is not null);
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

    private static (string, string) Run(WorldJob job, Dictionary<string, StateImage> states, WorldData data, bool verbose)
    {
        GameState state;
        byte[] before = job.Before;
        // The original's writer leaves out the record of a status-0 party slot and keeps the full count (B-1cec30, FixSaveWriterSkipsEmptySlots,
        // faithful here): a "before" file of the writer then lacks that record, and the rewrite is given what the original loaded instead (the
        // state's file with the job's edits). Its own faithful writer must leave out the same record (compared below like any other job).
        var input = before;
        var shortBefore = false;
        try
        {
            shortBefore = SaveSections.Cut(before, out var missing) is not null && missing > 0;
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
        {
        }
        if (shortBefore)
        {
            if ((states.TryGetValue(job.State, out var image) ? job.Edited(image.Sav) : null) is not { } edited)
            {
                return ("SKIP", "the original's writer left a status-0 slot record out of the before file (B-1cec30) and the job has no edits to rebuild what it loaded");
            }
            input = edited;
        }
        var generator = new MscRandom(job.Random0);
        var counting = new CountingRandom(generator);
        try
        {
            state = OriginalSaves.Read(input, [], "dksave0.sav", data, null, counting).State;
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
        if (context.Lenient ? counting.Draws < o.Draws.Count || counting.StateAfter(o.Draws.Count, job.Random0) != o.Random : generator.State != o.Random || counting.Draws != o.Draws.Count)
        {
            notes.Add($"random original {o.Random:X8} rewrite {generator.State:X8} ({o.Draws.Count} draws, rewrite {counting.Draws})");
        }
        var origAfter = o.SavAfter is null ? before : WorldJobs.Unpack(o.SavAfter);
        SaveSections ob, oa, cb, ca;
        int missingBefore, missingAfter, missingRewrite;
        var which = "";
        try
        {
            which = "original before";
            ob = SaveSections.Cut(before, out missingBefore);
            which = "original after";
            oa = SaveSections.Cut(origAfter, out missingAfter);
            which = "rewrite before";
            cb = SaveSections.Cut(start, out _);
            which = "rewrite after";
            ca = SaveSections.Cut(after, out missingRewrite);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
        {
            // a saved game that does not cut into records: the original's own writer's file with more events than its count, or the rewrite's with a different layout
            return (which.StartsWith("original", StringComparison.Ordinal) ? "SKIP" : "DIFFER", "saved game does not cut: " + which + " (" + (which.StartsWith("original", StringComparison.Ordinal) ? origAfter.Length : after.Length) + " bytes)");
        }
        // The original's writer leaves out the record of a status-0 party slot and keeps the full count (B-1cec30, FixSaveWriterSkipsEmptySlots,
        // faithful here): the rewrite's faithful writer must leave out the same records; the cut files are then compared as usual.
        if (missingBefore > 0 || missingAfter > 0 || missingRewrite > 0)
        {
            context.Tags.Add("short-file");
            if (missingAfter != missingRewrite)
            {
                notes.Add($"records left out of the after file: original {missingAfter}, rewrite {missingRewrite} (B-1cec30)");
            }
        }
        var changed = SaveSections.Differences(ob, oa);
        changed.UnionWith(SaveSections.Differences(cb, ca));
        var apart = SaveSections.Differences(oa, ca).Where(changed.Contains).OrderBy(k => k.Item1).ThenBy(k => k.Item2).ThenBy(k => k.Item3).ToList();
        if (apart.Count > 0)
        {
            var k = apart[0];
            notes.Add($"saved game {k.Item1}[{k.Item2}]+0x{k.Item3:X}: original {Show(oa.At(k))} rewrite {Show(ca.At(k))} ({apart.Count} places; before {Show(ob.At(k))})");
            if (Context.Strict)
            {
                notes.Add("places: " + string.Join(" ", apart.Take(16).Select(p => $"{p.Item1}[{p.Item2}]+0x{p.Item3:X}")));
            }
        }
        return notes.Count == 0 ? ("agree", string.Join(",", context.Tags)) : ("DIFFER", string.Join("; ", notes));
    }

    /// <summary>The game's generator with a count of the draws.</summary>
    internal sealed class CountingRandom(MscRandom inner) : Darklands.Core.Randomness.IRandomSource
    {
        private readonly List<uint> _states = [];

        public int Draws => _states.Count;

        /// <summary>The generator's state after the first <paramref name="draws"/> draws (<paramref name="start"/> for none).</summary>
        public uint StateAfter(int draws, uint start) => draws == 0 ? start : _states[draws - 1];

        private T Drawn<T>(T value)
        {
            _states.Add(inner.State);
            return value;
        }

        public int NextRaw() => Drawn(inner.NextRaw());

        public int Below(int n) => Drawn(inner.Below(n));

        public int Scaled(int n) => Drawn(inner.Scaled(n));

        public void Seed(uint seed) => inner.Seed(seed);
    }

    private static string Show(int? v) => v is { } b ? b.ToString("X2") : "none";
}
