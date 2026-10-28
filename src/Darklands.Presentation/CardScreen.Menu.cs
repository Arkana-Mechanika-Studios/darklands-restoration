using Darklands.Cards;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>The option menu of a card: hover highlight, Shift hints, the item and saint pop-up boxes, the choice.</summary>
// orig: card_menu R043+0x1BD2 (150B:1142) draft (bytes match) + card_mouse R043+0x21D4 (150B:1744) draft (bytes match) + the box: R043+0x2CE2 (text, OptionPopupText), R043+0x2ACA (drawing; draft, unmatched only in register/branch layout) and R043+0x3292 (the main lines' width while the box is up); tests: OptionPopupTests, Host.Headless
public sealed partial class CardScreen
{
    /// <summary>DS:5202 = -10: no Shift hint is shown.</summary>
    private const int NoHint = -10;

    /// <summary>The bottom limit of a pop-up box (0xC1) and the right edge of the card window's text (0xF5).</summary>
    private const int BoxBottom = 0xC1;
    private const int BoxRight = 0xF5;

    /// <summary>
    /// A laid-out option list on the screen: the lines, the column they share and the highlighted
    /// line (the layout struct DS:E96E, whose byte at +1 is menu_highlight).
    /// </summary>
    private sealed class OptionLayout(List<OptionArea> lines, int x, int width)
    {
        public List<OptionArea> Lines { get; } = lines;

        /// <summary>Screen x of the column (the window's x + 0x3C).</summary>
        public int X { get; } = x;

        public int Width { get; set; } = width;

        public int Highlight { get; set; } = -1;

        // orig: INSIDE(x, y, w, h, mx, my) of card_mouse: x + 0x3c <= mx <= w + x + 0x3c and y <= my <= y + h, both ends included, first hit wins
        public int At(int mx, int my) => Lines.FindIndex(a => X <= mx && mx <= X + Width && a.Y <= my && my <= a.Y + a.Height);
    }

    /// <summary>An item or saint box on the screen: its rectangle, what it covers and its own option list.</summary>
    private sealed record PopupBox(int X, int Y, int Width, int Height, SavedRegion Under, OptionLayout Layout, int MainWidth, Cards.PopupBox Kind)
    {
        public bool Contains(int mx, int my) => X <= mx && mx <= X + Width && Y <= my && my <= Y + Height;
    }

    /// <summary>The menu being chosen from (null: none): for <see cref="OptionPoint"/>.</summary>
    // C: per thread, so screens run side by side (parallel tests) do not read each other's menu; a game has one screen thread.
    [ThreadStatic]
    private static OptionLayout? _choosing;

    /// <summary>A point on line <paramref name="line"/> of the menu being chosen from (a scripted host's option:&lt;n&gt; step), null without one.</summary>
    public static (int X, int Y)? OptionPoint(int line) =>
        _choosing is { } l && line >= 0 && line < l.Lines.Count ? (l.X + 1, l.Lines[line].Y + 1) : null;

