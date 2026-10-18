using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The battle file's compression against the original program (Corpus/implode.json,
/// tools/oracle/build_implode_corpus.py): the PKWARE DCL 1.02 implode of record 3 (1016:04C6) on generated data of
/// every shape (random, few symbols, long runs, text, repeated records, sparse images; both literal codings, the
/// three dictionary sizes, more than one 0x1000 load, more than 0x800 bytes of output, a work area holding what an
/// earlier call left), byte for byte with its return code; and its explode (1016:0D07) on the results and on damaged copies.
/// </summary>
public sealed class ImplodeOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("implode.json"));

    [Fact]
    public void ImplodeWritesTheOriginalsBytes()
    {
        var cases = Data.Value.Cases("implode");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var data = Bytes(c.Inputs, "data");
            var stale = Bytes(c.Inputs, "stale");
            var work = new byte[Implode.WorkSize];
            for (var k = 0; k < work.Length; k++)
            {
                work[k] = stale[k % stale.Length];
            }
            var read = 0;
            var output = new List<byte>();
            var code = new Implode(work).Compress(
                (buffer, at, size) =>
                {
                    var n = Math.Min(size, data.Length - read);
                    Array.Copy(data, read, buffer, at, n);
                    read += n;
                    return n;
                },
                (buffer, at, size) => output.AddRange(buffer.AsSpan(at, size)),
                c.Inputs.GetProperty("type").GetInt32(), c.Inputs.GetProperty("dictionary").GetInt32());
            var kind = c.Inputs.GetProperty("kind").GetString();
            Assert.True(c.Ax == code, $"{kind} ({data.Length} bytes): code {code}, the original {c.Ax}");
            var expected = Convert.FromHexString(c.Element.GetProperty("packed").GetString()!);
            Assert.True(expected.AsSpan().SequenceEqual(output.ToArray()),
                $"{kind} ({data.Length} bytes, type {c.Inputs.GetProperty("type")}, dictionary {c.Inputs.GetProperty("dictionary")}): {output.Count} bytes, the original {expected.Length}");
        }
    }

    [Fact]
    public void ExplodeReturnsWhatTheOriginalReturns()
    {
        var cases = Data.Value.Cases("explode");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var packed = Bytes(c.Inputs, "packed");
            var read = 0;
            var output = new List<byte>();
            var code = new Explode().Run(
                (buffer, at, size) =>
                {
                    var n = Math.Min(size, packed.Length - read);
                    Array.Copy(packed, read, buffer, at, n);
                    read += n;
                    return n;
                },
                (buffer, at, size) => output.AddRange(buffer.AsSpan(at, size)));
            Assert.Equal(c.Ax, code);
            Assert.Equal(Convert.FromHexString(c.Element.GetProperty("plain").GetString()!), output.ToArray());
        }
    }

    private static byte[] Bytes(System.Text.Json.JsonElement e, string name) => Convert.FromHexString(e.GetProperty(name).GetString()!);
}
