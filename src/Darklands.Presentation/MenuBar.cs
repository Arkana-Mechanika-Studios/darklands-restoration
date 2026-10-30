using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The game's menu bar over the card window: "Game", "Orders", "Attack", "Party". Holding the right
/// button opens it (the entry under the pointer drops down, letting go chooses); F10 drives it from
/// the keyboard. Out of battle "Orders" and "Attack" are greyed and have no items.
/// </summary>
// orig: menu-bar-loop root 0000:0000 (Run), menu-bar-pointer-follow root 0000:01D4 (Track), menu-bar-paint root 0000:06C2 (DrawBar), dropdown-open root 0000:07D4, submenu-open root 0000:0984, dropdown-close root 0000:0BC2, submenu-close root 0000:0C2C, the keyboard menu mode root 0000:0DD2, all draft (0bc2 and 0c2c match the bytes; the others are drafts written from the asm; 0000 was checked against the original's disassembly); the menus are set up by R000+0x083E (0E76:083E) draft (bytes match); tests: MenuBarTests, Host.Headless
// C: coordinates are the card window's (card-text-window, x 0x3c on the screen); the bar's font is font 1 (card-text-window->f10 = 1 while it runs) and its texts use the window colour f0c: 0 for an entry that can be chosen, 7 for one that cannot.
// C: the battle runs this bar over its own menus (BattleScreen.BuildMenuBar: the enable bytes of the Orders and Attack items, the Throw list
// built anew, the steps DS:A394 of the menu its T unfolds); the pointer of the screens is shown while the bar is up, as root 0000:0000's
// order-mode cursor change does.
// DIVERGENCE: the "Show Changes" tick follows the option; the original's tick keeps its last toggle across a load (the load sets the music, sound and difficulty ticks only).
public sealed class MenuBar
{
    private const int WindowX = CardScreen.WindowX;
    private const int BarWidth = 0x104;
    private const int BarHeight = 0x0C;
    private const int RowHeight = 8;
    private const byte BoxColor = 0x80;
    private const byte Enabled = 0;
    private const byte Disabled = 7;
    private const byte Lit = Palette.Highlight;

    /// <summary>DS:0206 / DS:A340 = -0x63: no sub-item row.</summary>
    private const int NoRow = -0x63;

    /// <summary>
    /// A sub-menu item (0x16 bytes): its text, whether it can be chosen, the key shown, a count shown before it (2 and more), its
    /// action; <paramref name="State"/>, when given, is its enable byte as the game changes it (1 can be chosen, 2 greyed).
    /// </summary>
    public sealed record SubItem(Func<string> Text, bool Enabled, string Key, int Count, Action Run, Func<int>? State = null)
    {
        /// <summary>The enable byte: 1 can be chosen, other values not.</summary>
        public int On => State?.Invoke() ?? (Enabled ? 1 : 2);
    }

    /// <summary>
    /// A dropdown item (0x14 bytes): text, whether it can be chosen, the key shown, its action and its sub-menu;
    /// <paramref name="State"/> its enable byte when the game changes it (the battle's Orders and Attack items: 1 can be chosen,
    /// 2 greyed, 0 neither), <paramref name="Subs"/> a sub-menu the game builds anew (the potions to throw).
    /// </summary>
    public sealed record Item(Func<string> Text, bool Enabled, string Key, Action Run, IReadOnlyList<SubItem>? Sub = null,
        Func<int>? State = null, Func<IReadOnlyList<SubItem>?>? Subs = null)
    {
        /// <summary>The enable byte: 1 can be chosen, other values not.</summary>
        public int On => State?.Invoke() ?? (Enabled ? 1 : 2);

        /// <summary>The sub-menu (null: none).</summary>
        public IReadOnlyList<SubItem>? SubMenu => Subs is not null ? Subs() : Sub;
    }

    /// <summary>
    /// A bar entry (10 bytes): its title; <c>carries-items-test</c> 1 drops its items down, 2 is greyed and skipped. <paramref name="BeforeItems"/>
    /// is the word just before the entry's item table, which the keyboard's Right reads as <c>items[-1].sub</c> (null: 0).
    /// </summary>
    public sealed record Menu(string Title, bool HasItems, IReadOnlyList<Item> Items, Func<int>? HasItemsState = null, Func<int>? BeforeItems = null)
    {
        /// <summary>carries-items-test: 1 drops the items down, 2 is greyed (and skipped by the keyboard).</summary>
        public int Opens => HasItemsState?.Invoke() ?? (HasItems ? 1 : 2);
    }

