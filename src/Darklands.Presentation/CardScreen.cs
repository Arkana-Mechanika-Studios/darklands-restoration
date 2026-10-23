using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The card display: the card picture with its frame on the right, the party panel on the
/// left, card texts over the picture and option lines to choose from.
/// </summary>
// orig: the card engine draft (the byte-match summary counts 22 of its 28 functions as matching; R043 = the card engine overlay): card_load R043+0x0A9A (Show), card_text R043+0x0E40 (Text), card_options R043+0x0F2A + R043+0x0F54 (the drawing of a block, Choose), card_menu R043+0x1BD2 + card_mouse R043+0x21D4 (Choose / Select), wait_continue R043+0x32B6, R043+0x3376 (the frame), R043+0x1E82 (Redraw), R043+0x27E6 and R043+0x29D0 (side-window messages), pass_hours_hourglass R043+0x3494 (PassTime); tests: none directly (CardSmokeTests use a scripted screen)
// C: the option menu with its item and saint pop-up boxes is in CardScreen.Menu.cs; the menu bar is GameUi's (MenuBar), reached from every wait.
public sealed partial class CardScreen(GameUi ui, Func<CardContext> context, PartyPanel panel) : ICardScreen
{
    /// <summary>The card window starts 60 pixels from the left, right of the party panel.</summary>
    // orig: gfx_copy_rect(screen_ptr, 0x43, 6, 0xf5, 0xbc, card_window, 7, 6): the card window (DS:017C) is at screen x 0x3c = 60 and its text area at window (7, 6), 0xF5 x 0xBC
    public const int WindowX = 60;

    private const int TextAreaX = WindowX + 7;
    private const int TextAreaY = 6;
    private const int TextAreaWidth = 0xF5;
    private const int TextAreaHeight = 0xBC;

    /// <summary>Option lines are drawn as this colour by xor (colour word 0x380), invisible until the fade (see GreyThePicture).</summary>
    // orig: `if (color == 1) w->color = 0x380;` in draw_text_wrapped root 0265:0134 at the first option marker: colour 0x80, and bits 8..9 = 3 make the driver xor it in (TextMode)
    private const byte OptionColor = 0x80;

    private enum Mode
    {
        /// <summary>No text on the screen (DS:E898 = 0, or a plain text block).</summary>
        Text,

        /// <summary>An option list is shown, still waiting to be read (DS:E898 = 1).</summary>
        Options,

        /// <summary>The option list after the fade (DS:E898 = 2).</summary>
        OptionsFaded,
    }

    private readonly Canvas _canvas = ui.Canvas;
    private MessageFile? _messages;
    private SavedScreen? _picture;
    private string _pictureName = "";

    /// <summary>pic_buf (DS:E3E3): the picture of the last <see cref="Show"/>, which <see cref="ShowPicture"/> does not change.</summary>
    private string _mainPicture = "";
    private byte _textColor;
    private Mode _mode = Mode.Text;
    private int _block = -1;
    private IReadOnlyList<OptionState> _options = [];

    // orig: card_load R043+0x0A9A (+ card_load_near R043+0x0BA0 for ShowPicture): pic_draw_pal(1, "pics\\<pic>.pic") or, for an empty name, a fill of the whole screen with colour 0xFF; DS:EE59 = 15 for a name starting with 'x' else 0; party_panel_frames(0x100) and party_panel_rows(0x100); msg_load(name) reads msgfiles\<name>.msg
    // DIVERGENCE: msg_load refuses a file of 6000 bytes or more with message_box("<name>too large - <size>") (R029+0x5B40
    //   matched: draw_text(side_win, 10, 10, msg), getch(), quit_to_dos()); a modding guard no game file reaches, not reproduced.
    // DIVERGENCE: the original draws the picture onto an off-screen page and shows it at the first text (DS:EE55 = "picture not yet shown": the screen keeps the previous card until then); here it appears at once.
    // C: DS:E959, the block a redraw draws again, is left over from the previous card in the original until the new card shows a text; here a new card starts without one.
    public void Show(string cardName, string picture)
    {
        _messages = ui.Assets.Messages(cardName);
        _block = -1;
        _mode = Mode.Text;
        _mainPicture = picture;
        ShowPicture(picture);
    }

