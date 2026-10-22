namespace Darklands.Rendering;

/// <summary>
/// A colour change of the form shown = <see cref="Scale"/> × own + (<see cref="Red"/>, <see cref="Green"/>,
/// <see cref="Blue"/>) per channel (0..255): a fade toward a colour (scale 1 − t, offset t × colour), a fade to black
/// (offset 0), a flat colour (scale 0).
/// </summary>
public readonly record struct ColorLine(double Scale, double Red, double Green, double Blue)
{
    /// <summary>The line applied to a colour (0xAARRGGBB), its alpha kept.</summary>
    public uint Apply(uint argb) =>
        (argb & 0xFF000000u) | Channel(argb, 16, Red) << 16 | Channel(argb, 8, Green) << 8 | Channel(argb, 0, Blue);

    /// <summary>The line as a table: 256 entries per channel (red, green, blue), for applying it to many pixels.</summary>
    public byte[] Table()
    {
        var table = new byte[768];
        for (var v = 0; v < 256; v++)
        {
            table[v] = (byte)Channel((uint)v << 16, 16, Red);
            table[256 + v] = (byte)Channel((uint)v << 8, 8, Green);
            table[512 + v] = (byte)Channel((uint)v, 0, Blue);
        }
        return table;
    }

    private uint Channel(uint argb, int shift, double offset) =>
        (uint)Math.Clamp((int)Math.Round(Scale * ((argb >> shift) & 0xFF) + offset, MidpointRounding.AwayFromZero), 0, 255);
}

/// <summary>
/// The colour lines of a drawing's palette changes: found from the distinct pairs (own colour, shown colour) of its
/// changed pixels, and which line each pair takes.
/// </summary>
/// <remarks>
/// One line is fitted to all pairs (weighted by their pixel counts), then the pair farthest from its line starts a new
/// one and the pairs are shared out again, up to <see cref="MaxLines"/> lines or until every pair lies within
/// <see cref="Tolerance"/> of its line. A palette fade, which moves every colour of a range the same part of the way to
/// one colour, is one line and is reproduced exactly; changes not of that form (colour cycling, a remap to unrelated
/// colours) end on flat lines, the shown colour without the detail. When most of the drawing changed,
/// <see cref="Unchanged"/> is the line with the most pixels: the colours a fade leaves alone (outlines, shadows) follow
/// it, so the replacement shows nowhere unfaded inside a faded picture. Deterministic: the pairs come in a fixed order.
/// </remarks>
public sealed class ColorLines
{
    public const int MaxLines = 4;

    /// <summary>How far (per channel, 0..255) a pair may lie from its line: the game's colours have 6 bits, so 4 is one step.</summary>
    public const double Tolerance = 6;

    private readonly Dictionary<(uint Own, uint Shown), int> _lineOfPair;

    private ColorLines(ColorLine[] lines, Dictionary<(uint, uint), int> lineOfPair, int unchanged)
    {
        Lines = lines;
        _lineOfPair = lineOfPair;
        Unchanged = unchanged;
        Tables = [.. lines.Select(line => line.Table())];
    }

    public IReadOnlyList<ColorLine> Lines { get; }

    /// <summary>The line unchanged pixels take (-1: they keep the replacement's colours).</summary>
    public int Unchanged { get; }

    /// <summary>Per line, its <see cref="ColorLine.Table"/>.</summary>
    public IReadOnlyList<byte[]> Tables { get; }

    /// <summary>The line of a changed pair.</summary>
    public int LineOf(uint own, uint shown) => _lineOfPair[(own, shown)];

    /// <summary>
    /// The lines of the changed pairs (sorted by own then shown colour, with their pixel counts) of a drawing of
    /// <paramref name="pixels"/> pixels.
    /// </summary>
    public static ColorLines Fit(IReadOnlyList<((uint Own, uint Shown) Pair, int Count)> pairs, int pixels)
    {
        var lines = new List<ColorLine> { FitLine(pairs, null, -1) };
        var lineOfPair = new int[pairs.Count];
        while (true)
        {
            for (var round = 0; round < 10; round++)
            {
                var changed = false;
                for (var p = 0; p < pairs.Count; p++)
                {
                    var best = 0;
                    for (var l = 1; l < lines.Count; l++)
                    {
                        if (Residual(pairs[p].Pair, lines[l]) < Residual(pairs[p].Pair, lines[best]))
                        {
                            best = l;
                        }
                    }
                    changed |= best != lineOfPair[p];
                    lineOfPair[p] = best;
                }
                for (var l = 0; l < lines.Count; l++)
                {
                    if (lineOfPair.Contains(l))
                    {
                        lines[l] = FitLine(pairs, lineOfPair, l);
                    }
                }
                if (!changed && round > 0)
                {
                    break;
                }
            }
            var worst = -1;
            var worstResidual = Tolerance;
            for (var p = 0; p < pairs.Count; p++)
            {
                var residual = Residual(pairs[p].Pair, lines[lineOfPair[p]]);
                if (residual > worstResidual)
                {
                    (worst, worstResidual) = (p, residual);
                }
            }
            if (worst < 0 || lines.Count == MaxLines)
            {
                break;
            }
            var shown = pairs[worst].Pair.Shown;
            lines.Add(new ColorLine(0, (shown >> 16) & 0xFF, (shown >> 8) & 0xFF, shown & 0xFF));
        }
        var map = new Dictionary<(uint, uint), int>();
        var perLine = new long[lines.Count];
        long changedPixels = 0;
        for (var p = 0; p < pairs.Count; p++)
        {
            map[pairs[p].Pair] = lineOfPair[p];
            perLine[lineOfPair[p]] += pairs[p].Count;
            changedPixels += pairs[p].Count;
        }
        var unchanged = -1;
        if (changedPixels * 2 > pixels)
        {
            unchanged = 0;
            for (var l = 1; l < lines.Count; l++)
            {
                if (perLine[l] > perLine[unchanged])
                {
                    unchanged = l;
                }
            }
        }
        return new ColorLines([.. lines], map, unchanged);
    }

