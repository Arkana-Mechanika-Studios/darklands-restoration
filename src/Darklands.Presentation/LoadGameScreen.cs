using Darklands.Content.Formats.Graphics;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The load screen (PICS\LOADSCRN.PIC): the saved games in a list of nine on the right, each
/// "place year/month/day  hour" over its comment, the highlighted one chosen with the arrows,
/// Home and End or a click; on the left the buttons Load Saved Game (L), Delete Saved Game (D)
/// and, from the start screen, Return to Main Menu (R). Esc leaves.
/// </summary>
// orig: R037+0x81B2 (1C7D:0002) with its set-up R037+0x83FE, draw R037+0x847E,
// key handler R037+0x84BA, list R037+0x8896, labels R037+0x8B0E, clean-up
// R037+0x83C8 (mem_free of the button, the fonts back), delete R037+0x9B76, count R037+0x9C10, all
// matched; tests: LoadScreenTests
// C: DS:E89A is 1 when the start screen opened it: only then is the third button drawn and do R and its button leave.
// C: root 047C:1320(6, 0, 0, 0) on leaving (0218) and the 0568/056f pair around the set-up are the graphics driver's page
// handling; nothing here needs them.
public sealed class LoadGameScreen(GameUi ui, SaveFolder folder, IReadOnlyList<string> labels)
{
    private const int Rows = 9;
    private const int ButtonsX = 0x52;
    private const byte LabelColor = 8;
    private const byte LabelHighlight = 9;
    private const byte LineColor = 0x10;
    private const byte LineShadow = 0x11;
    private const byte LineHighlight = 0x12;

    // The screen's globals: DS:8C4C saves, DS:8C52 first shown, DS:8C50 cursor row, DS:8C54 rows shown, R136+0x1145E the rows' file names.
    private int _count;
    private int _first;
    private int _cursor;
    private int _shown;
    private readonly string[] _names = new string[Rows];
    private bool _fromStart;
    private SavedScreen _page = null!;
    private Pic? _button;

    /// <summary>Runs the screen; returns the game the player loaded, or null when he left it.</summary>
    /// <param name="fromStartScreen">The start screen opened it (DS:E89A == 1).</param>
    /// <param name="load">Reads the chosen file (R037+0x8ED4 behind the "Loading Game" box).</param>
    // orig: R037+0x81B2: `while (mouse_buttons & 1);` then the loop: hover over the buttons (0x44 < x < 0xc6, 0x2d < y < 0x5a)
    // lights the row's label (colour 8 -> 9), a click there picks L / D / (from the start screen) Esc by (y - 0x2d) / 15, a click in the
    // list (0xc3 < x < 0x140, 0x14 < y < 0xa5) moves the cursor to row (y - 0x14) / 16 if it is shown; a waiting key replaces a click;
    // Esc (and from the start screen R/r) leaves with 0, a key the handler answers with 0x11b loads (1)
    // C: the mouse tests read the button state, then wait for its release and take y after it; the hover tests the row each pass.
    public LoadedGame? Run(bool fromStartScreen, Func<string, LoadedGame> load)
    {
        _fromStart = fromStartScreen;
        SetUp();
        Draw();
        ScreenDrawing.WaitForRelease(ui);
        var lit = -1;
        while (true)
        {
            var action = ui.PollFrame();
            var mouse = ui.Mouse;
            var key = Keys.None;
            if (action is null && (mouse.Buttons & MouseButtons.Left) == 0 && InButtons(mouse.X, mouse.Y))
            {
                var row = (mouse.Y - 0x2D) / 15;
                if (row != lit)
                {
                    if (lit != -1)
                    {
                        ui.Canvas.Recolor(0x45, lit * 15 + 0x2F, 0x80, 8, LabelHighlight, LabelColor);
                    }
                    lit = row;
                    ui.Canvas.Recolor(0x45, row * 15 + 0x2F, 0x80, 8, LabelColor, LabelHighlight);
                }
            }
            else if (action is { IsLeftClick: true } click && InButtons(click.X, click.Y))
            {
                ScreenDrawing.WaitForRelease(ui);
                key = ((ui.Mouse.Y - 0x2D) / 15) switch
                {
                    0 => LoadKey,
                    1 => DeleteKey,
                    // C: the third row answers only from the start screen.
                    2 when _fromStart => Keys.Escape,
                    _ => Keys.None,
                };
            }
            else if (action is { IsLeftClick: true } pick && pick.X > 0xC3 && pick.X < 0x140 && pick.Y > 0x14 && pick.Y < 0xA5)
            {
                ScreenDrawing.WaitForRelease(ui);
                var row = (ui.Mouse.Y - 0x14) / 16;
                if (_shown > row)
                {
                    Highlight(false);
                    _cursor = row;
                    Highlight(true);
                }
            }
            if (action is { IsKey: true })
            {
                key = action.Value.Key;
            }
            if (key == Keys.Escape || _fromStart && Keys.IsLetter(key, 'r'))
            {
                return null;
            }
            if (key != Keys.None && Handle(key, load) is { } loaded)
            {
                return loaded;
            }
        }
    }

