using System.Text;

namespace Darklands.Content.Formats.Text;

/// <summary>
/// Decoder for the original MSGFILES catalog container.
/// </summary>
public sealed class MsgFilesArchiveDecoder
{
    public const int HeaderSize = 2;
    public const int RecordSize = 0x18;
    public const int NameSize = 0x0C;

    public MsgFilesArchive Decode(Stream stream, string assetName = "MSGFILES")
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(memory.ToArray(), assetName);
    }

    public MsgFilesArchive Decode(byte[] data, string assetName = "MSGFILES")
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("MSGFILES asset name is required.", nameof(assetName));
        }

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("MSGFILES is too short to contain its entry count.");
        }

        var count = ReadUInt16(data, 0);
        var catalogEnd = checked(HeaderSize + count * RecordSize);
        if (catalogEnd > data.Length)
        {
            throw new InvalidDataException(
                $"MSGFILES declares {count} records but its catalog extends past the file.");
        }

        var entries = new MsgFilesEntry[count];
        for (var index = 0; index < count; index++)
        {
            var recordOffset = HeaderSize + index * RecordSize;
            var rawName = data.AsSpan(recordOffset, NameSize).ToArray();
            var name = ReadName(rawName, index);
            var unknown = ReadUInt32(data, recordOffset + 0x0C);
            var size = ReadBoundedInt32(data, recordOffset + 0x10, index, "size");
            var offset = ReadBoundedInt32(data, recordOffset + 0x14, index, "offset");
            if ((long)offset + size > data.Length)
            {
                throw new InvalidDataException(
                    $"MSGFILES record {index} ({name}) payload 0x{offset:X}..0x{offset + size:X} extends past the file.");
            }

            entries[index] = new MsgFilesEntry(index, name, rawName, unknown, size, offset);
        }

        var duplicate = entries
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"MSGFILES contains duplicate entry name {duplicate.Key}.");
        }

        return new MsgFilesArchive(assetName, data.ToArray(), entries);
    }

    private static string ReadName(ReadOnlySpan<byte> bytes, int index)
    {
        var terminator = bytes.IndexOf((byte)0);
        var length = terminator >= 0 ? terminator : bytes.Length;
        if (length == 0)
        {
            throw new InvalidDataException($"MSGFILES record {index} has an empty name.");
        }

        return Encoding.ASCII.GetString(bytes[..length]);
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));

    private static int ReadBoundedInt32(ReadOnlySpan<byte> data, int offset, int index, string field)
    {
        var value = ReadUInt32(data, offset);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"MSGFILES record {index} {field} 0x{value:X8} exceeds the supported file range.");
        }

        return (int)value;
    }
}
