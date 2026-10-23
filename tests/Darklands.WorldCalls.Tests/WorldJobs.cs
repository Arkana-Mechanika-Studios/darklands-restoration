using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.WorldCalls.Tests;

/// <summary>
/// The jobs file of tools/oracle/world_calls.py (export): saved games the original wrote inside whole lockstep sessions, and the calls made
/// from the machines they came from: one original function with word arguments, after some edits to the saved game (which the original
/// loads), and what the original did (AX/DX, the generator's state, the srand calls, the saved game it writes after the call).
/// </summary>
internal sealed class WorldJobs
{
    [JsonPropertyName("states")] public Dictionary<string, StateImage> States { get; set; } = [];

    [JsonPropertyName("jobs")] public List<WorldJob> Jobs { get; set; } = [];

    public static WorldJobs? Load()
    {
        var path = Environment.GetEnvironmentVariable("DARKLANDS_WORLD_CALLS");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<WorldJobs>(gz) ?? throw new InvalidDataException(path);
    }

    internal static byte[] Unpack(string text)
    {
        using var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress);
        var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}

internal sealed class StateImage
{
    [JsonPropertyName("session")] public string Session { get; set; } = "";

    [JsonPropertyName("sav")] public string SavText { get; set; } = "";

    [JsonPropertyName("card")] public int Card { get; set; }

    [JsonPropertyName("random")] public uint Random { get; set; }

    private byte[]? _sav;

    public byte[] Sav => _sav ??= WorldJobs.Unpack(SavText);
}

internal sealed class WorldJob
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("function")] public string Function { get; set; } = "";

    [JsonPropertyName("state")] public string State { get; set; } = "";

    [JsonPropertyName("args")] public int[] Args { get; set; } = [];

    /// <summary>The saved game just before the call (the state's, or the edited one as the original's writer holds it after loading it).</summary>
    [JsonPropertyName("sav_before")] public string SavBefore { get; set; } = "";

    [JsonPropertyName("random0")] public uint Random0 { get; set; }

    /// <summary>DS words of the original's memory just before the call that the function reads and the saved game does not hold (the map view's registers ...), by DS offset.</summary>
    [JsonPropertyName("ops")] public System.Text.Json.JsonElement Ops { get; set; }

    [JsonPropertyName("ds")] public Dictionary<string, int> Ds { get; set; } = [];

    [JsonPropertyName("orig")] public OriginalOutcome Orig { get; set; } = new();

    /// <summary>A DS word of <see cref="Ds"/> (signed unless asked).</summary>
    public int Word(int offset, bool signed = true) => Ds.TryGetValue(offset.ToString(), out var v) ? signed ? (short)v : v & 0xFFFF : throw new InvalidDataException($"no DS:{offset:X4} in the job");

    public byte[] Before => WorldJobs.Unpack(SavBefore);

    /// <summary>
    /// The saved game the original loaded for the call: the state's file with the job's edits (world_fuzz.py Save.apply; the "ds" edits are
    /// memory, not file). Null when the job has no edits.
    /// </summary>
    public byte[]? Edited(byte[] stateSav)
    {
        if (Ops.ValueKind != System.Text.Json.JsonValueKind.Array || Ops.GetArrayLength() == 0)
        {
            return null;
        }
        const int HeadEnd = 0x189, Char = 0x22A, Event = 0x30, Place = 0x3A;
        var b = stateSav;
        var head = b[..HeadEnd];
        var at = HeadEnd;
        List<byte[]> Take(int count, int size)
        {
            var list = new List<byte[]>();
            for (var k = 0; k < count; k++, at += size)
            {
                list.Add(b[at..(at + size)]);
            }
            return list;
        }
        var chars = Take(BitConverter.ToUInt16(b, 0xF1), Char);
        var m = BitConverter.ToUInt16(b, at);
        at += 2;
        var events = Take(m, Event);
        var p = BitConverter.ToUInt16(b, at);
        at += 2;
        var places = Take(p, Place);
        var cache = b[at..];
        static void Put(byte[] rec, System.Text.Json.JsonElement op) => Convert.FromHexString(op.GetProperty("hex").GetString()!).CopyTo(rec, op.GetProperty("o").GetInt32());
        foreach (var op in Ops.EnumerateArray())
        {
            var i = op.TryGetProperty("i", out var iv) ? iv.GetInt32() : 0;
            switch (op.GetProperty("s").GetString())
            {
                case "head": Put(head, op); break;
                case "char" when i < chars.Count: Put(chars[i], op); break;
                case "event" when i < events.Count: Put(events[i], op); break;
                case "event+" when events.Count < 300: events.Add(Convert.FromHexString(op.GetProperty("hex").GetString()!)); break;
                case "event-" when i < events.Count: events.RemoveAt(i); break;
                case "place" when i < places.Count: Put(places[i], op); break;
            }
        }
        var output = new MemoryStream();
        output.Write(head);
        chars.ForEach(c => output.Write(c));
        output.Write(BitConverter.GetBytes((ushort)events.Count));
        events.ForEach(e => output.Write(e));
        output.Write(BitConverter.GetBytes((ushort)places.Count));
        places.ForEach(r => output.Write(r));
        output.Write(cache);
        return output.ToArray();
    }
}

internal sealed class OriginalOutcome
{
    [JsonPropertyName("ax")] public int Ax { get; set; }

    [JsonPropertyName("dx")] public int Dx { get; set; }

    [JsonPropertyName("random")] public uint Random { get; set; }

    [JsonPropertyName("draws")] public List<uint> Draws { get; set; } = [];

    /// <summary>The srand calls: [argument, 1 when it is the clock's seed].</summary>
    [JsonPropertyName("seeds")] public List<List<long>> Seeds { get; set; } = [];

    [JsonPropertyName("sav_after")] public string? SavAfter { get; set; }

    /// <summary>The DS bytes the call changed within the ranges the job keeps (world_fuzz.py WATCH): DS offset to byte.</summary>
    [JsonPropertyName("changed")] public Dictionary<string, int> Changed { get; set; } = [];
}
