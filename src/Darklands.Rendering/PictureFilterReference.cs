using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// The picture filters drawn on the CPU: what each <see cref="PictureFilter"/> must look like, independent of any graphics
/// library. The host's renderers (the GPU renderer's pixel shader, and render targets with blend modes elsewhere) are checked
/// against it (Darklands.Host.Sdl.Tests.PictureFilterTests), and its renders let a person judge and tune the looks without a
/// window. Slow (about a second a frame at 1920x1080): for tests and reference pictures, never for playing.
/// New code (presentation only, outside the game).
/// </summary>
public static class PictureFilterReference
{
    /// <summary>
    /// The picture <paramref name="argb"/> (<paramref name="sourceWidth"/> x <paramref name="sourceHeight"/>, opaque) shown at
    /// <paramref name="width"/> x <paramref name="height"/> output pixels through <paramref name="filter"/>, sampled at each
    /// output pixel's centre: sharp takes the nearest picture pixel; smooth blends, as a bilinear step does, the picture
    /// enlarged by <see cref="PictureFilterMath.Prescale"/>'s whole factors; the CRT is smooth times the scanline profile, the
    /// aperture mask and the gain (<see cref="PictureFilterMath.CrtFactors"/>), clamped to white. <paramref name="curvature"/>
    /// (the CRT only; 0: none) bends the picture: the point seen at an output pixel moves out from the middle by
    /// d |d|² x curvature (d: the pixel's place from the middle, in picture widths and heights); what falls outside is black.
    /// </summary>
    public static uint[] Render(ReadOnlySpan<uint> argb, int sourceWidth, int sourceHeight, PictureFilter filter, int width, int height,
        double curvature = 0)
    {
        var source = argb.ToArray();
        var output = new uint[width * height];
        var (kx, ky) = PictureFilterMath.Prescale(width, height, sourceWidth, sourceHeight);
        var crt = filter == PictureFilter.Crt;
        var bend = crt ? curvature : 0;
        var rowHeight = height / (double)sourceHeight;
        var depth = PictureFilterMath.ScanlineDepthFor(rowHeight);
        var gain = PictureFilterMath.CrtGain(depth);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                if (bend > 0)
                {
                    var dx = px / width - 0.5;
                    var dy = py / height - 0.5;
                    var r2 = dx * dx + dy * dy;
                    px = (px / width + dx * r2 * bend) * width;
                    py = (py / height + dy * r2 * bend) * height;
                    if (px < 0 || py < 0 || px > width || py > height)
                    {
                        output[y * width + x] = 0xFF000000;
                        continue;
                    }
                }
                var (c0, c1, fx) = Tap(px, width, sourceWidth, kx, filter == PictureFilter.Sharp);
                var (r0, r1, fy) = Tap(py, height, sourceHeight, ky, filter == PictureFilter.Sharp);
                var scanline = 1.0;
                if (crt)
                {
                    var v = py / rowHeight;
                    scanline = 1 - depth * (1 + Math.Cos(2 * Math.PI * (v - Math.Floor(v)))) / 2;
                }
                uint result = 0xFF000000;
                for (var c = 0; c < 3; c++)
                {
                    var shift = 16 - 8 * c;
                    double Channel(int row, int column) => source[row * sourceWidth + column] >> shift & 0xFF;
                    var top = Channel(r0, c0) * (1 - fx) + Channel(r0, c1) * fx;
                    var bottom = Channel(r1, c0) * (1 - fx) + Channel(r1, c1) * fx;
                    var value = top * (1 - fy) + bottom * fy;
                    if (crt)
                    {
                        value *= gain * scanline * (x % 3 == c ? 1 : 1 - PictureFilterMath.MaskStrength);
                    }
                    result |= (uint)Math.Clamp(Math.Round(value), 0, 255) << shift;
                }
                output[y * width + x] = result;
            }
        }
        return output;
    }

    /// <summary>
    /// At output position <paramref name="p"/> (pixels; a pixel's centre at i + 0.5) of <paramref name="size"/>: the two picture
    /// pixels blended and the weight of the second. Nearest: one pixel. Smooth: the bilinear taps in the picture enlarged k times
    /// (texel centres at i + 0.5, clamped at the edges), each enlarged texel being picture pixel i / k.
    /// </summary>
    private static (int First, int Second, double Weight) Tap(double p, int size, int source, int k, bool nearest)
    {
        if (nearest)
        {
            var s = Math.Clamp((int)(p * source / size), 0, source - 1);
            return (s, s, 0);
        }
        var enlarged = source * k;
        var t = p * enlarged / size - 0.5;
        var t0 = Math.Floor(t);
        var weight = t - t0;
        var a = (int)Math.Clamp(t0, 0, enlarged - 1) / k;
        var b = (int)Math.Clamp(t0 + 1, 0, enlarged - 1) / k;
        return (a, b, a == b ? 0 : weight);
    }
}
