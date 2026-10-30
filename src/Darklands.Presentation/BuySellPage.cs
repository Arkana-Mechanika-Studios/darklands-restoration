using Darklands.Content.Formats.Graphics;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The page the barter, item store and loot screens share (BUYSELL.PIC): a four-line menu at
/// the top, an upper list (what the shop, store or pile holds) and a lower list (the member's
/// pack), four rows each with arrows above and below. It draws text with the game's colour
/// bytes, restores parts from the clean picture, recolours rows (highlights) and tells which
/// part of the page the pointer is over. Coordinates are the original's.
/// </summary>
// orig: the page code the three overlays copy: barter (R032), item cache (R035) and loot (R040); the hit rectangles and the hover code are identical in trade-screen-entry R032+0x0000, stash-screen R035+0x0000 and R040+0x01C8
// C: the C tests are strict (x > 0x50 && x < 0xE6 ...); rows are (y − top) / 8 or >> 3, as written.
public sealed class BuySellPage(GameUi ui, PartyPanel panel)
{
    /// <summary>Normal text (and the active menu line).</summary>
    public const byte Ink = 8;

    /// <summary>A menu line that does not apply to the list in use.</summary>
    public const byte Faded = 7;

    /// <summary>The highlight: the row under the pointer or the cursor row.</summary>
    public const byte Lit = 9;

    public const int MenuX = 0x50;
    public const int MenuY = 0x1B;
    public const int RowHeight = 8;
    public const int Rows = 4;
    public const int UpperX = 0x55;
    public const int UpperY = 0x55;
    public const int LowerX = 0x75;
    public const int LowerY = 0x93;

    private Pic? _picture;

    public Canvas Canvas => ui.Canvas;

    /// <summary>The barter screen's list of the codes for sale (DS:86A2, see <see cref="GameUi.BarterListing"/>).</summary>
    public int[] BarterListing => ui.BarterListing;

    /// <summary>A short message box over the page (put back afterwards).</summary>
    public void ShowMessage(string text) => new MessageBox(ui).Show(text);

    /// <summary>Draws the picture (taking its colours), copies its right part to the screen and draws the party column.</summary>
    public void DrawBackground(Party party)
    {
        // C: image-paint-palette(1, "pics\\buysell.pic", palette-now) draws into the back page; palette-load; gfx-rect-copy(backdrop-window, 0x3C, 0, 0x104, 0xC8, screen-window, 0x3C, 0).
        _picture = ui.Assets.Picture("buysell");
        ui.Canvas.UsePalette(_picture);
        Restore(0x3C, 0, 0x104, 0xC8);
        DrawParty(party);
    }

    /// <summary>The party column (the original's party panel frames and rows).</summary>
    // orig: party-panel-frame-set(0x100) and party-panel-row-set(0x100) (root, resident) as PartyPanel; the argument 0x100 is the mask 0x1F (all five rows: root 0000:1591 `cmp bx,0x100` then `mov dx,0x1f`, and 0000:171F the same in party-panel-row-set), as the panel draws
    public void DrawParty(Party party) => panel.Draw(ui.Canvas, party);

    /// <summary>Copies a part of the clean picture back onto the screen.</summary>
    // orig: gfx-rect-copy(backdrop-window, x, y, w, h, screen-window, x, y) — graphics driver thunk root 047C (asm driver)
    public void Restore(int x, int y, int width, int height)
    {
        var image = (_picture ?? ui.Assets.Picture("buysell")).Image;
        for (var row = y; row < y + height && row < image.Height; row++)
        {
            for (var column = x; column < x + width && column < image.Width; column++)
            {
                ui.Canvas[column, row] = image.Pixels[row * image.Width + column];
            }
        }
    }

    /// <summary>
    /// Draws a line of text in <paramref name="color"/>. Bytes from 0x80 change the colour to
    /// their low seven bits (0xFF: back to <paramref name="color"/>), as the game's text driver does.
    /// </summary>
    // orig: gfx-text-draw(screen-window, x, y, s) — graphics driver thunk root 047C:055A (asm driver; colour-byte rule as decoded from the driver code)
    // UNCERTAIN: glyph clipping at the window's right edge is not reproduced (long lines are cut by the screen edge only).
    public void Text(int x, int y, string text, byte color)
    {
        var ink = color;
        foreach (var c in TextPainter.Encode(text))
        {
            if ((c & 0x80) != 0)
            {
                ink = (c & 0x7F) == 0x7F ? color : (byte)(c & 0x7F);
                continue;
            }
            x += ui.Text.DrawCharacter(ui.Canvas, x, y, c, ink);
        }
    }

    /// <summary>Replaces colour <paramref name="from"/> by <paramref name="to"/> in a rectangle.</summary>
    // orig: gfx-rect-recolour(screen-window, x, y, w, h, from, to) — graphics driver thunk root 047C:04DC (asm driver)
    public void Recolor(int x, int y, int width, int height, byte from, byte to) =>
        ui.Canvas.Recolor(x, y, width, height, from, to);

    /// <summary>A highlight on: <see cref="Ink"/> becomes <see cref="Lit"/>.</summary>
    public void Light(int x, int y, int width, int height = RowHeight) => Recolor(x, y, width, height, Ink, Lit);

    /// <summary>A highlight off: <see cref="Lit"/> becomes <see cref="Ink"/>.</summary>
    public void Unlight(int x, int y, int width, int height = RowHeight) => Recolor(x, y, width, height, Lit, Ink);

    // ---- where the pointer is ------------------------------------------------------------

