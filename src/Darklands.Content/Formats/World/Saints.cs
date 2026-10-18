namespace Darklands.Content.Formats.World;

/// <summary>
/// Decoded `DARKLAND.SNT` saint description table.
/// </summary>
/// <remarks>
/// The stock file contains one count byte followed by fixed 0x168-byte
/// null-padded description records. The record order matches the saint-name
/// order in `DARKLAND.LST`; the focused character-sheet Saints probe confirms
/// this with `St.Bathildis`, where index 14 seeks to
/// `1 + 14 * 0x168 = 0x13B1`.
///
/// The file `DARKLAND.SNT` has exactly
/// `1 + 136 * 0x168` bytes. Sampled records contain one null-terminated text
/// followed only by zero padding, so the file does not carry saint names, saint
/// days, success chances, or prayer effects. Those values are owned by
/// `DARKLAND.LST` and the character-sheet saint dispatch code.
/// </remarks>
public sealed record Saints(
    string AssetName,
    IReadOnlyList<SaintDescription> Descriptions);

public static class SaintsLayout
{
    public const int CountOffset = 0x0000;
    public const int StockSaintCount = 0x0088;
    public const int HeaderSize = 0x0001;
    public const int DescriptionRecordSize = 0x0168;
    public const int StockFileSize = HeaderSize + (StockSaintCount * DescriptionRecordSize);
}

public sealed record SaintDescription(
    int Index,
    string Text,
    int FileOffset);

