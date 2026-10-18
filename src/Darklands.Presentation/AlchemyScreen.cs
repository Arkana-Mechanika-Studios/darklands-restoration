using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Presentation;

// orig: alchemy_screen R033+0x0000 (1462:0000) draft (register-level diffs); tests: CampAndSheetRulesTests (the bench), visual
// C: the menu bar (menu_bar_run) comes with every GameUi.Poll (MenuBar).
// DIVERGENCE: the redraw on party_icons_dirty after the menu bar is not part of this screen.
/// <summary>
/// The alchemy screen. The batch page lists the alchemist's formulas on a scroll (Up/Down or
/// a click picks one) with "Use this formula" and "Do something else"; the formula page sets
/// the number of batches (More/Fewer), shows the chance of success and the ingredients, and
/// "Satisfied with this" brews them. Also the formula page the character sheet opens.
/// </summary>
public sealed class AlchemyScreen(GameUi ui, PartyPanel panel, IReadOnlyList<Formula> formulas)
{
    private const int VisibleRows = 17;
    private const int ListX = 0xC0;
    private const int ListTop = 0x2D;
    private const int RowHeight = 8;
    private const int MenuX = 0x50;
    private const int MenuTop = 0x24;
    private const byte ListColor = 4;
    private const byte ListHighlight = 0x0C;
    private const byte MenuColor = 8;
    private const byte MenuGreyed = 7;
    private const byte MenuHover = 0x0B;
    private const byte HeadingColor = 0x0E;
    private const int KeyD = 0x2064;
    private const int KeyCapitalD = 0x2044;

    private GameState _s = null!;
    private BrewingBench _bench = null!;
    private SavedScreen _back = null!;
    private int _current;
    private int _row;
    private bool _formulaPage;
    private int _hover = -1;
    private bool _canAddBatch;

    private Batch Selected => _bench.Batches[_current];

    // orig: R033+0x0000 (the key loop); leaving: R033+0x0628 (fonts, frees DARKLAND.ALC)
    /// <summary>The alchemy screen for <paramref name="member"/>; <paramref name="place"/> (the camp mode) goes into the brew.</summary>
    public void Use(GameState state, int member, int place)
    {
        _s = state;
        _current = 0;
        _row = 0;
        _bench = new BrewingBench(state, formulas, member);
        _back = ScreenDrawing.DrawBackground(ui, panel, state.Party, "alcscrn1");
        DrawBatchPage();
        ScreenDrawing.WaitForRelease(ui);
        while (true)
        {
            // DIVERGENCE: root 0000:0000 is GameUi.Poll's (MenuBar, HotKeys); the party_icons_dirty redraw is not modelled.
            var key = ui.KeyAfterTheMouse(MouseStep());
            if (key == Keys.AltQ)
            {
                throw new QuitGameException();
            }
            if (key is Keys.Escape or KeyD or KeyCapitalD)
            {
                if (!_formulaPage)
                {
                    // C: the original takes back the member's brews once more on leaving (R033+0x14E6); none are left by then.
                    _bench.TakeBackUnfinishedBrews();
                    return;
                }
                // C: k = count - x4, where x4 (the batch record's third word) is never set in this screen, so it is 0: the batches are all given back.
                // DORMANT: the k < 0 branch (x4 > count) that reserves -k batches again cannot happen while x4 stays 0.
                for (var k = Selected.Count; k > 0; k--)
                {
                    _bench.Release(Selected.Formula);
                }
                Selected.Count = 0;
                DrawBatchPage();
                continue;
            }
            if (_formulaPage && Keys.IsLetter(key, 's') && Selected.Count != 0)
            {
                _bench.Brew(Selected, place);
                return;
            }
            if (key != Keys.None)
            {
                if (_formulaPage)
                {
                    FormulaPageKey(key);
                }
                else
                {
                    BatchPageKey(key);
                }
            }
        }
    }

