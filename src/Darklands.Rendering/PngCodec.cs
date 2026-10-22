using System.Buffers.Binary;
using System.IO.Compression;

namespace Darklands.Rendering;

/// <summary>
/// Reads and writes PNG files as <see cref="RgbaImage"/>: every colour type at 8 bits (16 bits read to the high byte,
/// palette and grey also at 1, 2 and 4 bits), with transparency (alpha or tRNS); not interlaced.
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static RgbaImage Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Decodes a PNG; throws <see cref="InvalidDataException"/> for a broken or unsupported file.</summary>
    public static RgbaImage Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < Signature.Length || !file[..Signature.Length].SequenceEqual(Signature))
        {
            throw new InvalidDataException("not a PNG file");
        }
        int width = 0, height = 0, depth = 0, colorType = -1;
        byte[] palette = [];
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        var at = Signature.Length;
        while (true)
        {
            if (at + 8 > file.Length)
            {
                throw new InvalidDataException("PNG ends before IEND");
            }
            var length = BinaryPrimitives.ReadInt32BigEndian(file[at..]);
            var type = System.Text.Encoding.ASCII.GetString(file.Slice(at + 4, 4));
            if (length < 0 || at + 12 + length > file.Length)
            {
                throw new InvalidDataException($"PNG chunk {type} runs past the end");
            }
            var data = file.Slice(at + 8, length);
            at += 12 + length;
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8];
                    colorType = data[9];
                    if (data[12] != 0)
                    {
                        throw new InvalidDataException("interlaced PNG files are not supported");
                    }
                    if (width <= 0 || height <= 0 || (long)width * height > 1 << 26)
                    {
                        throw new InvalidDataException($"PNG size {width}x{height} out of range");
                    }
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    transparency = data.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    return Decode(compressed.ToArray(), width, height, depth, colorType, palette, transparency);
            }
        }
    }

    private static RgbaImage Decode(byte[] compressed, int width, int height, int depth, int colorType, byte[] palette, byte[]? transparency)
    {
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"PNG colour type {colorType} unknown"),
        };
        var depthOk = colorType switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => depth is 8 or 16,
        };
        if (!depthOk)
        {
            throw new InvalidDataException($"PNG bit depth {depth} invalid for colour type {colorType}");
        }
        var stride = (width * channels * depth + 7) / 8;
        var pixelBytes = Math.Max(1, channels * depth / 8);
        var raw = new byte[(stride + 1) * height];
        using (var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }
        var previous = new byte[stride];
        var current = new byte[stride];
        var image = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            var line = raw.AsSpan(y * (stride + 1), stride + 1);
            Unfilter(line[0], line[1..], previous, current, pixelBytes);
            var output = image.Argb.AsSpan(y * width, width);
            if (depth == 8 && colorType == 6)
            {
                for (var x = 0; x < width; x++)
                {
                    output[x] = (uint)current[x * 4 + 3] << 24 | (uint)current[x * 4] << 16 | (uint)current[x * 4 + 1] << 8 | current[x * 4 + 2];
                }
            }
            else if (depth == 8 && colorType == 2 && transparency is null)
            {
                for (var x = 0; x < width; x++)
                {
                    output[x] = 0xFF000000u | (uint)current[x * 3] << 16 | (uint)current[x * 3 + 1] << 8 | current[x * 3 + 2];
                }
            }
            else
            {
                for (var x = 0; x < width; x++)
                {
                    output[x] = Pixel(current, x, depth, colorType, palette, transparency);
                }
            }
            (previous, current) = (current, previous);
        }
        return image;
    }

    private static void Unfilter(byte filter, ReadOnlySpan<byte> line, byte[] previous, byte[] current, int pixelBytes)
    {
        switch (filter)
        {
            case 0:
                line.CopyTo(current);
                return;
            case 1:
                for (var i = 0; i < line.Length; i++)
                {
                    current[i] = (byte)(line[i] + (i >= pixelBytes ? current[i - pixelBytes] : 0));
                }
                return;
            case 2:
                for (var i = 0; i < line.Length; i++)
                {
                    current[i] = (byte)(line[i] + previous[i]);
                }
                return;
        }
        for (var i = 0; i < line.Length; i++)
        {
            int left = i >= pixelBytes ? current[i - pixelBytes] : 0;
            int up = previous[i];
            int upLeft = i >= pixelBytes ? previous[i - pixelBytes] : 0;
            var predicted = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"PNG row filter {filter} unknown"),
            };
            current[i] = (byte)(line[i] + predicted);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Sample <paramref name="index"/> of a row at the bit depth (16 bits: the high byte).</summary>
    private static int Sample(byte[] row, int index, int depth) => depth switch
    {
        8 => row[index],
        16 => row[index * 2],
        _ => row[index * depth / 8] >> (8 - depth - index * depth % 8) & ((1 << depth) - 1),
    };

    /// <summary>A whole 16-bit (or smaller) sample, to compare with tRNS.</summary>
    private static int FullSample(byte[] row, int index, int depth) =>
        depth == 16 ? row[index * 2] << 8 | row[index * 2 + 1] : Sample(row, index, depth);

    private static uint Pixel(byte[] row, int x, int depth, int colorType, byte[] palette, byte[]? transparency)
    {
        static uint Argb(int a, int r, int g, int b) => (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | (uint)b;
        int Scale(int value) => depth switch { 1 => value * 255, 2 => value * 85, 4 => value * 17, _ => value };
        switch (colorType)
        {
            case 0:
            {
                var grey = Scale(Sample(row, x, depth));
                var clear = transparency is { Length: >= 2 } && FullSample(row, x, depth) == (transparency[0] << 8 | transparency[1]);
                return Argb(clear ? 0 : 255, grey, grey, grey);
            }
            case 2:
            {
                var clear = transparency is { Length: >= 6 }
                    && FullSample(row, x * 3, depth) == (transparency[0] << 8 | transparency[1])
                    && FullSample(row, x * 3 + 1, depth) == (transparency[2] << 8 | transparency[3])
                    && FullSample(row, x * 3 + 2, depth) == (transparency[4] << 8 | transparency[5]);
                return Argb(clear ? 0 : 255, Sample(row, x * 3, depth), Sample(row, x * 3 + 1, depth), Sample(row, x * 3 + 2, depth));
            }
            case 3:
            {
                var index = Sample(row, x, depth);
                if (index * 3 + 2 >= palette.Length)
                {
                    throw new InvalidDataException($"PNG palette index {index} past the palette");
                }
                var alpha = transparency is not null && index < transparency.Length ? transparency[index] : 255;
                return Argb(alpha, palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]);
            }
            case 4:
            {
                var grey = Sample(row, x * 2, depth);
                return Argb(Sample(row, x * 2 + 1, depth), grey, grey, grey);
            }
            default:
                return Argb(Sample(row, x * 4 + 3, depth), Sample(row, x * 4, depth), Sample(row, x * 4 + 1, depth), Sample(row, x * 4 + 2, depth));
        }
    }

    /// <summary>Writes the picture as an 8-bit RGBA PNG.</summary>
    public static void Write(RgbaImage image, string path)
    {
        using var file = File.Create(path);
        Write(image, file);
    }

    public static void Write(RgbaImage image, Stream output)
    {
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + image.Width * 4];
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var c = image.Argb[y * image.Width + x];
                    row[1 + x * 4] = (byte)(c >> 16);
                    row[2 + x * 4] = (byte)(c >> 8);
                    row[3 + x * 4] = (byte)c;
                    row[4 + x * 4] = (byte)(c >> 24);
                }
                z.Write(row);
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
        data.CopyTo(body, 4);
        output.Write(body);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }
}
