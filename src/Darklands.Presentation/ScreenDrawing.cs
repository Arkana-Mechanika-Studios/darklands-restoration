using Darklands.Core.Characters;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>Drawing steps the full screens share: their background page, wrapped and centred text.</summary>
internal static class ScreenDrawing
{
    /// <summary>
    /// Draws a full-screen picture with its colours and the party panel over its left edge,
    /// and returns the result as the screen's back page (what it restores rectangles from).
    /// </summary>
    public static SavedScreen DrawBackground(GameUi ui, PartyPanel panel, Party party, string picture)
    {
        ui.Canvas.DrawPicture(ui.Assets.Picture(picture));
        panel.Draw(ui.Canvas, party);
        return ui.Canvas.SaveScreen();
    }

    /// <summary>A picture drawn over the screen (colour 0 transparent), when the game has it.</summary>
    public static void Overlay(GameUi ui, string picture, int x, int y, Canvas? on = null)
    {
        if (ui.Assets.PictureExists(picture))
        {
            (on ?? ui.Canvas).Draw(ui.Assets.Picture(picture).Image, x, y, transparent: true);
        }
    }

    // orig: text-paint-wrapped = root 0265:0134 draft, through CardTextLayout (no hot spots, no icon)
    /// <summary>
    /// Word-wrapped text as the game's <c>text-paint-wrapped</c>: a column of
    /// <paramref name="width"/> pixels, lines <paramref name="spacing"/> + <paramref name="leading"/> apart.
    /// </summary>
    public static void Wrapped(GameUi ui, string text, int width, int x, int y, byte color, int spacing = 8, int leading = 1) =>
        new CardTextLayout(ui.Text).Draw(ui.Canvas, TextPainter.Encode(text), x, y, width,
            new CardTextLayout.Settings { Color = color, Spacing = spacing, Leading = leading });

    /// <summary>Text centred on <paramref name="centerX"/> (left edge at centre − width / 2).</summary>
    public static void Centered(GameUi ui, int centerX, int y, string text, byte color) =>
        ui.Text.Draw(ui.Canvas, centerX - ui.Text.Measure(text) / 2, y, text, color);

    /// <summary>The menu's hotkey colour: "\x8c" + letter + "\xff" draws the letter in colour 12.</summary>
    public static string Hotkey(char letter) => (char)0x8C + $" {letter}" + (char)0xFF;

    /// <summary>
    /// The game's framed box: filled with <paramref name="background"/>, a dark outline, a light one a
    /// pixel inside it, and the four corner pieces of COMMONSP.IMG over the corners.
    /// </summary>
    // orig: framed-box-paint root 0000:2286 draft (bytes match): gfx-rect-fill(w, x, y, cw, ch, bg); x1 = x + cw - 1, y1 = y + ch - 1; four gfx-line-draw of colour 0x86 around (x, y) .. (x1, y1), four of 0x82 around (x + 1, y + 1) .. (x1 - 1, y1 - 1); then gfx-picture-draw of DS:A660 at (x, y), DS:A662 at (x, y1 - 3), DS:A664 at (x1 - 3, y), DS:A666 at (x1 - 3, y1 - 3)
    public static void FrameBox(Canvas canvas, Content.Formats.Graphics.CommonSprites sprites, int x, int y, int width, int height, byte background)
    {
        const byte Dark = 0x86;
        const byte Light = 0x82;
        canvas.Fill(x, y, width, height, background);
        var (x1, y1) = (x + width - 1, y + height - 1);
        canvas.Outline(x, y, width - 1, height - 1, Dark);
        canvas.Outline(x + 1, y + 1, width - 3, height - 3, Light);
        SpriteDrawing.Draw(canvas, sprites.CornerTopLeft, x, y);
        SpriteDrawing.Draw(canvas, sprites.CornerBottomLeft, x, y1 - 3);
        SpriteDrawing.Draw(canvas, sprites.CornerTopRight, x1 - 3, y);
        SpriteDrawing.Draw(canvas, sprites.CornerBottomRight, x1 - 3, y1 - 3);
    }

    /// <summary>Waits until the left mouse button is up again (`while (mouse-button-state &amp; 1);`: the keys stay waiting).</summary>
    public static void WaitForRelease(GameUi ui) => ui.WaitForButtonRelease();
}