    // orig: R033+0x16FA (alc_view) R033+0x16FA (15D1:000A) matched, with R033+0x1950, R033+0x1A2A and R033+0x1ABE
    // DORMANT: R033+0x1B9A (an empty drawing step) does nothing.
    /// <summary>The formula page of the character sheet's list: names, mystic number, description and ingredients. Esc or a click leaves.</summary>
    public void ShowFormula(GameState state, int member, int formula)
    {
        var f = formulas[formula];
        var stock = Brewing.IngredientsOnTheFormulaPage(state);
        ScreenDrawing.DrawBackground(ui, panel, state.Party, "alcscrn0");
        ui.Text.Draw(ui.Canvas, 100, 9, f.Name, HeadingColor);
        ui.Text.Draw(ui.Canvas, 100, 0x12, $"Mystical Number - {f.MysticNumber}", HeadingColor);
        ScreenDrawing.Wrapped(ui, f.Name, 0x5F, 0x49, 0x3A, MenuColor);
        ScreenDrawing.Wrapped(ui, f.Description, 0x5F, 0x49, 0x52, MenuColor);
        ui.Text.Draw(ui.Canvas, 0xCB, 0x39, "Ingredients:", MenuColor);
        var y = 0x41;
        foreach (var ing in f.Slots)
        {
            if (ing.Count != 0)
            {
                var have = stock[ing.Item & 0xFF];
                ui.Text.Draw(ui.Canvas, 0xCD, y, $"{ing.Count} {state.Item(ing.Item).Name} ({have})", have < ing.Count ? MenuGreyed : MenuColor);
            }
            y += 8;
        }
        // orig: R033+0x1724 the button waited out before the loop
        ScreenDrawing.WaitForRelease(ui);
        while (true)
        {
            // orig: R033+0x172D: the button held gives Esc, then it is waited out (R033+0x1737); then the menu bar, and a waiting key
            // replaces that Esc (a key other than Esc keeps the page open); tests: ScreenInputTests
            // DIVERGENCE: the party_icons_dirty test (key = 0) is not modelled.
            var key = 0;
            if (ui.Mouse.Left)
            {
                key = Keys.Escape;
                ui.WaitForButtonRelease();
            }
            key = ui.KeyAfterTheMouse(key);
            // orig: R033+0x1A10 matched: key 0x1000 (Alt-Q) calls R033+0x17D6(-1), then root 0000:13A0().
            if (key == Keys.AltQ)
            {
                throw new QuitGameException();
            }
            // orig: R033+0x17D6 matched: puts back the two window fonts saved on entry and frees the formula buffer;
            //   the C# changes neither, so leaving only returns (no wait at the exit).
            if (key == Keys.Escape)
            {
                return;
            }
        }
    }

    // ---- the batch page -------------------------------------------------------------

    // orig: R033+0x0796 (1462:0796) draft (register-level diffs)
    private void DrawBatchPage()
    {
        _formulaPage = false;
        var canvas = ui.Canvas;
        canvas.CopyFrom(_back, 0x3C, 0, 0x104, 200);
        ui.Text.Draw(canvas, MenuX, MenuTop, ScreenDrawing.Hotkey('U') + "se this formula", _bench.Batches.Count != 0 ? MenuColor : MenuGreyed);
        ui.Text.Draw(canvas, MenuX, MenuTop + 8, ScreenDrawing.Hotkey('D') + "o something else", MenuColor);
        DrawList();
        DrawSelectedFormula();
    }

    // orig: R033+0x1116 (1462:1116) draft (register-level diffs)
    /// <summary>The scroll with the batch counts and the short names, the chosen row highlighted.</summary>
    private void DrawList()
    {
        var canvas = ui.Canvas;
        ScreenDrawing.Overlay(ui, "alchem13", ListX, 0x1D);
        _current = Math.Min(_current, _bench.Batches.Count);
        for (var i = _current - _row; i < _bench.Batches.Count; i++)
        {
            var row = i - _current + _row;
            if (row >= VisibleRows || i < 0)
            {
                continue;
            }
            var b = _bench.Batches[i];
            if (b.Count != 0)
            {
                ui.Text.Draw(canvas, 0xCF, row * RowHeight + ListTop, $"{b.Count,2}x", ListColor);
            }
            ui.Text.Draw(canvas, 0xDE, row * RowHeight + ListTop, b.Formula.ShortName, ListColor);
        }
        HighlightRow(true);
    }

    private void HighlightRow(bool on) =>
        ui.Canvas.Recolor(ListX + 1, _row * RowHeight + ListTop, 0x5A, RowHeight, on ? ListColor : ListHighlight, on ? ListHighlight : ListColor);

    // orig: R033+0x1018 (1462:1018) draft (bytes match)
    /// <summary>The chosen formula's full name and description in the book.</summary>
    private void DrawSelectedFormula()
    {
        if (_bench.Batches.Count == 0)
        {
            return;
        }
        ui.Canvas.CopyFrom(_back, 0x55, 0x50, 0x6E, 0x54);
        ScreenDrawing.Wrapped(ui, Selected.Formula.Name, 0x69, 0x58, 0x53, MenuColor);
        ScreenDrawing.Wrapped(ui, Selected.Formula.Description, 0x69, 0x58, 0x6B, MenuColor);
    }