    // orig: card_load(card_name, pic_buf) R043+0x0A9A with pic_buf as the last card_load left it
    public void ShowWithTheLastPicture(string cardName) => Show(cardName, _mainPicture);

    // orig: R043+0x0A9A (150B:000A) again (thunk 09C0:1EA5) with the card's picture: the picture and the party column
    public void Reload() => ShowPicture(_pictureName);

    public void ShowPicture(string picture)
    {
        _pictureName = picture;
        _textColor = picture.StartsWith('x') ? Palette.White : Palette.Black;
        if (picture.Length > 0 && ui.Assets.PictureExists(picture))
        {
            _canvas.DrawPicture(ui.Assets.Picture(picture));
        }
        else
        {
            _canvas.Fill(0, 0, Canvas.Width, Canvas.Height, Palette.Blank);
        }
        panel.Draw(_canvas, context().State.Party);
        _picture = _canvas.SaveScreen();
        DrawFrame();
    }

    // orig: card_text(idx, how) R043+0x0E40: flush_keys; DS:E959 = idx; how 0 or 2 restore the text area from the picture page and draw the
    // frame (28e6), how 1 (or more than 2) fill the text area with colour 0xFF first; then R043+0x0F54(card_window, idx, 0, how) draws the
    // block (colour 0 when how == 1 else DS:EE59), wait_continue(), flush_keys()
    public void Text(int block, TextFrame frame = TextFrame.OnPicture)
    {
        ui.FlushKeys();
        PrepareWindow(frame);
        _block = block;
        _mode = Mode.Text;
        DrawBlock(block, frame == TextFrame.BlankPage ? Palette.Black : _textColor, []);
        WaitToContinue();
        ui.FlushKeys();
    }

