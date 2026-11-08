using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// Coverage-guided fuzzing of the C# card runs (opt-in, tools/oracle/card_fuzz.py; DARKLANDS_CARDFUZZ_PLAN and DARKLANDS_CARDFUZZ_OUT).
/// An input is a start saved game (bytes), the run's menu choices (key part P), forced draws (parts R&lt;k&gt;_&lt;v&gt;), plain or probe,
/// and the seed run's handler call and state-free parts. The saved game is kept in its canonical form c = Write(Read(raw)) and only
/// when that is a fixed point (Write(Read(c)) == c): the original loads c with its own loader and the C# reads c with OriginalSaves.Read,
/// so both programs start from the same bytes (bytes the model does not keep vanish on both sides alike). Kept: the inputs whose C#
/// run adds coverage the card's runs so far did not have; the signal is the AFL edge map of an instrumented Darklands.Cards.dll
/// (SharpFuzz, instrumented in a copy of the build: the committed build has none), else the run's trace lines. The kept inputs are
/// written as saved games (OUT/saves/&lt;id&gt;/FUZZ.SAV) and run keys (OUT/kept.jsonl: S&lt;id&gt;-V..-O&lt;n&gt;), to be run on the original
/// with coverage and replayed here; only agreeing runs count, and the original's coverage comes from its own trace.
/// </summary>
public sealed partial class CardFuzz
{
    private const int MapSize = 1 << 16;

    // the run key's parts that change no state (mutate_branch.py NOSTATE) and those that change what a saved game does not hold (the
    // generator: s<n>, rn<n>; the bankers' standings: fu, me, ha): kept in the input's key, applied after the load as for any S key
    [GeneratedRegex(@"^(F\d+\.\d+|B\d(_\d)*|Y[cfa](_[cfa])*|T\$\w+|s\d+|(rn|fu|me|ha)-?\d+)$")]
    private static partial Regex KeptPart();

    [GeneratedRegex(@"^(P\d+(_\d+)*|R\d+_\d+)$")]
    private static partial Regex FuzzedPart();

    private sealed record Input(byte[] Sav, int Option, string[] Fixed, int[] Path, SortedDictionary<int, int> Draws, bool Probe, string? Hand,
        string Seed)
    {
        public int Menus { get; set; }

        public int DrawCount { get; set; }
    }

