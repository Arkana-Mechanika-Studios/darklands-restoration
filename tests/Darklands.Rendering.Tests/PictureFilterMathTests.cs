namespace Darklands.Rendering.Tests;

/// <summary>The arithmetic of the picture filters: the whole-number enlargement before the smooth step, and the CRT mask.</summary>
public sealed class PictureFilterMathTests
{
    [Theory]
    [InlineData(640, 400, 2, 2)]      // whole multiples: exactly them
    [InlineData(1280, 800, 4, 4)]
    [InlineData(1728, 1080, 6, 6)]    // 1920x1080: 5.4 -> 6, shrunk a little
    [InlineData(1013, 571, 4, 3)]
    [InlineData(100, 50, 1, 1)]       // smaller than the picture: no enlargement
    public void ThePrescaleIsTheSmallestWholeFactorThatReachesTheDestination(int width, int height, int kx, int ky) =>
        Assert.Equal((kx, ky), PictureFilterMath.Prescale(width, height, 320, 200));

    [Fact]
    public void ThePrescaleStaysWithinTheLargestTexture() =>
        Assert.Equal((12, 20), PictureFilterMath.Prescale(7680, 4320, 320, 200, maxTexture: 4096));

    [Fact]
    public void TheScanlinesFollowThePicturesRows()
    {
        const int width = 30, height = 1080;
        var f = PictureFilterMath.CrtFactors(width, height);
        // one picture row is 5.4 window rows: brightest at its middle, darkest at its edge
        double Row(int y) => f[(y * width) * 3] + f[(y * width) * 3 + 1] + f[(y * width) * 3 + 2];
        Assert.True(Row(2) > Row(0) * 1.4);
        Assert.True(Row(8) > Row(5) * 1.4);
        // the mean is what the mask keeps
        Assert.InRange(f.Average(v => (double)v), PictureFilterMath.KeptBrightness - 0.02, PictureFilterMath.KeptBrightness + 0.02);
    }

    [Fact]
    public void NoScanlinesWhereTheRowsAreTooThinToShowThem()
    {
        var f = PictureFilterMath.CrtFactors(3, 200);
        Assert.All(Enumerable.Range(0, 200), y => Assert.Equal(f[0], f[y * 9]));
        Assert.Equal(0, PictureFilterMath.ScanlineDepthFor(1.4));
        Assert.Equal(PictureFilterMath.ScanlineDepth, PictureFilterMath.ScanlineDepthFor(5.4));
    }

    [Fact]
    public void TheApertureMaskRunsInRedGreenBlueColumns()
    {
        var f = PictureFilterMath.CrtFactors(6, 1);
        for (var x = 0; x < 6; x++)
        {
            var strongest = Enumerable.Range(0, 3).MaxBy(c => f[x * 3 + c]);
            Assert.Equal(x % 3, strongest);
        }
    }

    [Fact]
    public void TheTwoLayersMultiplyToTheFactors()
    {
        const int width = 9, height = 20;
        var f = PictureFilterMath.CrtFactors(width, height, 4);
        var (darken, brighten) = PictureFilterMath.CrtLayers(width, height, 4);
        for (var p = 0; p < width * height; p++)
        {
            for (var c = 0; c < 3; c++)
            {
                var shift = 16 - 8 * c;
                var d = (darken[p] >> shift & 0xFF) / 255.0;
                var b = (brighten[p] >> shift & 0xFF) / 255.0;
                Assert.InRange(d * (1 + b), f[p * 3 + c] - 0.01, f[p * 3 + c] + 0.01);
            }
            Assert.Equal(0xFF000000u, darken[p] & 0xFF000000u);
        }
    }
}

/// <summary>The CPU reference of the picture filters (what a host's renderer must draw).</summary>
public sealed class PictureFilterReferenceTests
{
    private static uint[] Checks(int width, int height)
    {
        var argb = new uint[width * height];
        for (var i = 0; i < argb.Length; i++)
        {
            argb[i] = (i % width + i / width) % 2 == 0 ? 0xFFC08040u : 0xFF204060u;
        }
        return argb;
    }