    private readonly GameUi _ui;
    private readonly Canvas _canvas;
    private readonly int[] _x0;
    private readonly int[] _x1;

    // the state of root 0000 (DGROUP): drawn DS:9409, bar entry lit DS:A2E7 and its range DS:9C1E..DS:9C20, dropdown DS:940D, sub-menu DS:940F, rows DS:0204 / DS:0206, over a row DS:944B / DS:9783, the sub-menu's item DS:A340
    private bool _drawn;
    private int _lit = -1;
    private int _litX0;
    private int _litX1;
    private bool _dropdownOpen;
    private bool _submenuOpen;
    private int _itemRow = -1;
    private int _subRow = NoRow;
    private bool _overItem;
    private bool _overSub;
    private int _subParent = NoRow;

    // dropdown geometry DS:9789 (x), DS:987B (right), DS:9A45 (key column), DS:99E7 (width), DS:99E9 (bottom), DS:9413 (items)
    private int _ddX;
    private int _ddRight;
    private int _ddKeyX;
    private int _ddWidth;
    private int _ddBottom;
    private int _ddCount;

    // sub-menu geometry DS:9C29 (x), DS:9C3A (right), DS:9C51 (count column), DS:9C53 (text), DS:9C48 (keys), DS:9C3F (width), DS:9C27 (y), DS:9C44 (bottom), DS:9415 (items)
    private int _sX;
    private int _sRight;
    private int _sCountWidth;
    private int _sTextX;
    private int _sKeyX;
    private int _sWidth;
    private int _sY;
    private int _sBottom;
    private int _sCount;

    private SavedRegion? _underBar;
    private SavedRegion? _underDropdown;
    private SavedRegion? _underSubmenu;

    // keyboard menu mode: DS:9C26, bar DS:9C2F, item DS:9C3C, sub-item DS:9C41 (-1 = none)
    private bool _keyboard;
    private int _kbMenu;
    private int _kbItem = -1;
    private int _kbSub = -1;

    private bool _running;

    /// <summary>DS:A394: the steps left of a menu unfolded by the battle's T.</summary>
    private int _unfold;

    /// <summary>DS:9409: the bar is drawn (the battle's mouse handler does nothing then).</summary>
    public bool Shown => _drawn;

    /// <summary>
    /// The battle's T (Throw): the keyboard menu mode on entry <paramref name="menu"/>, unfolded over three passes to its first
    /// item and that item's first sub-item, then run until it ends.
    /// </summary>
    // orig: R016+0x0000 case 0x1454 / 0x1474 (when DS:97DD == 1): DS:9C26 = 1, DS:9C2F = 2, DS:9C3C = DS:9C41 = -1, DS:A394 = 3; the next
    // root 0000:0000 runs the mode
    public void RunUnfolded(int menu)
    {
        (_keyboard, _kbMenu, _kbItem, _kbSub, _unfold) = (true, menu, -1, -1, 3);
        Run(keyboard: false);
    }

    public MenuBar(GameUi ui, IReadOnlyList<Menu> menus)
    {
        _ui = ui;
        _canvas = ui.Canvas;
        Menus = menus;
        _x0 = new int[menus.Count];
        _x1 = new int[menus.Count];
    }

    public IReadOnlyList<Menu> Menus { get; }

