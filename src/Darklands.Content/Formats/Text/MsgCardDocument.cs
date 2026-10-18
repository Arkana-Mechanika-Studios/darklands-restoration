namespace Darklands.Content.Formats.Text;

/// <summary>
/// Parsed card-style text document from a MSGFILES entry.
/// </summary>
public sealed record MsgCardDocument(
    byte DeclaredCardCount,
    MsgDocumentScannerValues ScannerValues,
    byte[] RawPayload,
    IReadOnlyList<MsgCard> Cards,
    byte[] TrailingBytes);

/// <summary>
/// Document-level values visible in the original MSG scanner.
/// </summary>
/// <remarks>
/// The scanner (R043+0x0C9E) reads the count byte, sign-extends it with `cbw`, and
/// scans cards only when the resulting word is positive.
/// </remarks>
public sealed record MsgDocumentScannerValues(
    short SignedCardCount,
    int ScannedCardCount,
    int ScannerConsumedLength);

/// <summary>
/// One text card: exact payload slice and raw low text byte observations only.
/// </summary>
public sealed record MsgCard(
    int Index,
    MsgCardLayout Layout,
    MsgCardScannerValues ScannerValues,
    int LayoutOffset,
    int TextOffset,
    int TerminatorOffset,
    byte[] RawTextBytes,
    IReadOnlyList<MsgCardRawLowTextByte> RawLowTextBytes);

/// <summary>
/// Five-byte MSG card layout preceding one card's text payload.
/// </summary>
/// <remarks>
/// The original scanner consumes these bytes as one byte, then one
/// little-endian word, then one little-endian word (orig: the scanner R043+0x0C9E). Their meaning
/// for the text renderer is not modelled here.
/// </remarks>
public sealed record MsgCardLayout(
    byte TextY,
    ushort TextX,
    ushort TextMaxX,
    byte[] RawBytes);

/// <summary>
/// Values written by the original MSG scanner after byte-local bounds helpers.
/// </summary>
/// <remarks>
/// These are the scanner's table values (R043+0x0C9E), not their meaning for the renderer.
/// The bounds helper is R029+0x4F76.
/// </remarks>
public sealed record MsgCardScannerValues(
    byte BoundedTextY,
    ushort BoundedTextX,
    ushort TextMaxX,
    ushort TextOffset);

/// <summary>
/// One raw byte below `0x20` observed in a MSG card text payload.
/// </summary>
/// <remarks>
/// This is a data-only byte observation. It is not an option row, renderer
/// action, navigation command, or original scanner output table.
/// </remarks>
public readonly record struct MsgCardRawLowTextByte(
    int CardRelativeOffset,
    int PayloadOffset,
    byte Value,
    MsgCardRawLowTextByteKind Kind);

public enum MsgCardRawLowTextByteKind
{
    UnknownBelow20,
    Byte06,
    Byte0A,
    Byte10,
    Byte14,
    Byte15,
    Byte16,
    Byte1D
}
