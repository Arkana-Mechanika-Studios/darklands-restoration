using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>The pack folder: files found by asset name, the manifest, problems reported; and the PNG codec under it.</summary>
public sealed class HdPackTests
{
    private static RgbaImage Solid(int width, int height, uint argb)
    {
        var image = new RgbaImage(width, height);
        Array.Fill(image.Argb, argb);
        return image;
    }

    [Fact]
    public void FilesAreFoundByTheAssetNameInAnyCase()
    {
        using var scratch = new Scratch();
        scratch.Png("pics/startscr.pic.png", Solid(2, 2, 0xFF010203));
        scratch.Png("F01C.CAT/F01WKSW.IMC#3.5.png", Solid(1, 1, 0xFF040506));
        scratch.Png("dropcap_D.png", Solid(1, 1, 0xFF070809));
        scratch.Png("BC/SET.EXT@1A2.PNG", Solid(1, 1, 0xFF0A0B0C));
        var pack = HdPack.Load(scratch.Path);
        Assert.Empty(pack.Problems);
        Assert.Equal(4, pack.Count);
        Assert.Equal(0xFF010203u, pack.Find(@"PICS\STARTSCR.PIC")!.Argb[0]);
        Assert.Equal(0xFF040506u, pack.Find("F01C.CAT/F01WKSW.IMC#3.5")!.Argb[0]);
        Assert.Equal(0xFF070809u, pack.Find("dropcap:D")!.Argb[0]);
        Assert.Equal(0xFF0A0B0Cu, pack.Find("BC/SET.EXT@1a2")!.Argb[0]);
        Assert.Null(pack.Find("COMMONSP.IMG#12"));
        Assert.Same(pack.Find(@"PICS\STARTSCR.PIC"), pack.Find("PICS/STARTSCR.PIC"));
    }

    [Fact]
    public void TheManifestNamesOtherFilesAndWins()
    {
        using var scratch = new Scratch();
        scratch.Png("title.png", Solid(1, 1, 0xFF112233));
        scratch.Png("COMMONSP.IMG#12.png", Solid(1, 1, 0xFF445566));
        scratch.Png("art/column.png", Solid(1, 1, 0xFF778899));
        File.WriteAllText(Path.Combine(scratch.Path, HdPack.ManifestName), """
            // replacements by name
            {
              "assets": {
                "PICS\\STARTSCR.PIC": "title.png",
                "COMMONSP.IMG#12": "art/column.png",
                "COMMONSP.IMG#13": "missing.png",
                "COMMONSP.IMG#14": 5,
              },
            }
            """);
        var pack = HdPack.Load(scratch.Path);
        Assert.Equal(0xFF112233u, pack.Find(@"PICS\STARTSCR.PIC")!.Argb[0]);
        Assert.Equal(0xFF778899u, pack.Find("COMMONSP.IMG#12")!.Argb[0]);
        Assert.False(pack.Has("COMMONSP.IMG#13"));
        Assert.Collection(pack.Problems,
            p => Assert.Contains("missing.png", p),
            p => Assert.Contains("COMMONSP.IMG#14", p));
    }

    [Fact]
    public void ProblemsAreReportedAndTheOriginalsKept()
    {
        using var scratch = new Scratch();
        Assert.Contains("not found", Assert.Single(HdPack.Load(Path.Combine(scratch.Path, "nowhere")).Problems));
        Directory.CreateDirectory(scratch.Path);
        File.WriteAllText(Path.Combine(scratch.Path, "COMMONSP.IMG#1.png"), "not a picture");
        File.WriteAllText(Path.Combine(scratch.Path, HdPack.ManifestName), "{ broken");
        var pack = HdPack.Load(scratch.Path);
        Assert.Contains("manifest ignored", Assert.Single(pack.Problems));
        Assert.True(pack.Has("COMMONSP.IMG#1"));
        Assert.Null(pack.Find("COMMONSP.IMG#1"));
        Assert.Contains(pack.Problems, p => p.Contains("keeps its original pixels"));
        // drawn on the screen, the asset keeps its pixels
        var screen = new Screen();
        new Canvas(screen).Draw(new Darklands.Content.Formats.Graphics.IndexedImage(2, 2, [1, 1, 1, 1], "COMMONSP.IMG#1"), 0, 0);
        Assert.Single(new HdRenderer(pack).Render(screen, new DisplayRectangle(0, 0, 320, 200)).Items);
    }