    // orig: card_options(idx) R043+0x0F2A + 04c4 mode 1 (draw, wait_continue, fade the picture and recolour the option lines) + card_menu R043+0x1BD2 (flush_keys, then the mouse / key loop)
    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null) =>
        Choose(block, options, hint, null);

    // orig: card_menu R043+0x1BD2 on the hotspot list DS:E96E the last draw_text_wrapped left (see ICardScreen.ChooseOnLastLayout)
    // C: the lines stay where that text put them; one is choosable when its option's state is enabled now. Nothing is drawn or faded.
    // DIVERGENCE: with no line to choose (a last text without option lines) the original's card_menu waits for ever (only the menu bar
    // and the portraits answer); Select takes line 0 here, as it does for any empty option list.
    public int ChooseOnLastLayout(IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint)
    {
        ui.FlushKeys();
        _options = options;
        var areas = _lastAreas.Where(a => a.Option < options.Count && options[a.Option] == OptionState.Enabled).ToList();
        return Select(areas, hint, popupHint);
    }

    /// <summary>The option lines of the last text laid out (DS:E96E), kept for a menu that draws none.</summary>
    private List<OptionArea> _lastAreas = [];

    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint) =>
        Choose(block, options, hint, popupHint, null);

    // orig: card_options (draw, wait_continue, fade), then whatever the card script calls (R061+0x2278: advance_hours(1)), then card_menu
    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint, Action? afterOptions)
    {
        ui.FlushKeys();
        PrepareWindow(TextFrame.OnPicture);
        _block = block;
        _options = options;
        _mode = Mode.Options;
        var areas = DrawBlock(block, _textColor, options);
        WaitToContinue();
        GreyThePicture(areas, animate: true);
        _mode = Mode.OptionsFaded;
        afterOptions?.Invoke();
        ui.FlushKeys();
        return Select(areas, hint, popupHint);
    }

    // orig: R043+0x29D0 (1F40): the message goes into the side window next to member `who` (-2 = the whole column): text centred in a 55-pixel column at x 3, y + 4 (y = 0x51 for the whole column, who * 0x28 + 1 for a member), then it waits for a key or the left button and eats the key
    // C: for the whole column it first clears the entire panel strip (3, 0, 0x36 x 0xC6) with the background sprite DS:A628 and centres the text in the middle at y 0x51; for one member the row (3, y, 0x36 x 0x26) with DS:A626.
    // DIVERGENCE: the text colour is the caller's (side_win->color = p_8); ICardScreen.Tell has none, the text is drawn white. The original does not put the panel back afterwards (its callers redraw it); here the covered area is restored when the message is dismissed.
    public void Tell(string message, int member = OptionHint.WholeParty)
    {
        var saved = _canvas.Save(0, 0, WindowX, Canvas.Height);
        var y = member == OptionHint.WholeParty ? 0x51 : member * 40 + 1;
        SideWindowBackground(member);
        new CardTextLayout(ui.Text).Draw(_canvas, TextPainter.Encode(message), 3, y + 4, 0x37,
            new CardTextLayout.Settings { Color = Palette.White, Leading = 0, Alignment = TextAlignment.Center });
        // C: the wait is `while (!key_pressed() && !(mouse_buttons & 1)) menu_bar_run(...)`, then the button is waited out and the key eaten: any key, or the left button anywhere.
        while (true)
        {
            var action = ui.WaitForAction();
            if (action.IsKey || action.IsLeftClick)
            {
                ui.WaitForButtonRelease();
                break;
            }
        }
        _canvas.Restore(saved);
    }

    // orig: show_message root 0000:24BC(text, ticks) (MessageBox.Show)
    // C: a framed box for the ticks, not the side window's wait for a key (Tell)
    public void ShowMessage(string text, int ticks) => new MessageBox(ui).Show(text, ticks * 1000 / 60);

    /// <summary>The hourglass while hours pass (see <see cref="Hourglass"/>).</summary>
    public void PassTime(int hours) => Hourglass.Show(ui, context().State.Date, hours);

    // orig: the first lines of 04c4 for a text block, card_text's two arms, and R043+0x3376 (the frame): restores the text area from the picture page (gfx_copy_rect(screen_ptr, 0x43, 6, 0xf5, 0xbc, card_window, 7, 6)), fills it with 0xFF for how == 1
    private void PrepareWindow(TextFrame frame)
    {
        if (_picture is not null)
        {
            RestoreWindowFrom(_picture);
        }
        if (frame == TextFrame.BlankPage)
        {
            _canvas.Fill(TextAreaX, TextAreaY, TextAreaWidth, TextAreaHeight, Palette.Blank);
        }
        DrawFrame();
    }

    // DIVERGENCE: the whole window right of the panel is restored from the picture (the original restores only the text area and redraws the frame, which covers the rest identically)
    private void RestoreWindowFrom(SavedScreen picture)
    {
        for (var y = 0; y < Canvas.Height; y++)
        {
            Array.Copy(picture.Pixels, y * Canvas.Width + WindowX, _canvas.Screen.Pixels, y * Canvas.Width + WindowX, Canvas.Width - WindowX);
        }
        picture.Palette.CopyTo(_canvas.Screen.Palette, 0);
    }

    /// <summary>The card frame: four border pictures around the text area.</summary>
    // orig: R043+0x3376 (150B:28E6): gfx_draw_pic of DS:A61E (rpbdrtop) at (7, 0), DS:A624 (rpbdrrgt) at (0xFC, 0),
    // DS:A622 (rpbdrlft) at (0, 0), DS:A620 (rpbdrbtm) at (7, 0xC2); the pictures are loaded once at start-up from pics\rpbdr*.pic,
    // filled by main root 03A1:000A
    private void DrawFrame()
    {
        DrawIfPresent("rpbdrtop", WindowX + 7, 0);
        DrawIfPresent("rpbdrrgt", WindowX + 0xFC, 0);
        DrawIfPresent("rpbdrlft", WindowX, 0);
        DrawIfPresent("rpbdrbtm", WindowX + 7, 0xC2);
    }

    private void DrawIfPresent(string picture, int x, int y)
    {
        if (ui.Assets.PictureExists(picture))
        {
            _canvas.Draw(ui.Assets.Picture(picture).Image, x, y, transparent: true);
        }
    }

    // orig: the block drawer R043+0x0F54 (04c4): text_expand(idx, msg_buf) (R043+0x118E, see TextVariables) then
    // draw_text_wrapped(card_window, msg_buf, w - 10, x + 5, y + 5, font_height, &DS:E96E, 1, 0, 1, 1, 1): init, no stop, leading 1, drop
    // cap on, option colour on
    // C: the block's x, y and width come from the .msg file (clamped to 0..200 for y and 0..320 for x by msg_load: clamp_range(0, 200, y), clamp_range(0, 0x140, x)).
    // DIVERGENCE: the clamps are not applied here (the data files stay inside them).
    private List<OptionArea> DrawBlock(int block, byte color, IReadOnlyList<OptionState> options)
    {
        if (_messages is null || block < 0 || block >= _messages.Blocks.Count)
        {
            return [];
        }
        var b = _messages.Blocks[block];
        _lastBlock = b;
        var c = context();
        var text = TextVariables.Expand(b.Text, c);
        var layout = new CardTextLayout(ui.CardText);
        var areas = layout.Draw(_canvas, text, WindowX + b.X + 5, b.Y + 5, b.Width - 10, new CardTextLayout.Settings
        {
            Options = options,
            Conditions = c.Variables.Conditions,
            Color = color,
            OptionColor = options.Count > 0 ? OptionColor : null,
            OptionMode = TextMode.Xor,
            DropCap = DropCap,
        });
        // orig: draw_text_wrapped leaves the option lines in the hotspot list DS:E96E as soon as it has laid the text out (before wait_continue), which is what a scripted host's option:<n> step reads
        _choosing = new OptionLayout(areas, WindowX + b.X + 5, b.Width - 10);
        return _lastAreas = areas;
    }

    private Content.Formats.Graphics.IndexedImage? DropCap(byte letter) => DropCaps.Get(ui.Assets, letter);

    /// <summary>
    /// After reading, the picture fades: its colours 0x20..0x7F turn grey, its colours
    /// 0xA0..0xFF move 70% toward a light colour, and the option lines stand out.
    /// </summary>
    // orig: the second half of 04c4 (mode 1) after wait_continue: `if (DS:9069 == 0)` palette_fade(7, 100, 0x1f or 0xff, 0x46, 3) = R043+0x3838 (17E5:0008) matched, then the inline asm loop over A000:0000 (FA00 bytes: every pixel 0x20..0x7F becomes colour 7) and gfx_recolor_rect(card_window, x, y[i], w, h[i], 7, text colour) for each option line
    // C: the option lines were drawn as colour 0x80 by xor (OptionColor); pictures repeat colours 0x20..0x7F in 0xA0..0xFF, so the lines are
    //   invisible until the fade separates the two halves: the xor-ed pixels of a picture in 0xA0..0xFF fall in 0x20..0x7F and turn grey,
    //   the remap makes them colour 7 and the recolour makes the choosable lines the text colour; lines shown but not choosable stay grey.
    // DORMANT: DS:9069 != 0 (a no-fade setting) skips the fade and the grey remap, and the recolour loop then reads an uninitialised local in R043+0x1E82; nothing sets DS:9069 here.
    private void GreyThePicture(List<OptionArea> areas, bool animate = false)
    {
        FadePalette(animate);
        var pixels = _canvas.Screen.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] is >= 0x20 and < 0x80)
            {
                pixels[i] = Palette.Grey;
            }
        }
        var block = _lastBlock;
        if (block is null)
        {
            return;
        }
        foreach (var area in areas)
        {
            _canvas.Recolor(WindowX + block.X + 5, area.Y, block.Width - 10, area.Height, Palette.Grey,
                _pictureName.StartsWith('x') ? Palette.White : Palette.Black);
        }
        _optionX = WindowX + block.X + 5;
        _optionWidth = block.Width - 10;
    }

    private MessageBlock? _lastBlock;
    private int _optionX;
    private int _optionWidth;

    /// <summary>The fade's step count: e * DS:5734 / 10 with e = 3 and DS:5734 the palette uploads that fit in 10 BIOS ticks.</summary>
    // C: every upload waits for the vertical retrace (drv+0x01B6), so DS:5734 is about 38 at 70 Hz (10 ticks = 549 ms) and the fade has
    //   3 * 38 / 10 = 11 steps of two uploads each (about 0.3 s).
    // DIVERGENCE: the original measures DS:5734 once per run (R043+0x3A1C) on the machine; a fixed 38 is used here.
    private const int FadeSteps = 3 * 38 / 10;

    // orig: R043+0x3838 (17E5:0008) matched + R043+0x3AE6 (1810:0006) asm, palette_fade(a, b, c, d, e): the DAC is read; per step i (1..steps) colours 0x20..0x7F become
    //   blend(start, colour a, i * saa) and 0xA0..0xFF blend(start, colour c, i * sca), saa = b * 0xFFFF / (steps * 100), sca = d * 0xFFFF /
    //   (steps * 100) (long division); blend R043+0x3AE6 (1810:0006, asm) = (src * (0xFFFF - y) + target * y + 0x8000) >> 16 on the 6-bit DAC
    //   values (checked against asm); the card calls palette_fade(7, 100, 0x1F or 0xFF, 70, 3). tests: tools/visuals/compare.py (the end of the fade and the departure choice)
    private void FadePalette(bool animate)
    {
        var palette = _canvas.Screen.Palette;
        var start = (uint[])palette.Clone();
        var grey = start[Palette.Grey];
        var light = start[_pictureName.StartsWith('x') ? 0x1F : 0xFF];
        var saa = (int)(100 * 0xFFFFL / (FadeSteps * 100));
        var sca = (int)(70 * 0xFFFFL / (FadeSteps * 100));
        for (var step = animate ? 1 : FadeSteps; step <= FadeSteps; step++)
        {
            for (var i = 0x20; i < 0x80; i++)
            {
                palette[i] = Blend(start[i], grey, step * saa);
            }
            for (var i = 0xA0; i <= 0xFF; i++)
            {
                palette[i] = Blend(start[i], light, step * sca);
            }
            if (animate)
            {
                ui.Present();
                ui.Clock.Sleep(2 * 1000 / 70);
            }
        }
    }

    private static uint Blend(uint from, uint to, int y)
    {
        uint Channel(int shift)
        {
            // C: the screen keeps 8-bit colours widened from the 6-bit DAC values (v << 2 | v >> 4), so v = c >> 2 exactly.
            var source = (long)((from >> shift) & 0xFF) >> 2;
            var target = (long)((to >> shift) & 0xFF) >> 2;
            var v = (int)((source * (0xFFFF - y) + target * y + 0x8000) >> 16) & 0xFF;
            return (uint)((v << 2) | (v >> 4)) << shift;
        }
        return 0xFF000000u | Channel(16) | Channel(8) | Channel(0);
    }

    // orig: R043+0x27E6 (150B:1D56): nothing at Expert (game_difficulty == 2); the side window (colour 15) gets the strip background, then the text: "The chance of success is " + N% (Basic) or a quality word (quality_words[pct / 20], "hopeless" for 0) for pct >= 0, "ERROR!!!" for pct == -2, otherwise the message the option's function put in msg_buf; centred in a 50-pixel column at x 5, vertically centred in the strip (h - text height) / 2 and at least 0
    // C: pct is clamped to 0..100 first (clamp_range), so "excellent" (index 5) is the highest word an option can show (quality_words has "superb" at 6).
    private void ShowHint(OptionHint hint)
    {
        var state = context().State;
        if (state.Difficulty == Difficulty.Expert)
        {
            return;
        }
        string text;
        if (hint.Chance >= 0)
        {
            var pct = Math.Clamp(hint.Chance, 0, 100);
            text = "The chance of success is " + (state.Difficulty == Difficulty.Basic
                ? pct.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
                : pct != 0 ? CardContext.QualityWords[pct / 20] : "hopeless");
        }
        else
        {
            text = hint.Chance == -2 ? "ERROR!!!" : hint.Message;
        }
        var (top, height) = hint.Member == OptionHint.WholeParty ? (1, 0xC6) : (hint.Member * 40 + 1, 0x26);
        SideWindowBackground(hint.Member);
        var layout = new CardTextLayout(ui.Text);
        var bytes = TextPainter.Encode(text);
        var textHeight = layout.MeasureHeight(bytes, 0x32, 0);
        var offset = Math.Max(0, (height - (textHeight - 1)) / 2);
        layout.Draw(_canvas, bytes, 5, top + offset, 0x32,
            new CardTextLayout.Settings { Color = Palette.White, Leading = 0, Alignment = TextAlignment.Center });
    }

    /// <summary>The background of the side window behind a message: one member's row, or the whole column.</summary>
    // orig: in R043+0x27E6 and R043+0x29D0: for the whole column `for (v = 0; v < 0xc6; v += 0xb) gfx_draw_pic(screen_ptr, 3, v, DS:A628)` then gfx_copy_rect(screen_ptr, 3, 1, 0x36, 0xc6, side_win, 3, 1); for member `who` gfx_draw_pic(side_win, 3, who * 0x28 + 1, DS:A626)
    // C: the column strips are drawn on the picture page (screen_ptr) and only its rows 1 .. 0xC6, columns 3 .. 0x38 are copied to the screen; the strips stay on that page's panel column, which nothing here copies back (Redraw draws the panel again).
    private void SideWindowBackground(int member)
    {
        var sprites = ui.Assets.CommonSprites;
        if (member == OptionHint.WholeParty)
        {
            var column = new Clip(3, 1, 3 + 0x36, 1 + 0xC6);
            for (var v = 0; v < 0xC6; v += 0xB)
            {
                SpriteDrawing.Draw(_canvas, sprites.ColumnBackground, 3, v, column);
            }
        }
        else
        {
            SpriteDrawing.Draw(_canvas, sprites.RowBackground, 3, member * 40 + 1);
        }
    }

    private void ClearHint()
    {
        if (context().State.Difficulty != Difficulty.Expert)
        {
            panel.Draw(_canvas, context().State.Party);
        }
    }

    // orig: wait_continue R043+0x32B6: loops menu_bar_run (the hot keys), the party_icons_dirty redraw, `mouse_buttons & 1 && !13a0()` ends it (a left press that did not open a member sheet), a key: Enter (0x1C0D) or Space (0x3920, turned into Enter) end it, Alt-Q (0x1000) goes to quit_to_dos, any other key goes to card_global_keys; finally it waits out the button
    // C: a press on a portrait whose slot is empty (or beyond party_size) is not a sheet, so it continues the card like any other press; Alt-Q answered "n" also ends the wait (see HotKeys).
    private void WaitToContinue()
    {
        ui.Redraw = Redraw;
        try
        {
            while (true)
            {
                var action = ui.WaitForAction();
                if (action.Key is Keys.Enter or Keys.Space)
                {
                    return;
                }
                if (action.IsLeftClick)
                {
                    if (action.X < WindowX)
                    {
                        if (OpenSheetAt(action.Y, refresh: true))
                        {
                            continue;
                        }
                    }
                    ui.WaitForButtonRelease();
                    return;
                }
            }
        }
        finally
        {
            ui.Redraw = null;
        }
    }

    // orig: R043+0x1E30 (150B:13A0): `if (mouse_x < 0x3c) { m = mouse_y / 0x28; if (party_size > m && party[m].status) { member_sheet_vec(m); party_panel_frames; party_panel_rows; 13f2(); return 1; } } return 0`
    private bool OpenSheetAt(int y, bool refresh)
    {
        var member = y / PartyPanel.RowHeight;
        var party = context().State.Party;
        if (member < party.Members.Count && party.Members[member].IsPresent)
        {
            context().Screens.CharacterSheet(member);
            if (refresh)
            {
                Redraw();
            }
            return true;
        }
        return false;
    }

    // orig: R043+0x1E82 (150B:13F2): redraws the card after a full screen was shown over it: pic_draw_pal of the picture DS:EE5B, set_palette, msg_load(DS:E957), text_expand(DS:E959) and the block as 04c4 drew it: DS:E898 == 1 the option list unfaded, == 2 the option list with the fade and the recolouring, anything else the plain text
    // C: the plain-text arm draws the block with the card colour DS:EE59 over the PICTURE (a blank-page text, how == 1, comes back as ordinary picture text), and the fade arm's recolour loop uses the grey remap's colour variable even when the fade is switched off (DS:9069 != 0). Both are reproduced as far as they apply here: the blank page is not restored.
    private void Redraw()
    {
        if (_pictureName.Length == 0 && _picture is null)
        {
            return;
        }
        ShowPicture(_pictureName);
        switch (_mode)
        {
            case Mode.Text:
                if (_block >= 0)
                {
                    PrepareWindow(TextFrame.OnPicture);
                    DrawBlock(_block, _textColor, []);
                }
                break;
            case Mode.Options:
                PrepareWindow(TextFrame.OnPicture);
                DrawBlock(_block, _textColor, _options);
                break;
            case Mode.OptionsFaded:
                PrepareWindow(TextFrame.OnPicture);
                GreyThePicture(DrawBlock(_block, _textColor, _options));
                break;
        }
    }
}
