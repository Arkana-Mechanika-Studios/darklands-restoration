namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoder for the solved Darklands .PAN animation format.
/// </summary>
public sealed class PanDecoder
{
    private const int MagicWord = 0x0A5A;
    private const int PaletteByteLength = Palette256.ColorCount * 3;
    private const int FramebufferLength = IndexedImage.OriginalVgaWidth * IndexedImage.OriginalVgaHeight;
    private static readonly byte[] FrameSignature = { 0x42, 0x00, 0x01, 0x00 };

    /// <summary>
    /// Decodes a PAN stream into the solved logical animation model.
    /// </summary>
    /// <remarks>
    /// This is format provenance, not a segment routine: 0DFC:0070-style expansion is represented
    /// inside the private logical-stream decoder, while playback behavior belongs to Engine 0B2E.
    /// </remarks>
    public Pan Decode(string assetName, Stream stream)
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
    /// Decodes PAN bytes into header, palette, frame records, and replayed indexed frames.
    /// </summary>
    /// <remarks>
    /// This method models the file format contract; it is not itself an original engine routine.
    /// </remarks>
    public Pan Decode(string assetName, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("Asset name is required.", nameof(assetName));
        }

        var logical = DecodeLogicalStream(bytes);
        if (logical.Length < 4)
        {
            throw new InvalidDataException("PAN logical stream is too short to contain a header.");
        }

        var magic = ReadUInt16(logical, 0);
        if (magic != MagicWord)
        {
            throw new InvalidDataException($"PAN logical stream magic mismatch. Expected 0x{MagicWord:X4}, got 0x{magic:X4}.");
        }

        var frameCount = ReadUInt16(logical, 2);
        var tableEnd = checked(4 + frameCount * 2);
        var paletteOffset = tableEnd;
        var firstRecordOffset = checked(paletteOffset + PaletteByteLength);
        if (firstRecordOffset > logical.Length)
        {
            throw new InvalidDataException("PAN logical stream ends before the embedded palette is complete.");
        }

        var header = new PanHeader(
            magic,
            frameCount,
            4,
            paletteOffset,
            firstRecordOffset,
            logical.Length);
        var records = ReadFrameRecords(logical, frameCount, firstRecordOffset);
        var palette = ReadPalette(logical.AsSpan(paletteOffset, PaletteByteLength));
        var frames = ReplayFrames(assetName, logical, records, palette);