    // orig: card_menu: combat_clock = 0, flush_keys, DS:EE41 = DS:EE42 = 0, menu_popup = DS:8DDE = 0, menu_highlight = -1; then per pass: menu_bar_run (GameUi), the party_icons_dirty redraw (not needed), a left press on a portrait -> 13a0, card_mouse (hover, box, hint, click), and a waiting key: Enter / Space with a highlighted line choose it (in the box: menu_member = opt / 4, menu_slot = opt % 4, the line is the main line the box belongs to, DS:A7BA[DS:A779]), Alt-Q quits (HotKeys), card_global_keys (HotKeys), Up / Down while the pointer is over a line move the highlight (wrapping) and the pointer onto it
    // C: a choice by Enter in the box sets menu_member and menu_slot but not menu_popup: the card rates the line with opt_fn[k], which is then the box's table (DS:E96E holds the box's layout; CardMenu.Run).
    // C: Up from no highlight cannot happen in the original (the cursor keys need the pointer over a line, which card_mouse has just highlighted).
    // DIVERGENCE: the pointer is not moved onto the line Up / Down select (the host owns it); the menu acts as if it were until the mouse moves. With GameUi.KeyboardCardMenus (an interface convenience, off unless a host turns it on): Up / Down also work while the pointer is off the lines (from no highlight: the first or last line), and the digits 1..9 choose the main list's lines in order.
    // DORMANT: the battle option kind (-1, marker 0x06): highlighting it calls R043+0x29CE, which does nothing (its message "Enemy stats will be displayed here" is never shown); leaving it redraws the panel, as here.
    private int Select(List<OptionArea> areas, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint)
    {
        if (areas.Count == 0)
        {
            return 0;
        }
        var c = context();
        c.MenuPopup = false;
        c.MenuBox = Cards.PopupBox.None;
        var main = new OptionLayout(areas, _optionX, _optionWidth);
        _choosing = main;
        var current = main;
        PopupBox? box = null;
        var hintShownFor = NoHint;
        var lastMouse = (ui.Mouse.X, ui.Mouse.Y);
        (int X, int Y)? warped = null;

        bool InBox() => box is not null && current == box.Layout;

        void CloseBox()
        {
            // orig: gfx_draw_pic(win, DS:EE0D, DS:EE0F, DS:A776) (the saved rectangle back), gfx_recolor_rect(card_window, x, y, w, h, 5, DS:EE59 or 0), free_buffer, DS:EE41 = 0, R043+0x3292(2) (the main width back)
            _canvas.RestoreTransparent(box!.Under);
            _canvas.Recolor(box.X, box.Y, box.Width, box.Height, 5, _textColor);
            main.Width = box.MainWidth;
            box = null;
        }

        void Unhighlight(OptionLayout layout)
        {
            Highlight(layout, layout.Highlight, false);
            if (layout.Lines[layout.Highlight].Kind == OptionKind.Battle)
            {
                RedrawPanel();
            }
        }

        // orig: the first half of card_mouse, from the pointer position (MX, MY)
        void Track(int mx, int my)
        {
            var i = current.At(mx, my);
            if (i >= 0 && current.Highlight != i)
            {
                if (!InBox() && box is not null)
                {
                    CloseBox();
                }
                if (current.Highlight != -1)
                {
                    Unhighlight(current);
                }
                current.Highlight = i;
                Highlight(current, i, true);
                if (current.Lines[i].Kind is OptionKind.Items or OptionKind.Saints && box is null)
                {
                    box = OpenBox(main, current.Lines[i]);
                }
            }
            if (i < 0 && (current.Highlight != -1 || InBox()))
            {
                if (box is not null && !InBox() && box.Contains(mx, my))
                {
                    // orig: DS:A778 = DS:E96E; DS:E96E = DS:EA64; menu_highlight = -1; DS:EE42 = 1 (the main line stays lit)
                    current = box.Layout;
                    current.Highlight = -1;
                    c.MenuBox = box.Kind;
                    return;
                }
                var keep = false;
                if (InBox() && !box!.Contains(mx, my))
                {
                    // orig: DS:EE42 = 0; DS:E96E = DS:A778 (the main list with its highlight); the box stays when the pointer is back on that very line
                    current = main;
                    c.MenuBox = Cards.PopupBox.None;
                    keep = main.At(mx, my) == main.Highlight;
                }
                if (box is not null && !InBox() && !keep)
                {
                    CloseBox();
                }
                if (current.Highlight != -1)
                {
                    Unhighlight(current);
                    current.Highlight = -1;
                }
            }
        }

        // orig: the Shift part of card_mouse: show = (Shift held, BIOS flags 0040:0017 bits 0 and 1) && DS:ED6C == 1 && menu_highlight != -1; `if (show && menu_highlight != DS:5202)`: the panel is redrawn first when a hint was up (not at Expert), menu_member = opt / 4, menu_slot = opt % 4, r = opt_fn[opt]() (in the box the box's handlers: item_fn / saint_fn), DS:5202 = menu_highlight, R043+0x27E6(r.a, r.b); `if (DS:5202 != -10 && !show)` DS:5202 = -10 and the panel is redrawn (not at Expert)
        // C: menu_member and menu_slot are set from the option number of a main line too (option / 4, option % 4): a Shift hint over line 5 leaves member 1, slot 1 behind.
        void Hint(bool over)
        {
            var show = ui.ShiftHeld && over && current.Highlight != -1;
            if (show && current.Highlight != hintShownFor)
            {
                if (hintShownFor != NoHint)
                {
                    ClearHint();
                }
                var option = current.Lines[current.Highlight].Option;
                c.MenuMember = option / 4;
                c.MenuSlot = option % 4;
                hintShownFor = current.Highlight;
                var r = InBox()
                    ? popupHint?.Invoke(main.Lines[main.Highlight].Option)
                    : hint?.Invoke(option);
                ShowHint(r ?? OptionHint.Say(CardTexts.SuccessAssured));
            }
            if (hintShownFor != NoHint && !show)
            {
                hintShownFor = NoHint;
                ClearHint();
            }
        }

        // After a full screen was shown over the card the picture, the text and the fade are drawn again; the highlight, a hint and the box are gone with them.
        // C: 13f2 leaves DS:EE41 / DS:EE42 set: the next move puts back the area saved under the box, which the redraw already shows as it was.
        void Refresh()
        {
            if (box is not null)
            {
                main.Width = box.MainWidth;
                box = null;
            }
            current = main;
            c.MenuBox = Cards.PopupBox.None;
            main.Highlight = -1;
            Redraw();
            hintShownFor = NoHint;
            warped = null;
        }

        ui.Redraw = Refresh;
        try
        {
            while (true)
            {
                var mouse = (ui.Mouse.X, ui.Mouse.Y);
                if (mouse != lastMouse)
                {
                    lastMouse = mouse;
                    warped = null;
                }
                var (mx, my) = warped ?? mouse;
                Track(mx, my);
                var over = current.At(mx, my) >= 0;
                Hint(over);
                // orig: card_menu R043 (150B) passes every key it does not use to card_global_keys (F1..F6, Ctrl-F1..F5)
                var action = ui.PollWithSheetKeys();
                if (action is not { } a)
                {
                    ui.Idle();
                    continue;
                }
                if (a.IsLeftClick && a.X < WindowX)
                {
                    // orig: R043+0x1E30 (a press with mouse_x < 0x3c): the sheet of the member in that row if there is one
                    if (OpenSheetAt(a.Y, refresh: false))
                    {
                        Refresh();
                    }
                    continue;
                }
                if (a.IsLeftClick)
                {
                    // C: card_mouse R043+0x21D4 follows the pointer before its click test in the same call (the click is the release INT 33h function 6 reports), so a click
                    //   is judged with the line under the pointer highlighted, even when the pointer reached it in the frame of the press.
                    Track(a.X, a.Y);
                }
                if (a.IsLeftClick && current.At(a.X, a.Y) >= 0 && current.Highlight >= 0)
                {
                    // orig: the click part of card_mouse: the press position over a line of the list shown; outside the box (and no box up) the highlighted line is chosen; in the box menu_slot = opt % 4, menu_member = opt / 4, menu_popup = 1 and the main line is chosen; with a box up, a click on the main lines does nothing
                    // C: the line chosen is menu_highlight (from the pointer), not the line under the press; they are the same line unless the pointer moved in between.
                    if (InBox())
                    {
                        var option = current.Lines[current.Highlight].Option;
                        c.MenuSlot = option % 4;
                        c.MenuMember = option / 4;
                        c.MenuPopup = true;
                        ui.WaitForButtonRelease();
                        KeepTheBoxsLayout(box!);
                        return main.Lines[main.Highlight].Option;
                    }
                    if (box is null)
                    {
                        ui.WaitForButtonRelease();
                        return current.Lines[current.Highlight].Option;
                    }
                    continue;
                }
                if (a.Key is Keys.Enter or Keys.Space && current.Highlight != -1)
                {
                    var option = current.Lines[current.Highlight].Option;
                    if (!InBox())
                    {
                        return option;
                    }
                    c.MenuMember = option / 4;
                    c.MenuSlot = option % 4;
                    KeepTheBoxsLayout(box!);
                    return main.Lines[main.Highlight].Option;
                }
                // orig: R043+0x1CE5 (Up, 0x4800) and R043+0x1D46 (Down, 0x5000): only with DS:ED6C == 1, which card_mouse R043+0x21D4 sets when the
                // pointer is inside a line of the list shown (R043+0x2232); else the key is dropped (R043+0x1DEB)
                if (a.Key is Keys.Up or Keys.Down && (over || ui.KeyboardCardMenus))
                {
                    var n = current.Lines.Count;
                    var from = current.Highlight;
                    if (from >= 0)
                    {
                        Highlight(current, from, false);
                    }
                    current.Highlight = a.Key == Keys.Down
                        ? (from + 1) % n
                        : from <= 0 ? n - 1 : from - 1;
                    Highlight(current, current.Highlight, true);
                    // orig: mouse_move(mouse_x, y[line] + 2)
                    var line = current.Lines[current.Highlight];
                    warped = (over ? mx : current.X + 1, line.Y + 2);
                    continue;
                }
                // orig: R043+0x1C77..0x1D46 compare the key with 0x1C0D, 0x3920, 0x1000, 0x4800 and 0x5000 only (R043+0x33C6 takes the F keys):
                // a digit is dropped there; the convenience below is not the original's
                var digit = (a.Key & 0xFF) - '1';
                if (ui.KeyboardCardMenus && !InBox() && a.Key >= '1' && digit is >= 0 and < 9 && digit < main.Lines.Count)
                {
                    return main.Lines[digit].Option;
                }
            }
        }
        finally
        {
            ui.Redraw = null;
            if (hintShownFor != NoHint)
            {
                ClearHint();
            }
        }
    }

