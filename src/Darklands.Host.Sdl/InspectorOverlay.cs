using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// The developer's inspector over the window (developer builds only: Program.cs sets <see cref="SdlHost.Inspector"/> under
/// DEV_TOOLS): while F9 is held, a frame round the game pixel under the pointer and a box with what drew it; Ctrl+F9 pins the box
/// at that pixel (and puts its lines on the clipboard), Ctrl+F9 again lets it go. Drawn at the window's own resolution like the
/// console, never into the game's picture: the frame's pixels and planes are only read.
/// </summary>
// Not in the original: a development aid (local post-1.0 design note, the dev-only inspector, step I-2).
internal sealed class InspectorOverlay(Func<int, int, IReadOnlyList<string>> describe) : IDisposable
{
    private const int FontSize = 10;   // pixels at the 720x400 window
    private static readonly (byte, byte, byte) TextColour = (235, 235, 235);
    private static readonly (byte, byte, byte) PinnedColour = (255, 215, 120);

    private ConsoleFont? _font;

    /// <summary>F9 is held.</summary>
    public bool Held { get; set; }

    /// <summary>The game pixel the box is pinned to (null: it follows the pointer).</summary>
    public (int X, int Y)? Pinned { get; set; }

    public bool Visible => Held || Pinned is not null;

    /// <summary>The game pixel described: the pinned one, else the pointer's.</summary>
    public (int X, int Y) Target(int pointerX, int pointerY) => Pinned ?? (pointerX, pointerY);

    /// <summary>The lines for game pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public IReadOnlyList<string> Lines(int x, int y) => describe(x, y);

    /// <summary>Ctrl+F9: pins the box at (<paramref name="x"/>, <paramref name="y"/>) and gives its lines (for the clipboard), or lets it go (null).</summary>
    public IReadOnlyList<string>? TogglePin(int x, int y)
    {
        if (Pinned is not null)
        {
            Pinned = null;
            return null;
        }
        Pinned = (x, y);
        return Lines(x, y);
    }

    /// <summary>The frame round the game pixel and the box beside it, in drawable pixels (the picture at <paramref name="picture"/>).</summary>
    public void Draw(IntPtr renderer, int width, int height, DisplayRectangle picture, int x, int y)
    {
        var pixels = Math.Max(8, FontSize * height / DisplayLayout.BaseHeight);
        var font = _font;
        if (font is null || font.PixelHeight != pixels)
        {
            font?.Dispose();
            _font = font = ConsoleFont.Load(renderer, null, pixels, _ => { });
        }
        var lines = Lines(x, y);
        var colour = Pinned is null ? TextColour : PinnedColour;
        var cell = PixelCell(picture, x, y);
        SdlNative.SDL_SetRenderDrawBlendMode(renderer, SdlNative.BlendModeBlend);
        // the pixel's cell framed (at least 3 drawable pixels), black inside yellow so it shows on any colour
        var edge = Math.Max(1, pixels / 10);
        SdlNative.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        Frame(renderer, cell.X - edge, cell.Y - edge, cell.Width + 2 * edge, cell.Height + 2 * edge, edge);
        SdlNative.SDL_SetRenderDrawColor(renderer, 255, 230, 60, 255);
        Frame(renderer, cell.X - 2 * edge, cell.Y - 2 * edge, cell.Width + 4 * edge, cell.Height + 4 * edge, edge);

        var margin = Math.Max(3, font.CharacterWidth / 2);
        var boxWidth = lines.Select(l => font.Measure(l)).DefaultIfEmpty(0).Max() + 2 * margin;
        var boxHeight = lines.Count * font.LineHeight + 2 * margin;
        var (bx, by) = BoxPlace(cell, boxWidth, boxHeight, width, height, 3 * edge + margin);
        SdlNative.SDL_SetRenderDrawColor(renderer, 12, 12, 22, 225);
        SdlNative.FillRect(renderer, new SdlNative.SdlRect { X = bx, Y = by, Width = boxWidth, Height = boxHeight });
        for (var i = 0; i < lines.Count; i++)
        {
            font.Draw(renderer, lines[i], bx + margin, by + margin + i * font.LineHeight, colour);
        }
    }

    /// <summary>Game pixel (<paramref name="x"/>, <paramref name="y"/>)'s cell of the picture in drawable pixels.</summary>
    internal static DisplayRectangle PixelCell(DisplayRectangle picture, int x, int y)
    {
        var x0 = picture.X + x * picture.Width / Screen.Width;
        var x1 = picture.X + (x + 1) * picture.Width / Screen.Width;
        var y0 = picture.Y + y * picture.Height / Screen.Height;
        var y1 = picture.Y + (y + 1) * picture.Height / Screen.Height;
        return new DisplayRectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    /// <summary>
    /// Where the box goes: below and right of the cell, on the other side where it would leave the window, then kept inside it.
    /// </summary>
    internal static (int X, int Y) BoxPlace(DisplayRectangle cell, int boxWidth, int boxHeight, int width, int height, int gap)
    {
        var x = cell.X + cell.Width + gap;
        if (x + boxWidth > width)
        {
            x = cell.X - gap - boxWidth;
        }
        var y = cell.Y + cell.Height + gap;
        if (y + boxHeight > height)
        {
            y = cell.Y - gap - boxHeight;
        }
        return (Math.Clamp(x, 0, Math.Max(0, width - boxWidth)), Math.Clamp(y, 0, Math.Max(0, height - boxHeight)));
    }

    private static void Frame(IntPtr renderer, int x, int y, int w, int h, int t)
    {
        SdlNative.FillRect(renderer, new SdlNative.SdlRect { X = x, Y = y, Width = w, Height = t });
        SdlNative.FillRect(renderer, new SdlNative.SdlRect { X = x, Y = y + h - t, Width = w, Height = t });
        SdlNative.FillRect(renderer, new SdlNative.SdlRect { X = x, Y = y, Width = t, Height = h });
        SdlNative.FillRect(renderer, new SdlNative.SdlRect { X = x + w - t, Y = y, Width = t, Height = h });
    }

    /// <summary>The font's texture destroyed (before its renderer is: a new window); loaded again on the next draw.</summary>
    public void ReleaseFont()
    {
        _font?.Dispose();
        _font = null;
    }

    public void Dispose() => _font?.Dispose();
}
