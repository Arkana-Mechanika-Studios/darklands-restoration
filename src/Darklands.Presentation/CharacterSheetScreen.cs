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
// orig: member_sheet R031+0x0124 (1462:0124) draft (the byte match: 1 compare-order line; an earlier port's check matched the
//   bytes with an alias of `who` for the equality guard, the logic unchanged); tests: none (visual, headless)
// DORMANT: R031+0x0000 (inventory_leave) opens the sheet from a state without the world data loaded and writes LOCS.TMP/EVENTS.TMP afterwards; nothing in this game opens the sheet that way.
// DIVERGENCE: the pointer does not change to the hand pictures (handicon, handicn2) over lists; the menu bar (menu_bar_run) comes with GameUi.Poll (MenuBar), its Alt keys through MenuBarKey.
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

    // orig: R136+0xBBE7 far 290E:3559 (the box table, read from the EXE's data section) the reference
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

    // orig: R136+0xA4F7 (attribute and skill labels of the open boxes) the reference
    private static readonly string[] FullLabels =
    [
        "Endurance", "Strength", "Agility", "Perception", "Intelligence", "Charisma", "Divine Favor", "Edged Wpns", "Impact Wpns",
        "Flail Wpns", "Polearm Wpns", "Thrown Wpns", "Bow Weapons", "Missile Device", "Alchemy", "Religious Trng", "Virtue",
        "Speak Common", "Speak Latin", "Read & Write", "Healing", "Artifice", "Stealth", "Streetwise", "Riding", "Woodwise",
    ];

    private static readonly string[] AttributeLabels = ["End", "Str", "Agl", "Per", "Int", "Chr", "DF"];
    private static readonly string[] WeaponLabels = ["wEdg", "wImp", "wFll", "wPol", "wThr", "wBow", "wMsD"];
    private static readonly string[] LearnedLabels = ["Alch", "Relg", "Virt", "SpkC", "SpkL", "R&W"];
    private static readonly string[] OtherLabels = ["Heal", "Artf", "Stlh", "StrW", "Ride", "WdWs"];

    /// <summary>The first visible line and the selected line of each list box, per member (kept between visits, as the game's table).</summary>
    private readonly int[,] _top = new int[19, Party.MaxMembers];
    private readonly int[,] _selected = new int[19, Party.MaxMembers];

    /// <summary>The back page (the game's back_win) the sheet restores rectangles from.</summary>
    private readonly Screen _backScreen = new();

    private GameState _s = null!;
    private int _who;
    private int _cursor;
    private bool _open;
    private SavedRegion? _underPanel;
    private int _listCount;

    /// <summary>Keys the game's menu bar handles while the sheet is up (Alt-Q, Alt-P …); true when one was handled.</summary>
    public Func<int, bool>? MenuBarKey { get; set; }

    /// <summary>The saint page a click in the Saints list opens (the prayer screen, not part of this screen).</summary>
    public Action<int, int>? ShowSaint { get; set; }

    /// <summary>In a battle, for a member who left the field through an exit: the "can't view" message, and true (the sheet then stays on its member).</summary>
    public Func<int, bool>? CannotView { get; set; }

    private Canvas Back => new(_backScreen);

    private SavedScreen BackPage => new(_backScreen.Pixels, _backScreen.Palette);

    private Canvas Screen => ui.Canvas;

    private Character Member => _s.Party.Slot(_who);

    private Box Current => Boxes[_cursor];

    private int Index => _top[_cursor, _who] + _selected[_cursor, _who];

    // ---- the loop -------------------------------------------------------------------

    /// <summary>Sets the Save Game and Load Saved Game items of the hot keys and the menu bar (1 on, 2 off).</summary>
    public Action<int>? SaveAndLoad { get; set; }

    /// <summary>The game's party_size where the party shown is not the game's party (the character menu: 0); null: the party's members.</summary>
    // C: the Ctrl-F1..F5 case of the original's key handler tests `slot < party_size` (DS:A67E, signed) before the status
    public Func<int>? PartySize { get; set; }

    // C: member_sheet sets DS:9920 = DS:9934 = 2 once the member is there (R031+0x0144) (no Save Game, no Load Saved Game while the sheet is up); R031+0x08F6
    //   on leaving (Esc, the member's own portrait or F key) sets both to 1, whatever they were before (a sheet opened from a stay leaves saving on); the return on an empty
    //   member box skips it
    public void Show(GameState state, int who)
    {
        _s = state;
        _who = who;
        // orig: member_sheet's guard: status 0 or 5 leaves at once
        if (Member.Status is CharacterStatus.Empty or CharacterStatus.Away)
        {
            return;
        }
        SaveAndLoad?.Invoke(2);
        _cursor = NameBox;
        _underPanel = null;
        // orig: R031+0x09B8 (1462:09B8) draft: fonts, pictures, then the sheet
        DrawSheet();
        _open = false;
        Outline(_cursor, CursorColor);
        // orig: R031+0x018C the button waited out after drawing
        ScreenDrawing.WaitForRelease(ui);
        // C: the screen below keeps no GameUi.Redraw while the sheet is up: the party information sets party_icons_dirty for the sheet's own test
        var below = ui.Redraw;
        ui.Redraw = null;
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
            var mouse = ui.Mouse;
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

    /// <summary>party_icons_dirty in the sheet's loop: the open box closed and the sheet drawn again, the cursor frame not.</summary>
    // orig: R031+0x06D2: if DS:9C46 { DS:9C46 = 0; the panel (root 0000:1588 / 0000:1716 with 0x100), R031+0x2518(member, 0xB), DS:867E = 0, the saved panel
    //   area DS:867A freed (0 after), R031+0x0A82(member) } (no R031+0x1D44: the cursor frame comes back with the next move)
    private void RedrawAfterPartyInformation()
    {
        _open = false;
        _underPanel = null;
        DrawSheet();
    }

    /// <summary>A click: on the sheet (a list line, its arrows, a box) or on the party panel. True to leave.</summary>
    private bool Click(int x, int y)
    {
        var b = Current;
        if (x > PartyPanel.Width)
        {
            if (_open && b.IsList && b.PanelX <= x && b.PanelW + b.PanelX >= x && b.PanelY + 9 <= y && b.PanelH + b.PanelY - 14 >= y)
            {
                ClickListLine(_cursor, y);
            }
            else if (_open && b.IsList && b.PanelX <= x && b.PanelW + b.PanelX >= x && b.PanelY <= y && b.PanelY + 9 >= y)
            {
                // orig: the scroll's up arrow: `while (mouse_buttons & 1) { step; R030+0x3096(200); }` (R031+0x0286, R031+0x036A): the
                // button is tested before each step, and the 200 ms wait follows every step, the last one too
                while (ui.Mouse.Left)
                {
                    if (_selected[_cursor, _who] == 0)
                    {
                        if (_top[_cursor, _who] != 0)
                        {
                            _top[_cursor, _who]--;
                        }
                        DrawListPanel(_cursor);
                    }
                    else
                    {
                        MoveSelection(-1, 8);
                    }
                    ui.Delay(200);
                }
            }
            else if (_open && b.IsList && b.PanelX <= x && b.PanelW + b.PanelX >= x && b.PanelH + b.PanelY - 14 <= y && b.PanelH + b.PanelY >= y)
            {
                // orig: the down arrow (R031+0x03E8, R031+0x04FD): the same loop; the end of the list breaks out before the wait
                while (ui.Mouse.Left)
                {
                    if (_selected[_cursor, _who] >= b.Lines - 1)
                    {
                        if (Index >= _listCount)
                        {
                            break;
                        }
                        _top[_cursor, _who]++;
                        DrawListPanel(_cursor);
                    }
                    else
                    {
                        if (Index >= _listCount - 1)
                        {
                            break;
                        }
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
                if (_open && _underPanel is not null)
                {
                    Screen.Restore(_underPanel);
                    _underPanel = null;
                }
                if (_open && _cursor == i)
                {
                    Outline(_cursor, CursorColor);
                    _open = false;
                }
                else
                {
                    Outline(_cursor, Current.Color);
                    Open(i);
                    _cursor = i;
                }
            }
            return false;
        }
        var m = y / PartyPanel.RowHeight;
        if (m == _who)
        {
            // orig: R031+0x0635 to the clean-up (R031+0x08E1: R031+0x08F6, DS:9920 = DS:9934 = 1)
            SaveAndLoad?.Invoke(1);
            return true;
        }
        if (_s.Party.Slot(m).Status == CharacterStatus.Able)
        {
            // orig: R031+0x0124: in a battle (DS:907A == 1) a member standing on floor 0x9D gets thunk 09C0:1D51(m) instead
            if (CannotView?.Invoke(m) == true)
            {
                return false;
            }
            ShowMember(m);
            Outline(_cursor, CursorColor);
        }
        return false;
    }

    /// <summary>A key; true to leave.</summary>
    private bool Key(int key)
    {
        if (key is >= Keys.F1 and <= Keys.F5 && (key & 0xFF) == 0)
        {
            var m = (key >> 8) - (Keys.F1 >> 8);
            if (m == _who)
            {
                // orig: R031+0x0828 to the clean-up (R031+0x08E1)
                SaveAndLoad?.Invoke(1);
                return true;
            }
            if (_s.Party.Slot(m).Status == CharacterStatus.Able)
            {
                // orig: the battle branch (thunk 09C0:1D51) as for a click
                if (CannotView?.Invoke(m) == true)
                {
                    return false;
                }
                ShowMember(m);
                Outline(_cursor, CursorColor);
            }
            return false;
        }
        if (key == Keys.Enter && _cursor is >= FirstMemberBox and <= FirstMemberBox + 4)
        {
            var m = _cursor - FirstMemberBox;
            if (_s.Party.Slot(m).Status == CharacterStatus.Empty)
            {
                // C: the original returns here without the clean-up of R031+0x08F6 (fonts and pointer stay as the sheet set them).
                return true;
            }
            // orig: the battle branch (thunk 09C0:1D51) before the `who != m` test: the sheet stays (no leaving for the member shown)
            if (CannotView?.Invoke(m) == true)
            {
                return false;
            }
            if (_who != m)
            {
                ShowMember(m);
                return false;
            }
            key = Keys.Escape;
        }
        if (key == Keys.Escape)
        {
            Outline(_cursor, Current.Color);
            // orig: R031+0x08F6 (1462:08F6) draft: restores fonts and pointer (nothing to restore here), DS:9920 = DS:9934 = 1
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
        var listOpen = b.IsList && _open;
        var packOpen = b.HoldsItems && _open;
        switch (key)
        {
            case Keys.Enter:
                if (_open)
                {
                    ClosePanel();
                    Outline(_cursor, CursorColor);
                }
                else
                {
                    Open(_cursor);
                }
                return;
            case Keys.Left:
            case Keys.Right:
                if (_open)
                {
                    ClosePanel();
                }
                Outline(_cursor, b.Color);
                _cursor = key == Keys.Left ? b.Left : b.Right;
                Outline(_cursor, CursorColor);
                return;
            case Keys.Up when listOpen:
                if (_selected[_cursor, _who] == 0)
                {
                    if (_top[_cursor, _who] == 0)
                    {
                        return;
                    }
                    _top[_cursor, _who]--;
                    DrawListPanel(_cursor);
                }
                else
                {
                    MoveSelection(-1, 5);
                }
                return;
            case Keys.Down when listOpen:
                _selected[_cursor, _who]++;
                if (_selected[_cursor, _who] >= b.Lines)
                {
                    var line = _selected[_cursor, _who];
                    _selected[_cursor, _who]--;
                    if (_top[_cursor, _who] + line >= _listCount)
                    {
                        return;
                    }
                    _top[_cursor, _who]++;
                    DrawListPanel(_cursor);
                }
                else
                {
                    if (Index >= _listCount)
                    {
                        _selected[_cursor, _who]--;
                        return;
                    }
                    var lineY = _selected[_cursor, _who] * 8 + b.PanelY;
                    Screen.Recolor(b.PanelX + 5, lineY + 1, b.PanelW - 15, 8, ListSelected, ListColor);
                    Screen.Recolor(b.PanelX + 5, lineY + 9, b.PanelW - 15, 8, ListColor, ListSelected);
                }
                return;
            case Keys.Up:
            case Keys.Down:
                if (_open)
                {
                    ClosePanel();
                }
                Outline(_cursor, b.Color);
                _cursor = key == Keys.Up ? b.Up : b.Down;
                Outline(_cursor, CursorColor);
                return;
            case Keys.PageUp when listOpen:
                _top[_cursor, _who] = _top[_cursor, _who] > 14 ? _top[_cursor, _who] - 14 : 0;
                DrawListPanel(_cursor);
                return;
            case Keys.PageDown when listOpen:
                if (_top[_cursor, _who] + 14 >= _listCount)
                {
                    return;
                }
                _top[_cursor, _who] += 14;
                if (Index >= _listCount)
                {
                    _top[_cursor, _who] = _listCount >= 14 ? _listCount - 14 : 0;
                    _selected[_cursor, _who] = _listCount >= 14 ? 13 : _listCount - 1;
                }
                DrawListPanel(_cursor);
                return;
            case Keys.End when listOpen:
                _top[_cursor, _who] = _listCount >= 14 ? _listCount - 14 : 0;
                _selected[_cursor, _who] = _listCount >= 14 ? 13 : _listCount - 1;
                DrawListPanel(_cursor);
                return;
            case Keys.Home when listOpen:
                _top[_cursor, _who] = 0;
                _selected[_cursor, _who] = 0;
                DrawListPanel(_cursor);
                return;
            case Keys.PageUp or Keys.PageDown or Keys.End or Keys.Home:
                return;
        }
        if (key is >= CtrlF1 and <= CtrlF1 + 0x400 && (key & 0xFF) == 0)
        {
            var m = (key >> 8) - (CtrlF1 >> 8);
            if (m >= (PartySize?.Invoke() ?? _s.Party.Members.Count) || _s.Party.Members[m].Status != CharacterStatus.Able)
            {
                return;
            }
            _s.Party.LeaderIndex = m;
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
                if (Index >= Member.Pack.Count)
                {
                    return;
                }
                if (ui.ShiftHeld)
                {
                    Inventory.RemoveStackAt(_s, _who, Index);
                }
                else
                {
                    Inventory.TakeAt(_s, _who, Index);
                }
                RedrawBodyUnderPanel();
                DrawListPanel(PackBox);
                DrawBoxText(EquipmentBox);
                return;
            case 'a' when packOpen:
            case 'u' when packOpen:
                // C: R031+0x192E / R031+0x19E2 test only the box (flag 0x10) and that it is open: the line need not be a stack, an entry past the pack's
                // end (Pack: what was last there, e.g. the stack just dropped or given) is readied or unreadied as it is.
                if (Keys.IsLetter(key, 'a'))
                {
                    // orig: R031+0x5A02 (18E7:11B2) via PackRules.Equip
                    PackRules.Equip(_s, _who, Index);
                }
                else
                {
                    // orig: R031+0x5B38 (18E7:12E8) via Inventory.Unequip
                    Inventory.Unequip(_s, _who, Index);
                }
                AfterEquipping();
                return;
            case 'p' when _cursor == PackBox && _open:
                // C: R031+0x1B2C: as for A and U no test of the pack's count: a line past the end drinks the entry left there (the count of the
                // pack, a signed char, goes down all the same: item_delete; -1 from an empty pack), and the list count DS:E7A4 follows it signed.
                if (!PotionEffects.CanDrink(_s, Member.Pack[Index].Code))
                {
                    return;
                }
                PotionEffects.DrinkFromPack(_s, _who, Index);
                if (Member.Pack.Stacks < _listCount)
                {
                    if (_top[PackBox, _who] != 0)
                    {
                        _top[PackBox, _who]--;
                    }
                    else if (_selected[PackBox, _who] != 0)
                    {
                        _selected[PackBox, _who]--;
                    }
                    _listCount = Member.Pack.Stacks;
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
            if (new MessageBox(ui).Ask("Quit to DOS?"))
            {
                throw new QuitGameException();
            }
            return;
        }
        // orig: the default case: any other key closes the open box
        if (_open)
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
            var to = (key & 7) - 1;
            // DIVERGENCE: Ctrl-F1 makes to −1, which the original reads as the record before member 0; here nothing is given.
            // C: the original reads record −1's status (DS:9BE9) and count (DS:9C53):
            //   DS:9BE9 is the first word of the 20th line of the battle's Throw sub-menu (DS:9A47 + 19 * 0x16, R019+0x0C80, a 22-byte record
            //   per line), 0 (BSS, read so at start-up) until a battle lists 19 or more Throw lines for a member, then the low byte of a
            //   potion-name pointer (DS:114A / DS:114E[]: 0xB0..0x14, never 0 or 5); while it is 0 the original returns as here. After such a
            //   battle the give goes on when DS:9C53, the last sub-menu's text column (MenuBar), is below 0x40 as a signed byte:
            //   give_one / give_stack(who, −1) add the item to the leader, member 0 (add_item_rec's −1, R031+0x4850), and take it from the
            //   member even when member 0's pack is full (the item is lost). That history (a Throw list of 19 lines, the menu bar's
            //   geometry) is the same not-modelled UI memory as DS:9C53 elsewhere (SaintBook, TacticalBattle.Dungeon): not reproduced.
            if (to < 0)
            {
                return;
            }
            var target = _s.Party.Slot(to);
            if (target.Status is CharacterStatus.Empty or CharacterStatus.Away || Index >= Member.Pack.Count)
            {
                return;
            }
            if (ui.ShiftHeld)
            {
                // orig: R031+0x48CC (give_stack) R031+0x48CC (18E7:007C) via PackRules.GiveStack
                PackRules.GiveStack(_s, _who, (key & 0xF) - 1, Index);
            }
            else
            {
                // orig: R031+0x4850 (give_one) R031+0x4850 (18E7:0000) via PackRules.GiveOne
                PackRules.GiveOne(_s, _who, (key & 0xF) - 1, Index);
            }
            RedrawBodyUnderPanel();
            _underPanel = Screen.Save(Current.PanelX - 1, Current.PanelY, Current.PanelW + 3, Current.PanelH + 0x11);
            DrawListPanel(PackBox);
            DrawBoxText(EquipmentBox);
            return;
        }
        // DIVERGENCE: the original reads party[(key & 0xF) + 11] (records past the five members, normally not "able"),
        // draws member frame 12..16 from past the frame table and redraws the sheet; here the sheet is redrawn only.
        _open = false;
        _underPanel = null;
        DrawSheet();
        Outline(_cursor, CursorColor);
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
        if (line + _top[k, _who] >= _listCount)
        {
            return;
        }
        Screen.Recolor(b.PanelX + 8, b.PanelY + _selected[k, _who] * 8 + 9, b.PanelW - 15, 8, ListSelected, ListColor);
        _selected[_cursor, _who] = line;
        Screen.Recolor(b.PanelX + 8, b.PanelY + _selected[k, _who] * 8 + 9, b.PanelW - 15, 8, ListColor, ListSelected);
        ScreenDrawing.WaitForRelease(ui);
        var (mx, my) = (ui.Mouse.X, ui.Mouse.Y);
        var index = line + _top[k, _who];
        if (mx > PartyPanel.Width)
        {
            if (b.PanelX <= mx && b.PanelW + b.PanelX >= mx && b.PanelY <= my && b.PanelH + b.PanelY >= my)
            {
                if (k == PackBox)
                {
                    // C: the drop line is (y − top − k) / 8 with k = 9, the same as the pick line's 9.
                    var to = Math.Max(0, (my - b.PanelY - k) / 8);
                    if (Member.Pack.Count > to)
                    {
                        // orig: R031+0x5922 (item_move) R031+0x5922 (18E7:10D2) via PackRules.Move
                        PackRules.Move(_s, _who, _top[k, _who] + (y - b.PanelY - k) / 8, _top[k, _who] + to);
                        DrawListPanel(k);
                    }
                }
                else if (k is FormulaeBox or SaintsBox)
                {
                    if (k == FormulaeBox)
                    {
                        // orig: alc_view_vec → R033+0x16FA (alc_view), formula from R031+0x42AC
                        alchemy.ShowFormula(_s, _who, NthFormula(index));
                    }
                    else
                    {
                        // orig: thunk 09C0:200D(who, saint from R031+0x433C, 0): the saint page (R034, another screen)
                        ShowSaint?.Invoke(_who, NthSaint(index));
                    }
                    DrawSheet();
                    Open(k);
                }
            }
            else if (BoxAt(mx, my, 19) == EquipmentBox)
            {
                // C: any list may be dropped on the equipment box: from Formulae or Saints the line number readies that pack stack.
                if (index < Member.Pack.Count)
                {
                    PackRules.Equip(_s, _who, index);
                }
                AfterEquipping();
            }
        }
        else if (_s.Party.Slot(my / PartyPanel.RowHeight).Status is not (CharacterStatus.Empty or CharacterStatus.Away) && b.HoldsItems)
        {
            var to = my / PartyPanel.RowHeight;
            if (shift)
            {
                PackRules.GiveStack(_s, _who, to, index);
            }
            else
            {
                PackRules.GiveOne(_s, _who, to, index);
            }
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
        if (_cursor == PackBox)
        {
            var b = Boxes[PackBox];
            _underPanel = Screen.Save(b.PanelX - 1, b.PanelY, b.PanelW + 3, b.PanelH + 0x11);
            DrawListPanel(_cursor);
        }
        else
        {
            Open(_cursor);
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
        Screen.Recolor(b.PanelX + xOffset, _selected[_cursor, _who] * 8 + b.PanelY + 9, b.PanelW - 15, 8, ListSelected, ListColor);
        _selected[_cursor, _who] += step;
        Screen.Recolor(b.PanelX + xOffset, _selected[_cursor, _who] * 8 + b.PanelY + 9, b.PanelW - 15, 8, ListColor, ListSelected);
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
            if (k == NameBox && _cursor == k)
            {
                _s.Party.LeaderIndex = _who;
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
        _open = true;
    }

    // orig: R031+0x0950 (1462:0950) draft (bytes match)
    private void ClosePanel()
    {
        if (_underPanel is not null)
        {
            Screen.Restore(_underPanel);
            _underPanel = null;
        }
        _open = false;
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
            // DIVERGENCE: the original redraws only panel row k − 12 (party_panel_frames(k − 12)); here the whole panel.
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
        _who = m;
        _open = false;
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
    }

    /// <summary>The party panel on the sheet's left edge, the shown member framed.</summary>
    // orig: R031+0x2518 (1462:2518) draft (bytes match): the frame from the table at far 290E:3829
    private void DrawPanel()
    {
        Screen.CopyFrom(BackPage, 0, 0, PartyPanel.Width, Canvas.Height);
        panel.Draw(Screen, _s.Party);
        Screen.Outline(0, _who * PartyPanel.RowHeight, 59, _who == 4 ? 39 : 40, MemberFrameColor);
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
                t.Draw(c, b.X + 0x82, b.Y + 3, _s.Party.LeaderIndex == _who ? "Leader" : "Not Leader", TextColor);
                break;
            case 1:
                t.Draw(c, b.X + 5, b.Y + 3, "Age", TextColor);
                t.Draw(c, b.X + 0x19, b.Y + 3, $"{m.Age:00}", TextColor);
                t.Draw(c, b.X + 0x28, b.Y + 3, m.IsFemale ? "(F)" : "(M)", TextColor);
                break;
            case 2:
                c.CopyFrom(BackPage, b.X, b.Y, b.W, b.H);
                string[] loads = ["Light Load", "Normal Load", "Heavy Load", "Overloaded"];
                t.Draw(c, b.X + 0x0C, b.Y + 4, loads[MemberRules.EncumbranceBand(_s, _who)], TextColor);
                // C: the load is a signed byte (record +0x35) and the limit Str + End in int.
                t.Draw(c, b.X + 0x48, b.Y + 4, $"{(sbyte)m.Encumbrance:00}/{m[Attribute.Strength] + m[Attribute.Endurance]:00}", TextColor);
                break;
            case 3:
            case 17:
                // DORMANT: case 17 (the same labels, values from y 0x20) is never drawn: the sheet draws boxes 0..16.
                for (var j = 0; j < 7; j++)
                {
                    t.Draw(c, 0xED, 0x12 + j * 8, AttributeLabels[j], TextColor);
                    t.Draw(c, 0xCA, (i == 3 ? 0x12 : 0x20) + j * 8, $"{m.Attributes[j]:00}/{m.AttributeMaximums[j]:00}", TextColor);
                }
                break;
            case 4:
                DrawSkillColumn(WeaponLabels, 0, 0x12, 0x10C);
                break;
            case 5:
                DrawSkillColumn(LearnedLabels, 7, 0x55, 0x10C);
                break;
            case 6:
                c.CopyFrom(BackPage, b.X, b.Y, b.W, b.H);
                string[] none = ["No Weapon", "No V:Armor", "No L:Armor", "No Shield", "No Msl Wpn"];
                for (var slot = 0; slot < 5; slot++)
                {
                    var code = Inventory.EquippedType(_s, _who, (EquipmentSlot)slot) != -1 ? Inventory.Equipped(_s, _who, (EquipmentSlot)slot) : -1;
                    t.Draw(c, b.X + 4, 0x58 + slot * 10, code != -1 ? _s.Item(code).ShortName : none[slot], TextColor);
                }
                DrawBoxText(2);
                break;
            case 7:
                DrawSkillColumn(OtherLabels, 13, 0x91, b.X + 3);
                break;
        }
    }

    private void DrawSkillColumn(string[] labels, int firstSkill, int top, int valueX)
    {
        for (var j = 0; j < labels.Length; j++)
        {
            ui.Text.Draw(Screen, 0x11D, top + j * 8, labels[j], TextColor);
            ui.Text.Draw(Screen, valueX, top + j * 8, $"{Member.Skills[firstSkill + j]:00}", TextColor);
        }
    }

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
                    t.Draw(c, x + 0x2B, y + 5 + j * 8, FullLabels[j], TextColor);
                    t.Draw(c, x + 8, y + 5 + j * 8, $"{m.Attributes[j]:00}/{m.AttributeMaximums[j]:00}", TextColor);
                }
                break;
            case 4:
                DrawFullSkills(b, 0x16, 7, 0, 7);
                break;
            case 5:
                DrawFullSkills(b, 0x16, 14, 7, 6);
                break;
            case 7:
                DrawFullSkills(b, 0x20, 20, 13, 6);
                break;
            case 6:
                string[] none = ["No Weapon", "No Vitals Armor", "No Limb Armor", "No Shield", "No Missile Weapon"];
                for (var slot = 0; slot < 5; slot++)
                {
                    // C: the original indexes the item list with equipped_item's result, −1 included, when the type is set but no stack matches.
                    var code = Inventory.EquippedType(_s, _who, (EquipmentSlot)slot) != -1 ? Inventory.Equipped(_s, _who, (EquipmentSlot)slot) : -1;
                    if (code != -1)
                    {
                        t.Draw(c, x + 4, 0x58 + slot * 10, _s.Item(code).Name, TextColor);
                    }
                    else
                    {
                        t.Draw(c, x + 5, 0x58 + slot * 10, none[slot], TextColor);
                    }
                }
                if (_s.Events.OwnedBy(EventKinds.SkillBoost, _who) || _s.Events.OwnedBy(EventKinds.AttributeBoost, _who)
                    || _s.Events.OwnedBy(EventKinds.PotionBonus, _who))
                {
                    t.Draw(c, x + 5, 0x8A, "Enhc/Bless", TextColor);
                }
                break;
            case PackBox:
                if (m.Pack.Count <= 0)
                {
                    // C: the line count (DS:E7A4) keeps the last list's value when the pack is empty.
                    break;
                }
                if (_top[k, _who] >= m.Pack.Count)
                {
                    _top[k, _who] = Math.Clamp(m.Pack.Count - 10, 0, 99);
                }
                if (_selected[k, _who] >= m.Pack.Count)
                {
                    _selected[k, _who] = m.Pack.Count - 1;
                }
                for (var j = 0; j < b.Lines; j++)
                {
                    if (_top[k, _who] + j < m.Pack.Count)
                    {
                        var st = m.Pack[_top[k, _who] + j];
                        t.Draw(c, x + 9, j * 8 + y + 9, $"{_s.Item(st.Code & 0xFFF).Name} {st.Quality:00}q ({st.Count})", ListColor);
                    }
                }
                HighlightLine(k);
                // C: DS:E7A4 = the pack's count as a signed char (R031+0x35B5)
                _listCount = m.Pack.Stacks;
                break;
            case FormulaeBox:
                DrawNames(m.Formulas.Order().Select(f => f < formulas.Count ? formulas[f].Name : "").ToList(), k);
                break;
            case SaintsBox:
                // C: the original reads 17 of the 20 bytes of saint bits: saints 0..135.
                DrawNames(m.Saints.Where(i => i < 136).Order().Select(i => i < saintNames.Count ? saintNames[i] : "").ToList(), k);
                break;
        }
    }

    private void DrawFullSkills(Box b, int labelOffset, int firstLabel, int firstSkill, int count)
    {
        for (var j = 0; j < count; j++)
        {
            ui.Text.Draw(Screen, b.PanelX + labelOffset, b.PanelY + 5 + j * 8, FullLabels[firstLabel + j], TextColor);
            ui.Text.Draw(Screen, b.PanelX + 8, b.PanelY + 5 + j * 8, $"{Member.Skills[firstSkill + j]:00}", TextColor);
        }
    }

    /// <summary>The visible names of a formula or saint list, from the first visible line.</summary>
    private void DrawNames(List<string> names, int k)
    {
        var b = Boxes[k];
        for (var n = 0; n < b.Lines && _top[k, _who] + n < names.Count; n++)
        {
            ui.Text.Draw(Screen, b.PanelX + 9, n * 8 + b.PanelY + 9, names[_top[k, _who] + n], ListColor);
        }
        HighlightLine(k);
        _listCount = names.Count;
    }

    private void HighlightLine(int k)
    {
        var b = Boxes[k];
        Screen.Recolor(b.PanelX + 8, _selected[k, _who] * 8 + b.PanelY + 9, b.PanelW - 15, 8, ListColor, ListSelected);
    }
}
