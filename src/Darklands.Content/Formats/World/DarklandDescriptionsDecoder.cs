using System.Text;

namespace Darklands.Content.Formats.World;

/// <summary>
/// Decodes `DARKLAND.DSC`, the original 0x50-byte city/place description table.
/// </summary>
public sealed class DarklandDescriptionsDecoder
{
    public const int RecordsOffset = 0x01;
    public const int DescriptionRecordSize = 0x50;

    public DarklandDescriptions Decode(Stream stream, string name)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(memory.ToArray(), name);
    }

    public DarklandDescriptions Decode(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length < RecordsOffset)
        {
            throw new InvalidDataException("DARKLAND.DSC is too small to contain its header byte.");
        }

        var payloadLength = data.Length - RecordsOffset;
        if (payloadLength % DescriptionRecordSize != 0)
        {
            throw new InvalidDataException(
                $"DARKLAND.DSC payload length {payloadLength} is not a multiple of {DescriptionRecordSize}.");
        }

        // The selected-row code seeks destinationIndex * 0x50 + 1 and reads
        // fixed-size records. The shipped 483.07 file's leading byte is not a
        // trustworthy record count, so the byte-backed seek/read shape wins.
        var count = payloadLength / DescriptionRecordSize;
        var records = new DarklandDescription[count];
        for (var index = 0; index < count; index++)
        {
            var offset = RecordsOffset + (index * DescriptionRecordSize);
            records[index] = new DarklandDescription(
                index,
                data.Slice(offset, DescriptionRecordSize).ToArray(),
                ReadString(data, offset));
        }

        return new DarklandDescriptions(name, records);
    }

    private static string ReadString(ReadOnlySpan<byte> data, int offset)
    {
        var length = 0;
        while (length < DescriptionRecordSize && data[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(data.Slice(offset, length));
    }
}