    /// <summary>
    /// The bar of the game outside battles: Game (save, load, difficulty, the options, pause, quit),
    /// Orders and Attack (greyed), Party (information, marching order).
    /// </summary>
    // orig: R000+0x083E: DS:9419 = Game (x6 1), Orders (2), Attack (2), Party (1); Game items DS:991E "\x02Save Game" "\x0fS" game-store-overlay-entry, "\x02Load Saved Game" "\x0fL" game-load-overlay-entry, "\x02Difficulty" "\x0fD" thunk 09C0:17E3 (root 0000:25F0, empty) with the sub-menu DS:99EC "\x02Basic" / "\x02Standard" / "\x02Expert" (difficulty-set-*), "\x01Show Changes" "\x0fC" saint-notices-switch, "\x01Music" "\x0fM" music-switch, "\x01Sound FX" "\x0fF" sound-switch, "\x02Pause" "\x0fP" root 0000:1080, "\x02Quit to DOS" "\x0fQ" exit-to-system, all enabled; Party items DS:987D "Party Info" key "\x0a" party-info-overlay-entry, "Change Marching Order" "\x0fO" party-order-chooser-overlay-entry
    // C: the first character of an item's text is its tick: 1 (a tick in font 1) or 2 (none); saint-notices-switch, music-switch, sound-switch and difficulty-display write it (*DS:995A, *DS:996E, *DS:9982, *DS:99EC / *DS:9A02 / *DS:9A18), so here it is read from the options when drawn.
    // C: key characters 0x0F and 0x0A are glyphs of font 1 (the "Alt" and "F6" signs).
    public static MenuBar ForGame(GameUi ui, GameState state, GameOptions options, HotKeys keys)
    {
        static string Tick(bool on) => on ? "\x01" : "\x02";
        var difficulty = new List<SubItem>
        {
            new(() => Tick(state.Difficulty == Difficulty.Basic) + "Basic", true, "", 0, () => keys.SetDifficulty(Difficulty.Basic)),
            new(() => Tick(state.Difficulty == Difficulty.Standard) + "Standard", true, "", 0, () => keys.SetDifficulty(Difficulty.Standard)),
            new(() => Tick(state.Difficulty == Difficulty.Expert) + "Expert", true, "", 0, () => keys.SetDifficulty(Difficulty.Expert)),
        };
        var game = new List<Item>
        {
            // C: the items' flag bytes are DS:9920 and DS:9934 (HotKeys.SaveGameItem / LoadGameItem): greyed while a screen switches them off (the inn's roster, a stay, the character sheet, barter: see HotKeys).
            new(() => "\x02Save Game", true, "\x0fS", () => keys.Save?.Invoke(), State: () => keys.SaveGameItem),
            new(() => "\x02Load Saved Game", true, "\x0fL", keys.LoadGame, State: () => keys.LoadGameItem),
            new(() => "\x02" + "Difficulty", true, "\x0f" + "D", () => { }, difficulty),
            new(() => Tick(state.ShowSaintlyChanges) + "Show Changes", true, "\x0f" + "C", keys.ToggleSaintlyChanges),
            new(() => Tick(options.Music) + "Music", true, "\x0fM", keys.ToggleMusic),
            new(() => Tick(options.SoundEffects) + "Sound FX", true, "\x0f" + "F", keys.ToggleSoundEffects),
            new(() => "\x02Pause", true, "\x0fP", keys.Pause),
            new(() => "\x02Quit to DOS", true, "\x0fQ", keys.QuitToDos),
        };
        // orig: the Party menu's second item calls thunk 09C0:183D (R022+0x051C, MarchingOrderScreen) with the flag DS:9893 (1 outside a battle)
        var party = new List<Item>
        {
            new(() => "Party Info", true, "\x0a", keys.ShowPartyInfo),
            new(() => "Change Marching Order", true, "\x0fO", () => keys.ChoosePartyOrder?.Invoke()),
        };
        MenuBar? bar = null;
        bar = new MenuBar(ui, [new("Game", true, game, BeforeItems: () => BeforeGameItems(state)), new("Orders", false, []), new("Attack", false, []),
            new("Party", true, party, BeforeItems: () => bar!.DropdownRight)])
        {
            QuitToDos = keys.QuitToDos,
        };
        return bar;
    }

    /// <summary>The word DS:991C before the Game items (DS:991E): its high byte DS:991D is top-scoring-member, the low byte has no writer (0).</summary>
    // C: the four item tables are DS:991E (Game), DS:9607 (Orders), DS:97DB (Attack), DS:987D (Party), the same in a battle; the words before
    // them are DS:991C (top-scoring-member << 8), DS:9605 and DS:97D9 (no writer: 0) and DS:987B (the last dropdown's right edge, DropdownRight).
    public static int BeforeGameItems(GameState state) => (state.Party.Last.Member & 0xFF) << 8;

    /// <summary>DS:987B: the right edge of the last dropdown opened (0 before the first).</summary>
    public int DropdownRight => _ddRight;