    [Fact]
    public void FuzzForTheOracle()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARDFUZZ_PLAN") is not { Length: > 0 } planFile
            || Environment.GetEnvironmentVariable("DARKLANDS_CARDFUZZ_OUT") is not { Length: > 0 } outDir)
        {
            return;
        }
        // first: an instrumented assembly writes into the map from its first instruction on (the cards' constructors below)
        var edges = EdgeMap.Attach();
        if (!CardHarness.Available)
        {
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(planFile));
        var plan = doc.RootElement;
        var clock = Convert.ToUInt32(plan.GetProperty("clock").GetString()!, 16);
        var seconds = plan.GetProperty("seconds").GetDouble();
        var maxKeep = plan.GetProperty("maxKeep").GetInt32();
        var rng = new Random(plan.GetProperty("seed").GetInt32());
        InPlayRangeOnly = plan.TryGetProperty("inPlayRange", out var ipr) && ipr.GetBoolean();
        var cards = typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
        Directory.CreateDirectory(Path.Combine(outDir, "saves"));
        using var kept = new StreamWriter(Path.Combine(outDir, "kept.jsonl"), append: true);
        using var log = new StreamWriter(Path.Combine(outDir, "fuzz.log"), append: true);
        log.WriteLine($"edge map: {(edges is null ? "none (trace lines)" : "SharpFuzz")}");
        log.Flush();
        Watchdog.Start(Path.Combine(outDir, "hangs.log"), plan.TryGetProperty("hangSeconds", out var hs) ? hs.GetDouble() : 20);
        ForcedDraws.Counting = true;
        try
        {
            foreach (var entry in plan.GetProperty("cards").EnumerateObject())
            {
                var number = Convert.ToInt32(entry.Name, 16);
                if (!cards.TryGetValue(number, out var card))
                {
                    continue;
                }
                var seeds = entry.Value.EnumerateArray().Select(e => e.GetString()!).ToList();
                FuzzCard(card, seeds, clock, seconds, maxKeep, rng, edges, outDir, kept, log);
                kept.Flush();
                log.Flush();
            }
        }
        finally
        {
            ForcedDraws.Counting = false;
            CardOracleTests.RunStarting = null;
            CardOracleStates.InMemorySaves.Clear();
        }
    }

    private static void FuzzCard(Card card, List<string> seeds, uint clock, double seconds, int maxKeep, Random rng, EdgeMap? edges, string outDir,
        StreamWriter kept, StreamWriter log)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var seen = new HashSet<long>();
        var tried = new HashSet<string>();
        var corpus = new List<Input>();
        int runs = 0, noops = 0, rejected = 0, keptCount = 0;
        foreach (var seed in seeds)
        {
            if (SeedInput(seed) is not { } input)
            {
                rejected++;
                continue;
            }
            var (features, _) = RunOne(card, input, clock, edges, tried);
            runs++;
            seen.UnionWith(features);
            corpus.Add(input);
        }
        while (corpus.Count > 0 && started.Elapsed.TotalSeconds < seconds && keptCount < maxKeep)
        {
            var parent = corpus[rng.Next(corpus.Count)];
            var child = Mutate(parent, rng);
            if (child is null)
            {
                noops++;
                continue;
            }
            var (features, key) = RunOne(card, child, clock, edges, tried);
            if (key is null)
            {
                noops++;
                continue;
            }
            runs++;
            var fresh = features.Count(f => !seen.Contains(f));
            if (fresh == 0)
            {
                continue;
            }
            seen.UnionWith(features);
            corpus.Add(child);
            keptCount++;
            var id = IdOf(child.Sav);
            var dir = Path.Combine(outDir, "saves", id);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "FUZZ.SAV"), child.Sav);
            }
            kept.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["card"] = card.Number,
                ["key"] = key,
                ["option"] = child.Option,
                ["probe"] = child.Probe,
                ["hand"] = child.Hand,
                ["new"] = fresh,
                ["seed"] = child.Seed,
            }));
            kept.Flush();
        }
        log.WriteLine($"card {card.Number:X} seeds {seeds.Count} rejected {rejected} runs {runs} no-ops {noops} kept {keptCount} features {seen.Count} {started.Elapsed.TotalSeconds:F0}s");
    }

    /// <summary>A seed run ("key" or "key M1L7.."): its start state's saved game in canonical form, its parts.</summary>
    private static Input? SeedInput(string seed)
    {
        var p = seed.Split(' ');
        var key = p[0];
        var hand = p.Length > 1 ? p[1] : null;
        var option = int.Parse(key[(key.LastIndexOf("-O", StringComparison.Ordinal) + 2)..]);
        byte[] sav;
        try
        {
            sav = CardOracleStates.Export(CardOracleStates.FromKey(key)).Sav;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
        if (Canonical(sav, option) is not { } canonical)
        {
            return null;
        }
        var v = key.IndexOf("-V", StringComparison.Ordinal);
        var parts = v < 0 ? [] : key[(v + 2)..key.LastIndexOf("-O", StringComparison.Ordinal)].Split('+');
        var path = CardOracleStates.PathOf(key) ?? [];
        var draws = new SortedDictionary<int, int>(CardOracleStates.DrawsOf(key));
        return new Input(canonical, option, parts.Where(x => KeptPart().IsMatch(x)).ToArray(), path, draws, true, hand, seed);
    }

    /// <summary>The plan's "inPlayRange": mutants outside <see cref="InPlayRange"/> are not run.</summary>
    private static bool InPlayRangeOnly { get; set; }

    /// <summary>
    /// Whether a canonical saved game keeps the values play gives where the first fuzz pass's differences all came from (cardfuzz
    /// notes: every differing run started outside them): the date (month 0..11, day 0..30, hour 0..23 at +0x6A / +0x6C / +0x6E),
    /// location-now (+0x7C) in -1 .. the places' count, the places' count of the parent, no record count grown past a few records; and
    /// (the second round's differences) the leader a party slot, the card fields card numbers, the places' unchanging fields the parent's.
    /// </summary>
    public static bool InPlayRange(byte[] c, byte[] parent)
    {
        if (c.Length > parent.Length + 4 * 0x22A || c.Length < 0x189)
        {
            return false;
        }
        var month = BitConverter.ToInt16(c, 0x6A);
        var day = BitConverter.ToInt16(c, 0x6C);
        var hour = BitConverter.ToInt16(c, 0x6E);
        if (month is < 0 or > 11 || day is < 0 or > 30 || hour is < 0 or > 23)
        {
            return false;
        }
        // the leader (+0xA1) a party slot; the card, the two remembered cards and the previous card (+0x82, +0x86, +0x88, +0x8A) card numbers
        if (c[0xA1] >= 5 || new[] { 0x82, 0x86, 0x88, 0x8A }.Any(at => BitConverter.ToInt16(c, at) is < -6 or > 365))
        {
            return false;
        }
        if (Places(c) is not { } n || n != Places(parent) || BitConverter.ToInt16(c, 0x7C) is var location && (location < -1 || location >= n))
        {
            return false;
        }
        // the places' fields play never changes (icon, position, ranges, cards, size: +0x00..+0x11; the name +0x26..): as the parent's
        var (at1, at2) = (PlacesAt(c), PlacesAt(parent));
        for (var k = 0; k < n; k++)
        {
            var (a, b) = (at1 + k * 0x3A, at2 + k * 0x3A);
            if (!c.AsSpan(a, 0x12).SequenceEqual(parent.AsSpan(b, 0x12)) || !c.AsSpan(a + 0x26, 0x14).SequenceEqual(parent.AsSpan(b + 0x26, 0x14)))
            {
                return false;
            }
        }
        return true;
    }

    private static int PlacesAt(byte[] b)
    {
        var at = 0x189 + BitConverter.ToUInt16(b, 0xF1) * 0x22A;
        return at + 2 + BitConverter.ToUInt16(b, at) * 0x30 + 2;
    }

    private static int? Places(byte[] b)
    {
        var at = 0x189 + BitConverter.ToUInt16(b, 0xF1) * 0x22A;
        if (at + 2 > b.Length)
        {
            return null;
        }
        at += 2 + BitConverter.ToUInt16(b, at) * 0x30;
        return at + 2 > b.Length ? null : BitConverter.ToUInt16(b, at);
    }

    /// <summary>Write(Read(raw)) when it is a fixed point of the round trip, else null.</summary>
    public static byte[]? Canonical(byte[] raw, int option)
    {
        try
        {
            var c = CardOracleStates.Export(CardOracleStates.FromBytes(raw, option)).Sav;
            var again = CardOracleStates.Export(CardOracleStates.FromBytes(c, option)).Sav;
            return again.AsSpan().SequenceEqual(c) ? c : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string IdOf(byte[] sav) => "fz" + Convert.ToHexString(SHA1.HashData(sav))[..12].ToLowerInvariant();

    public static string KeyOf(string id, int option, IEnumerable<string> fixedParts, int[] path, IReadOnlyDictionary<int, int> draws)
    {
        var parts = fixedParts.ToList();
        if (path.Length > 0)
        {
            parts.Add("P" + string.Join('_', path));
        }
        parts.AddRange(draws.OrderBy(d => d.Key).Select(d => $"R{d.Key}_{d.Value}"));
        return parts.Count == 0 ? $"S{id}-O{option}" : $"S{id}-V{string.Join('+', parts)}-O{option}";
    }

    private static (List<long> Features, string? Key) RunOne(Card card, Input input, uint clock, EdgeMap? edges, HashSet<string> tried)
    {
        var id = IdOf(input.Sav);
        var key = KeyOf(id, input.Option, input.Fixed, input.Path, input.Draws);
        if (!tried.Add($"{key} {input.Probe} {input.Hand}"))
        {
            return ([], null);
        }
        CardOracleStates.InMemorySaves[id] = input.Sav;
        CardOracleTests.RunStarting = edges is null ? null : edges.Reset;
        Watchdog.Running($"{card.Number:X} {key} {input.Probe} {input.Hand}");
        var fresh = (Card)Activator.CreateInstance(card.GetType())!;
        List<string> trace;
        string end;
        try
        {
            (trace, end, _, _) = CardOracleTests.Run(fresh, key, input.Option, input.Probe, clock, input.Hand is { } h ? HandCall.Parse(h) : null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            (trace, end) = ([], $"harness {e.GetType().Name}");
        }
        finally
        {
            Watchdog.Running(null);
            CardOracleTests.RunStarting = null;
            CardOracleStates.InMemorySaves.TryRemove(id, out _);
        }
        input.Menus = trace.Count(l => l.StartsWith("menu ", StringComparison.Ordinal));
        input.DrawCount = ForcedDraws.LastCount;
        var features = new List<long>();
        if (edges is not null && edges.Collect(features))
        {
            features.Add(Hash("end " + end.Split(':')[0]));
            return (features, key);
        }
        // no edge map: the trace lines (numbers folded) and the end
        features.AddRange(trace.Select(l => Hash(Digits().Replace(l, "#"))));
        features.Add(Hash("end " + end.Split(':')[0]));
        return (features, key);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    private static long Hash(string s)
    {
        var h = 1469598103934665603L;
        foreach (var ch in s)
        {
            h = unchecked((h ^ ch) * 1099511628211L);
        }
        return h | 1L << 62;
    }

    private static readonly int[] Interesting = [0, 1, 2, 3, 4, 5, 7, 8, 10, 15, 16, 20, 32, 50, 64, 99, 100, 127, 128, 255, 256, 1000, 0x7FFF, 0x8000, 0xFFFF, 0xFFFE];

    private static readonly int[] DrawValues = [0, 1, 2, 327, 3276, 8191, 8192, 10922, 16383, 16384, 21845, 24575, 24576, 29490, 32440, 32766, 32767];

    /// <summary>One to four changes of the parent; null when the saved game's change does not survive the round trip.</summary>
    private static Input? Mutate(Input parent, Random rng)
    {
        var sav = parent.Sav;
        var path = parent.Path;
        var draws = parent.Draws;
        var probe = parent.Probe;
        var savChanged = false;
        for (var n = 1 + rng.Next(4); n > 0; n--)
        {
            switch (rng.Next(10))
            {
                case < 5:
                    sav = savChanged ? sav : (byte[])sav.Clone();
                    savChanged = true;
                    // the header (the saved-game words the cards read most) a third of the time, else anywhere
                    var at = rng.Next(3) == 0 ? rng.Next(Math.Min(0x189, sav.Length - 1)) : rng.Next(sav.Length - 1);
                    var value = rng.Next(3) switch
                    {
                        0 => Interesting[rng.Next(Interesting.Length)],
                        1 => (sav[at] | sav[at + 1] << 8) + rng.Next(-8, 9),
                        _ => rng.Next(0x10000),
                    };
                    sav[at] = (byte)value;
                    if (rng.Next(2) == 0)
                    {
                        sav[at + 1] = (byte)(value >> 8);
                    }
                    break;
                case < 7:
                    var menus = Math.Max(1, Math.Min(12, parent.Menus));
                    var p = path.Length >= menus ? (int[])path.Clone() : [.. path, .. Enumerable.Repeat(0, menus - path.Length)];
                    if (path.Length == 0)
                    {
                        // without a P part the policy turns the lines round from the option's line: start from a random choice per menu
                        for (var i = 0; i < p.Length; i++)
                        {
                            p[i] = rng.Next(20);
                        }
                    }
                    p[rng.Next(p.Length)] = rng.Next(20);
                    path = p;
                    break;
                case < 9:
                    var d = new SortedDictionary<int, int>(draws);
                    var k = rng.Next(Math.Max(1, parent.DrawCount + 1));
                    d[k] = rng.Next(2) == 0 ? DrawValues[rng.Next(DrawValues.Length)] : rng.Next(0x8000);
                    draws = d;
                    break;
                default:
                    if (parent.Hand is null)
                    {
                        probe = !probe;
                    }
                    break;
            }
        }
        if (savChanged)
        {
            if (Canonical(sav, parent.Option) is not { } c || c.AsSpan().SequenceEqual(parent.Sav) || (InPlayRangeOnly && !InPlayRange(c, parent.Sav)))
            {
                return null;
            }
            sav = c;
        }
        return parent with { Sav = sav, Path = path, Draws = draws, Probe = probe };
    }

    /// <summary>
    /// A C# run that does not end (a card looping on a mutated state): the run's input goes to hangs.log and the process ends
    /// (a run cannot be stopped from outside); the driver goes on with the cards not yet done.
    /// </summary>
    private static class Watchdog
    {
        private static string? _what;
        private static long _since;

        public static void Running(string? what)
        {
            Volatile.Write(ref _since, Environment.TickCount64);
            Volatile.Write(ref _what, what);
        }

        public static void Start(string file, double seconds)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000);
                    if (Volatile.Read(ref _what) is { } what && Environment.TickCount64 - Volatile.Read(ref _since) > seconds * 1000)
                    {
                        File.AppendAllText(file, $"hang {what}\n");
                        Environment.FailFast($"cardfuzz: a run took more than {seconds} s: {what}");
                    }
                }
            }) { IsBackground = true };
            thread.Start();
        }
    }

    /// <summary>The AFL edge map of an instrumented Darklands.Cards.dll (SharpFuzz.Common.Trace), or none.</summary>
    private sealed class EdgeMap
    {
        private readonly IntPtr _memory;
        private readonly FieldInfo _previous;
        private readonly byte[] _zero = new byte[MapSize];
        private readonly byte[] _read = new byte[MapSize];

        private EdgeMap(IntPtr memory, FieldInfo previous)
        {
            _memory = memory;
            _previous = previous;
        }

        public static EdgeMap? Attach()
        {
            Type? trace;
            try
            {
                trace = Type.GetType("SharpFuzz.Common.Trace, SharpFuzz.Common", throwOnError: false);
            }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                return null;
            }
            if (trace?.GetField("SharedMem") is not { } shared || trace.GetField("PrevLocation") is not { } previous)
            {
                return null;
            }
            var memory = Marshal.AllocHGlobal(MapSize);
            // the field is a byte*: stored through a two-instruction method (no unsafe code here)
            var set = new DynamicMethod("SetSharedMem", typeof(void), [typeof(IntPtr)], typeof(CardFuzz).Module, skipVisibility: true);
            var il = set.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stsfld, shared);
            il.Emit(OpCodes.Ret);
            set.CreateDelegate<Action<IntPtr>>()(memory);
            var map = new EdgeMap(memory, previous);
            map.Reset();
            return map;
        }

        public void Reset()
        {
            Marshal.Copy(_zero, 0, _memory, MapSize);
            _previous.SetValue(null, 0);
        }

        /// <summary>The run's edges as features (edge, AFL count bucket); false when the map stayed empty (the build is not instrumented).</summary>
        public bool Collect(List<long> features)
        {
            Marshal.Copy(_memory, _read, 0, MapSize);
            var any = false;
            for (var i = 0; i < MapSize; i++)
            {
                if (_read[i] == 0)
                {
                    continue;
                }
                any = true;
                int count = _read[i];
                var bucket = count switch { 1 => 0, 2 => 1, 3 => 2, < 8 => 3, < 16 => 4, < 32 => 5, < 128 => 6, _ => 7 };
                features.Add((long)i << 3 | (long)bucket);
            }
            return any;
        }
    }
}
