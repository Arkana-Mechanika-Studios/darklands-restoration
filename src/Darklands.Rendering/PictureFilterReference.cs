using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// The picture filters drawn on the CPU: what each <see cref="PictureFilter"/> must look like, independent of any graphics
/// library. A host's renderer (SDL2's render targets and blend modes today, SDL3's pixel shaders later) is checked against it
/// (Darklands.Host.Sdl.Tests.PictureFilterTests), and its renders let a person judge and tune the looks without a window.
/// Slow (a few hundred milliseconds a frame at 1920x1080): for tests and reference pictures, never for playing.
/// New code (presentation only, outside the game).
/// </summary>
public static class PictureFilterReference
{
    /// <summary>
    /// The picture <paramref name="argb"/> (<paramref name="sourceWidth"/> x <paramref name="sourceHeight"/>, opaque) shown at
    /// <paramref name="width"/> x <paramref name="height"/> output pixels through <paramref name="filter"/>, sampled at each
    /// output pixel's centre: sharp takes the nearest picture pixel; smooth blends, as a bilinear step does, the picture
    /// enlarged by <see cref="PictureFilterMath.Prescale"/>'s whole factors; the CRT is smooth times
    /// <see cref="PictureFilterMath.CrtFactors"/>, clamped to white.
    /// </summary>
    public static uint[] Render(ReadOnlySpan<uint> argb, int sourceWidth, int sourceHeight, PictureFilter filter, int width, int height)
    {
        var output = new uint[width * height];
        var source = argb.ToArray();
        var (kx, ky) = PictureFilterMath.Prescale(width, height, sourceWidth, sourceHeight);
        var factors = filter == PictureFilter.Crt ? PictureFilterMath.CrtFactors(width, height, sourceHeight) : null;
        var columns = Taps(width, sourceWidth, kx, filter == PictureFilter.Sharp);
        var rows = Taps(height, sourceHeight, ky, filter == PictureFilter.Sharp);
        for (var y = 0; y < height; y++)
        {
            var (r0, r1, fy) = rows[y];
            for (var x = 0; x < width; x++)
            {
                var (c0, c1, fx) = columns[x];
                var p = y * width + x;
                uint result = 0xFF000000;
                for (var c = 0; c < 3; c++)
                {
                    var shift = 16 - 8 * c;
                    double Channel(int row, int column) => source[row * sourceWidth + column] >> shift & 0xFF;
                    var top = Channel(r0, c0) * (1 - fx) + Channel(r0, c1) * fx;
                    var bottom = Channel(r1, c0) * (1 - fx) + Channel(r1, c1) * fx;
                    var value = top * (1 - fy) + bottom * fy;
                    if (factors is not null)
                    {
                        value *= factors[p * 3 + c];
                    }
                    result |= (uint)Math.Clamp(Math.Round(value), 0, 255) << shift;
                }
                output[p] = result;
            }
        }
        return output;
    }

    /// <summary>
    /// For each output column (or row): the two picture pixels it blends and the weight of the second. Nearest: one pixel.
    /// Smooth: the bilinear taps in the picture enlarged k times (texel centres at i + 0.5, clamped at the edges), each
    /// enlarged texel being picture pixel i / k.
    /// </summary>
    private static (int First, int Second, double Weight)[] Taps(int size, int source, int k, bool nearest)
    {
        var taps = new (int, int, double)[size];
        var enlarged = source * k;
        for (var i = 0; i < size; i++)
        {
            if (nearest)
            {
                var s = Math.Min(source - 1, (int)((i + 0.5) * source / size));
                taps[i] = (s, s, 0);
                continue;
            }
            var t = (i + 0.5) * enlarged / size - 0.5;
            var t0 = (int)Math.Floor(t);
            var weight = t - t0;
            var a = Math.Clamp(t0, 0, enlarged - 1) / k;
            var b = Math.Clamp(t0 + 1, 0, enlarged - 1) / k;
            taps[i] = (a, b, a == b ? 0 : weight);
        }
        return taps;
    }
}
