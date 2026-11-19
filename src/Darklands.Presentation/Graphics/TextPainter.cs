using Darklands.Content.Formats.Graphics;
using Darklands.Platform;

namespace Darklands.Presentation.Graphics;

/// <summary>Draws text in one of the game's bitmap fonts (1 bit per pixel, up to 16 pixels wide).</summary>
// orig: the graphics driver's string output drv+0x1223 (root thunk 047C:055A, driver slot 21; asm), its parts drv+0x1374 (font set-up),
//   drv+0x12A8 (right clip), drv+0x1254 (left clip), drv+0x130A (rows), drv+0x0FF3 (the glyph loop) and drv+0x0FDC (colour codes);
//   glyph-width drv+0x11A9 (root 047C:04E3) and font-line-height drv+0x11FA (root 047C:051B); reached through text-paint root 0265:006E.
//   checked against asm; tests: tools/visuals/compare.py
// C: per byte of the string: 0 ends it; a byte with the top bit set is a colour code (colour = byte & 0x7F, 0xFF back to the call's
//   colour) and takes no room; a byte outside the font's first..last range takes no room and draws nothing; any other byte advances
//   by its width (the width table, or the font's fixed width) plus the font's spacing. Font 0 starts at code 0, so its control codes
//   0x01..0x1F have the widths of its table; fonts 1 and 2 start at 0x1F.
// C: a glyph row is a 16-bit big-endian word whose top bit is the leftmost pixel; only set bits are drawn (the window mode bit 0,
//   an opaque background, is not used by the screens here); the last character also draws its spacing column (always clear).
// C: the colour left by the last colour code is written back into the window record (+0x0C), so the window's later texts keep it;
//   Draw returns it (DrawColor) for callers that reproduce this.
// C: text-paint doubles x and the window's x0/x1 when the screen page is the 640-wide mode; the game runs in 320x200, so that branch is not written.
// DORMANT: the hi-res (doubled x) branch of text-paint / text-paint-far-string.
public sealed class TextPainter(FntFont font)
{
    // the text plane's id of the text being drawn (ScreenTextPlane; 0: none, or not recording)
    private int _text;

    public FntFont Font => font;

    /// <summary>The font's line height (font-line-height): the glyph height plus the font's line spacing.</summary>
    // orig: font-line-height drv+0x11FA: the header bytes at -4 (height) + -2 (line spacing) before the glyph data
    public int LineHeight => PackFont?.LineHeight ?? font.CharacterHeight + font.LineSpacing;

    /// <summary>The colour the last <see cref="Draw(Canvas, int, int, ReadOnlySpan{byte}, byte, int)"/> ended with (after its colour codes).</summary>
    public byte DrawColor { get; private set; }

    /// <summary>Horizontal advance of one character: its width plus the font's spacing (0 for a colour code or a code the font lacks).</summary>
    // orig: glyph-width drv+0x11A9: 0 for a byte with the top bit set or outside first..last; fixed width (header -5) when non-zero, else the width table; plus the spacing (header -3)
    public int Advance(byte c) =>
        PackFont is not null ? (c == 0 ? 0 : AdvanceCode(Darklands.Core.TextCodes.OfByte(c))) :
        c != 0 && c < 0x80 && font.Contains(c)
            ? (font.FixedWidth != 0 ? font.FixedWidth : font.GetCharacterWidth(c)) + font.CharacterSpacing
            : 0;

    public int Measure(ReadOnlySpan<byte> text)
    {
        var width = 0;
        foreach (var c in text)
        {
            if (c == 0)
            {
                break;
            }
            width += Advance(c);
        }
        return width;
    }

    public int Measure(string text) =>
        PackFont is null && Darklands.Core.TextCodes.FitsBytes(text) ? Measure(Encode(text)) : MeasureCodes(Darklands.Core.TextCodes.OfText(text));

    /// <summary>
    /// The font pack's font for this painter's slot (null: the game's font). A text in it, or one with a letter the game's bytes cannot
    /// hold, is drawn by codes (the Unicode text path, L-6b); every other text keeps the byte path, so without a pack nothing changes.
    /// </summary>
    // Not in the original: the 1.1 translations and font packs (docs/LOCALISATION.md).
    public IFont? PackFont { get; init; }

