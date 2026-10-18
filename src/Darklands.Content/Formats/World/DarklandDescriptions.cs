namespace Darklands.Content.Formats.World;

/// <summary>
/// Decoded `DARKLAND.DSC` fixed-record city/place description table.
/// </summary>
/// <remarks>
/// The Docks travel service seeks `DS:907E * 0x50 + 1` and reads 0x50 bytes
/// into `DS:978B`. The shipped 483.07 file is `1 + 92 * 0x50` bytes, matching
/// the city/location selector range used by the MSG menu VM.
/// </remarks>
public sealed record DarklandDescriptions(
    string AssetName,
    IReadOnlyList<DarklandDescription> Records)
{
    public DarklandDescription? Find(int index)
    {
        return index >= 0 && index < Records.Count ? Records[index] : null;
    }
}

public sealed record DarklandDescription(
    int Index,
    IReadOnlyList<byte> RawBytes,
    string Text);