    /// <summary>Runs the bar while the right button is held (or, with <paramref name="keyboard"/>, until the keyboard menu ends), then the chosen item.</summary>
    // orig: menu-bar-loop(m, 0): (hotkey-table for a waiting key is GameUi's) the loop: in menu mode a key or the right button goes to 0dd2; active while the right button is held or menu mode is on: menu-bar-paint and menu-bar-pointer-follow; once inactive with the bar drawn: DS:9409 = 0, the dropdown closed (0bc2), then the item under the pointer (DS:944B: items[DS:0204], if enabled and DS:9413 > DS:0204) and the sub-item (DS:9783: items[DS:A340].sub[DS:0206], if enabled and DS:9415 > DS:0206) are called, the window's colour and font put back first
    // C: from an item to its sub-menu DS:944B stays set, so letting go on a sub-item calls the item's function (for Difficulty the empty thunk 09C0:17E3) and then the sub-item's.
    // C: hotkey-table F10 (DS:9C26 = 1, DS:9C2F = 0, DS:9C3C = DS:9C41 = -1) starts the keyboard mode; GameUi passes F10 here.
    public void Run(bool keyboard)
    {
        if (_running)
        {
            return;
        }
        _running = true;
        // orig: root 0000:001E: the card window's font is 1 in the loop (the keyboard mode's Alt-Q box), put back before an item (Choose)
        var font = _ui.CardWindowFont;
        _ui.CardWindowFont = 1;
        _itemFont = font;
        try
        {
            if (keyboard)
            {
                (_keyboard, _kbMenu, _kbItem, _kbSub) = (true, 0, -1, -1);
            }
            while (true)
            {
                _ui.Present();
                if (_keyboard && (_ui.TryReadKey(out var key) || _ui.Mouse.Right))
                {
                    KeyboardStep(key);
                }
                if (_ui.Mouse.Right || _keyboard)
                {
                    DrawBar();
                    var (mx, my) = Pointer();
                    Track(mx, my);
                    _ui.Idle();
                    continue;
                }
                if (_drawn)
                {
                    Choose();
                }
                return;
            }
        }
        finally
        {
            _running = false;
            _ui.CardWindowFont = font;
        }
    }

    /// <summary>The card window's font menu-bar-loop kept at its start: the items run with it.</summary>
    private int _itemFont = 3;

    private void Choose()
    {
        _drawn = false;
        var (menu, item, sub, parent) = (_lit, _itemRow, _subRow, _subParent);
        CloseDropdown();
        _ui.Present();
        // orig: root 0000:014C..0x0152 / 0x019D..0x01A3: the window's colour and font put back before the item's call
        _ui.CardWindowFont = _itemFont;
        if (_overItem)
        {
            _overItem = false;
            if (menu >= 0 && item >= 0 && item < _ddCount && Menus[menu].Items[item] is { On: 1 } chosen)
            {
                chosen.Run();
            }
        }
        if (_overSub)
        {
            _overSub = false;
            if (menu >= 0 && parent >= 0 && parent < Menus[menu].Items.Count && Menus[menu].Items[parent].SubMenu is { } subs &&
                sub >= 0 && sub < _sCount && sub < subs.Count && subs[sub].On == 1)
            {
                subs[sub].Run();
            }
        }
    }

    /// <summary>The pointer, or in keyboard mode the place the bar, item and sub-item cursor stand for.</summary>
    // orig: menu-bar-pointer-follow: in menu mode sx = DS:9C41 == -1 ? 0 : DS:9C22[DS:9C2F] (the dropdown's width); mx = sx + DS:9C15[DS:9C2F] + 0x3c (DS:9C15: the title's x + 12); iy = DS:9C3C == -1 ? 0 : DS:9C3C * 8 + 0xc; sy = DS:9C41 == -1 ? 0 : DS:9C41 * 8; my = iy + sy + 4
    // C: the countdown DS:A394 (3 .. 1) that the battle's T sets: the first pass lights the entry, the second its first item
    // (DS:9C3C = 0), the third that item's first sub-item (DS:9C41 = 0).
    private (int X, int Y) Pointer()
    {
        if (!_keyboard)
        {
            return (_ui.Mouse.X, _ui.Mouse.Y);
        }
        if (_unfold == 3)
        {
            _unfold--;
        }
        else if (_unfold == 2)
        {
            _unfold--;
            _kbItem = 0;
        }
        else if (_unfold == 1)
        {
            _unfold--;
            _kbSub = 0;
        }
        var titleX = 15;
        for (var k = 0; k < _kbMenu; k++)
        {
            titleX += _ui.Text.Measure(Menus[k].Title) + 8;
        }
        var sx = _kbSub == -1 ? 0 : DropdownWidth(Menus[_kbMenu].Items);
        var iy = _kbItem == -1 ? 0 : _kbItem * RowHeight + BarHeight;
        var sy = _kbSub == -1 ? 0 : _kbSub * RowHeight;
        return (sx + titleX + WindowX, iy + sy + 4);
    }