    /// <summary>The font codes are drawn in: the pack's, else the game's (its letters through GameTextCodec).</summary>
    public IFont CodeFont => PackFont ?? (_original ??= new OriginalFont(this));

    private OriginalFont? _original;

    /// <summary>How far <paramref name="code"/> moves the pen on the Unicode path (a letter the font lacks: as '?'; a colour: 0).</summary>
    public int AdvanceCode(int code)
    {
        if (Darklands.Core.TextCodes.IsColour(code) || code == 0)
        {
            return 0;
        }
        var f = CodeFont;
        return f.Has(code) ? f.Advance(code) : code < 0x20 ? 0 : f.Advance('?');
    }

    /// <summary>The width of <paramref name="codes"/> on the Unicode path.</summary>
    public int MeasureCodes(ReadOnlySpan<int> codes)
    {
        var width = 0;
        foreach (var c in codes)
        {
            width += AdvanceCode(c);
        }
        return width;
    }

    /// <summary>
    /// Draws <paramref name="codes"/> (the Unicode path) as the byte Draw draws bytes: a colour code switches the colour, the character
    /// that crosses <paramref name="right"/> is cut there; returns the x after it.
    /// </summary>
    public int DrawCodes(Canvas canvas, int x, int y, ReadOnlySpan<int> codes, byte color, TextMode mode = TextMode.Copy, int right = int.MaxValue)
    {
        var current = color;
        var room = right == int.MaxValue ? int.MaxValue : right - x + 1;
        if (room < 0)
        {
            DrawColor = color;
            return x;
        }
        var f = CodeFont;
        foreach (var code in codes)
        {
            if (code == 0)
            {
                break;
            }
            if (Darklands.Core.TextCodes.IsColour(code))
            {
                current = code == Darklands.Core.TextCodes.ColourBack ? color : (byte)(code - Darklands.Core.TextCodes.ColourBase);
                continue;
            }
            var advance = AdvanceCode(code);
            if (advance == 0)
            {
                continue;
            }
            var visible = Math.Min(advance, room);
            var glyph = f.Has(code) ? code : '?';
            if (f is OriginalFont original && original.ByteOf(glyph) is { } b)
            {
                DrawGlyph(canvas, x, y, b, current, visible, mode);
            }
            else
            {
                // a scalable font's glyph: its 1x pixels name it, so the host draws it again at the window's resolution (L-6b set 2)
                var texel = f is IScalableFont scalable ? TextGlyphs.TexelOf(TextGlyphs.Id(scalable, glyph, x, y)) : 0;
                DrawFontGlyph(canvas, f, x, y, glyph, current, visible, mode, texel);
            }
            x += visible;
            room -= advance;
            if (room <= 0)
            {
                break;
            }
        }
        DrawColor = current;
        return x;
    }