    private const int LoadKey = 0x264C;
    private const int DeleteKey = 0x2044;

    private static bool InButtons(int x, int y) => x > 0x44 && x < 0xC6 && y > 0x2D && y < 0x5A;

    // orig: R037+0x83FE: pic_draw_pal(1, "pics\\loadscrn.pic"), DS:E88A = pic_load("pics\\buttnCR2.pic"), back_win_colour = 8,
    // fonts 1, DS:8C4C = R037+0x9C10(), DS:8C52 = 0, DS:8C50 = 0
    private void SetUp()
    {
        ui.Canvas.DrawPicture(ui.Assets.Picture("loadscrn"));
        _page = ui.Canvas.SaveScreen();
        _button = ui.Assets.PictureExists("buttncr2") ? ui.Assets.Picture("buttncr2") : null;
        _count = folder.Count();
        _first = 0;
        _cursor = 0;
    }

    // orig: R037+0x847E: the page copied to the screen, the labels (095e), the list (06e6)
    private void Draw()
    {
        ui.Canvas.Restore(_page);
        DrawLabels();
        DrawList();
    }

    // orig: R037+0x8B0E: colour 8; s[0] at (0x52, 0x2f), s[1] at (0x52, 0x3e), from the start screen s[2] at (0x52, 0x4d)
    private void DrawLabels()
    {
        ui.Text.DrawCoded(ui.Canvas, ButtonsX, 0x2F, Label(0), LabelColor);
        ui.Text.DrawCoded(ui.Canvas, ButtonsX, 0x3E, Label(1), LabelColor);
        if (_fromStart)
        {
            ui.Text.DrawCoded(ui.Canvas, ButtonsX, 0x4D, Label(2), LabelColor);
        }
    }

    private string Label(int i) => i < labels.Count ? labels[i] : "";

