using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

// orig: R036+0x0082 (1462:0082) draft (unmatched: register allocation), with R036+0x082C on leaving the reference; tests: CampAndSheetRulesTests (the stay), visual
// orig: the music: DS:9C3D = thunk 09C0:18F1(5), then thunk 09C0:18F1(0x15) in the wild (mode 1), else rand_mod(2) ? 9 : 10 (a draw of the
//   game's generator before anything else of the stay); R036+0x082C plays DS:9C3D back on leaving; tests: MusicTests
// C: the menu bar (menu_bar_run) comes with every GameUi.Poll (MenuBar).
// DIVERGENCE: the redraw after the menu bar is not part of this screen.
/// <summary>
/// The camp screen: an inn room, other lodging, a camp in the wild or the streets. The party
/// is listed with each member's activity for the day; the menu on the right sets the chosen
/// member's activity (Just relax, Regain strength, Pray, Alchemy work, Earn money, Guard the
/// camp, Train or study); the cost of a day is shown above; "Spend a day" or "Leave".
/// </summary>
public sealed class CampScreen(GameUi ui, PartyPanel panel, IReadOnlyList<Formula> formulas, AlchemyScreen alchemy,
    CharacterSheetScreen sheet)
{
    private const byte TextColor = 0x0F;
    private const byte PartyColor = 0x0E;
    private const byte ChosenColor = 0x0B;
    private const int MenuX = 0xD0;
    private const int MenuTop = 0x30;
    private const int TeacherBoxX = 0xF5;
    private const int TeacherBoxY = 0x2D;
    private const int TeacherBoxWidth = 0x49;
    private const int CtrlF1 = 0x5E00;

    private static readonly string[] ActivityTexts =
    [
        "Relaxes", "Regain strength (gain {0})", "Pray for divine favor (gain {0})", "Make {0} {1}", "Earn {0} pf/day as {1}",
        "Maintain camp, food", "Train or Study ({1})",
    ];

    private static readonly string[] MenuLines =
    [
        "J|ust Relax", "R|egain Strength", "P|ray for divine favor", "A|lchemy work", "E|arn a little money", "G|uard the camp",
        "T|rain or study",
    ];

    /// <summary>The camp's music in the wild (other places have the inn's 9 or 10).</summary>
    private const int WildMusic = 0x15;

    /// <summary>The poaching risk carries over from stay to stay (the game keeps it in a global).</summary>
    private int _risk;
    private int _dailyRisk;

    /// <summary>The teacher list's rows and the teacher picked, also kept from stay to stay (DS:8A38, DS:89FA).</summary>
    private readonly int[] _teacherRows = new int[EventLog.Capacity];
    private int _chosenTeacher;

    private GameState _s = null!;
    private CampStay _stay = null!;
    private SavedScreen _back = null!;
    private SavedRegion? _teacherBox;
    private int _chosen;

    /// <summary>A stay as cards open it (<see cref="CampPlace"/> numbering).</summary>
    public void Stay(GameState state, int mode)
    {
        // orig: R036+0x0082: DS:9C3D = music_play(5); `if (mode == 1) music_play(0x15); else if (rand_mod(2)) music_play(9); else music_play(10);`
        ui.SaveMusic();
        ui.PlayMusic((CampPlace)mode == CampPlace.Wilderness ? WildMusic : state.Random.Below(2) != 0 ? 9 : 10);
        _s = state;
        _stay = new CampStay(state, (CampPlace)mode, _teacherRows) {
            Risk = _risk, DailyRisk = _dailyRisk, ChosenTeacher = _chosenTeacher,
            Hourglass = hours => Darklands.Presentation.Hourglass.Show(ui, state.Date, hours),
        };
        try
        {
            if (Run())
            {
                // orig: R036+0x082C: the unfinished brews lost (event_remove(0x1D, ...)), the fonts back, music_play(DS:9C3D)
                Brewing.AbandonAll(state);
                ui.RestoreMusic();
            }
        }
        finally
        {
            _risk = _stay.Risk;
            _chosenTeacher = _stay.ChosenTeacher;
        }
    }

    // orig: R036+0x0000 (1462:0000) draft (bytes match), called through thunk 09C0:1FF9 by the map's C key and F8
    // C: event_new(-2, -1, 0, 0x61, 0, risk, 0, rand_mod(0x30) + 0xC, 0, 0).
    /// <summary>
    /// Camping from the map ('C'): the risk starts from the last camp's (none while camping is
    /// safe), grows by <paramref name="dailyRisk"/> a day, and is remembered for 12..59 hours.
    /// </summary>
    public void CampInTheWild(GameState state, int dailyRisk)
    {
        _dailyRisk = dailyRisk;
        // orig: R036+0x000B: the release count cleared and one waiting key thrown away
        ui.TakeLeftReleases();
        ui.TryReadKey(out _);
        _risk = state.Events.Started(EventKinds.SafeCamping) ? 0
            : state.Events.Started(EventKinds.CampRisk) ? state.Events.Value(EventKinds.CampRisk) : 0;
        Stay(state, (int)CampPlace.Wilderness);
        state.Events.Create(-2, -1, 0, EventKinds.CampRisk, 0, _risk, 0, state.Random.Below(48) + 12, 0, 0);
    }

    private bool Wild => _stay.Place == CampPlace.Wilderness;

    // orig: the loop of R036+0x0082 draft, checked against the asm: the button is read through the driver's release count
    // (INT 33h function 6, R036+0x01CB): with no release the menus light under the pointer; a release in the teacher box (pointer
    // there now) waits the button out and takes the row under the pointer then; a release in a menu acts on the place of the
    // release, then the button is waited out; a release over the party column (a separate test) waits the button out and opens
    // the sheet of the row of the release; then the menu bar, and a waiting key replaces the key of the mouse; tests: ScreenInputTests
    /// <summary>The screen's loop; false when it ended by a click on an empty party row (the game then skips its clean-up).</summary>
    private bool Run()
    {
        _chosen = 0;
        _teacherBox = null;
        // orig: R036+0x00BD: the release count cleared before the screen is drawn
        ui.TakeLeftReleases();
        Redraw();
        // orig: R036+0x01BD the button waited out after the clear: a button still held is counted when it is let go
        ui.WaitForButtonRelease();
        var menuHover = -1;
        var menu = 0;
        var teacherHover = -1;
        while (true)
        {
            var key = 0;
            var (released, rx, ry) = ui.TakeLeftReleases();
            var mouse = ui.Mouse;
            var inTeacherBox = _teacherBox is not null && mouse.X > 0xF5 && mouse.X < 0x13E && mouse.Y > 0x30 && mouse.Y < 0x71;
            if (inTeacherBox && released == 0)
            {
                var row = (mouse.Y - 0x30) / 9;
                if (row != teacherHover)
                {
                    ui.Canvas.Recolor(0xF6, teacherHover * 9 + 0x30, 0x48, 9, 9, 8);
                    teacherHover = row;
                    ui.Canvas.Recolor(0xF6, row * 9 + 0x30, 0x48, 9, 8, 9);
                }
            }
            else if (inTeacherBox)
            {
                // orig: R036+0x02AB the wait, then the row of the pointer after it (R036+0x02B2)
                ui.WaitForButtonRelease();
                key = TeacherKey((ui.Mouse.Y - 0x30) / 9);
            }
            else if (released == 0 && AreaAt(mouse.X, mouse.Y) == 1)
            {
                var row = (mouse.Y - 0xAA) / 9;
                if (row != menuHover)
                {
                    ui.Canvas.Recolor(0x47, menuHover * 9 + 0xAC, 0xED, 8, ChosenColor, PartyColor);
                    menuHover = row;
                    ui.Canvas.Recolor(0x47, row * 9 + 0xAC, 0xED, 8, PartyColor, ChosenColor);
                    menu = 1;
                }
            }
            else if (released == 0 && AreaAt(mouse.X, mouse.Y) == 2)
            {
                var row = (mouse.Y - 0x2D) / 9;
                if (row != menuHover)
                {
                    CloseTeacherBox();
                    var lit = Wild ? (byte)8 : (byte)10;
                    ui.Canvas.Recolor(0xCE, menuHover * 9 + 0x2F, 0x65, 8, ChosenColor, lit);
                    menuHover = row;
                    ui.Canvas.Recolor(0xCE, row * 9 + 0x2F, 0x65, 8, lit, ChosenColor);
                    if (row == 6 && _stay.TeachersHere)
                    {
                        OpenTeacherBox();
                        teacherHover = -1;
                    }
                    menu = 2;
                }
            }
            else if (released != 0 && AreaAt(rx, ry) is var area and not 0)
            {
                // orig: R036+0x04F6 the key of the place of the release, then the wait (R036+0x04FE); the lit row stays lit
                key = KeyForArea(area, rx, ry);
                ui.WaitForButtonRelease();
            }
            else if (menuHover != -1)
            {
                CloseTeacherBox();
                if (menu == 1)
                {
                    ui.Canvas.Recolor(0x47, menuHover * 9 + 0xAC, 0xED, 8, ChosenColor, PartyColor);
                }
                else
                {
                    ui.Canvas.Recolor(0xCE, menuHover * 9 + 0x2F, 0x65, 8, PartyColor, ChosenColor);
                }
                menuHover = -1;
            }
            if (released != 0 && rx < PartyPanel.Width)
            {
                // orig: R036+0x059F: the wait (R036+0x05AB), the member of the release's row; an empty slot returns at once
                ui.WaitForButtonRelease();
                var member = ry / PartyPanel.RowHeight;
                if (!_s.Party.Slot(member).IsPresent)
                {
                    return false;
                }
                sheet.Show(_s, member);
                Redraw();
                // orig: R036+0x05F5 the wait again, R036+0x0604 the release count cleared; no key this pass
                ui.WaitForButtonRelease();
                ui.TakeLeftReleases();
                key = 0;
            }
            // DIVERGENCE: root 0000:0000 is GameUi.Poll's (MenuBar, HotKeys); the party_icons_dirty redraw is not modelled.
            key = ui.KeyAfterTheMouse(key);
            if (key == Keys.Escape)
            {
                return true;
            }
            if (key != Keys.None && HandleKey(key))
            {
                return true;
            }
        }
    }

    // orig: R036+0x06D2 (1462:06D2) draft (bytes match)
    /// <summary>1 the bottom menu, 2 the activity menu, 3 the party list, 0 elsewhere.</summary>
    private static int AreaAt(int x, int y)
    {
        if (x > 0xCD && x < 0x134 && y > 0x2D && y < 0x6E)
        {
            return 2;
        }
        if (x > 0x46 && x < 0xC8 && y > 0x2D && y < 0x87)
        {
            return 3;
        }
        return x > 0x46 && x < 0x134 && y > 0xAA && y < 0xBC ? 1 : 0;
    }

    /// <summary>A click on row <paramref name="row"/> of the teacher box: the teacher is chosen and the box closes ('T'), if there is one.</summary>
    // orig: R036+0x02B2..: `if (teachers here > i) { DS:89FA = i; key = 0x1454; the box closes }`
    private int TeacherKey(int row)
    {
        if (_s.Events.CountAt(EventKinds.Teacher, _s.CurrentLocation) > row)
        {
            _stay.ChosenTeacher = row;
            CloseTeacherBox();
            return Keys.ForCharacter('T');
        }
        return Keys.None;
    }

    // orig: R036+0x0734 and R036+0x076A (draft; 076a unmatched only through 0082's length): the row of the area, then its key
    private int KeyForArea(int area, int x, int y)
    {
        switch (area)
        {
            case 1:
                return (y - 0xAA) / 9 == 0 ? Keys.ForCharacter('S') : Keys.Escape;
            case 2:
                var row = (y - 0x2D) / 9;
                return row < MenuLines.Length ? Keys.ForCharacter(MenuLines[row][0]) : Keys.None;
            case 3:
                var member = (y - 0x2D) / 0x12;
                return member < Party.MaxMembers && _s.Party.Slot(member).IsPresent ? Keys.ForCharacter((char)('1' + member)) : Keys.None;
        }
        return Keys.None;
    }

    // orig: R036+0x08EC (1462:08EC) draft (bytes match)
    // C: keys 1..5 (and the keypad's 0x4F31.. with the same characters) choose a slot without checking that it is occupied.
    /// <summary>A key of the camp screen; true when the stay is over.</summary>
    private bool HandleKey(int key)
    {
        var i = _chosen;
        if (key is >= Keys.F1 and <= Keys.F5 && (key & 0xFF) == 0)
        {
            sheet.Show(_s, (key >> 8) - (Keys.F1 >> 8));
            Redraw();
            // orig: R036+0x0EB6 the button waited out, R036+0x0EC5 the release count cleared
            ui.WaitForButtonRelease();
            ui.TakeLeftReleases();
            return false;
        }
        if (key is >= CtrlF1 and <= CtrlF1 + 0x400 && (key & 0xFF) == 0)
        {
            var m = (key >> 8) - (CtrlF1 >> 8);
            if (m < _s.Party.Members.Count && _s.Party.Members[m].IsAble)
            {
                _s.Party.LeaderIndex = m;
                panel.Draw(ui.Canvas, _s.Party);
            }
            return false;
        }
        if (key == Keys.AltQ)
        {
            Brewing.AbandonAll(_s);
            throw new QuitGameException();
        }
        switch (Keys.CharacterOf(key))
        {
            case >= '1' and <= '5':
                _chosen = Keys.CharacterOf(key) - '1';
                DrawPartyList();
                DrawActivityMenu();
                return false;
        }
        switch (char.ToLowerInvariant(Keys.CharacterOf(key)))
        {
            case 'j':
                _stay.Relax(i, formulas);
                break;
            case 'r':
                _stay.RegainStrength(i, formulas);
                break;
            case 'p':
                _stay.Pray(i, formulas);
                break;
            case 'a':
                if (_stay.CanDoAlchemy(i))
                {
                    _stay.DoAlchemy(i, () => alchemy.Use(_s, i, (int)_stay.Place));
                    Redraw();
                }
                return false;
            case 'e':
                _stay.EarnMoney(i, formulas);
                break;
            case 'g':
                _stay.Guard(i, formulas);
                break;
            case 't':
                if (_stay.TeachersHere)
                {
                    _stay.Train(i, formulas);
                    Redraw();
                }
                return false;
            case 'l':
                return true;
            case 's':
                return SpendADay();
            default:
                return MoveChoice(key);
        }
        DrawPartyList();
        DrawCost();
        return false;
    }

    private bool SpendADay()
    {
        if (_stay.SpendDay() == StayEnd.Over)
        {
            return true;
        }
        Redraw();
        // orig: R036+0x0DEA the button waited out; R036+0x0E03 the release count cleared when a mouse is there (DS:9C4C, set by
        // the mouse driver's reset at start-up: always here)
        ui.WaitForButtonRelease();
        ui.TakeLeftReleases();
        return false;
    }

    // C: going back from member 0 tries slot 5 (past the table, read as empty) before member 4.
    /// <summary>Up, Left and Right choose the member before, Down the next.</summary>
    private bool MoveChoice(int key)
    {
        if (key is Keys.Up or Keys.Left or Keys.Right)
        {
            do
            {
                if (--_chosen < 0)
                {
                    _chosen = Party.MaxMembers;
                }
            }
            while (!_s.Party.Slot(_chosen).IsPresent);
        }
        else if (key == Keys.Down)
        {
            do
            {
                _chosen++;
            }
            while (!_s.Party.Slot(_chosen % Party.MaxMembers).IsPresent);
            _chosen %= Party.MaxMembers;
        }
        else
        {
            return false;
        }
        DrawPartyList();
        DrawActivityMenu();
        return false;
    }

    // ---- drawing --------------------------------------------------------------------

    // orig: R036+0x0872 (1462:0872) draft (bytes match); fonts from R036+0x0862
    private void Redraw()
    {
        _teacherBox = null;
        _back = ScreenDrawing.DrawBackground(ui, panel, _s.Party, Wild ? "campwild" : "campcity");
        DrawActivityMenu();
        _back = ui.Canvas.SaveScreen();
        DrawPartyList();
        DrawCost();
    }

    // orig: R036+0x0F8C (1462:0F8C) draft (bytes match)
    /// <summary>The activity menu: what the chosen member cannot do is dimmed.</summary>
    private void DrawActivityMenu()
    {
        var (dim, lit) = Wild ? ((byte)7, (byte)8) : ((byte)2, (byte)10);
        bool[] usable =
        [
            true, _stay.CanRegainStrength(_chosen), _stay.CanPray(_chosen), _stay.CanDoAlchemy(_chosen), _stay.CanEarnMoney,
            _stay.CanGuard, _stay.TeachersHere,
        ];
        for (var k = 0; k < MenuLines.Length; k++)
        {
            var parts = MenuLines[k].Split('|');
            ui.Text.Draw(ui.Canvas, MenuX, MenuTop + k * 9, ScreenDrawing.Hotkey(parts[0][0]) + parts[1], usable[k] ? lit : dim);
        }
    }

    // orig: R036+0x1368 (1462:1368) draft (bytes match)
    // C: activity 6 formats DS:3AC0[6] with the detail alone; the others get (gain, detail) — printf ignores the unused argument.
    /// <summary>Each able member's name and activity; the chosen member in the highlight colour.</summary>
    private void DrawPartyList()
    {
        ui.Canvas.CopyFrom(_back, 0x46, 0x2D, 0x82, 0x5A);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            var m = _s.Party.Slot(i);
            if (!m.IsAble)
            {
                continue;
            }
            var color = _chosen == i ? ChosenColor : PartyColor;
            var y = i * 0x12 + 0x30;
            ui.Text.Draw(ui.Canvas, 0x49, y, m.ShortName + "...", color);
            var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, ActivityTexts[(int)_stay.Activity[i]],
                _stay.Gain[i], _stay.Detail[i]);
            ScreenDrawing.Wrapped(ui, text, 0x50, 0x78, y, color, leading: 0);
        }
    }

    // orig: R036+0x10D0 (1462:10D0) draft (unmatched: register choice only)
    // C: the price is printed as price / 12 groschen and price % 12 pfennigs, florins not separated (83gr for 1000 pf).
    /// <summary>What a day costs and leaves (or the poaching risk in the wild), and the bottom menu.</summary>
    private void DrawCost()
    {
        string text;
        if (Wild)
        {
            text = $"You live off the land, but the lord may be upset if you are caught. After {_stay.Days} days the risk is {_stay.Risk / 5}";
        }
        else
        {
            var cost = _stay.DayCost();
            var price = _stay.LodgingPrice;
            var at = $"You consider staying here today, at {price / 12}gr, {price % 12}pf.";
            var purse = _s.Party.Purse;
            if (cost > 0 && purse < Money.FromPfennigs(cost))
            {
                text = at + "  But, you do not have enough money.   For the day you will:";
            }
            else
            {
                var left = cost > 0 ? purse.Subtract(0, 0, cost) : purse.Add(0, 0, -cost);
                text = at + (cost > 0 ? "  That will leave you with " : "  With work that will leave you with ")
                    + $"{left.Florins}fl, {left.Groschen}gr, {left.Pfennigs}pf.   For the day you will:";
            }
        }
        ui.Canvas.CopyFrom(_back, 0x50, 0x12, 0xE6, 0x1C);
        ScreenDrawing.Wrapped(ui, text, 0xDC, 0x50, 0x12, TextColor);
        ui.Text.Draw(ui.Canvas, 0x4B, 0xA4, "You decide to...", TextColor);
        ui.Text.Draw(ui.Canvas, 0x5F, 0xAC, ScreenDrawing.Hotkey('S') + "pend a day, doing the above", PartyColor);
        ui.Text.Draw(ui.Canvas, 0x5F, 0xB5, ScreenDrawing.Hotkey('L') + "eave", PartyColor);
    }

    // orig: R036+0x1D24 and R036+0x1C3C (draft, bytes match)
    /// <summary>The teachers here, one skill a line, in a framed box beside the menu.</summary>
    private void OpenTeacherBox()
    {
        var canvas = ui.Canvas;
        var lines = string.Join("\n", _stay.TeacherList().Select(skill => MemberRules.SkillNames[skill]));
        _teacherBox = canvas.Save(TeacherBoxX, TeacherBoxY, TeacherBoxWidth, 0x41);
        canvas.Fill(TeacherBoxX, TeacherBoxY, TeacherBoxWidth, 0x41, Palette.Blank);
        canvas.Outline(TeacherBoxX, TeacherBoxY, TeacherBoxWidth - 1, 0x40, 1);
        ScreenDrawing.Wrapped(ui, lines, TeacherBoxWidth - 8, 0xFA, 0x30, 8, spacing: 9, leading: 0);
    }

    private void CloseTeacherBox()
    {
        if (_teacherBox is not null)
        {
            ui.Canvas.Restore(_teacherBox);
            _teacherBox = null;
        }
    }
}
