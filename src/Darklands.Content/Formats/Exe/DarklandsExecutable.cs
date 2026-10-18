using System.Buffers.Binary;

namespace Darklands.Content.Formats.Exe;

/// <summary>
/// Tables the game keeps in its program (DARKLAND.EXE 483.07) rather than in data files: the
/// RTLink/Plus section directory is read to find the preloaded data section (record 136, loaded
/// at paragraph 0x20A5), which holds DGROUP (paragraph 0x321A) and the far data segments. Only
/// the player's own EXE is read; no game bytes are copied into this program.
/// </summary>
public sealed class DarklandsExecutable
{
    /// <summary>The DGROUP paragraph of version 483.07.</summary>
    public const int DataGroup = 0x321A;

    private const int DataSection = 136;

    private readonly byte[] _data;
    private readonly int _dataStart;
    private readonly int _dataBase;
    private readonly int _dataLength;

    private DarklandsExecutable(byte[] data, int dataStart, int dataBase, int dataLength)
    {
        _data = data;
        _dataStart = dataStart;
        _dataBase = dataBase;
        _dataLength = dataLength;
    }

    public static DarklandsExecutable Load(GameFiles files) => Decode(files.Read("DARKLAND.EXE"));

    public static DarklandsExecutable Decode(byte[] exe)
    {
        // The MZ header gives the root image; the overlay manager sits at CS and keeps the
        // section table's count pointer, table pointer and entry size at +0xB58..+0xB5C.
        var headerParagraphs = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(8));
        var cs = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(0x16));
        var manager = headerParagraphs * 16 + cs * 16;
        int Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(manager + at));
        var countPointer = Word(0xB58);
        var tablePointer = Word(0xB5A);
        var entrySize = Word(0xB5C);
        var count = Word(countPointer);
        if (DataSection >= count)
        {
            throw new InvalidDataException("DARKLAND.EXE has no preloaded data section (not version 483.07?).");
        }
        var entry = manager + tablePointer + DataSection * entrySize;
        int Field(int k) => BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(entry + 2 * k));
        var fileOffset = (Field(2) | (Field(3) & 0xFF) << 16) * 16;
        var imageOffset = (fileOffset + 4 * Field(5) + 15) & ~15;
        return new DarklandsExecutable(exe, imageOffset, Field(0) * 16, Field(8) * 16);
    }

    /// <summary>Bytes at a linear address inside the preloaded data section.</summary>
    public ReadOnlySpan<byte> Linear(int address, int length)
    {
        var at = address - _dataBase;
        if (at < 0 || at + length > _dataLength)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Address outside the preloaded data section.");
        }
        return _data.AsSpan(_dataStart + at, length);
    }

    /// <summary>Bytes at a DGROUP offset (<c>DS:offset</c>).</summary>
    public ReadOnlySpan<byte> DataSegment(int offset, int length) => Linear(DataGroup * 16 + offset, length);

    /// <summary>The zero-terminated string at DS:<paramref name="offset"/> (at most 200 characters).</summary>
    public string String(int offset)
    {
        var bytes = DataSegment(offset, 200);
        var end = bytes.IndexOf((byte)0);
        return System.Text.Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>Bytes at a far address (<c>segment:offset</c>) inside the preloaded data section.</summary>
    public ReadOnlySpan<byte> Far(int segment, int offset, int length) => Linear(segment * 16 + offset, length);
}