    /// <summary>Over the menu: the line 0..4, or null.</summary>
    public static int? MenuLineAt(int x, int y) => x > 0x50 && x < 0xE6 && y > 0x1B && y < 0x43 ? (y - 0x1B) / 8 : null;

    /// <summary>Over the upper list: the row, or null.</summary>
    public static int? UpperRowAt(int x, int y) => x > 0x52 && x < 0x10B && y > 0x55 && y < 0x75 ? (y - 0x55) >> 3 : null;

    /// <summary>Over the lower list: the row, or null.</summary>
    public static int? LowerRowAt(int x, int y) => x > 0x72 && x < 0x11C && y > 0x93 && y < 0xB3 ? (y - 0x93) >> 3 : null;

    public static bool OnUpperUpArrow(int x, int y) => y > 0x4C && y < 0x55 && x > 0x52 && x < 0x10B;

    public static bool OnUpperDownArrow(int x, int y) => y > 0x75 && y < 0x7E && x > 0x52 && x < 0x10B;

    public static bool OnLowerUpArrow(int x, int y) => y > 0x8A && y < 0x93 && x > 0x72 && x < 0x12B;

    public static bool OnLowerDownArrow(int x, int y) => y > 0xB3 && y < 0xBC && x > 0x72 && x < 0x12B;

    /// <summary>Over the party column: the member's row.</summary>
    public static int? MemberAt(int x, int y) => x < 0x3C ? y / 40 : null;

    /// <summary>
    /// The menu's part of one pass of the screen's loop, on the button's state (not its press): with the button up, the line
    /// under the pointer lights up (and the lit one goes dark when the pointer leaves the menu); with the button held over
    /// the menu, the button is waited out and the line under the pointer <em>after</em> the release gives its key.
    /// </summary>
    /// <param name="lit">The line lit so far (−1: none); updated.</param>
    /// <param name="keys">The key of each line (a line past the table gives none).</param>
    /// <param name="leaveHeight">The height of the strip un-lit when the pointer leaves the menu (8, 9 on the barter screen).</param>
    // orig: R032+0x00FE..0x023E, R035 and R040's copies of the same tests (byte-matched C; the barter draft checked against the asm):
    // `if (!(mouse-button-state & 1) && over the menu) { hover } else if ((mouse-button-state & 1) && over the menu) { while (mouse-button-state & 1);
    // row = (pointer-row - 0x1b) / 8; switch (row) ... } else if (!(mouse-button-state & 1) && lit != -1) { un-light }`; tests: BuySellInputTests
    // C: the row after the release is a signed division truncated toward zero (cwd / sar): a pointer a little above the menu gives row 0.
    // C: with nothing lit (−1) the hover's un-highlight recolours the strip at y 0x13 (the line above the menu), as the original does.
    public int MenuStep(ref int lit, IReadOnlyList<int> keys, int leaveHeight)
    {
        var mouse = ui.Mouse;
        var over = MenuLineAt(mouse.X, mouse.Y) is not null;
        if (!mouse.Left && over)
        {
            var row = (mouse.Y - MenuY) / 8;
            if (row != lit)
            {
                Recolor(MenuX, lit * 8 + MenuY, 0x96, 8, Lit, Ink);
                lit = row;
                Recolor(MenuX, row * 8 + MenuY, 0x96, 8, Ink, Lit);
            }
        }
        else if (mouse.Left && over)
        {
            WaitForRelease();
            return MenuKey((ui.Mouse.Y - MenuY) / 8, keys);
        }
        else if (!mouse.Left && lit != -1)
        {
            Recolor(MenuX, lit * 8 + MenuY, 0x96, leaveHeight, Lit, Ink);
            lit = -1;
        }
        return 0;
    }

    /// <summary>The left button is held now with the pointer where <paramref name="at"/> says (the loop's `(mouse-button-state &amp; 1) &amp;&amp; …` tests).</summary>
    public bool Held(Func<int, int, bool> at) => ui.Mouse.Left && at(ui.Mouse.X, ui.Mouse.Y);

    /// <summary>The row <paramref name="at"/> gives for the pointer while the left button is held, or null.</summary>
    public int? HeldRow(Func<int, int, int?> at) => ui.Mouse.Left ? at(ui.Mouse.X, ui.Mouse.Y) : null;

    /// <summary>`while (mouse-button-state &amp; 1);`: the button is waited out.</summary>
    public void WaitForRelease() => ui.WaitForButtonRelease();

    /// <summary>The end of one pass of the loop (<see cref="GameUi.KeyAfterTheMouse"/>).</summary>
    // orig: `root 0000:0000(DS:9419, 0); if (root 047C:073D()) mkey = root 047C:0744();` at the end of the loops of R032+0x0000, R035+0x0000, R040+0x01C8
    // C: only barter's loop has the party-icons-stale test between the two (R032+0x06BF): <paramref name="redrawIfDirty"/>; its redraw keeps the mouse's key.
    public int KeyAfterTheMouse(int mouseKey, Action? redrawIfDirty = null) => ui.KeyAfterTheMouse(mouseKey, redrawIfDirty, redrawKeepsTheKey: true);

    /// <summary>A key code for a click on a menu line, using the screen's table (0: none).</summary>
    public static int MenuKey(int line, IReadOnlyList<int> keys) => line >= 0 && line < keys.Count ? keys[line] : 0;

    /// <summary>The key of a letter, typed in either case ('P': 0x1950 or 0x1970).</summary>
    public static bool Is(int key, char letter) =>
        key == Keys.ForCharacter(char.ToUpperInvariant(letter)) || key == Keys.ForCharacter(char.ToLowerInvariant(letter));
}
