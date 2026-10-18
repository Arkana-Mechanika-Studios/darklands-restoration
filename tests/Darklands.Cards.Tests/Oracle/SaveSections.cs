using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// Saved-game differences by section, as tools/oracle/savdiff.py stores them: the header after the
/// two texts (+0x64..+0x189), the character records (0x22A), the events (0x30), the places (0x3A) and
/// the cache. A section with the same record count is {record: {offset: byte}} for the records that
/// changed; one whose count changed is the whole list of hex records. Unchanged sections are left out.
/// </summary>
public static class SaveSections
{
    private const int TextsEnd = 0x64;
    private const int HeadEnd = 0x189;

    private static readonly string[] Names = ["head", "chars", "events", "places", "cache"];

    public static Dictionary<string, List<byte[]>> Cut(byte[] b)
    {
        int Word(int at) => b[at] | b[at + 1] << 8;
        var n = Word(0xF1);
        var at = HeadEnd;
        var chars = Records(b, at, n, 0x22A);
        at += n * 0x22A;
        var m = Word(at);
        at += 2;
        var events = Records(b, at, m, 0x30);
        at += m * 0x30;
        var p = Word(at);
        at += 2;
        var places = Records(b, at, p, 0x3A);
        at += p * 0x3A;
        return new()
        {
            ["head"] = [b[TextsEnd..HeadEnd]],
            ["chars"] = chars,
            ["events"] = events,
            ["places"] = places,
            ["cache"] = [b[Math.Min(at, b.Length)..]],
        };
    }

    private static List<byte[]> Records(byte[] b, int at, int count, int size) =>
        Enumerable.Range(0, count).Select(k => b.AsSpan(at + k * size, size).ToArray()).ToList();

    /// <summary>The differences of <paramref name="final"/> from <paramref name="start"/>, canonical JSON.</summary>
    public static JsonObject Diff(byte[] start, byte[] final)
    {
        var a = Cut(start);
        var b = Cut(final);
        var result = new JsonObject();
        foreach (var name in Names)
        {
            var x = a[name];
            var y = b[name];
            if (x.Count != y.Count || x.Zip(y).Any(p => p.First.Length != p.Second.Length))
            {
                result[name] = new JsonArray(y.Select(r => (JsonNode)JsonValue.Create(Convert.ToHexString(r))!).ToArray());
                continue;
            }
            var d = new JsonObject();
            for (var k = 0; k < x.Count; k++)
            {
                if (!x[k].AsSpan().SequenceEqual(y[k]))
                {
                    var bytes = new JsonObject();
                    for (var i = 0; i < y[k].Length; i++)
                    {
                        if (x[k][i] != y[k][i])
                        {
                            bytes[i.ToString("X")] = y[k][i].ToString("X2");
                        }
                    }
                    d[k.ToString(System.Globalization.CultureInfo.InvariantCulture)] = bytes;
                }
            }
            if (d.Count > 0)
            {
                result[name] = d;
            }
        }
        return result;
    }

    /// <summary>JSON with the object keys sorted (the form both sides are compared in).</summary>
    public static string Canonical(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(node, text);
        return text.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder text)
    {
        switch (node)
        {
            case JsonObject o:
                text.Append('{');
                var first = true;
                foreach (var (k, v) in o.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    text.Append(first ? "" : ",").Append('"').Append(k).Append("\":");
                    Write(v, text);
                    first = false;
                }
                text.Append('}');
                break;
            case JsonArray arr:
                text.Append('[');
                for (var i = 0; i < arr.Count; i++)
                {
                    text.Append(i > 0 ? "," : "");
                    Write(arr[i], text);
                }
                text.Append(']');
                break;
            case null:
                text.Append("null");
                break;
            default:
                text.Append(node.ToJsonString());
                break;
        }
    }

    /// <summary>The first place two canonical diffs differ, for the failure message.</summary>
    public static string FirstDifference(JsonObject want, JsonObject got)
    {
        foreach (var name in Names)
        {
            var w = want[name];
            var g = got[name];
            if (Canonical(w) == Canonical(g))
            {
                continue;
            }
            if (w is JsonObject wo && g is JsonObject go)
            {
                foreach (var k in wo.Select(p => p.Key).Union(go.Select(p => p.Key)).OrderBy(int.Parse))
                {
                    if (Canonical(wo[k]) != Canonical(go[k]))
                    {
                        return $"{name} record {k}: original {Short(Canonical(wo[k]))}, C# {Short(Canonical(go[k]))}";
                    }
                }
            }
            return $"{name}: original {Short(Canonical(w))}, C# {Short(Canonical(g))}";
        }
        return "?";
    }

    private static string Short(string s) => s.Length > 200 ? s[..200] + "…" : s;
}