    /// <summary>The list of saved games from the first shown, nine at most, and the cursor's highlight.</summary>
    // orig: R037+0x8896 (1C7D:06E6): the list area (0xc3, 0x14, 0x7d x 0x91) from the page, then for each saves\*.sav
    // after the first DS:8C52: sprintf("%s %d/%d/%d  %d", name, year, month + 1, day, hour) and the comment, each in colour 0x11 at
    // (0xc6, row * 16 + 7 / + 0xf) and in colour 0x10 one pixel up and left; `if (DS:8C4C <= DS:8C50) DS:8C50 = DS:8C54 - 1;` and the
    // highlight (0x10 -> 0x12 in 0xc4, row * 16 + 0x16, 0x7b x 0x10)
    // C: the rows are counted from 1 when drawn (DS:8C54 is incremented first), so row r is at y = (r + 1) * 16 + 6.
    // C: with no saved game the function returns before its mouse_show: the pointer stays hidden (the driver counts hides).
    // DIVERGENCE: the pointer stays visible (the host's pointer has no hide count).
    // C: the names of rows not shown this time stay in R136+0x1145E from an earlier list (a later delete of such a row reads them).
    private void DrawList()
    {
        ui.Canvas.CopyFrom(_page, 0xC3, 0x14, 0x7D, 0x91);
        _shown = 0;
        var names = folder.Names();
        if (names.Count == 0)
        {
            ui.Present();
            return;
        }
        var skip = 0;
        foreach (var name in names)
        {
            if (skip++ < _first)
            {
                continue;
            }
            if (++_shown > Rows)
            {
                _shown--;
                break;
            }
            _names[_shown - 1] = name;
            var s = folder.Summary(name);
            var line = $"{s.Place} {s.Year}/{s.Month + 1}/{s.Day}  {s.Hour}";
            var y = _shown << 4;
            ui.Text.Draw(ui.Canvas, 0xC6, y + 7, line, LineShadow);
            ui.Text.Draw(ui.Canvas, 0xC6, y + 0xF, s.Comment, LineShadow);
            ui.Text.Draw(ui.Canvas, 0xC5, y + 6, line, LineColor);
            ui.Text.Draw(ui.Canvas, 0xC5, y + 0xE, s.Comment, LineColor);
        }
        if (_count <= _cursor)
        {
            _cursor = _shown - 1;
        }
        Highlight(true);
    }

    // orig: gfx_recolor_rect(screen_win, 0xc4, (DS:8C50 << 4) + 0x16, 0x7b, 0x10, 0x10, 0x12) and back (0x12 -> 0x10)
    private void Highlight(bool on)
    {
        if (on)
        {
            ui.Canvas.Recolor(0xC4, (_cursor << 4) + 0x16, 0x7B, 0x10, LineColor, LineHighlight);
        }
        else
        {
            ui.Canvas.Recolor(0xC4, (_cursor << 4) + 0x16, 0x7B, 0x10, LineHighlight, LineColor);
        }
        ui.Present();
    }

    /// <summary>The key handler: the loaded game when L loaded one, else null.</summary>
    // orig: R037+0x84BA (1C7D:030A): 0x1000 quit, 0x264c/0x266c load, 0x2044/0x2064 delete, 0x4700 home,
    // 0x4800/0x4b00 up, 0x4d00/0x5000 down, 0x4f00 end
    // C: the original takes the exact codes; here any key whose character is l or d (either case) counts, as on the start screen.
    private LoadedGame? Handle(int key, Func<string, LoadedGame> load)
    {
        if (key == Keys.AltQ)
        {
            // C: 0218 frees the button picture before quit_to_dos asks; after "no" the screen goes on without it.
            if (new MessageBox(ui).Ask("Quit to DOS?"))
            {
                throw new QuitGameException();
            }
            // DIVERGENCE: with GameOptions.CleanDrawing (Fixed, Classic + comfort) the button picture is kept and still drawn when pressed.
            if (!ui.Options.CleanDrawing)
            {
                // C: a pressed button then shows its label alone (checked against the original's screen: vis10 session D-751050)
                _button = null;
            }
            return null;
        }
        if (Keys.IsLetter(key, 'l'))
        {
            return Load(load);
        }
        if (Keys.IsLetter(key, 'd'))
        {
            Delete();
            return null;
        }
        switch (key)
        {
            case Keys.Home:
                _cursor = _first = 0;
                DrawList();
                break;
            case Keys.Up:
            case Keys.Left:
                Highlight(false);
                if (--_cursor < 0)
                {
                    if (_first != 0)
                    {
                        _first--;
                        _cursor = 0;
                        DrawList();
                    }
                    else
                    {
                        _cursor = 0;
                    }
                }
                Highlight(true);
                break;
            case Keys.Right:
            case Keys.Down:
                Highlight(false);
                if (++_cursor >= _shown)
                {
                    // C: `if (DS:8C4C - 9 < ++DS:8C52) { DS:8C50--; DS:8C52--; } else { DS:8C50--; redraw }`
                    if (_count - Rows < ++_first)
                    {
                        _cursor--;
                        _first--;
                    }
                    else
                    {
                        _cursor--;
                        DrawList();
                    }
                }
                Highlight(true);
                break;
            case Keys.End:
                // orig: DS:8C52 = clamp_range(0, 9999, DS:8C4C - 9); DS:8C50 = clamp_range(0, DS:8C4C, 8);
                // C: with fewer than nine games the cursor is put one past them; the list puts it back on the last row.
                _first = Math.Clamp(_count - Rows, 0, 9999);
                _cursor = Math.Clamp(8, 0, Math.Max(_count, 0));
                DrawList();
                break;
        }
        return null;
    }

