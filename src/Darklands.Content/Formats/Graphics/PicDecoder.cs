namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoder for Darklands .PIC chunked images.
/// </summary>
public sealed class PicDecoder
{
    /// <summary>
    /// Decodes a PIC stream into the solved chunked image model.
    /// </summary>
    /// <remarks>
    /// This is format provenance, not a segment routine; specific runtime consumers are anchored
    /// where the engine loads or displays the image.
    /// </remarks>
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
    /// <remarks>
    /// This method models the file format contract; it is not itself an original engine routine.
    /// </remarks>
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
                (image, imageChunk) = DecodeImageChunk(payloadOffset, length, chunk);
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

    private static (IndexedImage Image, PicImageChunk Chunk) DecodeImageChunk(int payloadOffset, int payloadLength, ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            throw new PicDecodeException("PIC image chunk is too short.");
        }

        var width = ReadU16(data, 0);
        var height = ReadU16(data, 2);
        var mode = data[4];
        var compressed = data.Slice(5);
        var lzwDecoded = LzwDecompress(compressed, mode);
        var pixels = RleDecode(lzwDecoded);
        var expectedPixels = checked(width * height);
        if (pixels.Count < expectedPixels)
        {
            throw new PicDecodeException($"PIC image decoded {pixels.Count} pixels, expected {expectedPixels}.");
        }

        return (
            new IndexedImage(width, height, pixels.Take(expectedPixels)),
            new PicImageChunk(
                payloadOffset,
                payloadLength,
                width,
                height,
                mode,
                compressed.Length,
                lzwDecoded.Count,
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

    private static List<int> LzwBytesToIndexes(ReadOnlySpan<byte> data, int mode)
    {
        // The packed code width grows until it reaches the mode-specific limit.
        var parsedIndexes = new List<int>();
        var offset = 0;
        var usableBits = 0;
        var usableBitCount = 0;
        var indicatorLength = 1;
        var indicatorFlag = 0x001;
        var nextThreshold = 0x0100;
        var decodedCounter = 0;

        while (offset < data.Length)
        {
            while (usableBitCount < 8 + indicatorLength && offset < data.Length)
            {
                usableBits |= data[offset++] << usableBitCount;
                usableBitCount += 8;
            }

            while (usableBitCount >= 8 + indicatorLength)
            {
                var index = usableBits & (((indicatorFlag << 8) & 0xFF00) | 0x00FF);
                usableBits >>= 8;
                usableBitCount -= 8;
                usableBits >>= indicatorLength;
                usableBitCount -= indicatorLength;
                decodedCounter++;
                if (decodedCounter == nextThreshold)
                {
                    decodedCounter = 0;
                    indicatorLength++;
                    indicatorFlag = (indicatorFlag << 1) | 1;
                    nextThreshold <<= 1;
                    if (8 + indicatorLength > mode)
                    {
                        decodedCounter = 0;
                        indicatorLength = 1;
                        indicatorFlag = 0x001;
                        nextThreshold = 0x0100;
                    }
                }

                parsedIndexes.Add(index);
            }
        }

        return parsedIndexes;
    }

    private static List<byte> LzwDecompress(ReadOnlySpan<byte> data, int mode)
    {
        // PIC image data is LZW-expanded before the final RLE pass.
        var indexes = LzwBytesToIndexes(data, mode);
        var output = new List<byte>();
        var i = 0;

        while (i < indexes.Count)
        {
            var dictionary = new LzwDictionary(mode);
            var w = new List<byte> { (byte)indexes[i] };
            output.Add((byte)indexes[i]);

            while (!dictionary.IsFull && i < indexes.Count - 1)
            {
                i++;
                var k = indexes[i];
                List<byte>? entry;
                var dictionaryEntry = dictionary.GetEntry(k);
                if (dictionaryEntry is not null)
                {
                    entry = dictionaryEntry;
                }
                else if (k == dictionary.CurrentPosition)
                {
                    entry = AppendByte(w, w[0]);
                }
                else
                {
                    return output;
                }

                output.AddRange(entry);
                dictionary.AddEntry(AppendByte(w, entry[0]));
                w = entry;
            }

            i++;
        }

        return output;
    }

    private static List<byte> RleDecode(IReadOnlyList<byte> codedData)
    {
        if (codedData.Count == 0)
        {
            return new List<byte>();
        }

        var plainData = new List<byte> { codedData[0] };
        var i = 1;
        while (i < codedData.Count)
        {
            // 0x90 is the repeat marker; 0x90 00 escapes a literal marker.
            if (codedData[i] != 0x90)
            {
                plainData.Add(codedData[i]);
            }
            else
            {
                if (i + 1 >= codedData.Count)
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

    private static List<byte> AppendByte(IReadOnlyList<byte> source, byte value)
    {
        var result = new List<byte>(source.Count + 1);
        result.AddRange(source);
        result.Add(value);
        return result;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private sealed class LzwDictionary
    {
        private readonly int _maxEntries;
        private readonly List<List<byte>> _table = new List<List<byte>>();

        public LzwDictionary(int maxBits)
        {
            _maxEntries = 1 << maxBits;
            for (var i = 0; i < 256; i++)
            {
                _table.Add(new List<byte> { (byte)i });
            }

            _table.Add(new List<byte>());
            CurrentPosition = 0x0101;
        }

        public int CurrentPosition { get; private set; }

        public bool IsFull => CurrentPosition >= _maxEntries;

        public List<byte>? GetEntry(int position)
        {
            return position < CurrentPosition ? new List<byte>(_table[position]) : null;
        }

        public void AddEntry(List<byte> entry)
        {
            if (CurrentPosition < _maxEntries)
            {
                _table.Add(entry);
                CurrentPosition++;
            }
        }
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

