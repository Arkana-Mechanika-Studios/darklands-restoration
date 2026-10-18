namespace Darklands.Content.Formats.Text;

/// <summary>
/// Decoded catalog and retained payload bytes from the original <c>MSGFILES</c> archive.
/// </summary>
/// <remarks>
/// The catalog shape comes from the file-format KB and is independently
/// correlated with the original loader surface that reads selected payloads
/// into <c>DS:A895</c>. This type is data only; it does not choose a card,
/// publish rows, or route city navigation.
/// </remarks>
public sealed class MsgFilesArchive
{
    private readonly byte[] data;
    private readonly Dictionary<string, MsgFilesEntry> entriesByName;

    internal MsgFilesArchive(string assetName, byte[] data, IReadOnlyList<MsgFilesEntry> entries)
    {
        AssetName = assetName;
        this.data = data;
        Entries = entries;
        entriesByName = entries.ToDictionary(
            entry => Normalize(entry.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    public string AssetName { get; }

    public IReadOnlyList<MsgFilesEntry> Entries { get; }

    public int ByteLength => data.Length;

    public MsgFilesEntry? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("MSGFILES entry name is required.", nameof(name));
        }

        return entriesByName.TryGetValue(Normalize(name), out var entry) ? entry : null;
    }

    public byte[] ReadPayload(MsgFilesEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }
        ValidateOwnedEntry(entry);
        var payload = new byte[entry.Size];
        Array.Copy(data, entry.Offset, payload, 0, payload.Length);
        return payload;
    }

    public byte[] ReadPayload(string name)
    {
        var entry = Find(name)
            ?? throw new KeyNotFoundException($"MSGFILES does not contain {name}.");
        return ReadPayload(entry);
    }

    public MsgCardDocument ParseDocument(string name) =>
        MsgCardDocumentParser.ParseEntryPayload(ReadPayload(name));

    private void ValidateOwnedEntry(MsgFilesEntry entry)
    {
        if (entry.Index < 0
            || entry.Index >= Entries.Count
            || !ReferenceEquals(Entries[entry.Index], entry))
        {
            throw new ArgumentException("The MSGFILES entry belongs to a different archive.", nameof(entry));
        }
    }

    private static string Normalize(string name) =>
        name.Replace('/', '\\').Trim().ToUpperInvariant();
}

/// <summary>
/// One fixed 24-byte original MSGFILES catalog record.
/// </summary>
public sealed record MsgFilesEntry(
    int Index,
    string Name,
    byte[] RawNameBytes,
    uint Unknown0C,
    int Size,
    int Offset);
