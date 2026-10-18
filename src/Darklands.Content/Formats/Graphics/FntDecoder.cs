namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoder for Darklands FONTS.FNT/FONTS.UTL bitmap font sets.
/// </summary>
public sealed class FntDecoder
{
    private const int HeaderSize = 8;

    /// <summary>
    /// Decodes a font-set stream into the solved FNT format model.
    /// </summary>
    public Fnt Decode(string assetName, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("Asset name is required.", nameof(assetName));
        }

        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Decode(assetName, memory.ToArray());
    }

    /// <summary>
    /// Decodes FNT bytes into font headers, width tables, and packed bitmap rows.
    /// </summary>
    /// <remarks>
    /// This method models the file format contract. The runtime renderer loads
    /// these same offsets into its private source table before `4A65:1374`
    /// consumes them.
    /// </remarks>
    public Fnt Decode(string assetName, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("Asset name is required.", nameof(assetName));
        }

        if (bytes.Length < 2)
        {
            throw new FntDecodeException($"{assetName} is too short to contain a font count.");
        }

        var fontCount = ReadUInt16(bytes, 0);
        var tableLength = checked(2 + (fontCount * 2));
        if (fontCount <= 0)
        {
            throw new FntDecodeException($"{assetName} does not contain any fonts.");
        }

        if (tableLength > bytes.Length)
        {
            throw new FntDecodeException($"{assetName} font offset table extends past the file end.");
        }

        var fonts = new List<FntFont>(fontCount);
        for (var index = 0; index < fontCount; index++)
        {
            var offset = ReadUInt16(bytes, 2 + (index * 2));
            fonts.Add(DecodeFont(assetName, index, offset, bytes));
        }

        return new Fnt(assetName, fonts.ToArray());
    }

    private static FntFont DecodeFont(string assetName, int index, int pixelDataOffset, ReadOnlySpan<byte> bytes)
    {
        var headerOffset = pixelDataOffset - HeaderSize;
        if (headerOffset < 0 || pixelDataOffset > bytes.Length)
        {
            throw new FntDecodeException($"{assetName} font {index} has an invalid pixel-data offset 0x{pixelDataOffset:X4}.");
        }

        var start = bytes[headerOffset];
        var end = bytes[headerOffset + 1];
        if (end < start)
        {
            throw new FntDecodeException($"{assetName} font {index} has an invalid character range.");
        }

        var characterCount = end - start + 1;
        var characterDataWidth = bytes[headerOffset + 2];
        var characterHeight = bytes[headerOffset + 4];
        if (characterDataWidth == 0 || characterHeight == 0)
        {
            throw new FntDecodeException($"{assetName} font {index} has an empty bitmap geometry.");
        }

        var widthTableOffset = headerOffset - characterCount;
        if (widthTableOffset < 2)
        {
            throw new FntDecodeException($"{assetName} font {index} width table overlaps the file header.");
        }

        var pixelDataLength = checked(characterCount * characterDataWidth * characterHeight);
        if (pixelDataOffset + pixelDataLength > bytes.Length)
        {
            throw new FntDecodeException($"{assetName} font {index} pixel data extends past the file end.");
        }

        return new FntFont(
            index,
            widthTableOffset,
            headerOffset,
            pixelDataOffset,
            start,
            end,
            characterDataWidth,
            bytes[headerOffset + 3],
            characterHeight,
            bytes[headerOffset + 5],
            bytes[headerOffset + 6],
            bytes[headerOffset + 7],
            bytes.Slice(widthTableOffset, characterCount).ToArray(),
            bytes.Slice(pixelDataOffset, pixelDataLength).ToArray());
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }
}

/// <summary>
/// Raised when a font-set resource contradicts the decoded FNT contract.
/// </summary>
public sealed class FntDecodeException : Exception
{
    public FntDecodeException(string message)
        : base(message)
    {
    }
}

