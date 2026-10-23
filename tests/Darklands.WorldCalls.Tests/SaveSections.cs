namespace Darklands.WorldCalls.Tests;

/// <summary>A saved game cut as tools/oracle/savdiff.py cuts it: the header after the texts, the characters, the events, the places, the cache.</summary>
internal sealed class SaveSections
{
    private const int TextsEnd = 0x64, HeadEnd = 0x189, Char = 0x22A, Event = 0x30, Place = 0x3A;

    public Dictionary<string, List<byte[]>> Parts { get; } = [];

    public static SaveSections Cut(byte[] b) => Cut(b, out _);

    /// <summary>
    /// Cuts a saved game; <paramref name="missing"/> is the number of character records the file holds fewer than its count says.
    /// The original's writer (R037+0x2ACE) leaves out the record of a party slot whose status is 0 and still writes the full count
    /// (registered original bug B-1cec30, OriginalBehaviour.FixSaveWriterSkipsEmptySlots; off, i.e. faithful, in the oracles): such a
    /// file is cut with that many records fewer, found as the one layout whose place count is the game's 414.
    /// </summary>
    public static SaveSections Cut(byte[] b, out int missing)
    {
        var n = BitConverter.ToUInt16(b, 0xF1);
        SaveSections? first = null;
        Exception? error = null;
        for (missing = 0; missing <= n; missing++)
        {
            try
            {
                var s = Cut(b, n - missing);
                if (s.Parts["places"].Count == Places)
                {
                    return s;
                }
                first ??= missing == 0 ? s : null;
            }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
            {
                error ??= e;
            }
        }
        missing = 0;
        return first ?? throw error!;
    }

    private const int Places = 0x19E;

    private static SaveSections Cut(byte[] b, int characters)
    {
        var s = new SaveSections();
        s.Parts["head"] = [b[TextsEnd..HeadEnd]];
        var at = HeadEnd;
        s.Parts["chars"] = Records(b, ref at, characters, Char);
        var m = BitConverter.ToUInt16(b, at);
        at += 2;
        s.Parts["events"] = Records(b, ref at, m, Event);
        var p = BitConverter.ToUInt16(b, at);
        at += 2;
        s.Parts["places"] = Records(b, ref at, p, Place);
        s.Parts["cache"] = [b[at..]];
        return s;
    }

    private static List<byte[]> Records(byte[] b, ref int at, int count, int size)
    {
        var list = new List<byte[]>();
        for (var k = 0; k < count; k++)
        {
            list.Add(b[(at + k * size)..(at + (k + 1) * size)]);
        }
        at += count * size;
        return list;
    }

    /// <summary>The (section, record, offset) places where two saved games differ (a record past the shorter list: offset -1).</summary>
    public static HashSet<(string, int, int)> Differences(SaveSections a, SaveSections b)
    {
        var d = new HashSet<(string, int, int)>();
        foreach (var name in a.Parts.Keys)
        {
            var x = a.Parts[name];
            var y = b.Parts[name];
            for (var r = 0; r < Math.Max(x.Count, y.Count); r++)
            {
                if (r >= x.Count || r >= y.Count)
                {
                    d.Add((name, r, -1));
                    continue;
                }
                if (x[r].Length != y[r].Length)
                {
                    d.Add((name, r, -2));
                    continue;
                }
                for (var o = 0; o < x[r].Length; o++)
                {
                    if (x[r][o] != y[r][o])
                    {
                        d.Add((name, r, o));
                    }
                }
            }
        }
        return d;
    }

    public int? At((string Section, int Record, int Offset) k) =>
        Parts.TryGetValue(k.Section, out var list) && k.Record < list.Count && k.Offset >= 0 && k.Offset < list[k.Record].Length ? list[k.Record][k.Offset] : null;
}
