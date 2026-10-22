namespace Darklands.Platform;

/// <summary>An integer rectangle in window pixels.</summary>
public readonly record struct DisplayRectangle(int X, int Y, int Width, int Height);

/// <summary>
/// Where a host puts the PC's picture. The window's base size is the VGA text mode's 720×400
/// (80×25 cells of 9×16): the start-up banner is shown at its own resolution. The 320×200
/// graphics mode is doubled as the VGA (and DOSBox) shows it, 640×400, and kept at that aspect
/// inside the window (centred, black at the sides). Larger windows scale both pictures by the
/// same rules: <c>--scale N</c> makes the window N×720 × N×400, <c>--resolution W×H</c> any size.
/// New code (host presentation, not a reconstruction).
/// </summary>
public static class DisplayLayout
{
    public const int BaseWidth = TextModeRaster.Width;
    public const int BaseHeight = TextModeRaster.Height;

    /// <summary>The window size for the options (neither: 720×400).</summary>
    public static (int Width, int Height) WindowSize(int? scale, (int Width, int Height)? resolution)
    {
        if (scale is not null && resolution is not null)
        {
            throw new ArgumentException("--scale and --resolution cannot be used together.");
        }
        if (scale is { } n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "--scale needs a positive whole number.");
            }
            return (BaseWidth * n, BaseHeight * n);
        }
        if (resolution is { } size)
        {
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), "--resolution needs WIDTHxHEIGHT.");
            }
            return size;
        }
        return (BaseWidth, BaseHeight);
    }

    /// <summary>Reads <c>WIDTHxHEIGHT</c> (for example 1440x800).</summary>
    public static (int Width, int Height) ParseResolution(string text)
    {
        var parts = text.Split('x', 'X');
        if (parts.Length != 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0)
        {
            throw new FormatException("--resolution needs WIDTHxHEIGHT with positive numbers, e.g. 1440x800.");
        }
        return (width, height);
    }

    /// <summary>The text screen (720×400) in a window of the given size.</summary>
    public static DisplayRectangle Text(int windowWidth, int windowHeight) =>
        Fit(TextModeRaster.Width, TextModeRaster.Height, windowWidth, windowHeight);

    /// <summary>The graphics screen (320×200, shown as 640×400) in a window of the given size.</summary>
    public static DisplayRectangle Graphics(int windowWidth, int windowHeight) =>
        Fit(Screen.Width * 2, Screen.Height * 2, windowWidth, windowHeight);

    /// <summary>The largest rectangle of the picture's aspect centred in the window.</summary>
    public static DisplayRectangle Fit(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        int width, height;
        if ((long)windowWidth * pictureHeight <= (long)windowHeight * pictureWidth)
        {
            width = windowWidth;
            height = (int)((long)windowWidth * pictureHeight / pictureWidth);
        }
        else
        {
            height = windowHeight;
            width = (int)((long)windowHeight * pictureWidth / pictureHeight);
        }
        return new DisplayRectangle((windowWidth - width) / 2, (windowHeight - height) / 2, width, height);
    }

    /// <summary>
    /// A window position (in the same pixels as the rectangle) to the game screen pixel (0..319, 0..199) shown there. A
    /// stretched picture shows at each window pixel the game pixel under that pixel's centre, so the position is taken
    /// at its centre too: at a scale that is not whole (1920x1080) its left edge would name the pixel before at every
    /// boundary. MouseMappingTests checks this against the picture SDL draws.
    /// </summary>
    public static (int X, int Y) ToScreen(DisplayRectangle graphics, int x, int y) =>
        (Math.Clamp(Under(x - graphics.X, graphics.Width, Screen.Width), 0, Screen.Width - 1),
         Math.Clamp(Under(y - graphics.Y, graphics.Height, Screen.Height), 0, Screen.Height - 1));

    // Exact arithmetic. A centre exactly on the line between two game pixels (2.5 window pixels per game pixel, and the
    // like) is a tie the GPU breaks by its own rounding, either way: it is taken as the pixel before. The game draws its
    // pointer where it reads the mouse, so a click still lands where the pointer is shown.
    internal static int Under(int offset, int shown, int pixels)
    {
        var position = (2L * offset + 1) * pixels;
        var span = 2L * Math.Max(1, shown);
        var under = Math.DivRem(position, span, out var rest);
        return (int)(rest == 0 || rest < 0 ? under - 1 : under);
    }

    /// <summary>True when a window position's centre is exactly on the line between two game pixels (see <see cref="ToScreen"/>).</summary>
    public static bool IsTie(int offset, int shown, int pixels) => (2L * offset + 1) * pixels % (2L * Math.Max(1, shown)) == 0;
}
