namespace Darklands.Launcher.Drawing;

/// <summary>Scaling (with a box filter when it shrinks), mirroring and three-slice drawing of the launcher's art.</summary>
internal static class ImageOps
{
    /// <summary>
    /// The part (<paramref name="sx"/>, <paramref name="sy"/>, <paramref name="sw"/>, <paramref name="sh"/>) of <paramref name="source"/>
    /// at <paramref name="width"/> x <paramref name="height"/>: each pixel the average of the source pixels it covers (premultiplied),
    /// so a large painting shrinks without shimmering; enlarging samples bilinearly.
    /// </summary>
    public static Image Resized(Image source, double sx, double sy, double sw, double sh, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var argb = new uint[width * height];
        var fx = sw / width;
        var fy = sh / height;
        for (var y = 0; y < height; y++)
        {
            var y0 = sy + y * fy;
            for (var x = 0; x < width; x++)
            {
                var x0 = sx + x * fx;
                argb[y * width + x] = fx <= 1 && fy <= 1
                    ? Sample(source, x0 + fx / 2 - 0.5, y0 + fy / 2 - 0.5)
                    : Average(source, x0, y0, x0 + fx, y0 + fy);
            }
        }
        return new Image(width, height, argb);
    }

    private static uint Average(Image s, double x0, double y0, double x1, double y1)
    {
        int ix0 = Math.Clamp((int)x0, 0, s.Width - 1), iy0 = Math.Clamp((int)y0, 0, s.Height - 1);
        int ix1 = Math.Clamp((int)Math.Ceiling(x1), ix0 + 1, s.Width), iy1 = Math.Clamp((int)Math.Ceiling(y1), iy0 + 1, s.Height);
        double a = 0, r = 0, g = 0, b = 0, n = 0;
        for (var y = iy0; y < iy1; y++)
        {
            for (var x = ix0; x < ix1; x++)
            {
                var p = s.At(x, y);
                var pa = (double)(p >> 24);
                a += pa;
                r += ((p >> 16) & 0xFF) * pa;
                g += ((p >> 8) & 0xFF) * pa;
                b += (p & 0xFF) * pa;
                n++;
            }
        }
        return a <= 0 ? 0 : Pack(a / n, r / a, g / a, b / a);
    }

    private static uint Sample(Image s, double fx, double fy)
    {
        fx = Math.Clamp(fx, 0, s.Width - 1);
        fy = Math.Clamp(fy, 0, s.Height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, s.Width - 1), y1 = Math.Min(y0 + 1, s.Height - 1);
        double tx = fx - x0, ty = fy - y0;
        double a = 0, r = 0, g = 0, b = 0;
        void Add(uint p, double w)
        {
            var pa = (p >> 24) * w;
            a += pa;
            r += ((p >> 16) & 0xFF) * pa;
            g += ((p >> 8) & 0xFF) * pa;
            b += (p & 0xFF) * pa;
        }
        Add(s.At(x0, y0), (1 - tx) * (1 - ty));
        Add(s.At(x1, y0), tx * (1 - ty));
        Add(s.At(x0, y1), (1 - tx) * ty);
        Add(s.At(x1, y1), tx * ty);
        return a <= 0 ? 0 : Pack(a, r / a, g / a, b / a);
    }

    private static uint Pack(double a, double r, double g, double b) =>
        (uint)Math.Clamp(Math.Round(a), 0, 255) << 24 | (uint)Math.Clamp(Math.Round(r), 0, 255) << 16
        | (uint)Math.Clamp(Math.Round(g), 0, 255) << 8 | (uint)Math.Clamp(Math.Round(b), 0, 255);

