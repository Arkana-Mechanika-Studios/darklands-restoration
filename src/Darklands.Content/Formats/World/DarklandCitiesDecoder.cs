using System.Text;

namespace Darklands.Content.Formats.World;

/// <summary>
/// Decodes `DARKLAND.CTY`, the original fixed-record city table.
/// </summary>
public sealed class DarklandCitiesDecoder
{
    public const int CountOffset = 0x00;
    public const int RecordsOffset = 0x01;
    public const int CityRecordSize = 0x26E;
    public const int FixedStringSize = 0x20;

    public DarklandCities Decode(Stream stream, string name)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(memory.ToArray(), name);
    }

    public DarklandCities Decode(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length < RecordsOffset)
        {
            throw new InvalidDataException("DARKLAND.CTY is too small to contain its city count.");
        }

        var count = data[CountOffset];
        var required = RecordsOffset + (count * CityRecordSize);
        if (data.Length < required)
        {
            throw new InvalidDataException($"DARKLAND.CTY declares {count} city records but only contains {data.Length} bytes.");
        }

        var cities = new DarklandCity[count];
        for (var index = 0; index < count; index++)
        {
            cities[index] = DecodeCity(data, RecordsOffset + (index * CityRecordSize), index);
        }

        return new DarklandCities(name, cities);
    }

    private static DarklandCity DecodeCity(ReadOnlySpan<byte> data, int offset, int index)
    {
        return new DarklandCity(
            index,
            data.Slice(offset, CityRecordSize).ToArray(),
            ReadString(data, offset + 0x000),
            ReadString(data, offset + 0x020),
            DecodeCityData(data, offset + 0x040),
            ReadString(data, offset + 0x06E),
            ReadString(data, offset + 0x08E),
            ReadString(data, offset + 0x0CE),
            ReadString(data, offset + 0x0EE),
            ReadString(data, offset + 0x10E),
            ReadString(data, offset + 0x12E),
            ReadString(data, offset + 0x14E),
            ReadString(data, offset + 0x16E),
            ReadString(data, offset + 0x18E),
            ReadString(data, offset + 0x1AE),
            ReadString(data, offset + 0x1CE),
            ReadString(data, offset + 0x1EE),
            ReadString(data, offset + 0x20E),
            ReadString(data, offset + 0x22E),
            ReadString(data, offset + 0x24E));
    }

    /// <summary>Decodes one city record read by the original selected-city loader.</summary>
    public static DarklandCity DecodeRecord(ReadOnlySpan<byte> record, int index)
    {
        if (record.Length != CityRecordSize) throw new InvalidDataException("A city record must contain622 bytes.");
        return DecodeCity(record, 0, index);
    }

    private static DarklandCityData DecodeCityData(ReadOnlySpan<byte> data, int offset)
    {
        return new DarklandCityData(
            ReadUInt16(data, offset + 0x00),
            ReadUInt16(data, offset + 0x02),
            ReadUInt16(data, offset + 0x04),
            ReadUInt16(data, offset + 0x06),
            ReadUInt16(data, offset + 0x08),
            new[]
            {
                ReadUInt16(data, offset + 0x0A),
                ReadUInt16(data, offset + 0x0C),
                ReadUInt16(data, offset + 0x0E),
                ReadUInt16(data, offset + 0x10)
            },
            ReadUInt16(data, offset + 0x18),
            ReadUInt16BigEndian(data, offset + 0x1E));
    }

    private static string ReadString(ReadOnlySpan<byte> data, int offset)
    {
        var length = 0;
        while (length < FixedStringSize && data[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(data.Slice(offset, length));
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8);
    }

    private static int ReadUInt16BigEndian(ReadOnlySpan<byte> data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }
}
