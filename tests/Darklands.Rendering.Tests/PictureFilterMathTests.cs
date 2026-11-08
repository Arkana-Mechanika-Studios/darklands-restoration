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
    public void SharpRepeatsEachPixelAndSmoothIsTheSameAtWholeMultiples()
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
        Assert.Equal(sharp, PictureFilterReference.Render(picture, 8, 5, Darklands.Platform.PictureFilter.Smooth, 32, 20));
    }

    [Fact]
    public void SmoothBlendsAtMostOneOutputPixelAtEachEdge()
    {
        var picture = Checks(8, 1);
        // 8 -> 43: factor 6 (48), shrunk a little; between two flat stretches at most one blended pixel
        var smooth = PictureFilterReference.Render(picture, 8, 1, Darklands.Platform.PictureFilter.Smooth, 43, 1);
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
