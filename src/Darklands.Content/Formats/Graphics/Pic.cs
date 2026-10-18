namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoded .PIC image resource with its chunk metadata preserved.
/// </summary>
public sealed record Pic(
    string AssetName,
    IndexedImage Image,
    Palette256 Palette,
    IReadOnlyList<PicChunk> Chunks,
    PicPaletteChunk? EmbeddedPalette,
    PicImageChunk ImageChunk)
{
    public bool HasEmbeddedPalette => EmbeddedPalette is not null;

    public int Mode => ImageChunk.Mode;
}

/// <summary>
/// Raw chunk boundary from the original PIC file.
/// </summary>
public abstract record PicChunk(
    ushort Tag,
    int PayloadOffset,
    int PayloadLength);

/// <summary>
/// Optional embedded VGA palette range.
/// </summary>
public sealed record PicPaletteChunk(
    int PayloadOffset,
    int PayloadLength,
    int StartIndex,
    int EndIndex,
    IReadOnlyList<Rgb24?> Entries)
    : PicChunk(PicChunkTags.Palette, PayloadOffset, PayloadLength);

/// <summary>
/// Image chunk after LZW and RLE expansion.
/// </summary>
public sealed record PicImageChunk(
    int PayloadOffset,
    int PayloadLength,
    int Width,
    int Height,
    int Mode,
    int CompressedLength,
    int LzwDecodedLength,
    int RleDecodedLength)
    : PicChunk(PicChunkTags.Image, PayloadOffset, PayloadLength);

/// <summary>
/// Chunk retained for evidence even when the tag is not interpreted yet.
/// </summary>
public sealed record PicUnknownChunk(
    ushort UnknownTag,
    int PayloadOffset,
    int PayloadLength)
    : PicChunk(UnknownTag, PayloadOffset, PayloadLength);

/// <summary>
/// Known PIC chunk tags from current asset evidence.
/// </summary>
public static class PicChunkTags
{
    public const ushort Palette = 0x304D;
    public const ushort Image = 0x3058;
}