    // orig: R033+0x0D54 (1462:0D54) draft (bytes match)
    // DIVERGENCE: Up and Down redraw the whole scroll where the original recolours the two rows when it does not scroll (same picture).
    private void BatchPageKey(int key)
    {
        switch (key)
        {
            case var _ when Keys.IsLetter(key, 'u'):
                if (_bench.Batches.Count == 0)
                {
                    return;
                }
                if (_bench.HasIngredients(Selected.Formula))
                {
                    _bench.Reserve(Selected.Formula);
                    Selected.Count = 1;
                }
                DrawFormulaPage();
                break;
            case Keys.Up:
                if (_current == 0)
                {
                    return;
                }
                HighlightRow(false);
                _current--;
                if (_row > 0)
                {
                    _row--;
                }
                DrawList();
                DrawSelectedFormula();
                break;
            case Keys.Down:
                if (_bench.Batches.Count - 1 <= _current)
                {
                    return;
                }
                HighlightRow(false);
                _current++;
                _row = Math.Min(_row + 1, VisibleRows - 1);
                DrawList();
                DrawSelectedFormula();
                break;
        }
    }

    // orig: the list click in R033+0x0000
    /// <summary>
    /// A click on the scroll picks the row under it. Faithful quirk: the row may be 17, one
    /// below the last shown, and below the list's end the last formula is chosen.
    /// </summary>
    private void PickRow(int y)
    {
        HighlightRow(false);
        var offset = _current - _row;
        _row = Math.Clamp((y - ListTop) >> 3, 0, VisibleRows);
        if (_row + offset >= _bench.Batches.Count)
        {
            _current = _bench.Batches.Count - 1;
            _row = Math.Clamp(_current, 0, VisibleRows);
        }
        else
        {
            _current = offset + _row;
        }
        DrawList();
        DrawSelectedFormula();
    }

    // ---- the formula page -----------------------------------------------------------

    // orig: R033+0x094A (1462:094A) draft (register-level diffs), with R033+0x10F8 (restores the scroll's area)
    private void DrawFormulaPage()
    {
        _formulaPage = true;
        var canvas = ui.Canvas;
        var b = Selected;
        var f = b.Formula;
        var m = _s.Party.Slot(_bench.Alchemist);
        canvas.CopyFrom(_back, 0x3C, 0, 0x104, 200);
        _canAddBatch = _bench.HasIngredients(f);
        ScreenDrawing.Wrapped(ui, $"{m.ShortName}'s Formula - making {b.Count} batches.", 0xEA, 0x49, 0x0D, HeadingColor);
        ScreenDrawing.Wrapped(ui, $"{Brewing.BaseChance(b.Count)} + {_s.Party.PhilosophersStone}ps + {m[Attribute.Intelligence]}int + " +
            $"{m[Skill.Alchemy]}alch - {f.MysticNumber}mn = {_bench.Chance(b)}% success.", 0xEA, 0x49, 0x15, HeadingColor);
        ui.Text.Draw(canvas, MenuX, MenuTop, ScreenDrawing.Hotkey('M') + "ore batches", _canAddBatch ? MenuColor : MenuGreyed);
        ui.Text.Draw(canvas, MenuX, MenuTop + 8, ScreenDrawing.Hotkey('F') + "ewer batches", MenuColor);
        ui.Text.Draw(canvas, MenuX, MenuTop + 16, ScreenDrawing.Hotkey('D') + "ifferent formula", MenuColor);
        ui.Text.Draw(canvas, MenuX, MenuTop + 24, ScreenDrawing.Hotkey('S') + "atisfied with this", b.Count != 0 ? MenuColor : MenuGreyed);
        DrawSelectedFormula();
        canvas.CopyFrom(_back, 0xC3, 0x3C, 0x5A, 0x4C);
        ScreenDrawing.Wrapped(ui, $"{f.RiskName} risk - magic #{f.MysticNumber}", 0x55, 0xC6, 0x3F, MenuColor);
        ui.Text.Draw(canvas, 0xC6, 0x56, "Ingredients:", MenuColor);
        var y = 0x5E;
        foreach (var ing in f.Slots)
        {
            if (ing.Count != 0)
            {
                var have = _bench.Stock(ing.Item);
                ui.Text.Draw(canvas, 0xC8, y, $"{ing.Count} {_s.Item(ing.Item).Name} ({have})", have >= ing.Count ? MenuColor : MenuGreyed);
            }
            y += 8;
        }
    }