    private static double Channel(uint argb, int channel) => (argb >> (16 - 8 * channel)) & 0xFF;

    private static double Offset(ColorLine line, int channel) => channel switch { 0 => line.Red, 1 => line.Green, _ => line.Blue };

    private static double Residual((uint Own, uint Shown) pair, ColorLine line)
    {
        var worst = 0.0;
        for (var c = 0; c < 3; c++)
        {
            worst = Math.Max(worst, Math.Abs(Channel(pair.Shown, c) - (line.Scale * Channel(pair.Own, c) + Offset(line, c))));
        }
        return worst;
    }

    /// <summary>
    /// The line through the pairs of line <paramref name="line"/> (all pairs when <paramref name="lineOfPair"/> is null),
    /// weighted by their pixel counts: one scale for the three channels (least squares, kept within 0..1), an offset per channel.
    /// </summary>
    private static ColorLine FitLine(IReadOnlyList<((uint Own, uint Shown) Pair, int Count)> pairs, int[]? lineOfPair, int line)
    {
        double n = 0;
        Span<double> ownMean = stackalloc double[3];
        Span<double> shownMean = stackalloc double[3];
        for (var p = 0; p < pairs.Count; p++)
        {
            if (lineOfPair is not null && lineOfPair[p] != line)
            {
                continue;
            }
            n += pairs[p].Count;
            for (var c = 0; c < 3; c++)
            {
                ownMean[c] += pairs[p].Count * Channel(pairs[p].Pair.Own, c);
                shownMean[c] += pairs[p].Count * Channel(pairs[p].Pair.Shown, c);
            }
        }
        for (var c = 0; c < 3; c++)
        {
            ownMean[c] /= n;
            shownMean[c] /= n;
        }
        double covariance = 0, variance = 0;
        for (var p = 0; p < pairs.Count; p++)
        {
            if (lineOfPair is not null && lineOfPair[p] != line)
            {
                continue;
            }
            for (var c = 0; c < 3; c++)
            {
                var o = Channel(pairs[p].Pair.Own, c) - ownMean[c];
                covariance += pairs[p].Count * o * (Channel(pairs[p].Pair.Shown, c) - shownMean[c]);
                variance += pairs[p].Count * o * o;
            }
        }
        var scale = variance > 1e-9 ? Math.Clamp(covariance / variance, 0, 1) : 0;
        return new ColorLine(scale, shownMean[0] - scale * ownMean[0], shownMean[1] - scale * ownMean[1], shownMean[2] - scale * ownMean[2]);
    }
}

/// <summary>
/// What the palette does to an asset's drawing now: per pixel of the asset (screen orientation, as
/// <see cref="AssetPlacement.Owned"/>) the <see cref="ColorLines"/> line its replacement goes through, so the
/// replacement fades, greys or lightens with the original.
/// </summary>
/// <remarks>
/// A pixel's own colour is the asset's colour in the palette it was drawn with (<c>ScreenOwners.OwnColorsOf</c>); its
/// shown colour is the screen's where the asset still owns the pixel, else the current palette's colour of the asset's
/// own index (what the pixel would show uncovered), so a pointer or a box passing over the drawing does not change it.
/// </remarks>
public sealed class PaletteEffect(ColorLines lines, int[] lineOf)
{
    public ColorLines Lines => lines;

    /// <summary>Per asset pixel, the index of its line; -1 where the replacement keeps its colours.</summary>
    public int[] LineOf => lineOf;

    /// <summary>The colour a replacement pixel over asset pixel <paramref name="texel"/> shows.</summary>
    public uint Apply(int texel, uint argb) => Apply(lines.Tables, lineOf[texel], argb);

    internal static uint Apply(IReadOnlyList<byte[]> tables, int line, uint argb)
    {
        if (line < 0)
        {
            return argb;
        }
        var table = tables[line];
        return (argb & 0xFF000000u) | (uint)table[(argb >> 16) & 0xFF] << 16 | (uint)table[256 + ((argb >> 8) & 0xFF)] << 8 | table[512 + (argb & 0xFF)];
    }
}
