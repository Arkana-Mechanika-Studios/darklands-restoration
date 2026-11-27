using Darklands.Core;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The towns' dots on the party information screen's small map (OriginalBehaviour.FixPartyInfoTownHover): the dots are part of the
/// screen's picture (pics\ptystats.pic), drawn by hand, so they are found in it rather than computed. A dot is a lone red pixel or,
/// for the larger towns, a ring of eight red pixels round a pixel of another colour. The towns of the location list are matched one
/// to one to the dots: placed with the party marker's scale, given the dots that make the least total squared distance, the scale
/// refitted to those dots by least squares, and matched again until the match stays (two passes with the game's picture).
/// </summary>
public static class SmallMapDots
{
    /// <summary>The dots' colour in the picture's palette (red).</summary>
    public const byte DotColor = 12;

    /// <summary>How far from a dot's centre the tip of the hand still reveals its town, in pixels.</summary>
    public const int Reach = 4;

    /// <summary>
    /// The tip of the hand pointer (the game's pointer, its hot spot at (0, 0) on this screen) from the mouse position: its leftmost
    /// pixels are rows 1 and 2 of the picture.
    /// </summary>
    public const int TipDx = 0, TipDy = 1;

    /// <summary>The towns the screen's pointer looks among: the leading towns of the location list (it stops at the first non-town).</summary>
    public static List<(int X, int Y)> Towns(GameState state)
    {
        var towns = new List<(int X, int Y)>();
        // as PartyStanding.NearestTown counts them
        for (var i = 0; i < state.Locations.Count && i < 414 && state.Locations[i].Icon == 0; i++)
        {
            towns.Add((state.Locations[i].X, state.Locations[i].Y));
        }
        return towns;
    }

