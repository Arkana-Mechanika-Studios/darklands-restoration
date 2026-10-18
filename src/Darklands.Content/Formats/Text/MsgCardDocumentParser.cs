namespace Darklands.Content.Formats.Text;

/// <summary>
/// Data-only parser for the card scanner surface inside MSGFILES payloads.
/// </summary>
// orig: the MSG scanner R043+0x0C9E (..0x0E3F) and its bounds helper R029+0x4F76
public static class MsgCardDocumentParser
{
    private const int CardLayoutLength = 5;
    private const ushort ScannerLowerBound = 0x0000;
    private const ushort ScannerTextYUpperBound = 0x00C8;
    private const ushort ScannerTextXUpperBound = 0x0140;
    private const byte Byte06 = 0x06;
    private const byte Byte0A = 0x0A;
    private const byte Byte10 = 0x10;
    private const byte Byte14 = 0x14;
    private const byte Byte15 = 0x15;
    private const byte Byte16 = 0x16;
    private const byte Byte1D = 0x1D;

    /// <summary>
    /// Parses one MSGFILES payload into raw card records and raw low text bytes.
    /// </summary>
    /// <remarks>
    /// It does not infer display text, navigation behavior, option destinations, or renderer behavior.
    /// </remarks>
    public static MsgCardDocument ParseEntryPayload(byte[] payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (payload.Length == 0)
        {
            throw new InvalidDataException("MSG payload is empty.");
        }

        var cards = new List<MsgCard>();
        var count = payload[0];
        var signedCount = (short)(sbyte)count;
        var cardsToScan = signedCount > 0 ? signedCount : 0;
        var position = 1;

        for (var cardIndex = 0; cardIndex < cardsToScan; cardIndex++)
        {
            if (position + CardLayoutLength > payload.Length)
            {
                throw new InvalidDataException($"MSG card {cardIndex} layout is truncated.");
            }

            var layoutOffset = position;
            var layout = new MsgCardLayout(
                payload[position],
                ReadUInt16LittleEndian(payload, position + 1),
                ReadUInt16LittleEndian(payload, position + 3),
                payload[position..(position + CardLayoutLength)]);
            position += CardLayoutLength;

            var textOffset = position;
            var end = Array.IndexOf(payload, (byte)0, position);
            if (end < 0)
            {
                throw new InvalidDataException($"MSG card {cardIndex} text is not null-terminated.");
            }

            cards.Add(ParseCard(
                payload[position..end],
                layout,
                new MsgCardScannerValues(
                    (byte)MsgScannerBounds.ClampSignedWord(layout.TextY, ScannerLowerBound, ScannerTextYUpperBound),
                    MsgScannerBounds.ClampSignedWord(layout.TextX, ScannerLowerBound, ScannerTextXUpperBound),
                    layout.TextMaxX,
                    unchecked((ushort)textOffset)),
                cardIndex,
                layoutOffset,
                textOffset,
                end));
            position = end + 1;
        }

        return new MsgCardDocument(
            count,
            new MsgDocumentScannerValues(signedCount, cardsToScan, position),
            payload.ToArray(),
            cards,
            payload[position..]);
    }

    private static MsgCard ParseCard(
        byte[] payload,
        MsgCardLayout layout,
        MsgCardScannerValues scannerValues,
        int cardIndex,
        int layoutOffset,
        int textOffset,
        int terminatorOffset)
    {
        var rawLowTextBytes = new List<MsgCardRawLowTextByte>();

        for (var offset = 0; offset < payload.Length; offset++)
        {
            var value = payload[offset];
            TrackRawLowTextByte(value, offset, textOffset + offset, rawLowTextBytes);
        }

        return new MsgCard(
            cardIndex,
            layout,
            scannerValues,
            layoutOffset,
            textOffset,
            terminatorOffset,
            payload.ToArray(),
            rawLowTextBytes);
    }

    private static ushort ReadUInt16LittleEndian(byte[] payload, int offset)
    {
        return (ushort)(payload[offset] | (payload[offset + 1] << 8));
    }

    private static void TrackRawLowTextByte(
        byte value,
        int cardRelativeOffset,
        int payloadOffset,
        List<MsgCardRawLowTextByte> rawLowTextBytes)
    {
        if (value >= 0x20)
        {
            return;
        }

        rawLowTextBytes.Add(new MsgCardRawLowTextByte(
            cardRelativeOffset,
            payloadOffset,
            value,
            value switch
            {
                Byte06 => MsgCardRawLowTextByteKind.Byte06,
                Byte0A => MsgCardRawLowTextByteKind.Byte0A,
                Byte10 => MsgCardRawLowTextByteKind.Byte10,
                Byte14 => MsgCardRawLowTextByteKind.Byte14,
                Byte15 => MsgCardRawLowTextByteKind.Byte15,
                Byte16 => MsgCardRawLowTextByteKind.Byte16,
                Byte1D => MsgCardRawLowTextByteKind.Byte1D,
                _ => MsgCardRawLowTextByteKind.UnknownBelow20
            }));
    }
}
