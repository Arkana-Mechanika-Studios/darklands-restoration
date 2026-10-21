namespace Darklands.Launcher.Drawing;

/// <summary>An ARGB picture the launcher draws into (opaque; every drawing blends onto it).</summary>
internal sealed class Canvas(int width, int height)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public uint[] Pixels { get; } = new uint[width * height];

    public void Clear(uint colour) => Array.Fill(Pixels, colour | 0xFF000000);

    public void Fill(Rect r, uint colour)
    {
        var c = Clip(r);
        for (var y = c.Y; y < c.Bottom; y++)
        {
            Pixels.AsSpan(y * Width + c.X, c.Width).Fill(colour | 0xFF000000);
        }
    }

    /// <summary>A border <paramref name="thickness"/> pixels wide inside <paramref name="r"/>.</summary>
    public void Frame(Rect r, int thickness, uint colour)
    {
        Fill(new Rect(r.X, r.Y, r.Width, thickness), colour);
        Fill(new Rect(r.X, r.Bottom - thickness, r.Width, thickness), colour);
        Fill(new Rect(r.X, r.Y, thickness, r.Height), colour);
        Fill(new Rect(r.Right - thickness, r.Y, thickness, r.Height), colour);
    }

    /// <summary>Fills <paramref name="r"/> with a colour for each pixel (x, y relative to the rectangle).</summary>
    public void Paint(Rect r, Func<int, int, uint> colourAt)
    {
        var c = Clip(r);
        for (var y = c.Y; y < c.Bottom; y++)
        {
            var row = y * Width;
            for (var x = c.X; x < c.Right; x++)
            {
                Pixels[row + x] = colourAt(x - r.X, y - r.Y) | 0xFF000000;
            }
        }
    }

    /// <summary>Blends <paramref name="colour"/> at <paramref name="alpha"/> (0..255) onto one pixel.</summary>
    public void Blend(int x, int y, uint colour, int alpha)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || alpha <= 0)
        {
            return;
        }
        ref var p = ref Pixels[y * Width + x];
        p = alpha >= 255 ? colour | 0xFF000000 : Colours.Mix(p, colour, alpha);
    }

    /// <summary>Draws an ARGB image scaled into <paramref name="target"/> (bilinear), at <paramref name="opacity"/> (0..1).</summary>
    public void Image(Image image, Rect target, double opacity = 1)
    {
        var c = Clip(target);
        var sx = (double)image.Width / target.Width;
        var sy = (double)image.Height / target.Height;
        for (var y = c.Y; y < c.Bottom; y++)
        {
            var fy = Math.Clamp((y - target.Y + 0.5) * sy - 0.5, 0, image.Height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, image.Height - 1);
            var ty = fy - y0;
            for (var x = c.X; x < c.Right; x++)
            {
                var fx = Math.Clamp((x - target.X + 0.5) * sx - 0.5, 0, image.Width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, image.Width - 1);
                var tx = fx - x0;
                var (a, r, g, b) = Bilinear(image, x0, x1, y0, y1, tx, ty);
                Blend(x, y, (uint)r << 16 | (uint)g << 8 | (uint)b, (int)Math.Round(a * opacity));
            }
        }
    }

    // premultiplied, so transparent pixels do not darken the edges
    private static (int A, int R, int G, int B) Bilinear(Image image, int x0, int x1, int y0, int y1, double tx, double ty)
    {
        double a = 0, r = 0, g = 0, b = 0;
        void Add(uint p, double w)
        {
            var pa = (p >> 24) * w;
            a += pa;
            r += ((p >> 16) & 0xFF) * pa;
            g += ((p >> 8) & 0xFF) * pa;
            b += (p & 0xFF) * pa;
        }
        Add(image.At(x0, y0), (1 - tx) * (1 - ty));
        Add(image.At(x1, y0), tx * (1 - ty));
        Add(image.At(x0, y1), (1 - tx) * ty);
        Add(image.At(x1, y1), tx * ty);
        return a <= 0 ? (0, 0, 0, 0) : ((int)Math.Round(a), (int)Math.Round(r / a), (int)Math.Round(g / a), (int)Math.Round(b / a));
    }

    private Rect Clip(Rect r)
    {
        var x = Math.Max(0, r.X);
        var y = Math.Max(0, r.Y);
        var right = Math.Min(Width, r.Right);
        var bottom = Math.Min(Height, r.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}

/// <summary>A rectangle in pixels.</summary>
internal readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public Rect Inset(int by) => new(X + by, Y + by, Math.Max(0, Width - 2 * by), Math.Max(0, Height - 2 * by));
}

/// <summary>An ARGB image.</summary>
internal sealed class Image(int width, int height, uint[] argb)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public uint At(int x, int y) => argb[y * Width + x];
}

internal static class Colours
{
    /// <summary><paramref name="under"/> with <paramref name="over"/> laid on it at <paramref name="alpha"/> (0..255).</summary>
    public static uint Mix(uint under, uint over, int alpha)
    {
        var a = (uint)Math.Clamp(alpha, 0, 255);
        var inv = 255u - a;
        var r = (((under >> 16) & 0xFF) * inv + ((over >> 16) & 0xFF) * a + 127) / 255;
        var g = (((under >> 8) & 0xFF) * inv + ((over >> 8) & 0xFF) * a + 127) / 255;
        var b = ((under & 0xFF) * inv + (over & 0xFF) * a + 127) / 255;
        return 0xFF000000 | r << 16 | g << 8 | b;
    }

    /// <summary>A colour moved towards white (positive) or black (negative) by <paramref name="amount"/> (0..1).</summary>
    public static uint Shade(uint colour, double amount) =>
        Mix(colour, amount >= 0 ? 0xFFFFFFu : 0x000000u, (int)Math.Round(Math.Abs(amount) * 255));
}
