namespace Darklands.Content.Formats.World;

/// <summary>
/// Decodes `DARKLAND.LOC`, whose current confirmed shape is
/// `uint16 count` followed by `count` fixed `0x3A`-byte records.
/// </summary>
public sealed class DarklandLocationsDecoder
{
    public const int CountOffset = 0x0000;
    public const int RecordsOffset = 0x0002;
    public const int LocationRecordSize = 0x003A;
    public const int ExpectedRecordCount = 0x019E;
    public const int ExpectedByteCount = RecordsOffset + (ExpectedRecordCount * LocationRecordSize);

    public DarklandLocations Decode(Stream stream, string name)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(memory.ToArray(), name);
    }

    public DarklandLocations Decode(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length < RecordsOffset)
        {
            throw new InvalidDataException("DARKLAND.LOC is too small to contain its location count.");
        }

        var count = data[CountOffset] | (data[CountOffset + 1] << 8);
        var required = RecordsOffset + (count * LocationRecordSize);
        if (data.Length != required)
        {
            throw new InvalidDataException($"DARKLAND.LOC declares {count} location records but contains {data.Length} bytes.");
        }

        var rawBytes = data.ToArray();
        var locations = new DarklandLocation[count];
        for (var index = 0; index < count; index++)
        {
            var offset = RecordsOffset + (index * LocationRecordSize);
            locations[index] = new DarklandLocation(index, rawBytes.AsSpan(offset, LocationRecordSize).ToArray());
        }

        return new DarklandLocations(name, locations, rawBytes);
    }
}