    /// <summary>The picture scaled to <paramref name="target"/> and drawn there (shrunk with the box filter).</summary>
    public static void Draw(Canvas canvas, Image image, Rect target, double opacity = 1)
    {
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }
        var scaled = target.Width == image.Width && target.Height == image.Height ? image
            : Resized(image, 0, 0, image.Width, image.Height, target.Width, target.Height);
        canvas.Image(scaled, target, opacity);
    }

    /// <summary>Mirrored left to right (<paramref name="horizontal"/>) and/or top to bottom.</summary>
    public static Image Mirrored(Image image, bool horizontal, bool vertical)
    {
        var argb = new uint[image.Width * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                argb[y * image.Width + x] = image.At(horizontal ? image.Width - 1 - x : x, vertical ? image.Height - 1 - y : y);
            }
        }
        return new Image(image.Width, image.Height, argb);
    }

    /// <summary>
    /// A picture drawn in nine slices into <paramref name="target"/>: its corners (borders <paramref name="slice"/>, in its pixels)
    /// at <paramref name="scale"/> target pixels per picture pixel, its edges stretched along, its centre stretched both ways.
    /// Borders too large for the target shrink together.
    /// </summary>
    public static void NineSlice(Canvas canvas, Image image, Rect target, (int Left, int Top, int Right, int Bottom) slice, double scale)
    {
        var fit = Math.Min(1, Math.Min((double)target.Width / Math.Max(1, (slice.Left + slice.Right) * scale), (double)target.Height / Math.Max(1, (slice.Top + slice.Bottom) * scale)));
        int l = (int)Math.Round(slice.Left * scale * fit), r = (int)Math.Round(slice.Right * scale * fit);
        int t = (int)Math.Round(slice.Top * scale * fit), b = (int)Math.Round(slice.Bottom * scale * fit);
        int[] sx = [0, slice.Left, image.Width - slice.Right, image.Width];
        int[] sy = [0, slice.Top, image.Height - slice.Bottom, image.Height];
        int[] tx = [target.X, target.X + l, target.Right - r, target.Right];
        int[] ty = [target.Y, target.Y + t, target.Bottom - b, target.Bottom];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                int w = tx[col + 1] - tx[col], h = ty[row + 1] - ty[row];
                if (w <= 0 || h <= 0 || sx[col + 1] <= sx[col] || sy[row + 1] <= sy[row])
                {
                    continue;
                }
                canvas.Image(Resized(image, sx[col], sy[row], sx[col + 1] - sx[col], sy[row + 1] - sy[row], w, h), new Rect(tx[col], ty[row], w, h));
            }
        }
    }

    /// <summary>
    /// A plate in three slices: its ends (<paramref name="left"/> and <paramref name="right"/> picture pixels wide; each as wide as
    /// the picture is high when not given) keep their shape at the target's height, the middle stretches.
    /// </summary>
    public static void ThreeSlice(Canvas canvas, Image image, Rect target, int left, int right)
    {
        var k = (double)target.Height / image.Height;
        var fit = Math.Min(1, target.Width / Math.Max(1, (left + right) * k));
        var lw = (int)Math.Round(left * k * fit);
        var rw = (int)Math.Round(right * k * fit);
        if (lw > 0)
        {
            canvas.Image(Resized(image, 0, 0, left, image.Height, lw, target.Height), new Rect(target.X, target.Y, lw, target.Height));
        }
        if (rw > 0)
        {
            canvas.Image(Resized(image, image.Width - right, 0, right, image.Height, rw, target.Height), new Rect(target.Right - rw, target.Y, rw, target.Height));
        }
        var middle = target.Width - lw - rw;
        if (middle > 0 && image.Width - left - right > 0)
        {
            canvas.Image(Resized(image, left, 0, image.Width - left - right, image.Height, middle, target.Height), new Rect(target.X + lw, target.Y, middle, target.Height));
        }
    }

    /// <summary>
    /// A plate drawn in three slices: its left and right ends (each as wide as the picture is high) kept in shape, the middle
    /// stretched to <paramref name="target"/>'s width.
    /// </summary>
    public static void ThreeSlice(Canvas canvas, Image image, Rect target)
    {
        var cap = Math.Min(image.Height, image.Width / 2);
        var capWidth = Math.Min(target.Width / 2, (int)Math.Round((double)cap * target.Height / image.Height));
        canvas.Image(Resized(image, 0, 0, cap, image.Height, capWidth, target.Height), new Rect(target.X, target.Y, capWidth, target.Height));
        canvas.Image(Resized(image, image.Width - cap, 0, cap, image.Height, capWidth, target.Height), new Rect(target.Right - capWidth, target.Y, capWidth, target.Height));
        var middle = target.Width - 2 * capWidth;
        if (middle > 0)
        {
            canvas.Image(Resized(image, cap, 0, image.Width - 2 * cap, image.Height, middle, target.Height), new Rect(target.X + capWidth, target.Y, middle, target.Height));
        }
    }
}