    /// <summary>DS:9C22[k]: the widest item text plus the widest key plus 9.</summary>
    // orig: the loop at the end of R000+0x083E (with card-text-window's font set to 1)
    private int DropdownWidth(IReadOnlyList<Item> items) =>
        items.Count == 0 ? 9 : items.Max(i => _ui.Text.Measure(i.Text())) + items.Max(i => _ui.Text.Measure(i.Key)) + 9;

    // orig: menu-bar-paint: once per opening (DS:9409 == 0): the bar area (0, 0, 0x104, 0xc) saved, framed-box-paint(card-text-window, 0, 0, 0x104, 0xc, 0x80), the titles from x 3: x0 = x, the title at (x + 4, 3) in colour 0 (carries-items-test 1) or 7, x += width + 8, x1 = x - 1; DS:940D = DS:940F = 0, DS:9409 = 1, DS:A2E7 = -1
    private void DrawBar()
    {
        if (_drawn)
        {
            return;
        }
        _underBar = _canvas.Save(WindowX, 0, BarWidth, BarHeight);
        ScreenDrawing.FrameBox(_canvas, _ui.Assets.CommonSprites, WindowX, 0, BarWidth, BarHeight, BoxColor);
        var x = 3;
        for (var j = 0; j < Menus.Count; j++)
        {
            var width = _ui.Text.Measure(Menus[j].Title);
            _x0[j] = x;
            _ui.Text.Draw(_canvas, WindowX + x + 4, 3, Menus[j].Title, Menus[j].Opens == 1 ? Enabled : Disabled);
            x += width + 8;
            _x1[j] = x - 1;
        }
        (_dropdownOpen, _submenuOpen, _drawn, _lit) = (false, false, true, -1);
    }

