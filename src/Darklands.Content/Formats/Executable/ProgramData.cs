using System.Buffers.Binary;
using System.Text;

namespace Darklands.Content.Formats.Executable;

/// <summary>
/// The initialised data of DARKLAND.EXE (483.07): the RTLink/Plus data section that is loaded at
/// start-up (link frame 20A5), read at link-time addresses (segment:offset).
/// </summary>
/// <remarks>
/// The RTLink section table sits in the overlay manager (initial CS of the MZ header) at
/// offsets 0xB58 (count pointer), 0xB5A (table pointer) and 0xB5C (entry size); each entry gives
/// the section's load frame, file position and length. Only reading is done here; no code runs.
/// </remarks>
public sealed class ProgramData
{
    private readonly byte[] _image;
    private readonly int _loadSegment;

    private ProgramData(byte[] image, int loadSegment)
    {
        _image = image;
        _loadSegment = loadSegment;
    }

    /// <summary>Finds the data section that holds link frame <paramref name="segment"/>.</summary>
    public static ProgramData Load(byte[] exe, int segment = 0x290E)
    {
        if (exe.Length < 0x40 || exe[0] != 'M' || exe[1] != 'Z')
        {
            throw new InvalidDataException("DARKLAND.EXE is not an MZ program.");
        }
        int Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(offset));
        var headerSize = Word(8) * 16;
        var manager = headerSize + Word(0x16) * 16;
        var count = Word(manager + Word(manager + 0xB58));
        var table = manager + Word(manager + 0xB5A);
        var entrySize = Word(manager + 0xB5C);
        for (var i = 0; i < count; i++)
        {
            var e = table + i * entrySize;
            var load = Word(e);
            var fileOffset = (Word(e + 4) | (Word(e + 6) & 0xFF) << 16) * 16;
            var start = (fileOffset + 4 * Word(e + 10) + 15) & ~15;
            // The last section is declared a few bytes longer than the file (its padding to a paragraph).
            var length = Math.Min(Word(e + 16) * 16, exe.Length - start);
            var parent = Word(e + 12);
            if (parent == 0xFFFF && load <= segment && segment < load + Word(e + 8) && length > 0)
            {
                return new ProgramData(exe.AsSpan(start, length).ToArray(), load);
            }
        }
        throw new InvalidDataException("DARKLAND.EXE has no data section for frame " + segment.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    private int Linear(int segment, int offset)
    {
        var at = (segment - _loadSegment) * 16 + offset;
        return at >= 0 && at < _image.Length ? at : throw new InvalidDataException($"{segment:X4}:{offset:X4} is outside the data section.");
    }

    public ReadOnlySpan<byte> Bytes(int segment, int offset, int length) => _image.AsSpan(Linear(segment, offset), length);

    public sbyte SByte(int segment, int offset) => (sbyte)_image[Linear(segment, offset)];

    public short Int16(int segment, int offset) => BinaryPrimitives.ReadInt16LittleEndian(_image.AsSpan(Linear(segment, offset)));

    /// <summary>The zero-terminated string at an address, its bytes kept as Latin-1 characters (colour codes included).</summary>
    public string String(int segment, int offset)
    {
        var start = Linear(segment, offset);
        var end = Array.IndexOf(_image, (byte)0, start);
        return Encoding.Latin1.GetString(_image, start, (end < 0 ? _image.Length : end) - start);
    }

    /// <summary>The strings of a table of <paramref name="count"/> far pointers.</summary>
    public IReadOnlyList<string> StringTable(int segment, int offset, int count)
    {
        var list = new string[count];
        for (var i = 0; i < count; i++)
        {
            var pointerOffset = (ushort)Int16(segment, offset + 4 * i);
            var pointerSegment = (ushort)Int16(segment, offset + 4 * i + 2);
            list[i] = String(pointerSegment, pointerOffset);
        }
        return list;
    }
}
