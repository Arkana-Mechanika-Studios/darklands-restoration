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
        (argb & 0xFF000000u) | Channel(argb, 16, Scale, Red) | Channel(argb, 8, Scale, Green) | Channel(argb, 0, Scale, Blue);

    private static uint Channel(uint argb, int shift, double scale, double offset) =>
        (uint)Math.Clamp((int)Math.Round(scale * ((argb >> shift) & 0xFF) + offset, MidpointRounding.AwayFromZero), 0, 255) << shift;
}

/// <summary>
/// What the palette has done to an asset's drawing: per pixel of the asset (screen orientation, as
/// <see cref="AssetPlacement.Owned"/>) the colour the asset is meant to show and the colour the screen shows instead,
/// and the few <see cref="ColorLine"/>s that turn the first into the second. A replacement goes through the same lines,
/// so it fades, greys or lightens with the original.
/// </summary>
/// <remarks>
/// The lines are found from the pairs (own colour, shown colour) of the changed pixels: one line fitted to all, then
/// the pair farthest from its line starts a new one and the pairs are shared out again, up to <see cref="MaxLines"/>
/// lines or until every pair lies within <see cref="Tolerance"/> of its line. A palette fade, which moves every colour
/// of a range the same part of the way to one colour, is one line and is reproduced exactly; changes that are not of
/// that form (colour cycling, a remap to unrelated colours) end on flat lines, the shown colour without the detail.
/// Deterministic: the pairs are taken in a fixed order.
/// </remarks>
public sealed class PaletteEffect
{
    public const int MaxLines = 4;

    /// <summary>How far (per channel, 0..255) a pair may lie from its line: the game's colours have 6 bits, so 4 is one step.</summary>
    public const double Tolerance = 6;

    private PaletteEffect(uint[] own, uint[] shown, ColorLine[] lines, int[] lineOf)
    {
        Own = own;
        Shown = shown;
        Lines = lines;
        LineOf = lineOf;
    }

    /// <summary>Per asset pixel, its own colour where the screen shows another (0: unchanged or not seen).</summary>
    public uint[] Own { get; }

    /// <summary>Per asset pixel, the colour the screen shows where it is not the own colour (0: unchanged or not seen).</summary>
    public uint[] Shown { get; }

    public IReadOnlyList<ColorLine> Lines { get; }

    /// <summary>Per asset pixel, the index of its line in <see cref="Lines"/>; -1 where the replacement keeps its colours.</summary>
    public int[] LineOf { get; }

    /// <summary>The colour a replacement pixel over asset pixel <paramref name="texel"/> shows.</summary>
    public uint Apply(int texel, uint argb) => LineOf[texel] < 0 ? argb : Lines[LineOf[texel]].Apply(argb);

    /// <summary>The same changes (so the same lines and the same replacement pixels).</summary>
    public bool SameAs(PaletteEffect? other) =>
        other is not null && Own.AsSpan().SequenceEqual(other.Own) && Shown.AsSpan().SequenceEqual(other.Shown);

