using System.Text.Json;

namespace Darklands.Oracle.Tests;

/// <summary>An original-byte corpus: results of running original functions from DARKLAND.EXE.</summary>
internal sealed class Corpus
{
    private readonly JsonDocument _document;

    private Corpus(JsonDocument document)
    {
        _document = document;
        var root = document.RootElement;
        if (root.GetProperty("authority").GetString() != "test_only_original_byte_execution")
        {
            throw new InvalidDataException("not an original-byte corpus");
        }
        var ds = Hex(root.GetProperty("dataSegment").GetString()!);
        ScratchA = ds * 16 + Hex(root.GetProperty("scratchA").GetString()!);
        HoldRand = ds * 16 + Hex(root.GetProperty("holdrand").GetString()!);
    }

    /// <summary>Linear address of the first scratch structure.</summary>
    public int ScratchA { get; }

    /// <summary>Linear address of the C run-time random state.</summary>
    public int HoldRand { get; }

    public static Corpus Load(string file = "core.json") =>
        new(JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Corpus", file))));

    public JsonElement Root => _document.RootElement;

    public IReadOnlyList<OracleCase> Cases(string function) =>
        _document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("function").GetString() == function)
            .Select(c => new OracleCase(c))
            .ToList();

    internal static int Hex(string value) => Convert.ToInt32(value, 16);
}

/// <summary>One executed case: inputs, AX, and the bytes the original changed.</summary>
internal sealed class OracleCase(JsonElement element)
{
    private readonly Dictionary<int, byte> _changed = element.GetProperty("changed").EnumerateObject()
        .ToDictionary(p => Corpus.Hex(p.Name), p => (byte)Corpus.Hex(p.Value.GetString()!));

    public JsonElement Inputs { get; } = element.GetProperty("inputs");

    /// <summary>The whole case (for corpus-specific fields such as recorded calls).</summary>
    public JsonElement Element { get; } = element;

    public ushort Ax { get; } = (ushort)Corpus.Hex(element.GetProperty("ax").GetString()!);

    /// <summary>
    /// The 16-bit value the original left at <paramref name="linear"/>: its changed bytes over
    /// the value that was seeded there before the call (<paramref name="initial"/>).
    /// </summary>
    public short FinalInt16(int linear, short initial)
    {
        var lo = _changed.TryGetValue(linear, out var l) ? l : (byte)initial;
        var hi = _changed.TryGetValue(linear + 1, out var h) ? h : (byte)(initial >> 8);
        return (short)(lo | hi << 8);
    }

    /// <summary>The byte the original left at <paramref name="linear"/> over its seeded value.</summary>
    public byte FinalByte(int linear, byte initial) => _changed.TryGetValue(linear, out var b) ? b : initial;

    public uint FinalUInt32(int linear, uint initial)
    {
        uint value = 0;
        for (var k = 0; k < 4; k++)
        {
            var b = _changed.TryGetValue(linear + k, out var x) ? x : (byte)(initial >> (8 * k));
            value |= (uint)b << (8 * k);
        }
        return value;
    }
}
