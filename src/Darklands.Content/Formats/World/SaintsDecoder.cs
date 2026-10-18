using System.Text;

namespace Darklands.Content.Formats.World;

/// <summary>
/// Decodes `DARKLAND.SNT`, the original saint description data file.
/// </summary>
public sealed class SaintsDecoder
{
    public const int HeaderSize = SaintsLayout.HeaderSize;
    public const int DescriptionRecordSize = SaintsLayout.DescriptionRecordSize;

    public Saints Decode(Stream stream, string name)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return DecodeBytes(memory.ToArray(), name);
    }

    public static Saints DecodeBytes(byte[] data, string name)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("DARKLAND.SNT is too small to contain its count header.");
        }

        var count = data[0];
        var requiredLength = HeaderSize + (count * DescriptionRecordSize);
        if (data.Length < requiredLength)
        {
            throw new InvalidDataException(
                $"DARKLAND.SNT declares {count} saint descriptions but only contains {data.Length} bytes.");
        }

        var descriptions = new SaintDescription[count];
        for (var index = 0; index < count; index++)
        {
            var offset = HeaderSize + (index * DescriptionRecordSize);
            descriptions[index] = new SaintDescription(
                index,
                DecodeNullTerminatedAscii(data, offset, DescriptionRecordSize),
                offset);
        }

        return new Saints(name, descriptions);
    }

    private static string DecodeNullTerminatedAscii(byte[] data, int offset, int size)
    {
        var length = 0;
        while (length < size && data[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(data, offset, length);
    }
}