    // a pack font's glyph, pixel by pixel in the colour (as DrawGlyph does for the game's fonts)
    private void DrawFontGlyph(Canvas canvas, IFont f, int x, int y, int code, byte color, int columns, TextMode mode, int glyph)
    {
        for (var row = 0; row < f.Height; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!f.Pixel(code, column, row))
                {
                    continue;
                }
                var under = canvas[x + column, y + row];
                var value = mode switch
                {
                    TextMode.Copy => color,
                    TextMode.And => (byte)(under & color),
                    TextMode.Or => (byte)(under | color),
                    _ => (byte)(under ^ color),
                };
                if (glyph != 0)
                {
                    canvas.PlotText(x + column, y + row, value, glyph);
                }
                else if (mode == TextMode.Copy && _text != 0)
                {
                    canvas.PlotText(x + column, y + row, color, _text);
                }
                else if (mode == TextMode.Copy)
                {
                    canvas[x + column, y + row] = color;
                }
                else
                {
                    canvas.Repaint(x + column, y + row, value);
                }
            }
        }
    }

    /// <summary>
    /// Draws text and returns the x after its last character. A byte from 0x80 switches the colour to its
    /// low seven bits and 0xFF back to <paramref name="color"/> (menus draw their hotkey letters this way:
    /// "\x8c U\xffse this formula"). <paramref name="right"/> is the window's last column: the character
    /// that crosses it is cut there and the rest is not drawn.
    /// </summary>
    // orig: drv+0x123C: set-up, right clip (drv+0x12A8: the string is cut after the character that reaches the last column, that character
    //   loses the excess), left clip, rows, then the glyph loop; the x returned is the drawn width (drv+0x136B)
    public int Draw(Canvas canvas, int x, int y, ReadOnlySpan<byte> text, byte color, int right = int.MaxValue) =>
        Draw(canvas, x, y, text, color, TextMode.Copy, right);

    /// <summary>Draws text combining each glyph pixel with the screen as <paramref name="mode"/> says (see <see cref="TextMode"/>).</summary>
    public int Draw(Canvas canvas, int x, int y, ReadOnlySpan<byte> text, byte color, TextMode mode, int right = int.MaxValue)
    {
        if (PackFont is not null)
        {
            // a font pack's font: the game's bytes as codes, drawn in it (L-6b)
            return DrawCodes(canvas, x, y, Darklands.Core.TextCodes.OfBytes(text), color, mode, right);
        }
        var current = color;
        var room = right == int.MaxValue ? int.MaxValue : right - x + 1;
        if (room < 0)
        {
            DrawColor = color;
            return x;
        }
        foreach (var c in text)
        {
            if (c == 0)
            {
                break;
            }
            if (c >= 0x80)
            {
                current = c == 0xFF ? color : (byte)(c & 0x7F);
                continue;
            }
            var advance = Advance(c);
            if (advance == 0)
            {
                continue;
            }
            var visible = Math.Min(advance, room);
            DrawGlyph(canvas, x, y, c, current, visible, mode);
            x += visible;
            room -= advance;
            if (room <= 0)
            {
                break;
            }
        }
        DrawColor = current;
        return x;
    }

    public int Draw(Canvas canvas, int x, int y, string text, byte color, int right = int.MaxValue)
    {
        if (PackFont is not null || !Darklands.Core.TextCodes.FitsBytes(text))
        {
            return DrawText(canvas, x, y, Darklands.Core.TextCodes.OfText(text), text, color, right);
        }
        var bytes = Encode(text);
        if (!ScreenTextPlane.Recording)
        {
            return Draw(canvas, x, y, bytes, color, right);
        }
        // the developer's text plane (ScreenTextPlane): which text, by which key, each letter's pixels are; the pixels are the same. Its
        //   room: to the window's last column, else to the screen's right edge (the box a translation must fit, L-7w)
        var windowed = right != int.MaxValue;
        _text = ScreenTextPlane.Drawn(text, Measure(bytes), Math.Max(0, (windowed ? right + 1 : Screen.Width) - x), windowed, font.Index + 1);
        try
        {
            return Draw(canvas, x, y, bytes, color, right);
        }
        finally
        {
            _text = 0;
        }
    }

    // a text on the Unicode path, with the developer's text plane as the byte path keeps it
    private int DrawText(Canvas canvas, int x, int y, int[] codes, string text, byte color, int right)
    {
        if (!ScreenTextPlane.Recording)
        {
            return DrawCodes(canvas, x, y, codes, color, TextMode.Copy, right);
        }
        var windowed = right != int.MaxValue;
        _text = ScreenTextPlane.Drawn(text, MeasureCodes(codes), Math.Max(0, (windowed ? right + 1 : Screen.Width) - x), windowed, font.Index + 1);
        try
        {
            return DrawCodes(canvas, x, y, codes, color, TextMode.Copy, right);
        }
        finally
        {
            _text = 0;
        }
    }

    public int DrawCharacter(Canvas canvas, int x, int y, byte c, byte color)
    {
        if (PackFont is not null)
        {
            return DrawCodes(canvas, x, y, [Darklands.Core.TextCodes.OfByte(c)], color) - x;
        }
        var advance = Advance(c);
        if (advance != 0)
        {
            DrawGlyph(canvas, x, y, c, color, advance, TextMode.Copy);
        }
        return advance;
    }

    // orig: the foreground loop of drv+0x0FF3: per row the 16-bit word (bytes swapped), shifted left a bit per column, a set bit plots the colour
    //   with the instruction drv+0x0FFE / drv+0x0218 patched in at drv+0x10A9: mov, and, or or xor by bits 8..9 of the colour word
    private void DrawGlyph(Canvas canvas, int x, int y, byte c, byte color, int columns, TextMode mode)
    {
        for (var row = 0; row < font.CharacterHeight; row++)
        {
            var bytes = font.GetGlyphRow(c, row);
            var bits = bytes[0] << 8 | (bytes.Length > 1 ? bytes[1] : 0);
            for (var column = 0; column < columns && column < 16; column++, bits <<= 1)
            {
                if ((bits & 0x8000) != 0)
                {
                    var under = canvas[x + column, y + row];
                    if (mode == TextMode.Copy && _text != 0)
                    {
                        canvas.PlotText(x + column, y + row, color, _text);
                    }
                    else if (mode == TextMode.Copy)
                    {
                        canvas[x + column, y + row] = color;
                    }
                    else
                    {
                        // a recolour of what is there: the pixel keeps its owner (Screen.Owners)
                        canvas.Repaint(x + column, y + row, mode switch
                        {
                            TextMode.And => (byte)(under & color),
                            TextMode.Or => (byte)(under | color),
                            _ => (byte)(under ^ color),
                        });
                    }
                }
            }
        }
    }

    /// <summary>Draws a text with colour codes (see <see cref="Draw(Canvas, int, int, ReadOnlySpan{byte}, byte, int)"/>).</summary>
    // orig: root 0265:0004 draft (bytes match) and drv+0x0FDC: the colour codes as the driver reads them (the map row of DrawCoded)
    public int DrawCoded(Canvas canvas, int x, int y, string text, byte color) => Draw(canvas, x, y, text, color);

    public static byte[] Encode(string text) => Darklands.Core.GameTextCodec.Encode(text);
}