    // orig: R033+0x0F6E (1462:0F6E) draft (register-level diffs); its Up/Down only hide and show the pointer
    private void FormulaPageKey(int key)
    {
        if (Keys.IsLetter(key, 'f') && Selected.Count != 0)
        {
            Selected.Count--;
            _bench.Release(Selected.Formula);
            DrawFormulaPage();
            _hover = -1;
        }
        else if (Keys.IsLetter(key, 'm') && _canAddBatch)
        {
            Selected.Count++;
            _bench.Reserve(Selected.Formula);
            DrawFormulaPage();
            _hover = -1;
        }
    }

    // ---- the mouse ------------------------------------------------------------------

    // C: leaving the batch menu recolours a 0xB4-wide row, entering one a 0x6E-wide row, as the original.
    /// <summary>
    /// The mouse tests of one pass of the loop, on the button's state: the menu of the page (the line under the pointer lights
    /// while the button is up; held over the menu, the button is waited out and the line under the pointer then gives its key);
    /// on the batch page the scroll (the row of the press, after the button is waited out) and its arrows (the release count
    /// cleared, the button waited out, then Up/Down).
    /// </summary>
    // orig: R033+0x0000 draft, checked against the asm: R033+0x0083/0x0132/0x018C (the batch menu: keys U and Esc for lines 0 and 1,
    // none for another line), R033+0x01DE/0x026A (the formula menu: M, F, Esc, S for lines 0..3, none for another; no un-highlight),
    // R033+0x02DB (the scroll: the press's y read before the wait R033+0x0317), R033+0x03D4 / 0x0422 (the arrows: R033+0x0401 / 0x0451
    // the release count cleared, R033+0x0409 / 0x0459 the wait); tests: ScreenInputTests
    // C: the menu line after the release is (y − 0x24) / 8, a division truncated toward zero.
    private int MouseStep()
    {
        var key = 0;
        var mouse = ui.Mouse;
        var bottom = _formulaPage ? 0x46 : 0x36;
        var right = _formulaPage ? 0xB4 : 0xAA;
        var overMenu = mouse.X > 0x46 && mouse.X < right && mouse.Y > MenuTop && mouse.Y < bottom;
        if (!mouse.Left && overMenu)
        {
            var k = (mouse.Y - MenuTop) / 8;
            if (k != _hover)
            {
                ui.Canvas.Recolor(0x46, _hover * 8 + MenuTop, 0x6E, 8, MenuHover, MenuColor);
                _hover = k;
                ui.Canvas.Recolor(0x46, k * 8 + MenuTop, 0x6E, 8, MenuColor, MenuHover);
            }
        }
        else if (mouse.Left && overMenu)
        {
            ui.WaitForButtonRelease();
            var line = (ui.Mouse.Y - MenuTop) / 8;
            key = _formulaPage
                ? line switch
                {
                    0 => Keys.ForCharacter('M'),
                    1 => Keys.ForCharacter('F'),
                    2 => Keys.Escape,
                    3 => Keys.ForCharacter('S'),
                    _ => 0,
                }
                : line switch
                {
                    0 => Keys.ForCharacter('U'),
                    1 => Keys.Escape,
                    _ => 0,
                };
        }
        else if (!_formulaPage && !mouse.Left && _hover != -1)
        {
            ui.Canvas.Recolor(0x46, _hover * 8 + MenuTop, 0xB4, 8, MenuHover, MenuColor);
            _hover = -1;
        }
        if (_formulaPage)
        {
            return key;
        }
        mouse = ui.Mouse;
        if (mouse.Left && mouse.X > ListX && mouse.X < 0x11A && mouse.Y > ListTop && mouse.Y < 0xB5)
        {
            var y = mouse.Y;
            ui.WaitForButtonRelease();
            PickRow(y);
        }
        mouse = ui.Mouse;
        if (mouse.Left && mouse.Y > 0x1D && mouse.Y < 0x26 && mouse.X > ListX && mouse.X < 0x11A)
        {
            ui.TakeLeftReleases();
            ui.WaitForButtonRelease();
            key = Keys.Up;
        }
        mouse = ui.Mouse;
        if (mouse.Left && mouse.Y > 0xB5 && mouse.Y < 0xBE && mouse.X > ListX && mouse.X < 0x11A)
        {
            ui.TakeLeftReleases();
            ui.WaitForButtonRelease();
            key = Keys.Down;
        }
        return key;
    }
}