        return new Pan(assetName, header, palette, frames);
    }

    private static byte[] DecodeLogicalStream(ReadOnlySpan<byte> bytes)
    {
        var decoder = new Pan0070Decoder(bytes);
        var output = new List<byte>(bytes.Length * 2);
        int? expectedLength = null;

        // The expanded logical length is discovered only after the header and table decode.
        for (var span = 0; span < 256; span++)
        {
            var before = output.Count;
            decoder.DecodeSpan(output);

            expectedLength ??= TryGetExpectedLogicalLength(output);
            if (expectedLength is { } length && output.Count >= length)
            {
                return output.Take(length).ToArray();
            }

            if (output.Count == before)
            {
                break;
            }
        }

        throw new InvalidDataException("PAN stream did not produce a complete logical stream.");
    }

    private static int? TryGetExpectedLogicalLength(List<byte> output)
    {
        if (output.Count < 4)
        {
            return null;
        }

        var magic = output[0] | (output[1] << 8);
        if (magic != MagicWord)
        {
            return null;
        }

        var frameCount = output[2] | (output[3] << 8);
        if (frameCount <= 0 || frameCount > 4096)
        {
            return null;
        }

        var tableEnd = checked(4 + frameCount * 2);
        if (output.Count < tableEnd)
        {
            return null;
        }

        var logicalEnd = 0;
        for (var index = 0; index < frameCount; index++)
        {
            var offset = 4 + index * 2;
            logicalEnd += output[offset] | (output[offset + 1] << 8);
        }

        return logicalEnd;
    }

    private static PanFrameRecord[] ReadFrameRecords(byte[] logical, int frameCount, int firstRecordOffset)
    {
        var records = new PanFrameRecord[frameCount];
        var recordStart = firstRecordOffset;
        var recordEnd = 0;
        for (var index = 0; index < frameCount; index++)
        {
            // Table entries are end deltas accumulated from logical offset zero.
            recordEnd = checked(recordEnd + ReadUInt16(logical, 4 + index * 2));
            records[index] = new PanFrameRecord(index, recordStart, recordEnd);
            recordStart = recordEnd;
        }

        if (records.Length > 0 && records[records.Length - 1].LogicalRecordEndOffset > logical.Length)
        {
            throw new InvalidDataException("PAN record table points beyond the decoded logical stream.");
        }

        return records;
    }

    private static Palette256 ReadPalette(ReadOnlySpan<byte> rawPalette)
    {
        if (rawPalette.Length != PaletteByteLength)
        {
            throw new InvalidDataException("PAN palette must contain exactly 768 bytes.");
        }

        var colors = new Rgb24[Palette256.ColorCount];
        for (var index = 0; index < colors.Length; index++)
        {
            var rawOffset = index * 3;
            colors[index] = new Rgb24(
                Vga6ToRgb8(rawPalette[rawOffset]),
                Vga6ToRgb8(rawPalette[rawOffset + 1]),
                Vga6ToRgb8(rawPalette[rawOffset + 2]));
        }

        return new Palette256(colors);
    }

    private static List<PanFrame> ReplayFrames(
        string assetName,
        byte[] logical,
        IReadOnlyList<PanFrameRecord> records,
        Palette256 palette)
    {
        var framebuffer = new byte[FramebufferLength];
        var frames = new List<PanFrame>(records.Count);
        // Not in the original: the frames' asset names ("INTRO.PAN#3", see IndexedImage.AssetName); callers name a PAN without its extension.
        var file = assetName.ToUpperInvariant() + (Path.HasExtension(assetName) ? "" : ".PAN");

        // Each PAN frame mutates the same persistent VGA framebuffer.
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var recordStart = record.LogicalRecordStartOffset;
            var recordEnd = record.LogicalRecordEndOffset;
            if (recordEnd < recordStart || recordEnd > logical.Length)
            {
                throw new InvalidDataException($"{assetName} frame {index} has an invalid record boundary.");
            }

            DecodeFrameRecord(assetName, index, logical.AsSpan(recordStart, recordEnd - recordStart), framebuffer);
            frames.Add(new PanFrame(
                index,
                new IndexedImage(IndexedImage.OriginalVgaWidth, IndexedImage.OriginalVgaHeight, framebuffer, $"{file}#{index}"),
                palette,
                TimeSpan.Zero,
                record));
        }

        return frames;
    }

    private static void DecodeFrameRecord(string assetName, int frameIndex, ReadOnlySpan<byte> record, Span<byte> framebuffer)
    {
        if (record.Length < FrameSignature.Length || !record.Slice(0, FrameSignature.Length).SequenceEqual(FrameSignature))
        {
            throw new InvalidDataException($"{assetName} frame {frameIndex} does not begin with the expected 42/1 signature.");
        }

        var source = FrameSignature.Length;
        var destination = 0;

        while (source < record.Length)
        {
            var opcode = record[source++];

            if ((opcode & 0x80) != 0)
            {
                // High-bit opcodes cover skips plus extended skip/fill/literal records.
                var count = opcode - 0x80;
                if (count == 0)
                {
                    var word = ReadUInt16(record, source);
                    source += 2;

                    if (word == 0)
                    {
                        if (source != record.Length)
                        {
                            throw new InvalidDataException($"{assetName} frame {frameIndex} ended before its table boundary.");
                        }

                        return;
                    }

                    if ((word & 0x8000) == 0)
                    {
                        destination = checked(destination + word);
                        continue;
                    }

                    if ((word & 0x4000) != 0)
                    {
                        var fillCount = word - 0xC000;
                        var value = record[source++];
                        Fill(framebuffer, destination, fillCount, value, assetName, frameIndex);
                        destination = checked(destination + fillCount);
                        continue;
                    }

                    var literalCount = word - 0x8000;
                    CopyLiteral(record, ref source, framebuffer, destination, literalCount, assetName, frameIndex);
                    destination = checked(destination + literalCount);
                    continue;
                }

                destination = checked(destination + count);
                continue;
            }

            if (opcode == 0)
            {
                var count = record[source++];
                var value = record[source++];
                Fill(framebuffer, destination, count, value, assetName, frameIndex);
                destination = checked(destination + count);
                continue;
            }

            CopyLiteral(record, ref source, framebuffer, destination, opcode, assetName, frameIndex);
            destination = checked(destination + opcode);
        }

        throw new InvalidDataException($"{assetName} frame {frameIndex} did not terminate with end16.");
    }

    private static void CopyLiteral(
        ReadOnlySpan<byte> sourceBytes,
        ref int source,
        Span<byte> framebuffer,
        int destination,
        int count,
        string assetName,
        int frameIndex)
    {
        EnsureRange(sourceBytes.Length, source, count, $"{assetName} frame {frameIndex} literal source");
        EnsureRange(framebuffer.Length, destination, count, $"{assetName} frame {frameIndex} literal destination");
        sourceBytes.Slice(source, count).CopyTo(framebuffer.Slice(destination, count));
        source += count;
    }

    private static void Fill(Span<byte> framebuffer, int destination, int count, byte value, string assetName, int frameIndex)
    {
        EnsureRange(framebuffer.Length, destination, count, $"{assetName} frame {frameIndex} fill destination");
        framebuffer.Slice(destination, count).Fill(value);
    }

    private static void EnsureRange(int length, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || offset > length || count > length - offset)
        {
            throw new InvalidDataException($"{label} range is outside the expected buffer.");
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes.Length, offset, 2, "u16 read");
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static byte Vga6ToRgb8(byte value)
    {
        value &= 0x3F;
        return (byte)((value << 2) | (value >> 4));
    }

    private sealed class Pan0070Decoder
    {
        // Local model of the original 0DFC:0070 stream expander.
        private const int BufferBase = 0xA897;
        private const int BufferEnd = 0xB896;
        private const int InitialSi = BufferBase + 0x1000;

        private readonly ReadOnlyMemory<byte> _raw;
        private readonly Dictionary<int, byte> _memory = new Dictionary<int, byte>();
        private int _si = InitialSi;
        private int _rawOffset;
        private int _bp;
        private int _dx;
        private bool _carry;

        public Pan0070Decoder(ReadOnlySpan<byte> raw)
        {
            _raw = raw.ToArray();
        }

        public int DecodeSpan(List<byte> output)
        {
            var spanStart = output.Count;
            if (_dx == 0)
            {
                InitialControlWord();
            }

            while (true)
            {
                if (NextControlBit())
                {
                    EnsureByte();
                    output.Add(LoadByte());
                    continue;
                }

                var count = 0;
                if (NextControlBit())
                {
                    EnsureWord();
                    var word = LoadWord();
                    var bx = (((word >> 8) >> 3) | 0xE0) << 8;
                    bx = (bx | (word & 0x00FF)) & 0xFFFF;
                    var shortCount = (word >> 8) & 0x07;
                    if (shortCount != 0)
                    {
                        count = shortCount + 2;
                    }
                    else
                    {
                        EnsureByte();
                        var value = LoadByte();
                        if (value <= 1)
                        {
                            return output.Count - spanStart;
                        }

                        count = value + 1;
                    }

                    CopyFromBackReference(output, bx, count);
                    continue;
                }

                for (var index = 0; index < 2; index++)
                {
                    var bit = NextControlBit();
                    count = ((count << 1) | (bit ? 1 : 0)) & 0xFFFF;
                    _carry = false;
                }

                count += 2;
                EnsureByte();
                CopyFromBackReference(output, 0xFF00 | LoadByte(), count);
            }
        }

        private void InitialControlWord()
        {
            EnsureWord();
            _bp = LoadWord();
            _dx = 0x0010;
        }

        private void RefreshControlWord()
        {
            EnsureWord();
            _bp = Shr(_bp);
            var word = LoadWord();
            _dx = 0x0010;
            _bp = Rcl(word);
        }

        private bool NextControlBit()
        {
            _dx = (_dx - 1) & 0xFFFF;
            if (_dx == 0)
            {
                RefreshControlWord();
            }

            _bp = Rcr(_bp);
            return _carry;
        }

        private int Rcr(int value)
        {
            var oldCarry = _carry;
            _carry = (value & 0x0001) != 0;
            var result = value >> 1;
            if (oldCarry)
            {
                result |= 0x8000;
            }

            return result & 0xFFFF;
        }

        private int Rcl(int value)
        {
            var oldCarry = _carry;
            _carry = (value & 0x8000) != 0;
            var result = (value << 1) & 0xFFFF;
            if (oldCarry)
            {
                result |= 0x0001;
            }

            return result;
        }

        private int Shr(int value)
        {
            _carry = (value & 0x0001) != 0;
            return (value >> 1) & 0xFFFF;
        }

        private void EnsureWord()
        {
            if (_si >= BufferEnd)
            {
                Refill();
            }
        }

        private void EnsureByte()
        {
            if (_si > BufferEnd)
            {
                Refill();
            }
        }

        private void Refill()
        {
            var saved = _memory.TryGetValue(_si, out var savedByte) ? savedByte : (byte)0;
            _si = (_si - 0x1000) & 0xFFFF;
            _memory[_si] = saved;

            var span = _raw.Span;
            var count = Math.Min(0x1000, span.Length - _rawOffset);
            for (var index = 0; index < count; index++)
            {
                _memory[BufferBase + index] = span[_rawOffset + index];
            }

            _rawOffset += count;
        }

        private byte LoadByte()
        {
            var value = _memory.TryGetValue(_si, out var byteValue) ? byteValue : (byte)0;
            _si = (_si + 1) & 0xFFFF;
            return value;
        }

        private int LoadWord()
        {
            var lo = LoadByte();
            var hi = LoadByte();
            return lo | (hi << 8);
        }

        private static void CopyFromBackReference(List<byte> output, int bx, int count)
        {
            var signedBx = (bx & 0x8000) != 0 ? bx - 0x10000 : bx;
            for (var index = 0; index < count; index++)
            {
                var source = output.Count + signedBx;
                output.Add(source >= 0 && source < output.Count ? output[source] : (byte)0);
            }
        }
    }
}

