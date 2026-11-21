using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Presentation;

/// <summary>
/// A member's character sheet (the "arms" screen, F1..F5): the figure in its armour and
/// weapons, name, age, load, attributes, skills and equipment in boxes; the Pack, Formulae
/// and Saints scrolls. The red frame is the cursor (arrow keys, a click); Enter or a click
/// opens a box. In the open pack: A ready, U unready, D drop (Shift: the stack), P drink,
/// 1..5 give (Shift: the stack); dragging moves, readies or gives an item. A click on another
/// member in the panel shows that member; on the same member, or Esc, leaves.
/// </summary>
// orig: member-sheet-screen R031+0x0124 (1462:0124) draft (the bytes match except one compare order); tests: none (visual, headless)
// DORMANT: R031+0x0000 (pack-screen-exit) opens the sheet from a state without the world data loaded and writes LOCS.TMP/EVENTS.TMP afterwards; nothing in this game opens the sheet that way.
// DIVERGENCE: the pointer does not change to the hand pictures (handicon, handicn2) over lists; the menu bar (menu-bar-loop) comes with GameUi.Poll (MenuBar), its Alt keys through MenuBarKey.
// orig: the combat branch of R031+0x0124 (DS:907A == 1 and the member on floor 0x9D: thunk 09C0:1D51 = R022+0x04E4) for a portrait
//   click, F1..F5 and Enter on a member box: CannotView (BattleScreens.CannotViewMember); tests: SheetCannotViewTests
public sealed class CharacterSheetScreen(GameUi ui, PartyPanel panel, IReadOnlyList<Formula> formulas, IReadOnlyList<string> saintNames,
    AlchemyScreen alchemy)
{
    /// <summary>A box of the sheet (table at far 290E:3559, 0x24 bytes each).</summary>
    /// <param name="Flags">1 opens a panel, 2 the panel is a scrolling list, 0x10 it holds items.</param>
    private sealed record Box(int X, int Y, int W, int H, byte Color, int Flags, int PanelX, int PanelY, int PanelW, int PanelH,
        int Picture, int Lines, int Up, int Down, int Left, int Right)
    {
        public bool Opens => (Flags & 1) != 0;

        public bool IsList => (Flags & 2) != 0;

        public bool HoldsItems => (Flags & 0x10) != 0;

        public bool Contains(int x, int y) => X <= x && W + X >= x && Y <= y && Y + H >= y;
    }

    private const int NameBox = 0;
    private const int EquipmentBox = 6;
    private const int PackBox = 9;
    private const int FormulaeBox = 10;
    private const int SaintsBox = 11;
    private const int FirstMemberBox = 12;
    private const int BoxCount = 17;
    private const byte CursorColor = 12;
    private const byte MemberFrameColor = 11;
    private const byte TextColor = 0xBF;
    private const byte ListColor = 8;
    private const byte ListSelected = 9;
    private const int CtrlF1 = 0x5E00;

    // orig: R136+0xBBE7 far 290E:3559 (the box table, read from the EXE's data section)
    private static readonly Box[] Boxes =
    [
        new(64, 1, 193, 10, 190, 12, 0, 1, 193, 10, 0, 0, 0, 3, 12, 1),
        new(262, 1, 55, 10, 190, 12, 0, 1, 55, 10, 0, 0, 1, 4, 0, 1),
        new(73, 183, 108, 13, 190, 12, 0, 184, 108, 13, 0, 0, 0, 2, 16, 10),
        new(196, 14, 61, 67, 190, 13, 144, 14, 110, 67, 1, 0, 0, 6, 13, 4),
        new(262, 14, 55, 63, 190, 13, 222, 14, 94, 63, 4, 0, 1, 5, 3, 4),
        new(262, 80, 55, 56, 190, 13, 222, 80, 94, 56, 2, 0, 4, 7, 6, 5),
        new(196, 84, 61, 62, 190, 13, 144, 84, 113, 62, 5, 0, 3, 9, 14, 5),
        new(262, 139, 55, 58, 190, 13, 222, 139, 94, 58, 3, 0, 5, 7, 9, 7),
        new(196, 199, 61, 13, 190, 12, 0, 146, 61, 13, 0, 0, 11, 10, 2, 7),
        new(196, 149, 61, 13, 190, 0x5F, 62, 30, 137, 134, 0, 14, 6, 10, 14, 7),
        new(196, 165, 61, 13, 190, 0x8F, 62, 46, 137, 134, 0, 14, 9, 11, 15, 7),
        new(196, 181, 61, 13, 190, 0x8F, 62, 62, 137, 134, 0, 14, 10, 10, 15, 7),
        new(1, 1, 57, 38, 255, 0, 0, 1, 57, 38, 0, 0, 12, 13, 12, 0),
        new(1, 41, 57, 38, 255, 0, 0, 41, 57, 38, 0, 0, 12, 14, 13, 17),
        new(1, 81, 57, 38, 255, 0, 0, 81, 57, 38, 0, 0, 13, 15, 14, 6),
        new(1, 121, 57, 38, 255, 0, 0, 121, 57, 38, 0, 0, 14, 16, 15, 9),
        new(1, 161, 57, 37, 255, 0, 0, 161, 57, 37, 0, 0, 15, 16, 16, 10),
        // C: entries 17 and 18 of the table are all zero (a 0x0 box at (0, 0), colour 0, every neighbour box 0). Box 13's Right
        //   neighbour is 17 (R031+0x10AE case Left/Right reads the table byte), so the cursor can stand on it: the frame is one pixel at (0, 0),
        //   Up/Down/Left/Right go back to box 0. The click scan (R031+0x0124: 18 entries, 19 when dropping) reaches both.
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    ];

    /// <summary>The brush pictures of the boxes that open (index <see cref="Box.Picture"/>).</summary>
    private static readonly string[] Brushes = ["armbrs13", "armbrsh1", "armbrsh2", "armbrsh3", "armbrsh4", "armbrsh5"];

    // orig: R136+0xA4F7 = 290E:1E67 (the attribute and skill labels of the open boxes): OriginalWords.SheetLabels

    // the box texts R031+0x39C6 load their own copies: the short labels of cases 3 (and the dormant 17), 4, 5, 7 and their value formats
    private static readonly string[] AttributeLabels = ["exe:DS:2E2B", "exe:DS:2E2F", "exe:DS:2E33", "exe:DS:2E37", "exe:DS:2E3B", "exe:DS:2E3F", "exe:DS:2E43"];
    private static readonly string[] AttributeLabels17 = ["exe:DS:2EF2", "exe:DS:2EF6", "exe:DS:2EFA", "exe:DS:2EFE", "exe:DS:2F02", "exe:DS:2F06", "exe:DS:2F0A"];
    private static readonly string[] WeaponLabels = ["exe:DS:2E50", "exe:DS:2E55", "exe:DS:2E5A", "exe:DS:2E5F", "exe:DS:2E64", "exe:DS:2E69", "exe:DS:2E6E"];
    private static readonly string[] LearnedLabels = ["exe:DS:2E78", "exe:DS:2E7D", "exe:DS:2E82", "exe:DS:2E87", "exe:DS:2E8C", "exe:DS:2E91"];
    private static readonly string[] OtherLabels = ["exe:DS:2ECF", "exe:DS:2ED4", "exe:DS:2ED9", "exe:DS:2EDE", "exe:DS:2EE3", "exe:DS:2EE8"];

    /// <summary>The member shown, the cursor, the open box and the list rows (kept between visits, as the game's table).</summary>
    private readonly Simulation.SheetSession _s = new();

    /// <summary>The back page (the game's backdrop-window) the sheet restores rectangles from.</summary>
    private readonly Screen _backScreen = new();

    private GameState _state = null!;
    private SavedRegion? _underPanel;

    /// <summary>Keys the game's menu bar handles while the sheet is up (Alt-Q, Alt-P …); true when one was handled.</summary>
    public Func<int, bool>? MenuBarKey { get; set; }

    /// <summary>The saint page a click in the Saints list opens (the prayer screen, not part of this screen).</summary>
    public Action<int, int>? ShowSaint { get; set; }

    /// <summary>In a battle, for a member who left the field through an exit: the "can't view" message, and true (the sheet then stays on its member).</summary>
    public Func<int, bool>? CannotView { get; set; }

    private Canvas Back => new(_backScreen);

    private SavedScreen BackPage => new(_backScreen.Pixels, _backScreen.Palette);

    private Canvas Screen => ui.Canvas;

    private Character Member => _s.Member;

    private Box Current => Boxes[_s.Cursor];

    private int Index => _s.Index;

    // ---- the loop -------------------------------------------------------------------

    /// <summary>Sets the Save Game and Load Saved Game items of the hot keys and the menu bar (1 on, 2 off).</summary>
    public Action<int>? SaveAndLoad { get; set; }

    /// <summary>The game's party-headcount where the party shown is not the game's party (the character menu: 0); null: the party's members.</summary>
    // C: the Ctrl-F1..F5 case of the original's key handler tests `slot < party-headcount` (DS:A67E, signed) before the status
    public Func<int>? PartySize { get; set; }

    // C: member-sheet-screen sets DS:9920 = DS:9934 = 2 once the member is there (R031+0x0144) (no Save Game, no Load Saved Game while the sheet is up); R031+0x08F6
    //   on leaving (Esc, the member's own portrait or F key) sets both to 1, whatever they were before (a sheet opened from a stay leaves saving on); the return on an empty
    //   member box skips it
    public void Show(GameState state, int who)
    {
        // its texts as a translation pack lays them out (interface/layout.json), its hit tests and highlights with them
        using var screen = ui.OnScreen("CharacterSheetScreen");
        _state = state;
        _s.Begin(state, who);
        // orig: member-sheet-screen's guard: status 0 or 5 leaves at once
        if (Member.Status is CharacterStatus.Empty or CharacterStatus.Away)
        {
            return;
        }
        SaveAndLoad?.Invoke(2);
        _s.MoveCursor(NameBox);
        _underPanel = null;
        // orig: R031+0x09B8 (1462:09B8) draft: fonts, pictures, then the sheet
        DrawSheet();
        _s.Closed();
        Outline(_s.Cursor, CursorColor);
        // orig: R031+0x018C the button waited out after drawing
        ScreenDrawing.WaitForRelease(ui);
        // C: the screen below keeps no GameUi.Redraw while the sheet is up: the party information sets party-icons-stale for the sheet's own test
        var below = ui.Redraw;
        ui.Redraw = null;
        // host comfort (GameUi.SelectionList, the mouse wheel's ↑ / ↓): an open list or the pack (with no list open ↑ / ↓ move the frame between the boxes)
        using var wheel = ui.UseSelectionList(() => _s.IsOpen && Current.IsList);
        try
        {
            Loop();
        }
        finally
        {
            ui.Redraw = below;
        }
    }

    private void Loop()
    {
        while (true)
        {
            // orig: R031+0x0124 draft, checked against the asm: R031+0x019D tests the button's state (the pointer where it is now),
            // acts, then waits the button out (R031+0x06BE; a click on the member's own row returns at R031+0x08E1 without waiting);
            // then the menu bar and a waiting key in the same pass; tests: ScreenInputTests
            var mouse = ui.LaidMouse;
            if (mouse.Left)
            {
                if (Click(mouse.X, mouse.Y))
                {
                    return;
                }
                ui.WaitForButtonRelease();
            }
            var key = ui.KeyAfterTheMouse(0, RedrawAfterPartyInformation);
            if (key != 0 && Key(key))
            {
                return;
            }
        }
    }

    /// <summary>party-icons-stale in the sheet's loop: the open box closed and the sheet drawn again, the cursor frame not.</summary>
    // orig: R031+0x06D2: if DS:9C46 { DS:9C46 = 0; the panel (root 0000:1588 / 0000:1716 with 0x100), R031+0x2518(member, 0xB), DS:867E = 0, the saved panel
    //   area DS:867A freed (0 after), R031+0x0A82(member) } (no R031+0x1D44: the cursor frame comes back with the next move)
    private void RedrawAfterPartyInformation()
    {
        _s.Closed();
        _underPanel = null;
        DrawSheet();
    }

    /// <summary>A click: on the sheet (a list line, its arrows, a box) or on the party panel. True to leave.</summary>
    private bool Click(int x, int y)
    {
        var b = Current;
        if (x > PartyPanel.Width)
        {
            if (_s.IsOpen && b.IsList && b.PanelX <= x && b.PanelW + b.PanelX >= x && b.PanelY + 9 <= y && b.PanelH + b.PanelY - 14 >= y)
            {
                ClickListLine(_s.Cursor, y);
            }
            else if (_s.IsOpen && b.IsList && b.PanelX <= x && b.PanelW + b.PanelX >= x && b.PanelY <= y && b.PanelY + 9 >= y)
            {
                // orig: the scroll's up arrow: `while (mouse-button-state & 1) { step; R030+0x3096(200); }` (R031+0x0286, R031+0x036A): the
                // button is tested before each step, and the 200 ms wait follows every step, the last one too
                while (ui.LaidMouse.Left)
                {
                    if (_s.ArrowUp() == Simulation.SheetStep.Scrolled)
                    {
                        DrawListPanel(_s.Cursor);
                    }
                    else
                    {
                        MoveSelection(-1, 8);
                    }
                    ui.Delay(200);
                }
            }
            else if (_s.IsOpen && b.IsList && b.PanelX <= x && b.PanelW + b.PanelX >= x && b.PanelH + b.PanelY - 14 <= y && b.PanelH + b.PanelY >= y)
            {
                // orig: the down arrow (R031+0x03E8, R031+0x04FD): the same loop; the end of the list breaks out before the wait
                while (ui.LaidMouse.Left)
                {
                    var step = _s.ArrowDown(b.Lines);
                    if (step == Simulation.SheetStep.None)
                    {
                        break;
                    }
                    if (step == Simulation.SheetStep.Scrolled)
                    {
                        DrawListPanel(_s.Cursor);
                    }
                    else
                    {
                        MoveSelection(1, 8);
                    }
                    ui.Delay(200);
                }
            }
            else
            {
                var i = BoxAt(x, y, 18);
                if (i == 18)
                {
                    i = 0;
                }
                if (_s.IsOpen && _underPanel is not null)
                {
                    Screen.Restore(_underPanel);
                    _underPanel = null;
                }
                if (_s.IsOpen && _s.Cursor == i)
                {
                    Outline(_s.Cursor, CursorColor);
                    _s.Closed();
                }
                else
                {
                    Outline(_s.Cursor, Current.Color);
                    Open(i);
                    _s.MoveCursor(i);
                }
            }
            return false;
        }
        var m = y / PartyPanel.RowHeight;
        if (m == _s.Who)
        {
            // orig: R031+0x0635 to the clean-up (R031+0x08E1: R031+0x08F6, DS:9920 = DS:9934 = 1)
            SaveAndLoad?.Invoke(1);
            return true;
        }
        if (_state.Party.Slot(m).Status == CharacterStatus.Able)
        {
            // orig: R031+0x0124: in a battle (DS:907A == 1) a member standing on floor 0x9D gets thunk 09C0:1D51(m) instead
            if (CannotView?.Invoke(m) == true)
            {
                return false;
            }
            ShowMember(m);
            Outline(_s.Cursor, CursorColor);
        }
        return false;
    }

    /// <summary>A key; true to leave.</summary>
    private bool Key(int key)
    {
        if (key is >= Keys.F1 and <= Keys.F5 && (key & 0xFF) == 0)
        {
            var m = (key >> 8) - (Keys.F1 >> 8);
            if (m == _s.Who)
            {
                // orig: R031+0x0828 to the clean-up (R031+0x08E1)
                SaveAndLoad?.Invoke(1);
                return true;
            }
            if (_state.Party.Slot(m).Status == CharacterStatus.Able)
            {
                // orig: the battle branch (thunk 09C0:1D51) as for a click
                if (CannotView?.Invoke(m) == true)
                {
                    return false;
                }
                ShowMember(m);
                Outline(_s.Cursor, CursorColor);
            }
            return false;
        }
        if (key == Keys.Enter && _s.Cursor is >= FirstMemberBox and <= FirstMemberBox + 4)
        {
            var m = _s.Cursor - FirstMemberBox;
            if (_state.Party.Slot(m).Status == CharacterStatus.Empty)
            {
                // C: the original returns here without the clean-up of R031+0x08F6 (fonts and pointer stay as the sheet set them).
                return true;
            }
            // orig: the battle branch (thunk 09C0:1D51) before the `who != m` test: the sheet stays (no leaving for the member shown)
            if (CannotView?.Invoke(m) == true)
            {
                return false;
            }
            if (_s.Who != m)
            {
                ShowMember(m);
                return false;
            }
            key = Keys.Escape;
        }
        if (key == Keys.Escape)
        {
            Outline(_s.Cursor, Current.Color);
            // orig: R031+0x08F6 (1462:08F6) draft: restores fonts and pointer (pointer-shape-set(0, 0, DS:9C42) at R031+0x0924), DS:9920 = DS:9934 = 1
            (ui.PointerHotSpot, ui.PointerShape) = ((0, 0), null);
            SaveAndLoad?.Invoke(1);
            return true;
        }
        if (MenuBarKey?.Invoke(key) == true)
        {
            return false;
        }
        HandleKey(key);
        return false;
    }

    // orig: R031+0x10AE (1462:10AE) draft (bytes match); the sheet's key handler
    private void HandleKey(int key)
    {
        var b = Current;
        var listOpen = b.IsList && _s.IsOpen;
        var packOpen = b.HoldsItems && _s.IsOpen;
        switch (key)
        {
            case Keys.Enter:
                if (_s.IsOpen)
                {
                    ClosePanel();
                    Outline(_s.Cursor, CursorColor);
                }
                else
                {
                    Open(_s.Cursor);
                }
                return;
            case Keys.Left:
            case Keys.Right:
                if (_s.IsOpen)
                {
                    ClosePanel();
                }
                Outline(_s.Cursor, b.Color);
                _s.MoveCursor(key == Keys.Left ? b.Left : b.Right);
                Outline(_s.Cursor, CursorColor);
                return;
            case Keys.Up when listOpen:
                switch (_s.KeyUp())
                {
                    case Simulation.SheetStep.Scrolled:
                        DrawListPanel(_s.Cursor);
                        break;
                    case Simulation.SheetStep.Select:
                        MoveSelection(-1, 5);
                        break;
                }
                return;
            case Keys.Down when listOpen:
                switch (_s.KeyDown(b.Lines))
                {
                    case Simulation.SheetStep.Scrolled:
                        DrawListPanel(_s.Cursor);
                        break;
                    case Simulation.SheetStep.Moved:
                        var lineY = _s.Selected(_s.Cursor) * 8 + b.PanelY;
                        ui.Recolor(b.PanelX + 5, lineY + 1, b.PanelW - 15, 8, ListSelected, ListColor);
                        ui.Recolor(b.PanelX + 5, lineY + 9, b.PanelW - 15, 8, ListColor, ListSelected);
                        break;
                }
                return;
            case Keys.Up:
            case Keys.Down:
                if (_s.IsOpen)
                {
                    ClosePanel();
                }
                Outline(_s.Cursor, b.Color);
                _s.MoveCursor(key == Keys.Up ? b.Up : b.Down);
                Outline(_s.Cursor, CursorColor);
                return;
            case Keys.PageUp when listOpen:
                _s.PageUp();
                DrawListPanel(_s.Cursor);
                return;
            case Keys.PageDown when listOpen:
                if (_s.PageDown())
                {
                    DrawListPanel(_s.Cursor);
                }
                return;
            case Keys.End when listOpen:
                _s.ToEnd();
                DrawListPanel(_s.Cursor);
                return;
            case Keys.Home when listOpen:
                _s.ToStart();
                DrawListPanel(_s.Cursor);
                return;
            case Keys.PageUp or Keys.PageDown or Keys.End or Keys.Home:
                return;
        }
        if (key is >= CtrlF1 and <= CtrlF1 + 0x400 && (key & 0xFF) == 0)
        {
            var m = (key >> 8) - (CtrlF1 >> 8);
            if (!_s.Lead(m, PartySize?.Invoke() ?? _state.Party.Slots.Count))
            {
                return;
            }
            DrawPanel();
            // C: the case falls through into the 1..5 "give" case with key = the new leader's number,
            // so with the pack open the item goes to member (m & 7) − 1 (Ctrl-F2 gives to member 0).
            GiveOrShowMember(m, packOpen);
            return;
        }
        if (key is 0x0231 or 0x0332 or 0x0433 or 0x0534 or 0x0635)
        {
            GiveOrShowMember(key, packOpen);
            return;
        }
        switch (char.ToLowerInvariant(Keys.CharacterOf(key)))
        {
            case 'd' when packOpen:
                if (!_s.Drop(ui.ShiftHeld))
                {
                    return;
                }
                RedrawBodyUnderPanel();
                DrawListPanel(PackBox);
                DrawBoxText(EquipmentBox);
                return;
            case 'a' when packOpen:
            case 'u' when packOpen:
                // orig: R031+0x192E / R031+0x19E2 (SheetSession.Equip / Unequip: no test of the line)
                if (Keys.IsLetter(key, 'a'))
                {
                    _s.Equip();
                }
                else
                {
                    _s.Unequip();
                }
                AfterEquipping();
                return;
            case 'p' when _s.Cursor == PackBox && _s.IsOpen:
                // orig: R031+0x1B2C (SheetSession.Drink: no test of the pack's count)
                if (!_s.Drink())
                {
                    return;
                }
                DrawPanel();
                DrawListPanel(PackBox);
                DrawBoxText(EquipmentBox);
                return;
            case 'd' or 'a' or 'u' or 'p':
                // orig: R031+0x185E (D), R031+0x192E (A), R031+0x19E2 (U), R031+0x1B2C (P): without the pack open (P: the cursor on
                //   another box) the case returns at R031+0x1D40; the open box stays open (only the default case R031+0x1C66 closes it)
                return;
        }
        if (key == Keys.AltQ)
        {
            // orig: the key handler's case 0x1000 R031+0x1C3C: R031+0x08F6(-1) (the clean-up: DS:9920 = DS:9934 = 1, the pointer's shape),
            //   then the quit question root 0000:13A0, which asks "Quit to DOS?" in the font the sheet runs with (3); after "no" the sheet goes on
            //   with Save Game and Load Saved Game offered again (vis10 session b25font-sheet-menu: the original's box in font 3)
            SaveAndLoad?.Invoke(1);
            if (new MessageBox(ui).Ask(ui.Words(ScreenTexts.QuitToDos)))
            {
                throw new QuitGameException();
            }
            return;
        }
        // orig: the default case: any other key closes the open box
        if (_s.IsOpen)
        {
            ClosePanel();
        }
    }

    /// <summary>
    /// 1..5 with the pack open: give the line's item (Shift: the stack) to that member;
    /// otherwise the original takes (key &amp; 0xF) + 11 as a member and reads party records
    /// 12..16, past the table.
    /// </summary>
    private void GiveOrShowMember(int key, bool packOpen)
    {
        if (packOpen)
        {
            // orig: R031+0x10AE the 1..5 case (SheetSession.Give: Ctrl-F1's member −1 gives nothing, a DIVERGENCE noted there)
            if (!_s.Give(key, ui.ShiftHeld))
            {
                return;
            }
            RedrawBodyUnderPanel();
            _underPanel = Screen.Save(Current.PanelX - 1, Current.PanelY, Current.PanelW + 3, Current.PanelH + 0x11);
            DrawListPanel(PackBox);
            DrawBoxText(EquipmentBox);
            return;
        }
        // DIVERGENCE: the original reads party[(key & 0xF) + 11] (records past the five members, normally not "able"),
        // draws member frame 12..16 from past the frame table and redraws the sheet; here the sheet is redrawn only.
        _s.Closed();
        _underPanel = null;
        DrawSheet();
        Outline(_s.Cursor, CursorColor);
    }

    // ---- lists ----------------------------------------------------------------------

    /// <summary>
    /// A click on a list line selects it; where the button is released decides the rest: on
    /// another pack line the item moves there, on a formula or saint its page opens, on the
    /// equipment box the line's pack item is readied, on a member in the panel it is given.
    /// </summary>
    // orig: R031+0x1E42 (1462:1E42) draft (bytes match)
    private void ClickListLine(int k, int y)
    {
        var b = Boxes[k];
        var shift = ui.ShiftHeld;
        var line = (y - b.PanelY - 9) / 8;
        if (!_s.HasLine(k, line))
        {
            return;
        }
        ui.Recolor(b.PanelX + 8, b.PanelY + _s.Selected(k) * 8 + 9, b.PanelW - 15, 8, ListSelected, ListColor);
        _s.SelectLine(line);
        ui.Recolor(b.PanelX + 8, b.PanelY + _s.Selected(k) * 8 + 9, b.PanelW - 15, 8, ListColor, ListSelected);
        ScreenDrawing.WaitForRelease(ui);
        var (mx, my) = (ui.LaidMouse.X, ui.LaidMouse.Y);
        var index = line + _s.Top(k);
        if (mx > PartyPanel.Width)
        {
            if (b.PanelX <= mx && b.PanelW + b.PanelX >= mx && b.PanelY <= my && b.PanelH + b.PanelY >= my)
            {
                if (k == PackBox)
                {
                    // C: the drop line is (y − top − k) / 8 with k = 9, the same as the pick line's 9.
                    var to = Math.Max(0, (my - b.PanelY - k) / 8);
                    // orig: R031+0x5922 (item-relocate) via SheetSession.MovePackLine (moved when line `to` holds a stack)
                    if (_s.MovePackLine(k, (y - b.PanelY - k) / 8, to))
                    {
                        DrawListPanel(k);
                    }
                }
                else if (k is FormulaeBox or SaintsBox)
                {
                    if (k == FormulaeBox)
                    {
                        // orig: alchemy-screen-overlay-entry → R033+0x16FA (alchemy-screen-body), formula from R031+0x42AC
                        alchemy.ShowFormula(_state, _s.Who, NthFormula(index));
                    }
                    else
                    {
                        // orig: thunk 09C0:200D(who, saint from R031+0x433C, 0): the saint page (R034, another screen)
                        ShowSaint?.Invoke(_s.Who, NthSaint(index));
                    }
                    DrawSheet();
                    Open(k);
                }
            }
            else if (BoxAt(mx, my, 19) == EquipmentBox)
            {
                // orig: SheetSession.EquipLine (any list's line number readies that pack stack)
                _s.EquipLine(index);
                AfterEquipping();
            }
        }
        // orig: the drop on the party panel (SheetSession.GiveTo tests the member's status)
        else if (b.HoldsItems && _s.GiveTo(my / PartyPanel.RowHeight, index, shift))
        {
            // DORMANT: the original also redraws for a drag from the equipment box (k == 6), which is no list and never gets here.
            if (k == PackBox)
            {
                RedrawBodyUnderPanel();
                _underPanel = Screen.Save(b.PanelX - 1, b.PanelY, b.PanelW + 3, b.PanelH + 0x11);
                DrawListPanel(PackBox);
                DrawBoxText(EquipmentBox);
            }
        }
    }

    /// <summary>After readying or unreadying: the figure, the open box again, and the equipment summary.</summary>
    private void AfterEquipping()
    {
        if (_underPanel is not null)
        {
            Back.Restore(_underPanel);
        }
        DrawBody();
        Screen.CopyFrom(BackPage, 0x3C, 0x0D, 0x8A, 0xAA);
        if (_s.Cursor == PackBox)
        {
            var b = Boxes[PackBox];
            _underPanel = Screen.Save(b.PanelX - 1, b.PanelY, b.PanelW + 3, b.PanelH + 0x11);
            DrawListPanel(_s.Cursor);
        }
        else
        {
            Open(_s.Cursor);
        }
        var e = Boxes[EquipmentBox];
        Screen.CopyFrom(BackPage, e.X, e.Y, e.W, e.H);
        DrawBoxText(EquipmentBox);
    }

    /// <summary>The figure is drawn again and copied over the sheet (which also wipes the open panel).</summary>
    private void RedrawBodyUnderPanel()
    {
        if (_underPanel is not null)
        {
            Back.Restore(_underPanel);
        }
        DrawBody();
        Screen.CopyFrom(BackPage, 0x3C, 0x0D, 0x8A, 0xAA);
    }

    // C: the mouse paths recolour from x + 8, the keyboard paths from x + 5 (the same pixels change: the text starts at x + 9).
    private void MoveSelection(int step, int xOffset)
    {
        var b = Current;
        ui.Recolor(b.PanelX + xOffset, _s.Selected(_s.Cursor) * 8 + b.PanelY + 9, b.PanelW - 15, 8, ListSelected, ListColor);
        _s.Select(step);
        ui.Recolor(b.PanelX + xOffset, _s.Selected(_s.Cursor) * 8 + b.PanelY + 9, b.PanelW - 15, 8, ListColor, ListSelected);
    }

    /// <summary>The n-th known formula (by formula number).</summary>
    // orig: R031+0x42AC (1462:42AC) draft (bytes match)
    private int NthFormula(int n) => Member.Formulas.Order().Skip(n).FirstOrDefault();

    /// <summary>The n-th known saint (by saint number).</summary>
    // orig: R031+0x433C (1462:433C) draft (bytes match)
    private int NthSaint(int n) => Member.Saints.Where(i => i < 136).Order().Skip(n).FirstOrDefault();

    // ---- boxes ----------------------------------------------------------------------

    /// <summary>The first box (of <paramref name="limit"/>) containing the point, or <paramref name="limit"/>.</summary>
    private static int BoxAt(int x, int y, int limit)
    {
        for (var i = 0; i < limit; i++)
        {
            // C: the original scans 18 (19 when dropping) table entries; entries 17 and 18 are zero rectangles at (0, 0).
            if (Boxes[i].Contains(x, y))
            {
                return i;
            }
        }
        return limit;
    }

    /// <summary>
    /// Opens a box: a box that opens nothing just takes the cursor (on the name box a second
    /// time it makes the member the leader); the others show their brush or scroll with details.
    /// </summary>
    // orig: R031+0x264C (1462:264C) draft (bytes match)
    private void Open(int k)
    {
        var b = Boxes[k];
        if (!b.Opens)
        {
            if (k == NameBox && _s.Cursor == k)
            {
                _s.LeadShown();
                DrawPanel();
                Screen.CopyFrom(BackPage, b.X, b.Y, b.W, b.H);
                DrawBoxText(NameBox);
            }
            // C: DS:867E (a box is open) is left as it was: after a click from an open box onto one that opens nothing, keys still act as if open.
            Outline(k, CursorColor);
            return;
        }
        if (_underPanel is not null)
        {
            Screen.Restore(_underPanel);
        }
        _underPanel = Screen.Save(b.PanelX - 1, b.PanelY, b.PanelW + 3, b.PanelH + 0x11);
        if (b.IsList)
        {
            // The scroll unrolls: four growing pieces, then the whole scroll (each drawn on the back page and copied over).
            foreach (var (picture, offset) in new[] { ("armbrsh8", 0x71), ("armbrsh9", 0x67), ("armbrs10", 0x4E), ("armbrs12", 0x11), ("armbrs13", 0) })
            {
                Back.Restore(_underPanel);
                ScreenDrawing.Overlay(ui, picture, b.PanelX, b.PanelY + offset, Back);
                Screen.CopyFrom(BackPage, b.PanelX, b.PanelY, b.PanelW, b.PanelH);
                ui.Wait(20, skippable: false);
            }
            Back.Restore(_underPanel);
            // C: thunk 09C0:1905("full up", "a") is a heap check with no visible effect.
        }
        else
        {
            ScreenDrawing.Overlay(ui, Brushes[b.Picture], b.PanelX, b.PanelY);
        }
        DrawPanelText(k);
        _s.Opened();
    }

    // orig: R031+0x0950 (1462:0950) draft (bytes match)
    private void ClosePanel()
    {
        if (_underPanel is not null)
        {
            Screen.Restore(_underPanel);
            _underPanel = null;
        }
        // orig: R031+0x09A2: pointer-shape-set(0, 0, DS:9C42) whether a panel was open or not (GameUi.PointerHotSpot)
        (ui.PointerHotSpot, ui.PointerShape) = ((0, 0), null);
        _s.Closed();
    }

    /// <summary>
    /// The cursor frame round a box; the party rows (colour 255) are redrawn plain instead,
    /// with the member's frame.
    /// </summary>
    // orig: R031+0x1D44 (1462:1D44) draft (bytes match)
    private void Outline(int k, byte color)
    {
        var b = Boxes[k];
        if (color == 0xFF)
        {
            // DIVERGENCE: the original redraws only panel row k − 12 (party-panel-frame-set(k − 12)); here the whole panel.
            DrawPanel();
            return;
        }
        Screen.Outline(b.X, b.Y, b.W, b.H, color);
    }

    /// <summary>The scroll of a list box again, with its lines.</summary>
    // orig: R031+0x25DE (1462:25DE) draft (bytes match)
    private void DrawListPanel(int k)
    {
        var b = Boxes[k];
        ScreenDrawing.Overlay(ui, "armbrs13", b.PanelX, b.PanelY);
        DrawPanelText(k);
    }

    // ---- drawing the sheet ----------------------------------------------------------

    private void ShowMember(int m)
    {
        _s.Show(m);
        _underPanel = null;
        DrawSheet();
    }

    /// <summary>The background, the figure, the panel with the member's frame, and the text of every box.</summary>
    // orig: R031+0x0A82 (1462:0A82) draft (bytes match)
    private void DrawSheet()
    {
        var picture = ui.Assets.Picture("armback");
        Back.DrawPicture(picture);
        Screen.UsePalette(picture);
        DrawBody();
        Screen.CopyFrom(BackPage, 0x3C, 0, 0x104, 0xC8);
        DrawPanel();
        for (var i = 0; i < BoxCount; i++)
        {
            DrawBoxText(i);
        }
        // orig: R031+0x0B0B: pointer-shape-set(0, 0, DS:9C42) after the boxes: the sheet's gauntlet has hot spot (0, 0), also when it was
        //   opened from the map (GameUi.PointerHotSpot); tests: PointerHotSpotTests
        (ui.PointerHotSpot, ui.PointerShape) = ((0, 0), null);
    }

    /// <summary>The party panel on the sheet's left edge, the shown member framed.</summary>
    // orig: R031+0x2518 (1462:2518) draft (bytes match): the frame from the table at far 290E:3829
    private void DrawPanel()
    {
        Screen.CopyFrom(BackPage, 0, 0, PartyPanel.Width, Canvas.Height);
        panel.Draw(Screen, _state.Party);
        Screen.Outline(0, _s.Who * PartyPanel.RowHeight, 59, _s.Who == 4 ? 39 : 40, MemberFrameColor);
    }

    /// <summary>
    /// The figure on the back page: limb armour, vital armour, hand weapon, shield, missile
    /// weapon over the plain figure, each by its item type.
    /// </summary>
    // orig: R031+0x0B22 (1462:0B22) draft (bytes match)
    private void DrawBody()
    {
        var m = Member;
        var back = Back;
        void Draw(string? picture)
        {
            if (picture is not null)
            {
                ScreenDrawing.Overlay(ui, picture, 0x3B, 0x0D, back);
            }
        }
        var last = "armsback";
        Draw(last);
        // C: the original keeps the picture handle in one variable; an armour type outside its switch draws the previous picture again and frees
        // it again (R031+0x0BF6, +0x0C8C): DOS checks only the arena signature, which the freed block keeps, so the sheet goes on.
        last = LimbArmorPicture(m.LimbArmor.Type) ?? last;
        Draw(last);
        last = VitalArmorPicture(m.VitalArmor.Type) ?? last;
        Draw(last);
        if (!m.Weapon.IsEmpty && WeaponPicture(m.Weapon.Type) is { } weapon)
        {
            Draw($"weapon{weapon}");
        }
        if (!m.Shield.IsEmpty)
        {
            Draw(m.Shield.Type == 0x5F ? "smallsh" : "mediumsh");
        }
        if (!m.MissileWeapon.IsEmpty && MissilePicture(m.MissileWeapon.Type) is { } missile)
        {
            Draw($"weapon{missile}");
        }
    }

    private static string? LimbArmorPicture(int type) => type switch
    {
        0x4C or 0x4D => "pad-lims",
        0x4E => "leat-lim",
        0x4F => "studlim",
        0x50 => "cuirblim",
        0x51 => "scalelim",
        0x52 => "chainlim",
        0x53 => "briglim",
        0x54 => "platelim",
        _ => null,
    };

    private static string? VitalArmorPicture(int type) => type switch
    {
        0x43 or 0x44 => "pad-vit",
        0x45 => "leat-vit",
        0x46 => "studvit",
        0x47 => "cuirbvit",
        0x48 => "scalevit",
        0x49 => "chainvit",
        0x4A => "brigvit",
        0x4B => "platevit",
        _ => null,
    };

    /// <summary>The WEAPONn picture of a hand weapon type (types 7 and 23 share picture 0, 9 and 26 picture 1).</summary>
    private static int? WeaponPicture(int type) => type switch
    {
        >= 0 and <= 6 => type + 2,
        7 or 23 => 0,
        8 => 9,
        9 or 26 => 1,
        >= 10 and <= 21 => type + 1,
        22 => 24,
        24 or 25 => type + 2,
        >= 27 and <= 33 => type + 1,
        _ => null,
    };

    /// <summary>The picture of a missile weapon type: as for hand weapons, but type 23 is picture 23.</summary>
    private static int? MissilePicture(int type) => type switch
    {
        23 => 23,
        >= 24 and <= 33 => WeaponPicture(type),
        _ => null,
    };

    /// <summary>The text of a box on the sheet itself.</summary>
    // orig: R031+0x39C6 (1462:39C6) draft (bytes match), called through R031+0x39A0
    private void DrawBoxText(int i)
    {
        var m = Member;
        var b = Boxes[i];
        var t = ui.Text;
        var c = Screen;
        switch (i)
        {
            case 0:
                c.CopyFrom(BackPage, b.X, b.Y, b.W, b.H);
                t.Draw(c, b.X + 5, 3, m.Name, TextColor);
                t.Draw(c, b.X + 0x82, b.Y + 3, ui.Words(_state.Party.LeaderIndex == _s.Who ? "exe:DS:2DD1" : "exe:DS:2DD8"), TextColor);
                break;
            case 1:
                t.Draw(c, b.X + 5, b.Y + 3, ui.Words("exe:DS:2DE3"), TextColor);
                t.Draw(c, b.X + 0x19, b.Y + 3, ui.Format("exe:DS:2DE7", m.Age), TextColor);
                t.Draw(c, b.X + 0x28, b.Y + 3, ui.Words(m.IsFemale ? "exe:DS:2DF0" : "exe:DS:2DEC"), TextColor);
                break;
            case 2:
                c.CopyFrom(BackPage, b.X, b.Y, b.W, b.H);
                string[] loads = ["exe:DS:2DF4", "exe:DS:2DFF", "exe:DS:2E0B", "exe:DS:2E16"];
                t.Draw(c, b.X + 0x0C, b.Y + 4, ui.Words(loads[MemberRules.EncumbranceBand(_state, _s.Who)]), TextColor);
                // C: the load is a signed byte (record +0x35) and the limit Str + End in int; "%02d/%02d" (DS:2E21) prints a negative load as "-5".
                t.Draw(c, b.X + 0x48, b.Y + 4, ui.Format("exe:DS:2E21", (sbyte)m.Encumbrance, m[Attribute.Strength] + m[Attribute.Endurance]), TextColor);
                break;
            case 3:
            case 17:
                // DORMANT: case 17 (the same labels, values from y 0x20) is never drawn: the sheet draws boxes 0..16.
                for (var j = 0; j < 7; j++)
                {
                    t.Draw(c, 0xED, 0x12 + j * 8, ui.Words((i == 3 ? AttributeLabels : AttributeLabels17)[j]), TextColor);
                    t.Draw(c, 0xCA, (i == 3 ? 0x12 : 0x20) + j * 8, ui.Format(i == 3 ? "exe:DS:2E46" : "exe:DS:2F0D", m.Attributes[j], m.AttributeMaximums[j]), TextColor);
                }
                break;
            case 4:
                DrawSkillColumn(WeaponLabels, "exe:DS:2E73", 0, 0x12, 0x10C);
                break;
            case 5:
                DrawSkillColumn(LearnedLabels, "exe:DS:2E95", 7, 0x55, 0x10C);
                break;
            case 6:
                c.CopyFrom(BackPage, b.X, b.Y, b.W, b.H);
                string[] none = ["exe:DS:2E9A", "exe:DS:2EA4", "exe:DS:2EAF", "exe:DS:2EBA", "exe:DS:2EC4"];
                for (var slot = 0; slot < 5; slot++)
                {
                    var code = Inventory.EquippedType(_state, _s.Who, (EquipmentSlot)slot) != -1 ? Inventory.Equipped(_state, _s.Who, (EquipmentSlot)slot) : -1;
                    t.Draw(c, b.X + 4, 0x58 + slot * 10, code != -1 ? ui.ItemShortName(code, _state.Item(code).ShortName) : ui.Words(none[slot]), TextColor);
                }
                DrawBoxText(2);
                break;
            case 7:
                DrawSkillColumn(OtherLabels, "exe:DS:2EED", 13, 0x91, b.X + 3);
                break;
        }
    }

    private void DrawSkillColumn(string[] labels, string valueFormat, int firstSkill, int top, int valueX)
    {
        for (var j = 0; j < labels.Length; j++)
        {
            ui.Text.Draw(Screen, 0x11D, top + j * 8, ui.Words(labels[j]), TextColor);
            ui.Text.Draw(Screen, valueX, top + j * 8, ui.Format(valueFormat, Member.Skills[firstSkill + j]), TextColor);
        }
    }

    // the open panels' labels: the far table R136+0xA4F7
    private string FullLabel(int k) => ui.Word(OriginalWords.SheetLabels, k);

    /// <summary>The text of an open box: full names and values, the equipment, or a list.</summary>
    // orig: R031+0x2B14 (1462:2B14) draft (bytes match)
    private void DrawPanelText(int k)
    {
        var m = Member;
        var b = Boxes[k];
        var t = ui.Text;
        var c = Screen;
        int x = b.PanelX, y = b.PanelY;
        switch (k)
        {
            case 3:
                for (var j = 0; j < 7; j++)
                {
                    t.Draw(c, x + 0x2B, y + 5 + j * 8, FullLabel(j), TextColor);
                    t.Draw(c, x + 8, y + 5 + j * 8, ui.Format("exe:DS:2D5A", m.Attributes[j], m.AttributeMaximums[j]), TextColor);
                }
                break;
            case 4:
                DrawFullSkills(b, "exe:DS:2D64", 0x16, 7, 0, 7);
                break;
            case 5:
                DrawFullSkills(b, "exe:DS:2D69", 0x16, 14, 7, 6);
                break;
            case 7:
                DrawFullSkills(b, "exe:DS:2DBD", 0x20, 20, 13, 6);
                break;
            case 6:
                string[] none = ["exe:DS:2D6E", "exe:DS:2D78", "exe:DS:2D88", "exe:DS:2D96", "exe:DS:2DA0"];
                for (var slot = 0; slot < 5; slot++)
                {
                    // C: the original indexes the item list with slot-item's result, −1 included, when the type is set but no stack matches.
                    var code = Inventory.EquippedType(_state, _s.Who, (EquipmentSlot)slot) != -1 ? Inventory.Equipped(_state, _s.Who, (EquipmentSlot)slot) : -1;
                    if (code != -1)
                    {
                        t.Draw(c, x + 4, 0x58 + slot * 10, ui.ItemName(code, _state.Item(code).Name), TextColor);
                    }
                    else
                    {
                        t.Draw(c, x + 5, 0x58 + slot * 10, ui.Words(none[slot]), TextColor);
                    }
                }
                if (_state.Events.OwnedBy(EventKinds.SkillBoost, _s.Who) || _state.Events.OwnedBy(EventKinds.AttributeBoost, _s.Who)
                    || _state.Events.OwnedBy(EventKinds.PotionBonus, _s.Who))
                {
                    t.Draw(c, x + 5, 0x8A, ui.Words("exe:DS:2DB2"), TextColor);
                }
                break;
            case PackBox:
                if (m.Pack.Count <= 0)
                {
                    // C: the line count (DS:E7A4) keeps the last list's value when the pack is empty.
                    break;
                }
                _s.KeepInPack(k);
                for (var j = 0; j < b.Lines; j++)
                {
                    if (_s.Top(k) + j < m.Pack.Count)
                    {
                        var st = m.Pack[_s.Top(k) + j];
                        t.Draw(c, x + 9, j * 8 + y + 9, ui.Format("exe:DS:2DC2", ui.ItemNameArg(st.Code & 0xFFF, _state.Item(st.Code & 0xFFF).Name), st.Quality, st.Count), ListColor);  // "%Fs %02dq (%d)"
                    }
                }
                HighlightLine(k);
                // C: DS:E7A4 = the pack's count as a signed char (R031+0x35B5)
                _s.ListShown(m.Pack.Stacks);
                break;
            case FormulaeBox:
                DrawNames(m.Formulas.Order().Select(f => f < formulas.Count ? ui.FormulaName(formulas[f].Index, formulas[f].Name) : "").ToList(), k);
                break;
            case SaintsBox:
                // C: the original reads 17 of the 20 bytes of saint bits: saints 0..135.
                DrawNames(m.Saints.Where(i => i < 136).Order().Select(i => i < saintNames.Count ? ui.SaintName(i, saintNames[i]) : "").ToList(), k);
                break;
        }
    }

    private void DrawFullSkills(Box b, string valueFormat, int labelOffset, int firstLabel, int firstSkill, int count)
    {
        for (var j = 0; j < count; j++)
        {
            ui.Text.Draw(Screen, b.PanelX + labelOffset, b.PanelY + 5 + j * 8, FullLabel(firstLabel + j), TextColor);
            ui.Text.Draw(Screen, b.PanelX + 8, b.PanelY + 5 + j * 8, ui.Format(valueFormat, Member.Skills[firstSkill + j]), TextColor);
        }
    }

    /// <summary>The visible names of a formula or saint list, from the first visible line.</summary>
    private void DrawNames(List<string> names, int k)
    {
        var b = Boxes[k];
        for (var n = 0; n < b.Lines && _s.Top(k) + n < names.Count; n++)
        {
            ui.Text.Draw(Screen, b.PanelX + 9, n * 8 + b.PanelY + 9, names[_s.Top(k) + n], ListColor);
        }
        HighlightLine(k);
        _s.ListShown(names.Count);
    }

    private void HighlightLine(int k)
    {
        var b = Boxes[k];
        ui.Recolor(b.PanelX + 8, _s.Selected(k) * 8 + b.PanelY + 9, b.PanelW - 15, 8, ListColor, ListSelected);
    }
}
