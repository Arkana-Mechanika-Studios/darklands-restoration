using System.Security.Cryptography;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The card fade palette-ramp(7, 100, c, 70, 3) (R043+0x3838) for the two measures DS:5734 of D-dff56b: 14 (the tester's emulator, 4 steps)
/// and 38 (a 70 Hz VGA, 11 steps, the C#'s fixed value). The original's uploads (root 047C:058B: the 96 colours of each half, every step)
/// come from direct calls of R043+0x3838 with DS:5734 set to each value and the same DAC at DS:530A (scratch run fade_direct.py,
/// round visuals 2026-11-06): the SHA-256 of all uploads in order.
/// </summary>
public sealed class FadeRampTests
{
    private static readonly byte[] Dac = Enumerable.Range(0, 768).Select(k => (byte)((k * 37 + 11) % 64)).ToArray();

    [Theory]
    [InlineData(14, 0xFF, 8, "ff39a2ec1872843203871358821354095c13842a820b5e30feacec5ba2adcac5")]
    [InlineData(14, 0x1F, 8, "b72f00586d4505b451e486cb2f4f4149999fd8c272801999cc7d341e61c8ae4b")]
    [InlineData(38, 0xFF, 22, "752d7a0a2e55d3c223421b7f587b0fa6af76e2849fa52ca75819f94a7633859a")]
    [InlineData(38, 0x1F, 22, "803b845ef540e21da38934b56a43b19bd31b2d1f2fba7a02da85d29a1b9495dc")]
    public void TheRampMakesTheOriginalsUploadsForEitherMeasure(int measure, int light, int count, string sha256)
    {
        var uploads = CardScreen.PaletteRamp(Dac, 7, 100, light, 70, 3, measure);
        Assert.Equal(count, uploads.Count);
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(uploads.SelectMany(u => u).ToArray())).ToLowerInvariant());
    }

    /// <summary>The C# uses a 70 Hz VGA's measure; the last picture of the fade differs from the tester's 4-step one in a few DAC values.</summary>
    [Fact]
    public void TheCardFadeUsesAVgasMeasure()
    {
        Assert.Equal(38, CardScreen.FadeMeasure);
        Assert.Equal(11, CardScreen.RampSteps(3, CardScreen.FadeMeasure));
        Assert.Equal(4, CardScreen.RampSteps(3, 14));
        var vga = CardScreen.PaletteRamp(Dac, 7, 100, 0xFF, 70, 3, 38)[^1];
        var tester = CardScreen.PaletteRamp(Dac, 7, 100, 0xFF, 70, 3, 14)[^1];
        var differ = vga.Zip(tester).Count(p => p.First != p.Second);
        Assert.InRange(differ, 1, 288);
        Assert.All(vga.Zip(tester), p => Assert.InRange(p.First - p.Second, -1, 1));
    }
}
