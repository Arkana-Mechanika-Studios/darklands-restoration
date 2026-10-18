using System.Buffers.Binary;
using System.Text;

namespace Darklands.Audio.Machine;

/// <summary>
/// A sound driver file (xSOUND.DC/.DB/.DLC/.DLB): a DOS MZ image the game loads as an overlay
/// (DOS 4B03) and enters through the far pointers of its slot table.
/// Driver-format facts (offsets in the image, i.e. after the MZ header; ASOUND.DC: header 200h):
/// 10h name and date ("DrkCardAdLib07-29-92"), 28h the image's own segment (a relocated 0),
/// 2Ah/2Ch the size the loader keeps resident (paragraphs and bytes), 2Eh the first slot
/// number (100), 30h the slot count (11), 32h the slots' offsets.
/// </summary>
internal sealed class DriverImage
{
    private DriverImage(byte[] body, IReadOnlyList<int> relocations)
    {
        Body = body;
        Relocations = relocations;
    }

    public byte[] Body { get; }

    public IReadOnlyList<int> Relocations { get; }

    /// <summary>The slot number of the first entry point (100 for the sound drivers).</summary>
    public int FirstSlot => BinaryPrimitives.ReadUInt16LittleEndian(Body.AsSpan(0x2E));

    public ushort EntryPoint(int slot)
    {
        var index = slot - FirstSlot;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(Body.AsSpan(0x30));
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"driver has no slot {slot}");
        }
        return BinaryPrimitives.ReadUInt16LittleEndian(Body.AsSpan(0x32 + 2 * index));
    }

    public static DriverImage Parse(byte[] file)
    {
        if (file.Length < 0x40 || file[0] != 'M' || file[1] != 'Z')
        {
            throw new InvalidDataException("not an MZ driver image");
        }
        var lastPage = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2));
        var pages = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
        var relocationCount = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6));
        var headerParagraphs = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(8));
        var relocationTable = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x18));
        var size = pages * 512 - (lastPage == 0 ? 0 : 512 - lastPage);
        size = Math.Min(size, file.Length);
        var header = headerParagraphs * 16;
        var body = file.AsSpan(header, size - header).ToArray();
        var relocations = new List<int>();
        for (var i = 0; i < relocationCount; i++)
        {
            var entry = file.AsSpan(relocationTable + 4 * i);
            var offset = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var segment = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            relocations.Add(segment * 16 + offset);
        }
        return new DriverImage(body, relocations);
    }

    /// <summary>The driver's name and date at offset 10h of its image.</summary>
    public static string Signature(byte[] file)
    {
        try
        {
            var body = Parse(file).Body;
            return Encoding.ASCII.GetString(body, 0x10, 20);
        }
        catch (InvalidDataException)
        {
            return "";
        }
    }

    /// <summary>Copies the image into memory at <paramref name="segment"/> and relocates it, as DOS 4B03 does.</summary>
    public void LoadInto(byte[] memory, ushort segment)
    {
        var start = segment * 16;
        Array.Clear(memory, start, Math.Min(memory.Length - start, Body.Length + 0x1000));
        Body.CopyTo(memory, start);
        foreach (var relocation in Relocations)
        {
            var at = start + relocation;
            var value = (ushort)(memory[at] | (memory[at + 1] << 8));
            value += segment; // C: 16-bit wrap, as the loader adds the relocation factor
            memory[at] = (byte)value;
            memory[at + 1] = (byte)(value >> 8);
        }
    }
}