    /// <summary>
    /// The effect of the changed pixels (<paramref name="shown"/> non-zero) of a drawing <paramref name="width"/> pixels
    /// wide, with its lines. An unchanged owned pixel among mostly changed ones (more than half of the owned pixels within
    /// two of it) takes their most frequent line: in a faded picture its outline and shadow colours, which the fade leaves
    /// alone, do not show the replacement unfaded.
    /// </summary>
    public static PaletteEffect Fit(uint[] own, uint[] shown, bool[] owned, int width)
    {
        var counts = new Dictionary<(uint Own, uint Shown), int>();
        for (var t = 0; t < shown.Length; t++)
        {
            if (shown[t] != 0)
            {
                counts[(own[t], shown[t])] = counts.GetValueOrDefault((own[t], shown[t])) + 1;
            }
        }
        var pairs = counts.OrderBy(p => p.Key.Own).ThenBy(p => p.Key.Shown).ToArray();
        var lines = new List<ColorLine>();
        var lineOfPair = new int[pairs.Length];
        if (pairs.Length > 0)
        {
            lines.Add(FitLine(pairs, lineOfPair, -1));
            while (true)
            {
                for (var round = 0; round < 10; round++)
                {
                    var changed = false;
                    for (var p = 0; p < pairs.Length; p++)
                    {
                        var best = 0;
                        for (var l = 1; l < lines.Count; l++)
                        {
                            if (Residual(pairs[p].Key, lines[l]) < Residual(pairs[p].Key, lines[best]))
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
                for (var p = 0; p < pairs.Length; p++)
                {
                    var residual = Residual(pairs[p].Key, lines[lineOfPair[p]]);
                    if (residual > worstResidual)
                    {
                        (worst, worstResidual) = (p, residual);
                    }
                }
                if (worst < 0 || lines.Count == MaxLines)
                {
                    break;
                }
                var shownColor = pairs[worst].Key.Shown;
                lines.Add(new ColorLine(0, (shownColor >> 16) & 0xFF, (shownColor >> 8) & 0xFF, shownColor & 0xFF));
            }
        }
        var index = new Dictionary<(uint, uint), int>();
        for (var p = 0; p < pairs.Length; p++)
        {
            index[pairs[p].Key] = lineOfPair[p];
        }
        var changedLine = new int[shown.Length];
        for (var t = 0; t < shown.Length; t++)
        {
            changedLine[t] = shown[t] == 0 ? -1 : index[(own[t], shown[t])];
        }
        var lineOf = (int[])changedLine.Clone();
        var height = shown.Length / width;
        Span<int> votes = stackalloc int[MaxLines];
        for (var t = 0; t < shown.Length; t++)
        {
            if (changedLine[t] >= 0 || !owned[t])
            {
                continue;
            }
            votes.Clear();
            var (ownedNear, changedNear) = (0, 0);
            var (x, y) = (t % width, t / width);
            for (var ny = Math.Max(0, y - 2); ny <= Math.Min(height - 1, y + 2); ny++)
            {
                for (var nx = Math.Max(0, x - 2); nx <= Math.Min(width - 1, x + 2); nx++)
                {
                    var n = ny * width + nx;
                    if (owned[n])
                    {
                        ownedNear++;
                        if (changedLine[n] >= 0)
                        {
                            changedNear++;
                            votes[changedLine[n]]++;
                        }
                    }
                }
            }
            if (changedNear * 2 > ownedNear)
            {
                var best = 0;
                for (var l = 1; l < MaxLines; l++)
                {
                    if (votes[l] > votes[best])
                    {
                        best = l;
                    }
                }
                lineOf[t] = best;
            }
        }
        return new PaletteEffect(own, shown, [.. lines], lineOf);
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
    /// The line through the pairs of line <paramref name="line"/> (all pairs for -1), weighted by their pixel counts: one
    /// scale for the three channels (least squares, kept within 0..1), an offset per channel.
    /// </summary>
    private static ColorLine FitLine(KeyValuePair<(uint Own, uint Shown), int>[] pairs, int[] lineOfPair, int line)
    {
        double n = 0;
        Span<double> ownMean = stackalloc double[3];
        Span<double> shownMean = stackalloc double[3];
        for (var p = 0; p < pairs.Length; p++)
        {
            if (line >= 0 && lineOfPair[p] != line)
            {
                continue;
            }
            n += pairs[p].Value;
            for (var c = 0; c < 3; c++)
            {
                ownMean[c] += pairs[p].Value * Channel(pairs[p].Key.Own, c);
                shownMean[c] += pairs[p].Value * Channel(pairs[p].Key.Shown, c);
            }
        }
        for (var c = 0; c < 3; c++)
        {
            ownMean[c] /= n;
            shownMean[c] /= n;
        }
        double covariance = 0, variance = 0;
        for (var p = 0; p < pairs.Length; p++)
        {
            if (line >= 0 && lineOfPair[p] != line)
            {
                continue;
            }
            for (var c = 0; c < 3; c++)
            {
                var o = Channel(pairs[p].Key.Own, c) - ownMean[c];
                covariance += pairs[p].Value * o * (Channel(pairs[p].Key.Shown, c) - shownMean[c]);
                variance += pairs[p].Value * o * o;
            }
        }
        var scale = variance > 1e-9 ? Math.Clamp(covariance / variance, 0, 1) : 0;
        return new ColorLine(scale, shownMean[0] - scale * ownMean[0], shownMean[1] - scale * ownMean[1], shownMean[2] - scale * ownMean[2]);
    }
}
