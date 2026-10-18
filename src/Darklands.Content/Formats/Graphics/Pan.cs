namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoded .PAN animation resource.
/// </summary>
public sealed record Pan(
    string AssetName,
    PanHeader Header,
    Palette256 Palette,
    IReadOnlyList<PanFrame> Frames)
{
    public int FrameCount => Header.FrameCount;
}

/// <summary>
/// Logical stream header and derived offsets after 0DFC:0070-style expansion.
/// </summary>
public sealed record PanHeader(
    int Magic,
    int FrameCount,
    int RecordTableOffset,
    int PaletteOffset,
    int FirstRecordOffset,
    int LogicalStreamLength);

/// <summary>
/// One frame-delta record boundary inside the decoded logical stream.
/// </summary>
public sealed record PanFrameRecord(
    int Index,
    int LogicalRecordStartOffset,
    int LogicalRecordEndOffset)
{
    public int LogicalRecordLength => LogicalRecordEndOffset - LogicalRecordStartOffset;
}

/// <summary>
/// Replayed 320x200 framebuffer state after applying one PAN frame record.
/// </summary>
public sealed record PanFrame(
    int Index,
    IndexedImage Image,
    Palette256 Palette,
    TimeSpan Duration,
    PanFrameRecord Record)
{
    public int LogicalRecordStartOffset => Record.LogicalRecordStartOffset;

    public int LogicalRecordEndOffset => Record.LogicalRecordEndOffset;

    public int LogicalRecordLength => Record.LogicalRecordLength;
}

