namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoder for Darklands .PIC chunked images.
/// </summary>
public sealed class PicDecoder
{
    /// <summary>
    /// Decodes a PIC stream into the solved chunked image model.
    /// </summary>
    public Pic Decode(string assetName, Stream stream, Palette256? fallbackPalette = null)
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
        return Decode(assetName, memory.ToArray(), fallbackPalette);
    }

    /// <summary>
    /// Decodes PIC bytes into chunk records, optional embedded palette, and indexed image pixels.
    /// </summary>
    public Pic Decode(string assetName, ReadOnlySpan<byte> data, Palette256? fallbackPalette = null)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("Asset name is required.", nameof(assetName));
        }

        PicPaletteChunk? embeddedPalette = null;
        PicImageChunk? imageChunk = null;
        IndexedImage? image = null;
        var chunks = new List<PicChunk>();
        var offset = 0;

        while (offset < data.Length)
        {
            // PIC files are a sequence of little-endian tag/length chunks.
            if (offset + 4 > data.Length)
            {
                throw new PicDecodeException($"PIC chunk header extends past end of {assetName}.");
            }

            var tag = ReadU16(data, offset);
            var length = ReadU16(data, offset + 2);
            offset += 4;
            var payloadOffset = offset;
            if (offset + length > data.Length)
            {
                throw new PicDecodeException($"PIC chunk 0x{tag:X4} extends past end of {assetName}.");
            }

            var chunk = data.Slice(offset, length);
            if (tag == PicChunkTags.Palette)
            {
                embeddedPalette = DecodePalette(payloadOffset, length, chunk);
                chunks.Add(embeddedPalette);
            }
            else if (tag == PicChunkTags.Image)
            {
                (image, imageChunk) = DecodeImageChunk(assetName, payloadOffset, length, chunk);
                chunks.Add(imageChunk);
            }
            else
            {
                chunks.Add(new PicUnknownChunk(tag, payloadOffset, length));
            }

            offset += length;
        }

        if (image is null || imageChunk is null)
        {
            throw new PicDecodeException($"{assetName} did not contain a PIC image chunk.");
        }

        var palette = MergePalette(embeddedPalette, fallbackPalette ?? Palette256.DefaultVga);
        return new Pic(assetName, image, palette, chunks.ToArray(), embeddedPalette, imageChunk);
    }

    private static PicPaletteChunk DecodePalette(int payloadOffset, int payloadLength, ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new PicDecodeException("PIC palette chunk is too short.");
        }

        var start = (int)data[0];
        var end = (int)data[1];
        if (end < start)
        {
            throw new PicDecodeException("PIC palette chunk has an invalid index range.");
        }

        var expectedLength = 2 + ((end - start + 1) * 3);
        if (data.Length < expectedLength)
        {
            throw new PicDecodeException("PIC palette chunk is truncated.");
        }

        var colors = new Rgb24?[Palette256.ColorCount];
        var offset = 2;
        for (var i = start; i <= end; i++)
        {
            colors[i] = new Rgb24(
                ExpandVgaDacChannel(data[offset++]),
                ExpandVgaDacChannel(data[offset++]),
                ExpandVgaDacChannel(data[offset++]));
        }

        return new PicPaletteChunk(payloadOffset, payloadLength, start, end, colors);
    }

    private static byte ExpandVgaDacChannel(byte value)
    {
        if (value > 0x3F)
        {
            throw new PicDecodeException(
                $"PIC palette channel 0x{value:X2} exceeds the 6-bit VGA DAC range.");
        }

        return (byte)((value << 2) | (value >> 4));
    }

    private static (IndexedImage Image, PicImageChunk Chunk) DecodeImageChunk(string assetName, int payloadOffset, int payloadLength, ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            throw new PicDecodeException("PIC image chunk is too short.");
        }

        var width = ReadU16(data, 0);
        var height = ReadU16(data, 2);
        var mode = data[4];
        var compressed = data.Slice(5);
        var (lzwDecoded, lzwCount) = LzwDecompress(compressed, mode);
        var pixels = RleDecode(lzwDecoded, lzwCount);
        var expectedPixels = checked(width * height);
        if (pixels.Count < expectedPixels)
        {
            throw new PicDecodeException($"PIC image decoded {pixels.Count} pixels, expected {expectedPixels}.");
        }

        return (
            new IndexedImage(width, height, pixels.Count == expectedPixels ? pixels : pixels.GetRange(0, expectedPixels), assetName),
            new PicImageChunk(
                payloadOffset,
                payloadLength,
                width,
                height,
                mode,
                compressed.Length,
                lzwCount,
                pixels.Count));
    }

    private static Palette256 MergePalette(PicPaletteChunk? primary, Palette256 fallback)
    {
        if (primary is null)
        {
            return fallback;
        }

        var colors = new Rgb24[Palette256.ColorCount];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = primary.Entries[i] ?? fallback[i];
        }

        return new Palette256(colors);
    }

    private static List<int> UnpackLzwCodes(ReadOnlySpan<byte> data, int mode)
    {
        // The packed code width grows until it reaches the mode-specific limit.
        var parsedIndexes = new List<int>();
        var offset = 0;
        var bitBuffer = 0;
        var bitCount = 0;
        var extraBitCount = 1;
        var extraBitsMask = 0x001;
        var widenAfterCodes = 0x0100;
        var codesSinceWider = 0;

        while (offset < data.Length)
        {
            while (bitCount < 8 + extraBitCount && offset < data.Length)
            {
                bitBuffer |= data[offset++] << bitCount;
                bitCount += 8;
            }

            while (bitCount >= 8 + extraBitCount)
            {
                var index = bitBuffer & (((extraBitsMask << 8) & 0xFF00) | 0x00FF);
                bitBuffer >>= 8;
                bitCount -= 8;
                bitBuffer >>= extraBitCount;
                bitCount -= extraBitCount;
                codesSinceWider++;
                if (codesSinceWider == widenAfterCodes)
                {
                    codesSinceWider = 0;
                    extraBitCount++;
                    extraBitsMask = (extraBitsMask << 1) | 1;
                    widenAfterCodes <<= 1;
                    if (8 + extraBitCount > mode)
                    {
                        codesSinceWider = 0;
                        extraBitCount = 1;
                        extraBitsMask = 0x001;
                        widenAfterCodes = 0x0100;
                    }
                }

                parsedIndexes.Add(index);
            }
        }

        return parsedIndexes;
    }

    private static (byte[] Bytes, int Count) LzwDecompress(ReadOnlySpan<byte> data, int mode)
    {
        // PIC image data is LZW-expanded before the final RLE pass. A dictionary of up to 2^mode strings, codes 0..255 the bytes,
        // 256 an empty string, new strings from 0x101; when it is full the next code starts a new dictionary as a plain byte.
        // Each string is kept as (prefix code, last byte, length, first byte) and written out backwards: the bytes are those of the
        // list-of-strings form this replaced (PicDecoderReferenceTests compares the two on every picture).
        var indexes = UnpackLzwCodes(data, mode);
        var size = Math.Max(1 << mode, 0x0101);
        var maxEntries = 1 << mode;
        var prefix = new int[size];
        var last = new byte[size];
        var length = new int[size];
        var first = new byte[size];
        for (var b = 0; b < 256; b++)
        {
            last[b] = (byte)b;
            first[b] = (byte)b;
            length[b] = 1;
        }
        var output = new byte[Math.Max(256, data.Length * 4)];
        var count = 0;

        void Write(int code)
        {
            var n = length[code];
            if (count + n > output.Length)
            {
                Array.Resize(ref output, Math.Max(output.Length * 2, count + n));
            }
            for (var p = count + n - 1; p >= count; p--)
            {
                output[p] = last[code];
                code = prefix[code];
            }
            count += n;
        }

        var i = 0;
        while (i < indexes.Count)
        {
            var position = 0x0101;
            int w = (byte)indexes[i];
            Write(w);

            while (position < maxEntries && i < indexes.Count - 1)
            {
                i++;
                var k = indexes[i];
                if (k < position)
                {
                    if (length[k] == 0)
                    {
                        // code 256, the empty string: the list form failed reading its first byte
                        throw new ArgumentOutOfRangeException(nameof(data), "PIC LZW code 256 (the empty string) used as a string.");
                    }
                    Write(k);
                    (prefix[position], last[position], length[position], first[position]) = (w, first[k], length[w] + 1, first[w]);
                }
                else if (k == position)
                {
                    // the string not yet in the dictionary: the previous one and its own first byte
                    (prefix[position], last[position], length[position], first[position]) = (w, first[w], length[w] + 1, first[w]);
                    Write(k);
                }
                else
                {
                    return (output, count);
                }
                position++;
                w = k;
            }

            i++;
        }

        return (output, count);
    }

    private static List<byte> RleDecode(byte[] codedData, int codedCount)
    {
        if (codedCount == 0)
        {
            return new List<byte>();
        }

        var plainData = new List<byte>(codedCount * 2) { codedData[0] };
        var i = 1;
        while (i < codedCount)
        {
            // 0x90 is the repeat marker; 0x90 00 escapes a literal marker.
            if (codedData[i] != 0x90)
            {
                plainData.Add(codedData[i]);
            }
            else
            {
                if (i + 1 >= codedCount)
                {
                    throw new PicDecodeException("PIC RLE stream ended after repeat marker.");
                }

                if (codedData[i + 1] == 0)
                {
                    plainData.Add(codedData[i]);
                    i++;
                }
                else
                {
                    var repeatCount = codedData[i + 1];
                    i++;
                    var value = plainData[plainData.Count - 1];
                    for (var repeat = 0; repeat < repeatCount - 1; repeat++)
                    {
                        plainData.Add(value);
                    }
                }
            }

            i++;
        }

        return plainData;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }
}

/// <summary>
/// Raised when a PIC file contradicts the decoded format contract.
/// </summary>
public sealed class PicDecodeException : Exception
{
    public PicDecodeException(string message)
        : base(message)
    {
    }
}