    // orig: menu-bar-pointer-follow (see the class comment); x = mx - 0x3c
    // C: a dropdown's top frame line is drawn at y 0xB over the bar's bottom line, and switching to another entry puts back only rows 0xC and below: the corner pieces stay on the bar until it closes.
    private void Track(int mx, int my)
    {
        var x = mx - WindowX;
        if (my < BarHeight)
        {
            _overItem = false;
            for (var j = 0; j < Menus.Count; j++)
            {
                if (x < _x0[j] || _x1[j] < x)
                {
                    continue;
                }
                if (_lit != j)
                {
                    if (_lit >= 0 && _lit < Menus.Count)
                    {
                        Recolor(_litX0, 0, _litX1 - _litX0 + 1, BarHeight, Lit, Enabled);
                    }
                    if (_submenuOpen)
                    {
                        CloseSubmenu();
                        _subParent = NoRow;
                    }
                    if (_dropdownOpen)
                    {
                        _canvas.Restore(_underDropdown!);
                        _dropdownOpen = false;
                        _itemRow = -1;
                    }
                    (_lit, _litX0, _litX1) = (j, _x0[j], _x1[j]);
                    Recolor(_litX0, 0, _litX1 - _litX0 + 1, BarHeight, Enabled, Lit);
                    if (Menus[j].Opens == 1)
                    {
                        OpenDropdown(Menus[j].Items, _x0[j], my);
                    }
                }
                else if (_itemRow >= 0 && _itemRow < _ddCount)
                {
                    Recolor(_ddX, _itemRow * RowHeight + BarHeight, _ddWidth, RowHeight, Lit, Enabled);
                    _itemRow = -1;
                }
            }
            return;
        }
        if (_dropdownOpen && x >= _ddX && x <= _ddRight && my >= BarHeight && _ddBottom >= my)
        {
            _overItem = true;
            var row = (my - BarHeight) / RowHeight;
            if (row != _itemRow)
            {
                if (_itemRow >= 0 && _itemRow < _ddCount)
                {
                    Recolor(_ddX, _itemRow * RowHeight + BarHeight, _ddWidth, RowHeight, Lit, Enabled);
                }
                if (_submenuOpen)
                {
                    CloseSubmenu();
                    _subParent = NoRow;
                }
                _itemRow = row;
                if (_itemRow < _ddCount)
                {
                    Recolor(_ddX, _itemRow * RowHeight + BarHeight, _ddWidth, RowHeight, Enabled, Lit);
                    // C: row == DS:9413 reads the terminating entry (empty text, no sub-menu)
                    var item = Menus[_lit].Items[_itemRow];
                    if (item.SubMenu is { } subMenu && !_submenuOpen && item.On == 1)
                    {
                        OpenSubmenu(subMenu, _ddRight + 1, _itemRow * RowHeight + BarHeight);
                        _subParent = _itemRow;
                    }
                }
                return;
            }
            if (_subRow != NoRow)
            {
                Recolor(_sX, _subRow * RowHeight + _sY + 1, _sWidth, RowHeight, Lit, Enabled);
                _subRow = NoRow;
            }
            return;
        }
        if (_submenuOpen && x >= _sX && x <= _sRight && _sY <= my && _sBottom >= my)
        {
            _overSub = true;
            var row = (my - BarHeight) / RowHeight;
            if (row - _subParent != _subRow)
            {
                if (_subRow >= 0 && _subRow < _sCount)
                {
                    Recolor(_sX, _subRow * RowHeight + _sY + 1, _sWidth, RowHeight, Lit, Enabled);
                }
                _subRow = row - _subParent;
                if (_subRow >= 0 && _subRow < _sCount)
                {
                    Recolor(_sX, _subRow * RowHeight + _sY + 1, _sWidth, RowHeight, Enabled, Lit);
                }
            }
            return;
        }
        if (_overItem)
        {
            Recolor(_ddX, _itemRow * RowHeight + BarHeight, _ddWidth, RowHeight, Lit, Enabled);
            _overItem = false;
            _itemRow = -1;
            return;
        }
        if (_overSub)
        {
            Recolor(_sX, _subRow * RowHeight + _sY + 1, _sWidth, RowHeight, Lit, Enabled);
            _overSub = false;
            _subRow = NoRow;
        }
    }

    // orig: dropdown-open(items, x, y): widths of the widest text and key; DS:9789 = x, DS:987B = x + wkey + wname + 9, DS:9A45 = x + wname + 6, DS:99E7 = DS:987B - x + 1, DS:99E9 = n * 8 + 0xe; the area (x, 0xc, DS:99E7, n * 8 + 3) saved; framed-box-paint(card-text-window, x, 0xb, DS:99E7, n * 8 + 4, 0x80); each item at (x + 3, 0xe + 8j) and its key at (DS:9A45, 0xe + 8j), colour 0 when enabled == 1 else 7; DS:0204 = (y - 0xe) / 8 lit when inside; DS:940D = 1
    // C: (y - 0xe) / 8 truncates toward zero: opening with the pointer at y 7..11 of the bar lights the first item.
    private void OpenDropdown(IReadOnlyList<Item> items, int x, int y)
    {
        _ddCount = items.Count;
        var nameWidth = items.Max(i => _ui.Text.Measure(i.Text()));
        var keyWidth = items.Max(i => _ui.Text.Measure(i.Key));
        _ddX = x;
        _ddRight = x + keyWidth + nameWidth + 9;
        _ddKeyX = _ddX + nameWidth + 6;
        _ddWidth = _ddRight - _ddX + 1;
        _ddBottom = (_ddCount << 3) + 0x0E;
        _underDropdown = _canvas.Save(WindowX + _ddX, BarHeight, _ddWidth, (_ddCount << 3) + 3);
        ScreenDrawing.FrameBox(_canvas, _ui.Assets.CommonSprites, WindowX + _ddX, 0x0B, _ddWidth, (_ddCount << 3) + 4, BoxColor);
        for (var j = 0; j < _ddCount; j++)
        {
            var color = items[j].On == 1 ? Enabled : Disabled;
            _ui.Text.Draw(_canvas, WindowX + x + 3, 0x0E + j * RowHeight, items[j].Text(), color);
            _ui.Text.Draw(_canvas, WindowX + _ddKeyX, 0x0E + j * RowHeight, items[j].Key, color);
        }
        _itemRow = (y - 0x0E) / RowHeight;
        if (_itemRow >= 0 && _itemRow < _ddCount)
        {
            Recolor(_ddX, (_itemRow << 3) + BarHeight, _ddWidth, RowHeight, Enabled, Lit);
        }
        _dropdownOpen = true;
    }

