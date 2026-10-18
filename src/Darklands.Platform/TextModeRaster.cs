namespace Darklands.Platform;

/// <summary>
/// Turns a <see cref="TextScreen"/> into pixels as a VGA shows mode 3: 720×400 (9×16 character
/// cells, an 8×16 font), the 16 text colours, the ninth column repeated for the line-drawing
/// characters C0h..DFh, blinking cursor and blinking characters at the card's rates.
/// </summary>
// orig: BIOS video mode 3 (int 10h AX = 3) set by R000+0x0114 draft (bytes match) for the banner; the VGA's 9-dot cells and blink timing
// DIVERGENCE: the VGA's own ROM font is not part of the game; a host can load an 8×16 BIOS font dump
// (TextFont.Load), else the built-in TextFont.Fallback draws the characters (same cell layout, other glyph shapes).
public static class TextModeRaster
{
    public const int CellWidth = 9;
    public const int CellHeight = 16;
    public const int Width = TextScreen.Columns * CellWidth;
    public const int Height = TextScreen.Rows * CellHeight;

    /// <summary>The VGA's mode-3 refresh: 70.086 Hz.</summary>
    private const double FrameMilliseconds = 1000.0 / 70.086;

    /// <summary>The 16 text colours (the EGA/VGA default palette), 0xAARRGGBB.</summary>
    public static readonly uint[] Colours =
    [
        0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA, 0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA,
        0xFF555555, 0xFF5555FF, 0xFF55FF55, 0xFF55FFFF, 0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF,
    ];

    /// <summary>The cursor is on during the first 8 frames of every 16 (VGA: a 16-frame cycle).</summary>
    public static bool CursorPhase(long milliseconds) => (long)(milliseconds / FrameMilliseconds) / 8 % 2 == 0;

    /// <summary>Blinking characters (attribute bit 7) show during the first 16 frames of every 32.</summary>
    public static bool BlinkPhase(long milliseconds) => (long)(milliseconds / FrameMilliseconds) / 16 % 2 == 0;

    /// <summary>Draws the screen into <paramref name="pixels"/> (<see cref="Width"/> × <see cref="Height"/>, 0xAARRGGBB).</summary>
    /// <param name="milliseconds">The host's clock, for the blinking.</param>
    public static void Render(TextScreen screen, TextFont font, long milliseconds, Span<uint> pixels)
    {
        var cursorOn = screen.CursorVisible && CursorPhase(milliseconds);
        var blinkOn = BlinkPhase(milliseconds);
        for (var row = 0; row < TextScreen.Rows; row++)
        {
            for (var column = 0; column < TextScreen.Columns; column++)
            {
                var cell = row * TextScreen.Columns + column;
                var character = screen.Characters[cell];
                var attribute = screen.Attributes[cell];
                var foreground = Colours[attribute & 0x0F];
                // C: the BIOS leaves attribute bit 7 as "blink" (not a bright background) after a mode set.
                var background = Colours[(attribute >> 4) & 0x07];
                var hidden = (attribute & 0x80) != 0 && !blinkOn;
                var lineGraphics = character is >= 0xC0 and <= 0xDF;
                for (var y = 0; y < CellHeight; y++)
                {
                    var bits = hidden ? 0 : font.Row(character, y);
                    if (cursorOn && row == screen.CursorRow && column == screen.CursorColumn && y is 13 or 14)
                    {
                        // the mode-3 cursor: scan lines 13 and 14 in the cell's foreground colour
                        bits = 0xFF;
                    }
                    var line = pixels.Slice((row * CellHeight + y) * Width + column * CellWidth, CellWidth);
                    for (var x = 0; x < 8; x++)
                    {
                        line[x] = (bits & (0x80 >> x)) != 0 ? foreground : background;
                    }
                    line[8] = lineGraphics && (bits & 1) != 0 ? foreground : background;
                }
            }
        }
    }
}
