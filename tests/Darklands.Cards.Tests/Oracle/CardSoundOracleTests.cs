using System.IO.Compression;
using System.Text.Json;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The sound requests of the cards against the ORIGINAL's, run by run: a collection tool for corpora recorded before the sound lines
/// existed. DARKLANDS_CARD_ORACLE_SOUNDS names a sidecar written by local/batch/audio/audio_rerun.py (the original rerun over the runs of
/// a corpus, only its "sound n" and "music x" lines kept: {"clock", "cards": {card: {"runs": {run name: [lines]}}}}); every run is replayed
/// on the C# and its sound lines must be the same sequence. The differing runs go to DARKLANDS_CARD_ORACLE_SOUND_DIFFS as
/// <c>card, run, first differing line, original's sequence, C#'s sequence, other</c> (tab separated); the test fails only when the file is not asked for.
/// With the corpus itself also given (DARKLANDS_CARD_ORACLE_CORPUS) "other" is the first difference of the run's other lines, end, random state and saved
/// game (CardOracleTests.Compare without the sound lines, before the divergence list): a sound difference on a run that differs otherwise is a consequence
/// of that difference, not a lead of its own.
/// </summary>
public sealed class CardSoundOracleTests
{
    /// <summary>
    /// Which runs of a corpus (DARKLANDS_CARD_ORACLE_CORPUS) make the C# card ask for sound at all: DARKLANDS_CARD_ORACLE_SOUND_SCAN names the file that gets
    /// <c>card, run, sound lines</c> for each of them. A run the original is not rerun on (a card whose overlay calls no sound routine and that has no
    /// battle or film) must not be one of these.
    /// </summary>
    [Fact]
    public void ScanTheRunsThatSound()
    {
        if (!CardHarness.Available || Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SOUND_SCAN") is not { Length: > 0 } outFile
            || CardOracleTests.Corpus() is not { } corpus)
        {
            return;
        }
        var cards = typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
        var clock = Convert.ToUInt32(corpus.RootElement.GetProperty("clock").GetString()!, 16);
        var found = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(corpus.RootElement.GetProperty("cards").EnumerateObject().ToList(), entry =>
        {
            if (!cards.TryGetValue(Convert.ToInt32(entry.Name, 16), out var template))
            {
                return;
            }
            foreach (var run in entry.Value.GetProperty("runs").EnumerateObject())
            {
                var parts = run.Name.Split(' ');
                var key = parts[1];
                var hand = parts[0] == "hand" ? HandCall.Parse(parts[2]) : null;
                var direct = parts[0] == "direct" ? parts[2] : null;
                var option = int.Parse(key[(key.LastIndexOf("-O") + 2)..]);
                var (trace, _, _, _) = CardOracleTests.Run((Card)Activator.CreateInstance(template.GetType())!, key, option,
                    parts[0] is "probe" or "hand" or "direct", clock, hand, direct);
                if (trace.Where(CardOracleTests.IsSound).ToList() is { Count: > 0 } sounds)
                {
                    found.Add($"{entry.Name}\t{run.Name}\t{string.Join(" | ", sounds)}");
                }
            }
        });
        File.WriteAllLines(outFile, found.OrderBy(l => l, StringComparer.Ordinal));
    }

    [Fact]
    public void SoundLinesAgreeWithTheOriginal()
    {
        if (!CardHarness.Available || Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SOUNDS") is not { Length: > 0 } sidecar || !File.Exists(sidecar))
        {
            return;
        }
        using var file = File.OpenRead(sidecar);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var corpus = JsonDocument.Parse(zip);
        var cards = typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
        var clock = Convert.ToUInt32(corpus.RootElement.GetProperty("clock").GetString()!, 16);
        using var recorded = CardOracleTests.Corpus();
        var differences = new System.Collections.Concurrent.ConcurrentBag<string>();
        var counts = new System.Collections.Concurrent.ConcurrentBag<(int Runs, int Differ, int Sounding)>();
        Parallel.ForEach(corpus.RootElement.GetProperty("cards").EnumerateObject().ToList(), entry =>
        {
            var number = Convert.ToInt32(entry.Name, 16);
            int runs = 0, differ = 0, sounding = 0;
            foreach (var run in entry.Value.GetProperty("runs").EnumerateObject())
            {
                runs++;
                var want = run.Value.EnumerateArray().Select(e => e.GetString()!).ToList();
                if (!cards.TryGetValue(number, out var template))
                {
                    continue;
                }
                var parts = run.Name.Split(' ');
                var probe = parts[0] is "probe" or "hand" or "direct";
                var key = parts[1];
                var hand = parts[0] == "hand" ? HandCall.Parse(parts[2]) : null;
                var direct = parts[0] == "direct" ? parts[2] : null;
                var option = int.Parse(key[(key.LastIndexOf("-O") + 2)..]);
                var (trace, end, _, _) = CardOracleTests.Run((Card)Activator.CreateInstance(template.GetType())!, key, option, probe, clock, hand, direct);
                var got = trace.Where(CardOracleTests.IsSound).ToList();
                if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SOUND_DUMP") is { Length: > 0 } dump && !want.SequenceEqual(got))
                {
                    // the C#'s whole trace of a differing run (what the card did around the sound lines)
                    Directory.CreateDirectory(dump);
                    File.WriteAllText(Path.Combine(dump, $"{entry.Name} {run.Name}.txt"), string.Join(Environment.NewLine, trace) + Environment.NewLine + end);
                }
                if (want.Count > 0 || got.Count > 0)
                {
                    sounding++;
                }
                if (want.SequenceEqual(got))
                {
                    continue;
                }
                differ++;
                var at = 0;
                while (at < want.Count && at < got.Count && want[at] == got[at])
                {
                    at++;
                }
                var other = "";
                if (recorded is not null && recorded.RootElement.GetProperty("cards").TryGetProperty(entry.Name, out var recordedCard)
                    && recordedCard.GetProperty("runs").TryGetProperty(run.Name, out var recordedRun))
                {
                    var texts = recordedCard.TryGetProperty("texts", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
                    other = (CardOracleTests.Compare((Card)Activator.CreateInstance(template.GetType())!, run.Name, recordedRun, clock, null, texts) ?? "")
                        .Replace('\t', ' ').Replace('\n', ' ');
                    other = other.Length > 160 ? other[..160] : other;
                }
                differences.Add($"{entry.Name}\t{run.Name}\t{at}\t{string.Join(" | ", want)}\t{string.Join(" | ", got)}\t{other}");
            }
            counts.Add((runs, differ, sounding));
        });
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SOUND_DIFFS") is { Length: > 0 } diffsFile)
        {
            File.WriteAllLines(diffsFile, differences.OrderBy(l => l, StringComparer.Ordinal));
            File.WriteAllText(diffsFile + ".summary", $"runs {counts.Sum(c => c.Runs)} sounding {counts.Sum(c => c.Sounding)} differ {counts.Sum(c => c.Differ)}\n");
        }
        else
        {
            Assert.True(differences.IsEmpty, $"{differences.Count} run(s) differ in their sound lines, e.g. {differences.OrderBy(l => l, StringComparer.Ordinal).First()}");
        }
    }
}