    // orig: submenu-open(subs, x, y): the count column (number-with-separators of counts of 2 and more) DS:9C51 = its width + 3 or 0; DS:9C29 = x - 1, DS:9C3A = DS:9C29 + wkey + DS:9C51 + wname + 9, DS:9C53 = DS:9C29 + DS:9C51 + 3, DS:9C48 = DS:9C29 + DS:9C51 + wname + 6, DS:9C3F = DS:9C3A - DS:9C29 + 1, DS:9C27 = y - 1, DS:9C44 = DS:9C27 + n * 8 - 1; the area saved, framed-box-paint(card-text-window, DS:9C29, DS:9C27, DS:9C3F, n * 8 + 4, 0x80); each line at y DS:9C27 + 3 + 8j: the count centred in its column, the text at DS:9C53, the key at DS:9C48; DS:0206 = -0x63, DS:940F = 1
    // C: the area is saved to the back page one pixel right and down of where it is and put back from there: the same rectangle.
    private void OpenSubmenu(IReadOnlyList<SubItem> subs, int x, int y)
    {
        _sCount = subs.Count;
        string Count(SubItem s) => s.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var countWidth = subs.Where(s => s.Count >= 2).Select(s => _ui.Text.Measure(Count(s))).DefaultIfEmpty(0).Max();
        var nameWidth = subs.Max(s => _ui.Text.Measure(s.Text()));
        var keyWidth = subs.Max(s => _ui.Text.Measure(s.Key));
        _sCountWidth = countWidth != 0 ? countWidth + 3 : 0;
        _sX = x - 1;
        _sRight = _sX + keyWidth + _sCountWidth + nameWidth + 9;
        _sTextX = _sX + _sCountWidth + 3;
        _sKeyX = _sX + _sCountWidth + nameWidth + 6;
        _sWidth = _sRight - _sX + 1;
        _sY = y - 1;
        _sBottom = _sY + (_sCount << 3) - 1;
        _underSubmenu = _canvas.Save(WindowX + _sX, _sY, _sWidth, (_sCount << 3) + 4);
        ScreenDrawing.FrameBox(_canvas, _ui.Assets.CommonSprites, WindowX + _sX, _sY, _sWidth, (_sCount << 3) + 4, BoxColor);
        for (var j = 0; j < _sCount; j++)
        {
            var color = subs[j].On == 1 ? Enabled : Disabled;
            var lineY = _sY + j * RowHeight + 3;
            if (subs[j].Count > 1)
            {
                var number = Count(subs[j]);
                _ui.Text.Draw(_canvas, WindowX + (_sCountWidth - _ui.Text.Measure(number)) / 2 + _sX + 3, lineY, number, color);
            }
            _ui.Text.Draw(_canvas, WindowX + _sTextX, lineY, subs[j].Text(), color);
            _ui.Text.Draw(_canvas, WindowX + _sKeyX, lineY, subs[j].Key, color);
        }
        _subRow = NoRow;
        _submenuOpen = true;
    }

    // orig: dropdown-close: submenu-close; the dropdown's area put back (DS:0204 = -1) when one is open; the bar's area put back; DS:A2E7 = -1
    private void CloseDropdown()
    {
        CloseSubmenu();
        if (_dropdownOpen)
        {
            _canvas.Restore(_underDropdown!);
            _itemRow = -1;
        }
        if (_underBar is not null)
        {
            _canvas.Restore(_underBar);
        }
        _lit = -1;
    }

    // orig: submenu-close: when DS:940F, the sub-menu's area put back, DS:0206 = -0x63, DS:940F = 0
    private void CloseSubmenu()
    {
        if (_submenuOpen)
        {
            _canvas.Restore(_underSubmenu!);
            _subRow = NoRow;
            _submenuOpen = false;
        }
    }