    [Fact]
    public void SharpRepeatsEachPixelAndEvenIsTheSameAtWholeMultiples()
    {
        var picture = Checks(8, 5);
        var sharp = PictureFilterReference.Render(picture, 8, 5, Darklands.Platform.PictureFilter.Sharp, 32, 20);
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                Assert.Equal(picture[y / 4 * 8 + x / 4], sharp[y * 32 + x]);
            }
        }
        Assert.Equal(sharp, PictureFilterReference.Render(picture, 8, 5, Darklands.Platform.PictureFilter.Even, 32, 20));
    }

    [Fact]
    public void EvenBlendsAtMostOneOutputPixelAtEachEdge()
    {
        var picture = Checks(8, 1);
        // 8 -> 43: factor 6 (48), shrunk a little; between two flat stretches at most one blended pixel
        var smooth = PictureFilterReference.Render(picture, 8, 1, Darklands.Platform.PictureFilter.Even, 43, 1);
        var blended = 0;
        for (var x = 0; x < 43; x++)
        {
            if (smooth[x] != picture[0] && smooth[x] != picture[1])
            {
                blended++;
                Assert.True(x > 0 && x < 42 && (smooth[x - 1] == picture[0] || smooth[x - 1] == picture[1]));
            }
        }
        Assert.InRange(blended, 1, 7);
    }

    [Fact]
    public void TheCrtKeepsMostOfTheBrightness()
    {
        var grey = Enumerable.Repeat(0xFF808080u, 320 * 200).ToArray();
        var crt = PictureFilterReference.Render(grey, 320, 200, Darklands.Platform.PictureFilter.Crt, 1728, 1080);
        var mean = crt.Average(p => (double)((p >> 16 & 0xFF) + (p >> 8 & 0xFF) + (p & 0xFF))) / (3 * 0x80);
        Assert.InRange(mean, PictureFilterMath.KeptBrightness - 0.02, PictureFilterMath.KeptBrightness + 0.02);
    }
}

/// <summary>What the filters of the shader only must do (the CPU reference; the shader is held to it in the host's tests).</summary>
public sealed class PictureFilterLookTests
{
    private const uint Dark = 0xFF204060, Light = 0xFFC08040;

