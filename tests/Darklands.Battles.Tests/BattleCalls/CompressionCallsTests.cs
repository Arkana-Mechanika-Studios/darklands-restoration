using System.Text;
using System.Text.Json;
using Darklands.Persistence;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// The battle file's compression library against the original's bytes on inputs found by coverage-guided fuzzing (tools/oracle/implode_fuzz.py):
/// implode (R003+0x1E5E, entered at 1016:04C6) on generated data, types and dictionary sizes, explode (R003+0x26AE, 1016:0D07) on the compressed
/// results and on damaged copies. Set DARKLANDS_COMPRESSION_CALLS to the fuzzer's jobs.json; results.tsv and excluded.txt ("function|nN", as the
/// coverage lines name them) go to DARKLANDS_COMPRESSION_CALLS_OUT (default: the jobs file's folder). Without the file the test passes vacuously.
/// Differences are reported, not asserted: they are leads.
/// </summary>
public sealed class CompressionCallsTests
{
    [Fact]
    public void TheCompressionLibraryMakesTheOriginalsBytes()
    {
        var path = Environment.GetEnvironmentVariable("DARKLANDS_COMPRESSION_CALLS");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var results = new StringBuilder();
        var excluded = new StringBuilder();
        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            var function = job.GetProperty("function").GetString();
            string verdict;
            try
            {
                verdict = function == "implode" ? Implode(job) : Explode(job);
            }
            catch (Exception e)
            {
                verdict = "EXCEPTION " + e.GetType().Name + ": " + e.Message.Split('\n')[0];
            }
            results.AppendLine($"{id}\t{verdict}");
            if (verdict != "agree")
            {
                excluded.AppendLine(id);
            }
        }
        var folder = Environment.GetEnvironmentVariable("DARKLANDS_COMPRESSION_CALLS_OUT") ?? Path.GetDirectoryName(path)!;
        File.WriteAllText(Path.Combine(folder, "results.tsv"), results.ToString());
        File.WriteAllText(Path.Combine(folder, "excluded.txt"), excluded.ToString());
    }

    private static string Implode(JsonElement job)
    {
        var data = Convert.FromHexString(job.GetProperty("data").GetString()!);
        var stale = Convert.FromHexString(job.GetProperty("stale").GetString()!);
        var work = new byte[Persistence.Implode.WorkSize];
        for (var k = 0; k < work.Length; k++)
        {
            work[k] = stale[k % stale.Length];
        }
        var read = 0;
        var output = new List<byte>();
        var code = new Persistence.Implode(work).Compress(
            (buffer, at, size) =>
            {
                var n = Math.Min(size, data.Length - read);
                Array.Copy(data, read, buffer, at, n);
                read += n;
                return n;
            },
            (buffer, at, size) => output.AddRange(buffer.AsSpan(at, size)),
            job.GetProperty("type").GetInt32() & 0xFFFF, job.GetProperty("dictionary").GetInt32() & 0xFFFF);
        var ax = job.GetProperty("ax").GetInt32();
        if (code != ax)
        {
            return $"DIFFER code {code}, the original {ax}";
        }
        var expected = Convert.FromHexString(job.GetProperty("packed").GetString()!);
        if (!expected.AsSpan().SequenceEqual(output.ToArray()))
        {
            var first = First(expected, output);
            return $"DIFFER packed {output.Count} bytes, the original {expected.Length}; first difference at {first}";
        }
        return "agree";
    }

    private static string Explode(JsonElement job)
    {
        var packed = Convert.FromHexString(job.GetProperty("packed").GetString()!);
        var read = 0;
        var output = new List<byte>();
        var code = new Persistence.Explode().Run(
            (buffer, at, size) =>
            {
                var n = Math.Min(size, packed.Length - read);
                Array.Copy(packed, read, buffer, at, n);
                read += n;
                return n;
            },
            (buffer, at, size) => output.AddRange(buffer.AsSpan(at, size)));
        var ax = job.GetProperty("ax").GetInt32();
        if (code != ax)
        {
            return $"DIFFER code {code}, the original {ax}";
        }
        var expected = Convert.FromHexString(job.GetProperty("plain").GetString()!);
        if (!expected.AsSpan().SequenceEqual(output.ToArray()))
        {
            var first = First(expected, output);
            return $"DIFFER plain {output.Count} bytes, the original {expected.Length}; first difference at {first}";
        }
        return "agree";
    }

    private static int First(byte[] expected, List<byte> output)
    {
        var n = Math.Min(expected.Length, output.Count);
        for (var i = 0; i < n; i++)
        {
            if (expected[i] != output[i])
            {
                return i;
            }
        }
        return n;
    }
}
