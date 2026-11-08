using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The PIC decoder's LZW stage keeps each string as a prefix code and a byte (fast); the form it replaced kept each string as a byte
/// list and copied it at every code (a member's first character sheet took 120..280 ms). Every picture of the game folder and of its
/// CAT archives decodes to the same pixels and the same chunk lengths with both.
/// </summary>
public sealed class PicDecoderReferenceTests
{
    [Fact]
    public void EveryPictureDecodesAsWithTheListOfStringsDecoder()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var pictures = new List<(string Name, byte[] Bytes)>();
        foreach (var name in files.List("PICS", "*.PIC"))
        {
            pictures.Add((name, files.Read("PICS\\" + name)));
        }
        foreach (var cat in files.List("", "*.CAT"))
        {
            var archive = CatArchive.Decode(files.Read(cat));
            foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".PIC", StringComparison.OrdinalIgnoreCase)))
            {
                var stored = archive.ReadStored(entry.Name);
                pictures.Add((cat + "/" + entry.Name, stored));
                try
                {
                    pictures.Add((cat + "/" + entry.Name + "@unpacked", CatMemberDecoder.Decode(stored, 0x20000)));
                }
                catch (Exception)
                {
                    // not a packed member
                }
            }
        }
        Assert.True(pictures.Count > 100);
        var compared = 0;
        foreach (var (name, bytes) in pictures)
        {
            var expected = Reference(bytes);
            Pic? actual = null;
            Exception? failure = null;
            try
            {
                actual = new PicDecoder().Decode(name, bytes);
            }
            catch (Exception e)
            {
                failure = e;
            }
            if (expected is not var (pixels, lzwCount, rleCount))
            {
                Assert.True(failure is not null || actual is null, $"{name}: decodes, the reference does not");
                continue;
            }
            Assert.True(failure is null, $"{name}: {failure?.Message}");
            Assert.Equal(lzwCount, actual!.ImageChunk.LzwDecodedLength);
            Assert.Equal(rleCount, actual.ImageChunk.RleDecodedLength);
            Assert.True(pixels.AsSpan().SequenceEqual(actual.Image.Pixels), $"{name}: pixels differ");
            compared++;
        }
        Assert.True(compared > 100, $"{compared} pictures compared");
    }

    /// <summary>The image chunk's pixels and its LZW and RLE lengths as the list-of-strings decoder made them; null when it failed.</summary>
    private static (byte[] Pixels, int Lzw, int Rle)? Reference(byte[] data)
    {
        try
        {
            var offset = 0;
            while (offset + 4 <= data.Length)
            {
                var tag = data[offset] | (data[offset + 1] << 8);
                var length = data[offset + 2] | (data[offset + 3] << 8);
                offset += 4;
                if (offset + length > data.Length)
                {
                    return null;
                }
                if (tag == 0x3058)
                {
                    var chunk = data.AsSpan(offset, length);
                    var width = chunk[0] | (chunk[1] << 8);
                    var height = chunk[2] | (chunk[3] << 8);
                    var lzw = LzwDecompress(chunk[5..], chunk[4]);
                    var rle = RleDecode(lzw);
                    return width == 0 || height == 0 || rle.Count < width * height ? null : ([.. rle.Take(width * height)], lzw.Count, rle.Count);
                }
                offset += length;
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The decoder's LZW and RLE stages as they were before the prefix-code form (unchanged, for the comparison).
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
                    throw new InvalidDataException("PIC RLE stream ended after repeat marker.");
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