    /// <summary>
    /// The dots in a window of the canvas (exclusive bounds, as the screen's map test), in reading order: lone pixels of
    /// <see cref="DotColor"/> and the centres of its 3 × 3 rings.
    /// </summary>
    public static List<(int X, int Y)> FindDots(Canvas canvas, int left, int top, int right, int bottom)
    {
        bool Red(int x, int y) => x > left && x < right && y > top && y < bottom && canvas[x, y] == DotColor;
        var dots = new List<(int X, int Y)>();
        for (var y = top + 1; y < bottom; y++)
        {
            for (var x = left + 1; x < right; x++)
            {
                var around = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if ((dx != 0 || dy != 0) && Red(x + dx, y + dy))
                        {
                            around++;
                        }
                    }
                }
                if (Red(x, y) ? around == 0 : around == 8)
                {
                    dots.Add((x, y));
                }
            }
        }
        return dots;
    }

    /// <summary>
    /// Each town's dot (screen pixels), in the towns' order: the match described on the class. With fewer dots than towns the towns
    /// left over take their place by the fitted scale.
    /// </summary>
    /// <param name="towns">The towns' world positions.</param>
    /// <param name="dots">The dots found (<see cref="FindDots"/>).</param>
    /// <param name="scaleX">The first guess of the world-to-screen scale: screen x = x × scaleX + offsetX (the party marker's).</param>
    public static (int X, int Y)[] Match(IReadOnlyList<(int X, int Y)> towns, IReadOnlyList<(int X, int Y)> dots,
        double scaleX, double offsetX, double scaleY, double offsetY)
    {
        var result = new (int X, int Y)[towns.Count];
        if (towns.Count == 0)
        {
            return result;
        }
        int[]? match = null;
        for (var pass = 0; pass < 10 && dots.Count > 0; pass++)
        {
            var next = Assign(towns, dots, scaleX, offsetX, scaleY, offsetY);
            var pairs = Enumerable.Range(0, towns.Count).Where(t => next[t] >= 0).ToList();
            if (pairs.Count >= 2)
            {
                (scaleX, offsetX) = Fit(pairs.Select(t => (double)towns[t].X), pairs.Select(t => (double)dots[next[t]].X), scaleX, offsetX);
                (scaleY, offsetY) = Fit(pairs.Select(t => (double)towns[t].Y), pairs.Select(t => (double)dots[next[t]].Y), scaleY, offsetY);
            }
            var same = match is not null && match.AsSpan().SequenceEqual(next);
            match = next;
            if (same)
            {
                break;
            }
        }
        for (var t = 0; t < towns.Count; t++)
        {
            result[t] = match is not null && match[t] >= 0
                ? dots[match[t]]
                : ((int)Math.Round(towns[t].X * scaleX + offsetX), (int)Math.Round(towns[t].Y * scaleY + offsetY));
        }
        return result;
    }

    /// <summary>The town whose dot is nearest the point, if within <see cref="Reach"/> (the first such town on a tie), or −1.</summary>
    public static int TownAt(IReadOnlyList<(int X, int Y)> townDots, int x, int y)
    {
        var best = -1;
        var bestDistance = Reach * Reach + 1;
        for (var t = 0; t < townDots.Count; t++)
        {
            var dx = townDots[t].X - x;
            var dy = townDots[t].Y - y;
            var d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = t;
            }
        }
        return best;
    }

    /// <summary>
    /// The one-to-one match of towns to dots of the least total squared distance (the Hungarian method; with more towns than dots the
    /// towns left over get −1).
    /// </summary>
    private static int[] Assign(IReadOnlyList<(int X, int Y)> towns, IReadOnlyList<(int X, int Y)> dots,
        double scaleX, double offsetX, double scaleY, double offsetY)
    {
        // rows: the smaller side, so every row is matched
        var townsAreRows = towns.Count <= dots.Count;
        var n = townsAreRows ? towns.Count : dots.Count;
        var m = townsAreRows ? dots.Count : towns.Count;
        double Cost(int row, int col)
        {
            var (t, d) = townsAreRows ? (row, col) : (col, row);
            var dx = towns[t].X * scaleX + offsetX - dots[d].X;
            var dy = towns[t].Y * scaleY + offsetY - dots[d].Y;
            return dx * dx + dy * dy;
        }
        var u = new double[n + 1];
        var v = new double[m + 1];
        var p = new int[m + 1];
        var way = new int[m + 1];
        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = Enumerable.Repeat(double.PositiveInfinity, m + 1).ToArray();
            var used = new bool[m + 1];
            do
            {
                used[j0] = true;
                int i0 = p[j0], j1 = 0;
                var delta = double.PositiveInfinity;
                for (var j = 1; j <= m; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }
                    var cur = Cost(i0 - 1, j - 1) - u[i0] - v[j];
                    if (cur < minv[j])
                    {
                        minv[j] = cur;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }
                for (var j = 0; j <= m; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }
                j0 = j1;
            }
            while (p[j0] != 0);
            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }
        var townDot = Enumerable.Repeat(-1, towns.Count).ToArray();
        for (var j = 1; j <= m; j++)
        {
            if (p[j] != 0)
            {
                if (townsAreRows)
                {
                    townDot[p[j] - 1] = j - 1;
                }
                else
                {
                    townDot[j - 1] = p[j] - 1;
                }
            }
        }
        return townDot;
    }

    /// <summary>The least-squares line through the pairs (the old one if the towns all stand in one column or row).</summary>
    private static (double Scale, double Offset) Fit(IEnumerable<double> world, IEnumerable<double> screen, double oldScale, double oldOffset)
    {
        var w = world.ToArray();
        var s = screen.ToArray();
        var mw = w.Average();
        var ms = s.Average();
        double num = 0, den = 0;
        for (var i = 0; i < w.Length; i++)
        {
            num += (w[i] - mw) * (s[i] - ms);
            den += (w[i] - mw) * (w[i] - mw);
        }
        return den == 0 ? (oldScale, oldOffset) : (num / den, ms - num / den * mw);
    }
}