    // orig: case 0x264c/0x266c: `if (DS:8C54 > DS:8C50)`: buttnCR2 drawn at (0x42, 0x2c), s[0] at (0x52, 0x30) in colour 8 (the pressed
    // button), R037+0x8ED4(R136+0x1145E[DS:8C50]), the party's shield pictures loaded from charactr.tmp's slots, return 0x11b
    // C: the shield pictures are the party panel's (loaded on demand); root 0000:2652(0x35) is an empty function.
    private LoadedGame? Load(Func<string, LoadedGame> load)
    {
        if (_shown <= _cursor)
        {
            return null;
        }
        if (_button is { } button)
        {
            ui.Canvas.Draw(button.Image, 0x42, 0x2C);
        }
        ui.Text.DrawCoded(ui.Canvas, ButtonsX, 0x30, Label(0), LabelColor);
        ui.Present();
        return load(_names[_cursor]);
    }

    // orig: R037+0x84BA (1C7D:030A) case 0x2044/0x2064: the three labels' highlight off (0x45, 0x2f, 0x80 x 0x2d, 9 -> 8), buttnCR2 at
    // (0x41, 0x3b), s[1] at (0x52, 0x3f); ask_yes_no("Delete this Game?"): 9b76(R136+0x1145E[DS:8C50]) (SaveFolder.Delete) and the
    // list again; then the button area
    // (0x41, 0x3b, 0x82 x 0xe) from the page, s[1] at (0x52, 0x3f) and its highlight (0x45, 0x3e, 0x80 x 8, 8 -> 9)
    // C: the label comes back one pixel lower (0x3f) than the first drawing put it (0x3e).
    // C: there is no test that a row is shown: with an empty list the name is a stale one (whose file is gone).
    private void Delete()
    {
        ui.Canvas.Recolor(0x45, 0x2F, 0x80, 0x2D, LabelHighlight, LabelColor);
        if (_button is { } button)
        {
            ui.Canvas.Draw(button.Image, 0x41, 0x3B);
        }
        ui.Text.DrawCoded(ui.Canvas, ButtonsX, 0x3F, Label(1), LabelColor);
        ui.Present();
        if (new MessageBox(ui).Ask("Delete this Game?"))
        {
            if (_names[_cursor] is { } name && folder.Delete(name, Playing))
            {
                _count--;
            }
            DrawList();
        }
        ui.Canvas.CopyFrom(_page, 0x41, 0x3B, 0x82, 0xE);
        ui.Text.DrawCoded(ui.Canvas, ButtonsX, 0x3F, Label(1), LabelColor);
        ui.Canvas.Recolor(0x45, 0x3E, 0x80, 8, LabelColor, LabelHighlight);
        ui.Present();
    }

    /// <summary>The name of the game being played (DS:A680): it cannot be deleted.</summary>
    public string? Playing { get; init; }
}
