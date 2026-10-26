using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// The jobs file of tools/oracle/battle_calls.py (export): states captured inside whole fights of the original (its DS and the far
/// segment with the level image, paths and loot piles), and the calls made from them: one original function with word arguments after
/// some changes to the memory, and what the original did (AX/DX, the bytes it changed, the generator's state and every draw's state).
/// </summary>
internal sealed class CallJobs
{
    [JsonPropertyName("states")] public Dictionary<string, StateImage> States { get; set; } = [];

    [JsonPropertyName("jobs")] public List<CallJob> Jobs { get; set; } = [];

    [JsonPropertyName("fights")] public Dictionary<string, JsonElement> Fights { get; set; } = [];

    public static CallJobs? Load()
    {
        var path = Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<CallJobs>(gz) ?? throw new InvalidDataException(path);
    }
}

internal sealed class StateImage
{
    [JsonPropertyName("fight")] public string Fight { get; set; } = "";

    [JsonPropertyName("pass")] public int Pass { get; set; }

    [JsonPropertyName("ds")] public string DsText { get; set; } = "";

    [JsonPropertyName("far")] public string FarText { get; set; } = "";

    [JsonPropertyName("randomState")] public uint RandomState { get; set; }

    /// <summary>The stack pointer the state was captured at (the calls' frames lie below it). Absent in older jobs files.</summary>
    [JsonPropertyName("sp0")] public int Sp0 { get; set; }

    /// <summary>The segment DS:7D4A names: the twin doors (+0xC00) and the doors opened (+0xBB6). Absent in older jobs files.</summary>
    [JsonPropertyName("far2")] public string Far2Text { get; set; } = "";

    /// <summary>The packs' segment (2E38: member k at 0x3E + 0x180 k, enemy k at 0xBDE + 0x60 k), its first 0x1100 bytes. Absent in older jobs files.</summary>
    [JsonPropertyName("far3")] public string Far3Text { get; set; } = "";

    /// <summary>The loot file (the machine's cfile.dat) as gzip + base64, "absent" when the file does not exist; null in older jobs files (the loot is then not carried).</summary>
    [JsonPropertyName("loot")] public string? LootText { get; set; }

    private byte[]? _ds, _far, _far2, _far3;

    public byte[] Ds => _ds ??= Unpack(DsText);

    public byte[] Far => _far ??= Unpack(FarText);

    public byte[]? Far2 => Far2Text.Length == 0 ? null : _far2 ??= Unpack(Far2Text);

    public byte[]? Loot => LootText is null || LootText == "absent" ? null : Unpack(LootText);

    public byte[]? Far3 => Far3Text.Length == 0 ? null : _far3 ??= Unpack(Far3Text);

    private static byte[] Unpack(string text)
    {
        using var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress);
        var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}

internal sealed class CallJob
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("function")] public string Function { get; set; } = "";

    [JsonPropertyName("state")] public string State { get; set; } = "";

    [JsonPropertyName("args")] public int[] Args { get; set; } = [];

    [JsonPropertyName("ops")] public List<MemoryOp> Ops { get; set; } = [];

    /// <summary>Functions the original ran in this call that the rewrite's state cannot follow (the job is reported, not judged).</summary>
    [JsonPropertyName("gaps")] public List<string> Gaps { get; set; } = [];

    [JsonPropertyName("orig")] public OriginalOutcome Orig { get; set; } = new();
}

internal sealed class MemoryOp
{
    /// <summary>"ds" (a DGROUP offset) or "floor" (an offset of the far segment 20A5).</summary>
    [JsonPropertyName("w")] public string Where { get; set; } = "ds";

    [JsonPropertyName("o")] public int Offset { get; set; }

    [JsonPropertyName("hex")] public string Hex { get; set; } = "";
}

internal sealed class OriginalOutcome
{
    [JsonPropertyName("ax")] public int Ax { get; set; }

    [JsonPropertyName("dx")] public int Dx { get; set; }

    [JsonPropertyName("changed")] public Dictionary<string, int> Changed { get; set; } = [];

    [JsonPropertyName("far_changed")] public Dictionary<string, int> FarChanged { get; set; } = [];

    [JsonPropertyName("far2_changed")] public Dictionary<string, int> Far2Changed { get; set; } = [];

    [JsonPropertyName("far3_changed")] public Dictionary<string, int> Far3Changed { get; set; } = [];

    /// <summary>The loot file after the call as hex, "absent" when there is none; null in older jobs files.</summary>
    [JsonPropertyName("loot")] public string? Loot { get; set; }

    [JsonPropertyName("random")] public uint Random { get; set; }

    [JsonPropertyName("draws")] public List<uint> Draws { get; set; } = [];
}

/// <summary>The memory a job starts from (state + ops) and the memory the original left (plus its changes).</summary>
internal sealed class MemoryPair(byte[] ds, byte[] far, byte[]? far2 = null, byte[]? far3 = null, byte[]? loot = null, bool lootKnown = false)
{
    /// <summary>The loot file (null: absent); only compared where <see cref="LootKnown"/>.</summary>
    public byte[]? Loot { get; set; } = loot;

    public bool LootKnown { get; set; } = lootKnown;

    public byte[] Ds { get; } = ds;

    public byte[] Far { get; } = far;

    public byte[]? Far2 { get; } = far2;

    public byte[]? Far3 { get; } = far3;

    /// <summary>The stack pointer of the state the call starts from.</summary>
    public int Sp0 { get; set; }

    public MemoryPair Clone() => new((byte[])Ds.Clone(), (byte[])Far.Clone(), (byte[]?)Far2?.Clone(), (byte[]?)Far3?.Clone(), (byte[]?)Loot?.Clone(), LootKnown) { Sp0 = Sp0 };

    public static MemoryPair Input(StateImage state, CallJob job)
    {
        var m = new MemoryPair((byte[])state.Ds.Clone(), (byte[])state.Far.Clone(), (byte[]?)state.Far2?.Clone(), (byte[]?)state.Far3?.Clone(), state.Loot, state.LootText is not null) { Sp0 = state.Sp0 };
        foreach (var op in job.Ops)
        {
            if (op.Where == "loot")
            {
                m.Loot = Convert.FromHexString(op.Hex);
                continue;
            }
            Convert.FromHexString(op.Hex).CopyTo(op.Where == "ds" ? m.Ds : op.Where == "far2" ? m.Far2! : op.Where == "far3" ? m.Far3! : m.Far, op.Offset);
        }
        return m;
    }

    public MemoryPair Expected(OriginalOutcome o)
    {
        var m = Clone();
        foreach (var (k, v) in o.Changed)
        {
            m.Ds[int.Parse(k)] = (byte)v;
        }
        foreach (var (k, v) in o.FarChanged)
        {
            m.Far[int.Parse(k)] = (byte)v;
        }
        foreach (var (k, v) in o.Far2Changed)
        {
            m.Far2![int.Parse(k)] = (byte)v;
        }
        foreach (var (k, v) in o.Far3Changed)
        {
            m.Far3![int.Parse(k)] = (byte)v;
        }
        if (o.Loot is not null)
        {
            m.Loot = o.Loot == "absent" ? null : Convert.FromHexString(o.Loot);
        }
        return m;
    }
}