    /// <summary>
    /// A choice in the box leaves the box's layout as the menu's (DS:E96E): a card that calls card_menu next without drawing its
    /// options (ChooseOnLastLayout) works on the box's entries, at the places the box had.
    /// </summary>
    // orig: card_mouse R043+0x246B (DS:E96E = DS:EA64 when the pointer entered the box); neither the click (R043+0x27A6) nor Enter (card_menu R043+0x1C8A) puts the main list back
    private void KeepTheBoxsLayout(PopupBox box)
    {
        _lastAreas = box.Layout.Lines;
        _optionX = box.Layout.X;
        _optionWidth = box.Layout.Width;
    }

    /// <summary>Opens the box of an item or saint line next to the line.</summary>
    // orig: card_mouse: DS:EA64 = DS:EB5A (kind 1) or DS:EC50 (kind 2), then R043+0x2ACA(win, win2, R043+0x2CE2(win, menu_highlight, kind)), DS:EE41 = 1, R043+0x3292(1)
    // orig: R043+0x2ACA: font 1; hi = (measure_text_wrapped(win, msg_buf, w, 0, 0, font height, &DS:EA64, 1, 0, 0, 0) >> 16) + 6; y = the line's y, or 0xC1 - hi when y + hi passes 0xC1; x = the list's x + its width, or 0xF5 - w when x + w passes 0xF5; gfx_recolor_rect(card_window, x, y, w, hi, DS:EE59 or 0, 5), the rectangle saved (gfx_save_rect), recoloured back (5 -> DS:EE59 or 0); `if (DS:EE72 >= 0)` filled with 0xFF and outlined in colour 1; then draw_text_wrapped(win, msg_buf, w - 3, x + 3, y + 3, font height, &DS:EA64, 1, 0, 0, 0, 0) in the card's text colour; DS:EE0D / DS:EE0F / DS:EE11 / DS:EE10 = x, y, w, hi
    // orig: R043+0x3292(1): DS:EE13 = DS:E970; DS:E970 = DS:EE0D - DS:E972 (the main lines end where the box begins); 2802(2) puts the width back
    // C: the recolouring around the save turns colour-5 pixels under the box into the text colour when the box goes; the saved rectangle is a driver picture and goes back with colour 0 transparent (Canvas.RestoreTransparent), which is why the text colour is recoloured to 5 before the save.
    // DORMANT: DS:EE72 < 0 (no fill and outline): every card sets DS:EE72 = 7.
    private PopupBox OpenBox(OptionLayout main, OptionArea line)
    {
        var c = context();
        var text = OptionPopupText.Build(c, line.Kind == OptionKind.Saints, ui.Text, _pictureName.StartsWith('x'));
        // orig: the entries' states R043+0x2CE2 writes into the box layout DS:EA64 (+0x2E): what opt_on becomes after a choice in the box
        text.States.CopyTo(c.MenuBoxStates, 0);
        var layout = new CardTextLayout(ui.Text);
        var w = text.Width;
        var hi = layout.MeasureHeight(text.Text, w, new CardTextLayout.Settings { Options = text.States, Leading = 0 }) - 1 + 6;
        var y = line.Y + hi <= BoxBottom ? line.Y : BoxBottom - hi;
        var x = main.X - WindowX + main.Width;
        if (x + w > BoxRight)
        {
            x = BoxRight - w;
        }
        x += WindowX;
        _canvas.Recolor(x, y, w, hi, _textColor, 5);
        var under = _canvas.Save(x, y, w, hi);
        _canvas.Recolor(x, y, w, hi, 5, _textColor);
        _canvas.Fill(x, y, w, hi, Palette.Blank);
        _canvas.Outline(x, y, w - 1, hi - 1, 1);
        var lines = layout.Draw(_canvas, text.Text, x + 3, y + 3, w - 3, new CardTextLayout.Settings
        {
            Options = text.States,
            Color = _textColor,
            Leading = 0,
        });
        var box = new PopupBox(x, y, w, hi, under, new OptionLayout(lines, x + 3, w - 3), main.Width,
            line.Kind == OptionKind.Saints ? Cards.PopupBox.Saints : Cards.PopupBox.Items);
        main.Width = x - main.X;
        return box;
    }

    // orig: the highlight of card_mouse: gfx_recolor_rect(win, x, y[i], w, h[i], DS:EE59, 9) on, (…, 9, DS:EE59) off (DS:EE57 = 9); DS:EE59 is the text colour, 15 or 0; x and w are the list's (the main list's width is cut at the box while one is up)
    // orig: highlight_rect R043+0x1E04 (150B:1374) draft (fn matched): mouse_hide, gfx_recolor_rect(win, x, y, w, h, c1, c2), mouse_show;
    //   card_menu's Up/Down keys use it: off (…, DS:EE57 = 9, DS:EE59) on the old line, on (…, DS:EE59, DS:EE57) on the new one.
    private void Highlight(OptionLayout layout, int index, bool on)
    {
        if (index < 0 || index >= layout.Lines.Count)
        {
            return;
        }
        var area = layout.Lines[index];
        _canvas.Recolor(layout.X, area.Y, layout.Width, area.Height,
            on ? _textColor : Palette.Highlight, on ? Palette.Highlight : _textColor);
    }

    // orig: party_panel_frames(0x100); party_panel_rows(0x100)
    private void RedrawPanel() => panel.Draw(_canvas, context().State.Party);
}
