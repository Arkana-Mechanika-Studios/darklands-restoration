using System.Buffers.Binary;
using System.Text;

namespace Darklands.Content.Formats.Graphics;

/// <summary>A CAT directory and its stored resource bytes.</summary>
/// <remarks>
/// Original 05D9:002A catalog lookup uses a count word and 24-byte entries:
/// name[12], opaque DWORD, stored length DWORD, absolute offset DWORD.
/// This resource reader does not select an actor's catalog or sprite.
/// Stored lengths describe compressed bytes, not decoded image capacities.
/// </remarks>
public sealed class CatArchive
{
    private readonly byte[] data;
    private readonly Dictionary<string, CatEntry> byName;

    private CatArchive(byte[] data, CatEntry[] entries)
    {
        this.data = data;
        Entries = Array.AsReadOnly(entries);
        byName = entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CatEntry> Entries { get; }

    public static CatArchive Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) throw new InvalidDataException("CAT has no entry count.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        int directoryEnd = 2 + count * 24;
        if (directoryEnd > bytes.Length) throw new InvalidDataException("CAT directory is truncated.");
        var entries = new CatEntry[count];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < count; index++)
        {
            var row = bytes.Slice(2 + index * 24, 24);
            var nameBytes = row.Slice(0, 12);
            int terminator = nameBytes.IndexOf((byte)0);
            string name = Encoding.ASCII.GetString(terminator < 0 ? nameBytes : nameBytes.Slice(0, terminator));
            if (name.Length == 0 || !names.Add(name))
                throw new InvalidDataException("CAT contains an empty or duplicate resource name.");
            uint opaque = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(12));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(16));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(20));
            if (offset < directoryEnd || (ulong)offset + length > (ulong)bytes.Length)
                throw new InvalidDataException($"CAT resource {name} has an invalid stored range.");
            entries[index] = new CatEntry(index, name, opaque, (int)offset, (int)length);
        }
        return new CatArchive(bytes.ToArray(), entries);
    }

    public byte[] ReadStored(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!byName.TryGetValue(name, out var entry))
            throw new KeyNotFoundException($"CAT does not contain {name}.");
        return data.AsSpan(entry.Offset, entry.StoredLength).ToArray();
    }
}

/// <summary>One resource directory entry; the DWORD at byte 12 is uninterpreted.</summary>
public sealed record CatEntry(int Index, string Name, uint Opaque, int Offset, int StoredLength);