    // orig: root 0000:0DD2: the right button (a waiting key eaten) or Esc / F10 leave menu mode with DS:944B = DS:9783 = 0 (nothing is chosen); Enter leaves it (the item under the cursor is chosen); Alt-Q exit-to-system; Up: the sub-item above (not past the first), else the enabled item above; Left: out of the sub-menu, else the previous entry with items; Right: into the item's sub-menu, else the next entry with items (entries of carries-items-test 2 skipped); Down: the next sub-item, else the next item that is not greyed (enabled 2 skipped)
    // C: Up from the first item goes to "no item" (it reads items[-1].enabled on the way, which only decides whether the loop stops one step early).
    // C: Right tests items[DS:9C3C].sub also with no item chosen (DS:9C3C = -1): it then reads the word before the item table (Menu.BeforeItems):
    // for Game top-scoring-member's byte, for Party the last dropdown's right edge, so with top-scoring-member != 0 (F10, Right) or on Party the cursor goes into
    // a "sub-menu" of item -1 (DS:9C41 = 0) instead of to the next entry: the pointer it stands for is on the bar, shifted right by the dropdown's width.
    // DIVERGENCE: Down in that sub-menu of item -1 tests the text of its next entry at that word + 0x16 * (DS:9C41 + 1): whatever DGROUP holds there (data at DS:0100..0400, the stack for top-scoring-member 0xFF); here it does not move.
    private void KeyboardStep(int key)
    {
        if (_ui.Mouse.Right)
        {
            (_keyboard, _overItem, _overSub) = (false, false, false);
            return;
        }
        var items = Menus[_kbMenu].Items;
        switch (key)
        {
            case Keys.Escape:
            case Keys.F10:
                (_keyboard, _overItem, _overSub) = (false, false, false);
                break;
            case Keys.AltQ:
                QuitToDos?.Invoke();
                break;
            case Keys.Enter:
                _keyboard = false;
                break;
            case Keys.Up:
                if (_kbSub != -1)
                {
                    if (_kbSub > 0)
                    {
                        _kbSub--;
                    }
                }
                else
                {
                    while (_kbItem != -1)
                    {
                        _kbItem--;
                        if (_kbItem >= 0 && items[_kbItem].On == 1)
                        {
                            break;
                        }
                    }
                }
                break;
            case Keys.Left:
                if (_kbSub != -1)
                {
                    _kbSub = -1;
                    return;
                }
                if (_kbMenu > 0)
                {
                    (_kbItem, _kbSub) = (-1, -1);
                    while (_kbMenu != 0)
                    {
                        _kbMenu--;
                        if (Menus[_kbMenu].Opens == 1)
                        {
                            return;
                        }
                    }
                }
                break;
            case Keys.Right:
                if (_kbItem >= 0 && _kbItem < items.Count ? items[_kbItem].SubMenu is not null : _kbItem == -1 && (Menus[_kbMenu].BeforeItems?.Invoke() ?? 0) != 0)
                {
                    _kbSub = 0;
                    return;
                }
                var next = _kbMenu + 1;
                // C: carries-items-test 2 is skipped; the move is made only onto an entry of carries-items-test 1
                while (next < Menus.Count && Menus[next].Opens == 2)
                {
                    next++;
                }
                if (next < Menus.Count && Menus[next].Opens == 1)
                {
                    (_kbMenu, _kbItem, _kbSub) = (next, -1, -1);
                }
                break;
            case Keys.Down:
                if (_kbSub != -1)
                {
                    if (_kbItem >= 0 && _kbSub + 1 < (items[_kbItem].SubMenu?.Count ?? 0))
                    {
                        _kbSub++;
                    }
                    break;
                }
                var n = _kbItem;
                // C: items with enabled == 2 are skipped; an item of another value stops the search without being taken
                while (n + 1 < items.Count && items[n + 1].On == 2)
                {
                    n++;
                }
                if (n + 1 < items.Count && items[n + 1].On == 1)
                {
                    _kbItem = n + 1;
                }
                break;
        }
    }

    /// <summary>Alt-Q in keyboard menu mode (exit-to-system); HotKeys.QuitToDos.</summary>
    public Action? QuitToDos { get; set; }

    private void Recolor(int x, int y, int width, int height, byte from, byte to) =>
        _canvas.Recolor(WindowX + x, y, width, height, from, to);
}
