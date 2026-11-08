namespace Darklands.Rendering;

/// <summary>
/// The arithmetic of the picture filters (Darklands.Platform.PictureFilter), apart from any graphics library: how much the
/// game's picture is enlarged with hard pixels before the smooth step, and the CRT mask a host lays over the picture.
/// New code (presentation only, outside the game).
/// </summary>
public static class PictureFilterMath
{
    /// <summary>How deep the scanlines go between two rows of the picture once a row is tall enough to show them (0..1).</summary>
    public const double ScanlineDepth = 0.45;

    /// <summary>How much the two colours a column of the aperture mask does not carry are dimmed there (0..1).</summary>
    public const double MaskStrength = 0.12;

    /// <summary>The mean brightness the CRT mask keeps of the picture: what the scanlines and the mask take is mostly given back.</summary>
    public const double KeptBrightness = 0.88;

    // Soft: the beam's spread (a Gaussian's standard deviation, in game pixels), across and down. Across, enough for a
    // 1-pixel checkerboard to fall to about a fifth of its contrast at a texel's centre ((1 - 2e) / (1 + 2e), e = exp(-1 / (2 sigma^2)));
    // down, less (a VGA monitor drew each of the 200 lines twice, sharp from line to line).
    public const double SoftSpreadX = 0.68, SoftSpreadY = 0.32;

    // CRT+: the beam's height (standard deviation, in game rows) for black and for white, between them by brightness; its
    // mask's strength and the beam's spread across (game pixels); the glow's spread and amount; the mean brightness kept before the glow.
    public const double PlusBeamDark = 0.20, PlusBeamBright = 0.36, PlusMaskStrength = 0.30, PlusSpreadX = 0.40;
    public const double PlusGlowSpread = 1.0, PlusGlowAmount = 0.12, PlusKeptBrightness = 0.86;

    // HQ: two colours are the same when their channels (0..1) differ by less than this in all.
    public const double HqSameColour = 1.5 / 255;

    /// <summary>CRT+'s gain on the beams: the mean of a beam row's light over a row (its Gaussian's area) back to 1, and the mask's mean to <see cref="PlusKeptBrightness"/>.</summary>
    public static double PlusBeamGain => 1 / (0.5 * (PlusBeamDark + PlusBeamBright) * Math.Sqrt(2 * Math.PI));

    /// <summary>CRT+'s gain for its mask: the mask's mean raised to <see cref="PlusKeptBrightness"/>.</summary>
    public static double PlusMaskGain => PlusKeptBrightness / (1 - 2 * PlusMaskStrength / 3);

    /// <summary>
    /// The whole factors by which the picture (<paramref name="sourceWidth"/> x <paramref name="sourceHeight"/>) is enlarged with
    /// hard pixels before it is scaled smoothly onto the destination: the smallest that reach the destination's size, so the
    /// smooth step only shrinks a little and blends no more than one window pixel at each edge between two picture pixels.
    /// A destination that is a whole multiple of the picture gets exactly that multiple (the smooth step then changes nothing).
    /// Kept within <paramref name="maxTexture"/> (the graphics card's largest texture).
    /// </summary>
    public static (int X, int Y) Prescale(int destinationWidth, int destinationHeight, int sourceWidth, int sourceHeight, int maxTexture = 8192)
    {
        static int Factor(int destination, int source, int max) =>
            Math.Clamp((destination + source - 1) / Math.Max(1, source), 1, Math.Max(1, max / Math.Max(1, source)));
        return (Factor(destinationWidth, sourceWidth, maxTexture), Factor(destinationHeight, sourceHeight, maxTexture));
    }

    /// <summary>How deep the scanlines are for picture rows <paramref name="rowHeight"/> window pixels tall: none below 1.5, full from 3.</summary>
    public static double ScanlineDepthFor(double rowHeight) => ScanlineDepth * Math.Clamp((rowHeight - 1.5) / 1.5, 0, 1);

    /// <summary>The CRT's gain for scanlines of that depth: the mean of the scanline profile and of the mask raised to <see cref="KeptBrightness"/>.</summary>
    public static double CrtGain(double depth) => KeptBrightness / ((1 - depth / 2) * (1 - 2 * MaskStrength / 3));

    /// <summary>
    /// The CRT mask for a picture of <paramref name="sourceHeight"/> rows shown <paramref name="width"/> x <paramref name="height"/>
    /// window pixels, as the factor each window pixel's red, green and blue are multiplied by (row by row, three values a pixel):
    /// a scanline profile across every picture row (brightest at the row's middle, darkest at its edges) times an aperture
    /// mask of red, green and blue columns three window pixels wide, raised so the mean is <see cref="KeptBrightness"/>.
    /// Factors above 1 brighten (a host clamps the result to white).
    /// </summary>
    public static float[] CrtFactors(int width, int height, int sourceHeight = 200)
    {
        var rowHeight = height / (double)Math.Max(1, sourceHeight);
        var depth = ScanlineDepthFor(rowHeight);
        // the means of the scanline profile (1 - depth (1 + cos 2 pi f) / 2 over a row) and of the mask, for the gain
        var gain = CrtGain(depth);
        var rows = new double[height];
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5) / rowHeight;
            var f = v - Math.Floor(v);
            rows[y] = 1 - depth * (1 + Math.Cos(2 * Math.PI * f)) / 2;
        }
        var factors = new float[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                for (var c = 0; c < 3; c++)
                {
                    var mask = x % 3 == c ? 1 : 1 - MaskStrength;
                    factors[i + c] = (float)(gain * rows[y] * mask);
                }
            }
        }
        return factors;
    }

    /// <summary>
    /// The CRT mask as two ARGB pictures for a host with a multiplying blend only (dst x src, src at most 1): the darkening part
    /// (each factor up to 1), drawn multiplied; and the brightening part (what is above 1), drawn as dst + dst x src.
    /// Together: dst x factor.
    /// </summary>
    public static (uint[] Darken, uint[] Brighten) CrtLayers(int width, int height, int sourceHeight = 200)
    {
        var factors = CrtFactors(width, height, sourceHeight);
        var darken = new uint[width * height];
        var brighten = new uint[width * height];
        for (var p = 0; p < darken.Length; p++)
        {
            uint dark = 0xFF000000, bright = 0xFF000000;
            for (var c = 0; c < 3; c++)
            {
                var factor = factors[p * 3 + c];
                var shift = 16 - 8 * c;   // red, green, blue
                dark |= (uint)Math.Round(Math.Min(factor, 1) * 255) << shift;
                bright |= (uint)Math.Round(Math.Clamp(factor - 1, 0, 1) * 255) << shift;
            }
            darken[p] = dark;
            brighten[p] = bright;
        }
        return (darken, brighten);
    }
}