/// <summary>How a text pixel combines with the screen: bits 8..9 of the window's colour word (0x380 = colour 0x80 by xor).</summary>
// orig: drv+0x0218 (root thunk 047C:05E6, driver slot 41): the low byte is the colour, (high byte & 3) picks an opcode from the table
//   drv+0x0214 = {mov 0x88, and 0x20, or 0x08, xor 0x30}; the text loop drv+0x0FF3 calls it with the window's colour word. checked against asm
public enum TextMode
{
    Copy = 0,
    And = 1,
    Or = 2,
    Xor = 3,
}

/// <summary>
/// One of the game's fonts as an <see cref="IFont"/> (the Unicode path drawing a translation without a font pack): a code is the byte
/// GameTextCodec gives its letter (ä ö ü Ä Ö Ü ß and ASCII) or a control code; a letter the game's bytes cannot hold is not in it (drawn
/// as '?').
/// </summary>
// Not in the original: the 1.1 translations (L-6b).
internal sealed class OriginalFont(TextPainter painter) : IFont
{
    public int Height => painter.Font.CharacterHeight;

    public int LineHeight => painter.LineHeight;

    /// <summary>The game byte of <paramref name="code"/> (a letter, a control code), or null when the game has none for it.</summary>
    public byte? ByteOf(int code)
    {
        if (code is < 0 or > 0xFFFF || Darklands.Core.TextCodes.IsColour(code))
        {
            return null;
        }
        var c = (char)code;
        return Darklands.Core.TextCodes.FitsBytes(c.ToString()) && Darklands.Core.TextCodes.ColourOf(c) < 0 ? Darklands.Core.GameTextCodec.ToByte(c) : null;
    }

    public bool Has(int code) => ByteOf(code) is { } b && (painter.Advance(b) > 0 || code == ' ');

    public int Advance(int code) => ByteOf(code) is { } b ? painter.Advance(b) : 0;

    public bool Pixel(int code, int x, int y)
    {
        if (ByteOf(code) is not { } b || y < 0 || y >= painter.Font.CharacterHeight || x is < 0 or >= 16)
        {
            return false;
        }
        var row = painter.Font.GetGlyphRow(b, y);
        var bits = row[0] << 8 | (row.Length > 1 ? row[1] : 0);
        return (bits & (0x8000 >> x)) != 0;
    }
}