    [Fact]
    public void PngRoundTripsWithAlpha()
    {
        var random = new Random(5);
        var image = new RgbaImage(37, 23);
        for (var i = 0; i < image.Argb.Length; i++)
        {
            image.Argb[i] = (uint)random.Next() ^ (uint)random.Next() << 16;
        }
        using var stream = new MemoryStream();
        PngCodec.Write(image, stream);
        Assert.Equal(image.Argb, PngCodec.Read(stream.ToArray()).Argb);
    }

    [Fact]
    public void ReadsTheGamesOwnScreenshots()
    {
        using var scratch = new Scratch();
        Directory.CreateDirectory(scratch.Path);
        var screen = new Screen();
        new Random(2).NextBytes(screen.Pixels);
        for (var i = 0; i < 256; i++)
        {
            screen.Palette[i] = 0xFF000000u | (uint)(i * 0x010307) & 0xFFFFFF;
        }
        var path = Path.Combine(scratch.Path, "shot.png");
        PngWriter.Write(screen, path);
        Assert.Equal(screen.Pixels.Select(p => screen.Palette[p]), PngCodec.Read(path).Argb);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0, 4)]
    [InlineData(3, 8)]
    [InlineData(3, 2)]
    [InlineData(4, 8)]
    [InlineData(2, 16)]
    [InlineData(6, 16)]
    public void ReadsTheOtherColourTypes(int colorType, int depth)
    {
        // a 3x2 picture: greys 0, 1, 2 / 3, 0, 1 in the type's own scale (palette: index; alpha 0x80 where there is alpha)
        int[] values = [0, 1, 2, 3, 0, 1];
        var max = (1 << Math.Min(depth, 8)) - 1;
        var channels = colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
        var rows = new List<byte>();
        for (var y = 0; y < 2; y++)
        {
            rows.Add(0);
            var bits = new List<int>();
            for (var x = 0; x < 3; x++)
            {
                var v = colorType == 3 ? values[y * 3 + x] : values[y * 3 + x] * max / 3;
                for (var c = 0; c < channels; c++)
                {
                    var sample = (colorType is 4 && c == 1) || (colorType is 6 && c == 3) ? 0x80 * max / 255 : v;
                    bits.Add(sample);
                }
            }
            if (depth == 16)
            {
                bits.ForEach(b => rows.AddRange([(byte)b, 0]));
            }
            else if (depth == 8)
            {
                bits.ForEach(b => rows.Add((byte)b));
            }
            else
            {
                var packed = new byte[(bits.Count * depth + 7) / 8];
                for (var i = 0; i < bits.Count; i++)
                {
                    packed[i * depth / 8] |= (byte)(bits[i] << (8 - depth - i * depth % 8));
                }
                rows.AddRange(packed);
            }
        }
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(file, "IHDR", [0, 0, 0, 3, 0, 0, 0, 2, (byte)depth, (byte)colorType, 0, 0, 0]);
        if (colorType == 3)
        {
            Chunk(file, "PLTE", [0, 0, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90]);
            Chunk(file, "tRNS", [255, 0x80]);
        }
        using (var raw = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(rows.ToArray());
            }
            Chunk(file, "IDAT", raw.ToArray());
        }
        Chunk(file, "IEND", []);
        var image = PngCodec.Read(file.ToArray());
        uint[] palette = [0xFF000000, 0x800A141E, 0xFF28323C, 0xFF46505A];
        for (var i = 0; i < 6; i++)
        {
            var expected = colorType == 3 ? palette[values[i]] : Grey(values[i] * 255 / 3, colorType is 4 or 6 ? 0x80u : 0xFFu);
            Assert.Equal(expected, image.Argb[i]);
        }
    }

    private static uint Grey(int value, uint alpha) => alpha << 24 | (uint)value << 16 | (uint)value << 8 | (uint)value;

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
        output.Write(body);
        output.Write([0, 0, 0, 0]); // the reader does not check CRCs
    }
}
