using System.Text;

namespace Darklands.Content.Formats.World;

/// <summary>
/// Decodes `DARKLAND.LST`, the original global item/saint/formula name table.
/// </summary>
public sealed class DarklandListDecoder
{
    public const int HeaderSize = 0x03;
    public const int ItemRecordSize = 0x2E;
    public const int ItemNameSize = 20;
    public const int ItemShortNameSize = 10;
    public const int ItemTypeOffset = 0x1E;
    public const int ItemFlagsOffset = 0x20;
    public const int ItemFlagByteCount = 5;
    public const int ItemWeightOffset = 0x25;
    public const int ItemQualityOffset = 0x26;
    public const int ItemRarityOffset = 0x27;
    public const int ItemUnknown1Offset = 0x28;
    public const int ItemUnknown2Offset = 0x2A;
    public const int ItemValueOffset = 0x2C;

    public DarklandList Decode(Stream stream, string name)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return DecodeBytes(memory.ToArray(), name);
    }

    public static DarklandList DecodeBytes(byte[] data, string name)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("DARKLAND.LST is too small to contain its count header.");
        }

        var itemCount = data[0];
        var saintCount = data[1];
        var formulaCount = data[2];
        var offset = HeaderSize;
        var requiredItemBytes = itemCount * ItemRecordSize;
        if (data.Length < offset + requiredItemBytes)
        {
            throw new InvalidDataException(
                $"DARKLAND.LST declares {itemCount} item records but only contains {data.Length} bytes.");
        }

        var items = new DarklandItemDefinition[itemCount];
        for (var index = 0; index < itemCount; index++)
        {
            items[index] = DecodeItem(data, offset, index);
            offset += ItemRecordSize;
        }

        var saintFullNames = ReadNullTerminatedStrings(data, ref offset, saintCount, "saint full names");
        var saintShortNames = ReadNullTerminatedStrings(data, ref offset, saintCount, "saint short names");
        var formulaFullNames = ReadNullTerminatedStrings(data, ref offset, formulaCount, "formula full names");
        var formulaShortNames = ReadNullTerminatedStrings(data, ref offset, formulaCount, "formula short names");

        var saints = new DarklandSaintName[saintCount];
        for (var index = 0; index < saints.Length; index++)
        {
            saints[index] = new DarklandSaintName(index, saintFullNames[index], saintShortNames[index]);
        }

        var formulae = new DarklandFormulaName[formulaCount];
        for (var index = 0; index < formulae.Length; index++)
        {
            formulae[index] = new DarklandFormulaName(index, formulaFullNames[index], formulaShortNames[index]);
        }

        return new DarklandList(name, items, saints, formulae);
    }

    private static DarklandItemDefinition DecodeItem(byte[] data, int offset, int code)
    {
        return new DarklandItemDefinition(
            code,
            DecodeFixedString(data, offset, ItemNameSize),
            DecodeFixedString(data, offset + ItemNameSize, ItemShortNameSize),
            ReadUInt16(data, offset + ItemTypeOffset),
            data.Skip(offset + ItemFlagsOffset).Take(ItemFlagByteCount).ToArray(),
            data[offset + ItemWeightOffset],
            data[offset + ItemQualityOffset],
            data[offset + ItemRarityOffset],
            ReadUInt16(data, offset + ItemUnknown1Offset),
            ReadUInt16(data, offset + ItemUnknown2Offset),
            ReadUInt16(data, offset + ItemValueOffset));
    }

    private static IReadOnlyList<string> ReadNullTerminatedStrings(
        byte[] data,
        ref int offset,
        int count,
        string tableName)
    {
        var strings = new string[count];
        for (var index = 0; index < count; index++)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException($"DARKLAND.LST ended while reading {tableName}.");
            }

            var start = offset;
            while (offset < data.Length && data[offset] != 0)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                throw new InvalidDataException($"DARKLAND.LST has an unterminated string in {tableName}.");
            }

            strings[index] = Encoding.ASCII.GetString(data, start, offset - start);
            offset++;
        }

        return strings;
    }

    private static string DecodeFixedString(byte[] data, int offset, int size)
    {
        var length = 0;
        while (length < size && data[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(data, offset, length);
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8);
    }
}

