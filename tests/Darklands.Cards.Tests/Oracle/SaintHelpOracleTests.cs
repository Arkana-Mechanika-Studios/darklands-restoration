using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The help of every saint against the ORIGINAL program on whole games: Oracle/saint-help.json.gz
/// is tools/oracle/saint_oracle.py running each saint function of R039 (what = 8) on the card oracle's
/// start states (the C#'s own states, loaded by the original's saved-game loader), for the leader, each
/// member and the whole party, with the saintly-changes messages on and off. <see cref="SaintBook.Help"/>
/// on the same state must show the same messages, leave the random generator in the same state and the
/// saved game with the same bytes.
/// </summary>
public sealed class SaintHelpOracleTests
{
    private static string Folder([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    /// <summary>The messages of the rules, in the oracle's words.</summary>
    private sealed class Recorder(List<string> log) : IGameMessages
    {
        public void Show(string text, int ticks = 100) => log.Add($"tell -2 {text}");

        public void Report(int member, string text, bool good) => log.Add($"tell {member} {text}");
    }

    [Fact]
    public void EverySaintHelpsAsInTheOriginal()
    {
        var path = Path.Combine(Folder(), "saint-help.json.gz");
        if (!CardHarness.Available || !File.Exists(path))
        {
            return;
        }
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var corpus = JsonDocument.Parse(zip);
        var clock = Convert.ToUInt32(corpus.RootElement.GetProperty("clock").GetString()!, 16);
        var dump = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_DUMP");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var report = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(corpus.RootElement.GetProperty("saints").EnumerateObject().ToList(), entry =>
        {
            var saint = int.Parse(entry.Name, System.Globalization.CultureInfo.InvariantCulture);
            int agree = 0, differ = 0;
            var first = "";
            foreach (var run in entry.Value.GetProperty("runs").EnumerateObject())
            {
                var difference = Compare(saint, run.Name, run.Value, clock, dump);
                if (difference is null)
                {
                    agree++;
                }
                else
                {
                    differ++;
                    first = first.Length > 0 ? first : $"{run.Name}: {difference}";
                }
            }
            report.Add($"{saint}\t{agree}\t{differ}\t{first}");
            if (differ > 0)
            {
                failures.Add($"saint {saint}: {differ} run(s) differ, first {first}");
            }
        });
        if (Environment.GetEnvironmentVariable("DARKLANDS_SAINT_ORACLE_REPORT") is { Length: > 0 } reportFile)
        {
            File.WriteAllLines(reportFile, report.OrderBy(l => int.Parse(l.Split('\t')[0], System.Globalization.CultureInfo.InvariantCulture)));
        }
        Assert.True(failures.IsEmpty, $"{failures.Count} saint(s) differ from the original:\n" +
            string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(40)));
    }

    private static string? Compare(int saint, string runName, JsonElement expected, uint clock, string? dump)
    {
        // "L3-O0 who -1 messages"
        var parts = runName.Split(' ');
        var key = parts[0];
        var location = int.Parse(key[1..key.IndexOf('-')], System.Globalization.CultureInfo.InvariantCulture);
        var option = int.Parse(key[(key.IndexOf("-O") + 2)..], System.Globalization.CultureInfo.InvariantCulture);
        var who = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        var state = CardOracleStates.New(location, option);
        state.ClockSeed = () => clock;
        state.ShowSaintlyChanges = parts[3] == "messages";
        var (start, _) = Persistence.OriginalSaves.Write(state);
        var log = new List<string>();
        state.Messages = new Recorder(log);
        string end;
        var ok = false;
        try
        {
            SaintBook.Help(state, who, saint);
            end = "returns 0";
            ok = true;
        }
        catch (Exception e)
        {
            end = $"throws {e.GetType().Name}: {e.Message}";
        }
        var random = state.Random is MscRandom msc ? msc.State.ToString("X8") : "?";
        var sav = ok ? CardOracleTests.SaveDiff(start, state) : ((string, System.Text.Json.Nodes.JsonObject)?)null;
        return CardOracleTests.CompareResult(expected, log, end, random, sav,
            dump is { Length: > 0 } ? Path.Combine(dump, $"saint {saint} {runName}.txt") : null);
    }
}