    private static uint[] Picture(int width, int height, Func<int, int, bool> light)
    {
        var argb = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                argb[y * width + x] = light(x, y) ? Light : Dark;
            }
        }
        return argb;
    }

    private static uint[] Render(uint[] picture, int width, int height, Darklands.Platform.PictureFilter filter, int outWidth, int outHeight) =>
        PictureFilterReference.Render(picture, width, height, filter, outWidth, outHeight);

    private static int Red(uint c) => (int)(c >> 16 & 0xFF);

    [Theory]
    [InlineData(Darklands.Platform.PictureFilter.Soft)]
    [InlineData(Darklands.Platform.PictureFilter.Hq)]
    [InlineData(Darklands.Platform.PictureFilter.Even)]
    public void AFlatColourStaysThatColour(Darklands.Platform.PictureFilter filter) =>
        Assert.All(Render(Picture(40, 25, (_, _) => true), 40, 25, filter, 216, 135), c => Assert.Equal(Light, c));

    [Fact]
    public void SoftBlendsACheckerboardIntoItsColour()
    {
        // 1-pixel checks: the soft beam leaves less than a fifth of their contrast, around the mean of the two colours
        var checks = Picture(40, 25, (x, y) => (x + y) % 2 == 0);
        var soft = Render(checks, 40, 25, Darklands.Platform.PictureFilter.Soft, 216, 135);
        var inner = Enumerable.Range(20, 95).SelectMany(y => Enumerable.Range(20, 176).Select(x => Red(soft[y * 216 + x]))).ToList();
        var contrast = Red(Light) - Red(Dark);
        Assert.InRange(inner.Max() - inner.Min(), 0, contrast / 5);
        Assert.InRange(inner.Average(), (Red(Light) + Red(Dark)) / 2.0 - 8, (Red(Light) + Red(Dark)) / 2.0 + 8);
    }

    [Fact]
    public void HqLeavesDitherRectanglesAndLonePixelsAsEvenDoes()
    {
        foreach (var picture in new[]
        {
            Picture(40, 25, (x, y) => (x + y) % 2 == 0),                            // checkerboard dithering
            Picture(40, 25, (x, y) => (x / 2 + y / 2) % 2 == 0),                    // 2 x 2 checks
            Picture(40, 25, (x, y) => x is >= 10 and < 30 && y is >= 5 and < 20),   // a rectangle
            Picture(40, 25, (x, y) => x == 20 && y == 12),                          // a lone pixel
            Picture(40, 25, (x, _) => x >= 20),                                     // a straight edge
        })
        {
            Assert.Equal(Render(picture, 40, 25, Darklands.Platform.PictureFilter.Even, 216, 135),
                Render(picture, 40, 25, Darklands.Platform.PictureFilter.Hq, 216, 135));
        }
    }

    [Fact]
    public void HqRoundsAStaircase()
    {
        // a 45-degree edge: below the diagonal light, above it dark; HQ cuts the steps' corners, both sides alike
        var stairs = Picture(40, 25, (x, y) => x + y >= 32);
        var even = Render(stairs, 40, 25, Darklands.Platform.PictureFilter.Even, 216, 135);
        var hq = Render(stairs, 40, 25, Darklands.Platform.PictureFilter.Hq, 216, 135);
        var changed = Enumerable.Range(0, even.Length).Where(i => even[i] != hq[i]).ToList();
        Assert.NotEmpty(changed);
        // only near the edge (within a pixel and a half of the diagonal, in game pixels)
        Assert.All(changed, i => Assert.InRange((i % 216 + 0.5) / 5.4 + (i / 216 + 0.5) / 5.4, 32 - 1.5, 32 + 1.5));
        // light gained on the dark side and dark on the light side, in equal measure
        var gained = changed.Count(i => Red(hq[i]) > Red(even[i]));
        var lost = changed.Count(i => Red(hq[i]) < Red(even[i]));
        Assert.InRange(gained, lost * 0.8, lost * 1.25);
    }

    [Fact]
    public void CrtPlusKeepsTheBrightnessAndShowsScanlinesOnlyWhereTheRowsAreTall()
    {
        var grey = Enumerable.Repeat(0xFF808080u, 320 * 200).ToArray();
        var tall = Render(grey, 320, 200, Darklands.Platform.PictureFilter.CrtPlus, 1728, 1080);
        var mean = tall.Average(p => (double)((p >> 16 & 0xFF) + (p >> 8 & 0xFF) + (p & 0xFF))) / (3 * 0x80);
        Assert.InRange(mean, 0.85, 1.05);
        double Row(uint[] pixels, int width, int y) => Enumerable.Range(0, width).Average(x => (double)(pixels[y * width + x] >> 8 & 0xFF));
        var rows = Enumerable.Range(400, 11).Select(y => Row(tall, 1728, y)).ToList();
        Assert.True(rows.Max() > rows.Min() * 1.4, $"scanlines: {rows.Min()}..{rows.Max()}");
        // rows 1 output pixel tall: no scanlines (only the mask's columns)
        var small = Render(grey, 320, 200, Darklands.Platform.PictureFilter.CrtPlus, 320, 200);
        var flat = Enumerable.Range(50, 100).Select(y => Row(small, 320, y)).ToList();
        Assert.InRange(flat.Max() - flat.Min(), 0, 1.0);
    }
}
