namespace Darklands.Content.Formats.World;

/// <summary>
/// Decoded `DARKLAND.LOC`, the startup-loaded location definition table.
/// </summary>
/// <remarks>
/// Original code at `1C85:4315..435B` materializes `LOCS.TMP` by walking
/// resident pointers to these `0x3A`-byte records. Field names remain narrow
/// until individual consumers are reconstructed; callers can still access
/// bytes and little-endian words by original record offset.
/// </remarks>
public sealed record DarklandLocations(
    string AssetName,
    IReadOnlyList<DarklandLocation> Locations,
    byte[] RawBytes)
{
    public DarklandLocation? Find(int index)
    {
        return index >= 0 && index < Locations.Count ? Locations[index] : null;
    }
}

public sealed record DarklandLocation(
    int Index,
    byte[] RawBytes)
{
    public int ReadByte(int offset)
    {
        if (offset < 0 || offset >= RawBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return RawBytes[offset];
    }

    public int ReadUInt16(int offset)
    {
        if (offset < 0 || offset + 1 >= RawBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return RawBytes[offset] | (RawBytes[offset + 1] << 8);
    }
}

