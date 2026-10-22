namespace Darklands.Rendering;

/// <summary>
/// Draws an <see cref="HdFrame"/> into one picture in software: every item scaled to its destination by the nearest
/// pixel and blended by its alpha. For hosts without a graphics card (the headless host's HD shots) and for tests;
/// deterministic, so its pictures can be compared.
/// </summary>
public static class FrameCompositor
{
    /// <summary>The frame on a picture of the given size, black where no item draws.</summary>
    public static RgbaImage Compose(HdFrame frame, int width, int height)
    {
        var target = new RgbaImage(width, height);
        Array.Fill(target.Argb, 0xFF000000u);
        foreach (var item in frame.Items)
        {
            Draw(target, item);
        }
        return target;
    }

    /// <summary>One item over the picture.</summary>
    public static void Draw(RgbaImage target, DrawItem item)
    {
        var (texture, d) = (item.Texture, item.Destination);
        if (d.Width <= 0 || d.Height <= 0 || texture.Width == 0 || texture.Height == 0)
        {
            return;
        }
        for (var y = Math.Max(0, d.Y); y < Math.Min(target.Height, d.Y + d.Height); y++)
        {
            var sy = (int)((long)(y - d.Y) * texture.Height / d.Height);
            for (var x = Math.Max(0, d.X); x < Math.Min(target.Width, d.X + d.Width); x++)
            {
                var sx = (int)((long)(x - d.X) * texture.Width / d.Width);
                ref var under = ref target.Argb[y * target.Width + x];
                under = Over(texture.Argb[sy * texture.Width + sx], under);
            }
        }
    }

    /// <summary>A colour blended by its alpha over an opaque one (integer, rounded).</summary>
    public static uint Over(uint top, uint under)
    {
        var alpha = top >> 24;
        if (alpha == 255)
        {
            return top;
        }
        if (alpha == 0)
        {
            return under;
        }
        uint Channel(int shift) => (((top >> shift) & 0xFF) * alpha + ((under >> shift) & 0xFF) * (255 - alpha) + 127) / 255;
        return 0xFF000000u | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    /// <summary>Division rounded down (also for negative numbers).</summary>
    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }
}
