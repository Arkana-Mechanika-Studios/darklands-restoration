using Darklands.Cards;
using Darklands.Content.Formats.Battles;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

// The battle screen: the menu bar and what it runs (save, load, difficulty, resume). The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    /// <summary>The menu bar of the battle (Game, Orders, Attack, Party): the game's bar over the battle's menus.</summary>
    private MenuBar? _menuBar;

    /// <summary>The bar's titles and entries over the battle's menu state.</summary>
    // orig: the battle menus of R000+0x083E (0E76:083E) draft, bytes match: their texts (DS strings), keys and handlers; each entry's enable
    // byte is the battle's (DS:9607 for Orders, DS:97DB for Attack), each title's carries-items-test too; tests: BattleUiTests
    private MenuBar BuildMenuBar()
    {
        var tb = _battle!;
        var m = tb.Menus;
        var exe = _assets.Executable;
        string S(int at) => exe.String(at);
        // a menu string with its tick byte as the program writes it; a key label or title as it is
        string Ticked(bool on, string id) => Tick(on) + _ui.Words(id)[1..];
        string W(string id) => _ui.Words(id);
        static void Nothing()
        {
        }
        MenuBar.Item O(OrdersItem item, Func<string> text, string key, Action run) => new(text, true, key, run, State: () => m.Orders[(int)item]);
        MenuBar.Item A(AttackItem item, Func<string> text, string key, Action run) => new(text, true, key, run, State: () => m.Attack[(int)item]);
        string With(int at, int pos, string suffix)
        {
            var t = S(at).TrimStart('\x01', '\x02').TrimEnd();
            var text = S(at).TrimStart('\x01', '\x02');
            return (text.Length > pos ? text[..pos] : t) + suffix;
        }
        // C: the Game texts of R000+0x083E start with their tick byte too (6 pixels wide, so the dropdown dropdown-open root 0000:07D4
        // C: opens is 6 pixels wider than the words, and its sub-menu with it): 2 for Save Game, Load Saved Game, Difficulty, Pause and Quit
        // C: to DOS; Show Changes, Music and Sound FX as saint-notices-switch / music-switch / sound-switch set them (*DS:995A, *DS:996E,
        // C: *DS:9982); Basic / Standard / Expert as difficulty-display root 0000:1438 does (*DS:99EC / *DS:9A02 / *DS:9A18). They are read
        // C: from the options when drawn, as MenuBar.ForGame's (its DIVERGENCE about the Show Changes tick across a load holds here too).
        var options = _ui.Options;
        var difficulty = new List<MenuBar.SubItem>
        {
            new(() => Ticked(_state.Difficulty == Difficulty.Basic, "exe:DS:0D2F"), true, "", 0, () => SetDifficulty(Difficulty.Basic)),
            new(() => Ticked(_state.Difficulty == Difficulty.Standard, "exe:DS:0D36"), true, "", 0, () => SetDifficulty(Difficulty.Standard)),
            new(() => Ticked(_state.Difficulty == Difficulty.Expert, "exe:DS:0D40"), true, "", 0, () => SetDifficulty(Difficulty.Expert)),
        };
        var game = new List<MenuBar.Item>
        {
            new(() => Ticked(false, "exe:DS:0CBB"), true, W("exe:DS:0CC6"), SaveGame, State: () => m.SaveGame),
            new(() => Ticked(false, "exe:DS:0CC9"), true, W("exe:DS:0CDA"), LoadGame, State: () => m.LoadGame),
            new(() => Ticked(false, "exe:DS:0CDD"), true, W("exe:DS:0CE9"), Nothing, difficulty),
            new(() => Ticked(_state.ShowSaintlyChanges, "exe:DS:0CEC"), true, W("exe:DS:0CFA"), () => _hotKey?.Invoke(Keys.AltC)),
            new(() => Ticked(options.Music, "exe:DS:0CFD"), true, W("exe:DS:0D04"), () => _hotKey?.Invoke(Keys.AltM)),
            new(() => Ticked(options.SoundEffects, "exe:DS:0D07"), true, W("exe:DS:0D11"), () => _hotKey?.Invoke(Keys.AltF)),
            new(() => Ticked(false, "exe:DS:0D14"), true, W("exe:DS:0D1B"), () => _hotKey?.Invoke(Keys.AltP)),
            new(() => Ticked(false, "exe:DS:0D1E"), true, W("exe:DS:0D2B"), () => _hotKey?.Invoke(Keys.AltQ)),
        };
        // C: every Orders text starts with its tick byte (font 1: 1 a tick, 2 none, both 6 pixels wide): 2, but Travel As Group's (DS:07AA) and
        // C: Travel Single File's (DS:07BC), which R016+0x0BD6 / R016+0x0CDC set to 1 for the mode turned on and every other writer (R002+0x0000,
        // C: R014+0x0000 at real combat, R037+0x8B76) to 2 with travel-mode 0: the tick is travel-mode's
        static string Tick(bool on) => on ? "" : "";
        var orders = new List<MenuBar.Item>
        {
            O(OrdersItem.Resume, () => W("exe:DS:0D49"), W("exe:DS:0D51"), Resume),
            O(OrdersItem.Finished, () => "" + m.Giver + S(BattleScreens.FinishedText), W("exe:DS:0D56"), () => tb.LeaveOrderMode()),
            O(OrdersItem.EnemyInfo, () => W("exe:DS:0D58"), W("exe:DS:0D64"), EnemyInfo),
            O(OrdersItem.Walk, () => S(0x67C).TrimEnd(), W("exe:DS:0D67"), () => tb.ChooseOrder(1)),
            O(OrdersItem.Flee, () => S(0x690).TrimEnd(), W("exe:DS:0D6A"), () => tb.ChooseOrder(1, flee: true)),
            O(OrdersItem.Halt, () => W("exe:DS:06A4"), W("exe:DS:0D6D"), () => tb.Halt(tb.Battle.Leader)),
            O(OrdersItem.Group, () => Ticked(tb.MoveMode == 1, "exe:DS:07AA"), W("exe:DS:0D70"), tb.ToggleGroup),
            O(OrdersItem.SingleFile, () => Ticked(tb.MoveMode == 2, "exe:DS:07BC"), W("exe:DS:0D73"), tb.ToggleSingleFile),
            O(OrdersItem.UseDoor, () => "" + With(0x6AA, 8, m.DoorSuffix), W("exe:DS:0D76"), () => tb.UseKey(tb.Selected)),
            O(OrdersItem.UseStairs, () => "" + With(0x6BA, 10, m.StairsSuffix), W("exe:DS:0D79"), () => tb.UseKey(tb.Selected)),
            O(OrdersItem.OpenChest, () => "" + With(0x6CC, 10, m.ChestSuffix), W("exe:DS:0D7C"), () => tb.OpenKey(tb.Selected)),
            O(OrdersItem.PickLock, () => "" + With(0x6DE, 9, m.PickSuffix), W("exe:DS:0D7F"), () => tb.PickKey(tb.Selected)),
            O(OrdersItem.DissolveLock, () => "" + With(0x6F0, 13, m.DissolveSuffix), W("exe:DS:0D82"), () => tb.DissolveKey(tb.Selected)),
            O(OrdersItem.DisarmTrap, () => S(m.DisarmText), W("exe:DS:0D85"), () => tb.DissolveKey(tb.Selected)),
            O(OrdersItem.Surrender, () => W("exe:DS:0706"), W("exe:DS:0D88"), Surrender),
            // orig: R016+0x13CA (1EF8:13CA) matched (R016+0x13CA): R016+0x1590 with 9
            O(OrdersItem.LootBodies, () => W("exe:DS:0718"), W("exe:DS:0D8B"), () => tb.ChoosePlace(PlaceAction.TakeLoot)),
            O(OrdersItem.Exit, () => "" + With(0x726, 16, m.ExitSuffix), W("exe:DS:0D8E"), tb.ExitKey),
            O(OrdersItem.Cancel, () => W("exe:DS:0D91"), W("exe:DS:0DA6"), tb.CancelOrder),
        };
        // orig: the Attack items' functions DS:97DB (R000+0x083E), each R016+0x156E(action), matched: R016+0x130A (0x12), R016+0x1314 (0x0A)
        // orig: R016+0x131E (6), R016+0x1328 (0x22), R016+0x136E (0x80), matched; the Throw item's own function does nothing
        var attack = new List<MenuBar.Item>
        {
            new(() => W("exe:DS:0DA9"), true, W("exe:DS:0DAF"), Nothing, State: () => m.Attack[(int)AttackItem.Throw], Subs: ThrowEntries),
            A(AttackItem.Standard, () => W("exe:DS:0DB2"), W("exe:DS:0DBE"), () => tb.ChooseOrder(0x12)),
            A(AttackItem.Vulnerable, () => W("exe:DS:0DC0"), W("exe:DS:0DCB"), () => tb.ChooseOrder(0x0A)),
            A(AttackItem.Berserk, () => W("exe:DS:0DCD"), W("exe:DS:0DD5"), () => tb.ChooseOrder(0x06)),
            A(AttackItem.Parry, () => W("exe:DS:0DD7"), W("exe:DS:0DDD"), () => tb.ChooseOrder(0x22)),
            A(AttackItem.Missile, () => W("exe:DS:0DDF"), W("exe:DS:0DEB"), () => tb.ChooseOrder(0x80)),
        };
        var party = new List<MenuBar.Item>
        {
            new(() => W("exe:DS:0DEE"), true, "\x0a", () => _owner.PartyInfo?.Invoke(), State: () => m.PartyInfo),
            new(() => W("exe:DS:0DF9"), true, W("exe:DS:0E0F"), ChooseMarchingOrder, State: () => m.MarchingOrder),
        };
        // C: the words before the item tables (the keyboard's Right with no item chosen) as outside a battle: MenuBar.BeforeGameItems
        MenuBar? bar = null;
        bar = new MenuBar(_ui,
        [
            new(W("exe:DS:0CA1"), true, game, BeforeItems: () => MenuBar.BeforeGameItems(_state)),
            new(W("exe:DS:0CA6"), true, orders, () => m.OrdersMenu),
            new(W("exe:DS:0CAD"), true, attack, () => m.AttackMenu),
            new(W("exe:DS:0CB4"), true, party, () => m.PartyMenu, () => bar!.DropdownRight),
        ])
        {
            QuitToDos = () => _hotKey?.Invoke(Keys.AltQ),
        };
        return bar;

        // orig: the Throw list's functions DS:9A47[k] (R000+0x083E), each R016+0x152E(k) (k = 0..19: line k's potion and quality, DS:9A47[k].f9 / f8):
        // orig: R016+0x1466, R016+0x1470, R016+0x147A, R016+0x1484, R016+0x148E matched
        // orig: R016+0x1498, R016+0x14A2, R016+0x14AC, R016+0x14B6, R016+0x14C0 matched
        // orig: R016+0x14CA, R016+0x14D4, R016+0x14DE, R016+0x14E8, R016+0x14F2 matched
        // orig: R016+0x14FC, R016+0x1506, R016+0x1510, R016+0x151A, R016+0x1524 matched
        IReadOnlyList<MenuBar.SubItem> ThrowEntries() => [.. m.ThrowLines.Select(l => new MenuBar.SubItem(
            () => PotionMenuName(l.Potion), true, $"q{l.Quality}", l.Count, () => tb.ChooseThrow(l.Potion, l.Quality)))];
    }

    /// <summary>A Throw line's name (DS:114A[2 + potion]): the short name of items 0x5F.. read from DARKLAND.LST at the battle's start.</summary>
    // orig: R002+0x1762 (0E76:1762) unmatched: ten records at 0x1115 (items 0x5F..0x68),
    // the byte at +0x1D zeroed, so the name (+0x14) is at most 9 characters; R019+0x0C80 puts DS:114E[id] in the menu line
    private string PotionMenuName(int potion)
    {
        if (potion is < 0 or > 9 || potion + 0x5F >= _assets.Items.Count)
        {
            return "";
        }
        var name = _assets.Items[potion + 0x5F].ShortName;
        return name.Length > 9 ? name[..9] : name;
    }

    /// <summary>Save Game (Alt-S): while DS:9920 offers it, the game's save in its battle form.</summary>
    // orig: R037+0x2A68 (1462:2A68) draft through its thunk, from the hot keys (root 0000:0CAA) and the Game menu when DS:9920 is 1; the
    // battle-state save R003+0x00DC ends with the level switch to the shown level
    // orig: R037+0x2A68 / R037+0x2ACE over a battle: text-prompt shows the driver's pointer after the comment box (R037+0x4544) and
    //   R037+0x2ACE shows it again after drawing the "Saving Game" box (R037+0x2B97, root 047C:1293), in the shape the battle's last pass
    //   set (R014+0x33A2; no shape is set meanwhile), at the mouse while the file is written; vis10 session D-b797f7: the original's
    //   "saving" shot has the order pointer at the mouse (65 px at x 77..92, y 76..83); tests: BattleUiTests
    private void SaveGame()
    {
        if (_battle!.Menus.SaveGame == 1 && _owner.Save is { } save)
        {
            ErasePointer();
            var restore = ShowDriverPointer();
            try
            {
                save(_battle);
            }
            finally
            {
                restore();
            }
            _battle.SwitchLevel(_battle.Levels.ShownIndex);
            // orig: R037+0x451A..0x453C: the comment box (text-prompt) is put back with colour 0 transparent and colour 5 turned into 0, so
            //   the screen keeps some of its pixels (TextInputBox); the battle's back page never had the box, and the save's level switch
            //   (R024+0x059A through thunk 09C0:1B3F at R003+0x0CD6) paints nothing: only a later frame's sprite boxes or a full paint
            //   take those pixels back (vis10 session visleads-mines-save: the box's strip at x 265..267, y 83..129 after the save)
            KeepScreenLeftover(TextInputBox.Area);
        }
    }

    /// <summary>
    /// What the screen holds in <paramref name="area"/> that the battle's own drawing did not put there: kept over every later frame
    /// until a frame's sprite box takes a pixel back or the view is painted whole.
    /// </summary>
    // orig: fight-draw's screen copy (R014+0x74A2) takes only the boxes of the frame's sprites and animated pieces (DS:E172) from the
    //   back page; floor-view-paint (R017+0x0000) paints the whole view. A box drawn straight on the screen in between (the save's
    //   comment) stays where neither reaches.
    private void KeepScreenLeftover((int X, int Y, int Width, int Height) area)
    {
        if (_ui.Options.CleanDrawing)
        {
            return;
        }
        var canvas = _ui.Canvas;
        var pixels = new Dictionary<int, byte>();
        for (var y = area.Y; y < area.Y + area.Height; y++)
        {
            for (var x = area.X; x < area.X + area.Width; x++)
            {
                pixels[y * Canvas.Width + x] = canvas.Screen.Pixels[y * Canvas.Width + x];
            }
        }
        _screenLeftover = pixels;
    }

    /// <summary>The screen's pixels that the battle's drawing has not taken back yet (by screen offset), null when none.</summary>
    private Dictionary<int, byte>? _screenLeftover;

    /// <summary>Puts the leftover pixels over the frame just drawn, except where this frame's sprite boxes reach (those are taken back).</summary>
    private void DrawScreenLeftover(Canvas canvas)
    {
        if (_screenLeftover is not { } left)
        {
            return;
        }
        foreach (var (x, y, w, h) in _dirty.Rects)
        {
            for (var sy = Math.Max(y, 0); sy < Math.Min(y + h, Canvas.Height); sy++)
            {
                for (var sx = Math.Max(x, 0); sx < Math.Min(x + w, BattlefieldView.Width); sx++)
                {
                    left.Remove(sy * Canvas.Width + sx + BattlefieldView.Left);
                }
            }
        }
        foreach (var (offset, value) in left)
        {
            canvas.Screen.Pixels[offset] = value;
        }
        if (left.Count == 0)
        {
            _screenLeftover = null;
        }
    }

    /// <summary>Load Saved Game (Alt-L): while DS:9934 offers it (always in a battle), the game's load; a game loaded leaves the battle.</summary>
    // orig: R037+0x8B76 (1C7D:09C6) matched through its thunk, its branch for a battle (DS:907A = 1: the battle's
    // buffers freed, its flags reset, then the load screen and the jump back into the main loop)
    private void LoadGame()
    {
        if (_battle!.Menus.LoadGame == 1)
        {
            // C: the load leaves DS:A532 as this battle has it (a battle taken up from the game loaded starts from it)
            _battle.KeepModifiers();
            _owner.Load?.Invoke();
        }
    }

    /// <summary>difficulty-easy-set / _standard / _expert (root 0000:1408..0x1428): the game's difficulty, which the battle reads too, and its box.</summary>
    // C: the battle bar's Difficulty sub-items (DS:99EC[0..2], filled by the battle menus' set-up) hold difficulty-easy-set / _standard /
    //   _expert, the same functions as the bar outside a battle (HotKeys.SetDifficulty): difficulty-setting = 0 / 1 / 2, then difficulty-display(1):
    //   "Difficulty is now set to " + the name in notice-display(text, 0x3C) (text colour 1, box colour 0xFF, centred in the card window, the
    //   pointer hidden, 60 ticks of DS:07D2 with nothing else running). menu-bar-loop puts the card window's colour and font back before the
    //   sub-item's call (root 0000:019D..0x01A9), so the box is in the font it found (GameUi.CardWindowFont), not the hot keys' font 1;
    //   vis10 session tleads-battle-difficulty: the original's box and this one identical; tests: BattleUiTests
    private void SetDifficulty(Difficulty d)
    {
        // C: the battle's difficulty is the game's (Battle.DifficultyOf)
        Simulation.GameCommands.SetDifficulty(_state, d);
        // C: the box's wait counts in the bar's run (RunMenu: the menu's measured callbacks hold it)
        new MessageBox(_ui).Show(HotKeys.DifficultyText(_ui, d));
    }

    private void Resume()
    {
        var tb = _battle!;
        // C: the Orders item Resume calls R016+0x1436 through root 09C0:1941 with no test of DS:A695; its menu state (DS:9609) is 1 from a
        // pause to its resume, so it is chosen with A695 1 or 0x11 (after Esc with nobody selected), both of which TogglePause resumes
        if (tb.PauseByte != 0)
        {
            tb.TogglePause();
        }
    }

    /// <summary>
    /// root 0000:0000 in the battle: the hot keys for a waiting key (F10 starts the keyboard menu), then the bar while the right
    /// button is held or the keyboard menu lasts, the battle standing still meanwhile. True when the bar was up.
    /// </summary>
    // orig: root 0000:0000 (DS:07D3 = 1 while it loops) with root 0000:0CAA for a waiting key (case 0x4400: DS:9C26 = 1,
    // DS:9C2F = 0, DS:9C3C = DS:9C41 = -1); tests: BattleUiTests
    private bool MenuBarRun()
    {
        // orig: root 0000:001E..0x0033: these hot keys run with the card window's font 1 (GameUi.CardWindowFont); R016's own call keeps it
        _ui.InMenuBarLoop(() =>
        {
            HotKeysForWaitingKey();
            return 0;
        });
        if (_unfoldedMenu is { } unfolded)
        {
            // C: the T key's flags (DS:9C26 = 1, DS:9C2F = 2, DS:A394 = 3) open the bar here, at the next root 0000:0000 of the battle's loop
            _unfoldedMenu = null;
            RunMenu(() => _menuBar!.RunUnfolded(unfolded));
            _keyboardMenu = false;
            return true;
        }
        if (!_ui.Mouse.Right && !_keyboardMenu)
        {
            return false;
        }
        RunMenu(() => _menuBar!.Run(keyboard: _keyboardMenu));
        _keyboardMenu = false;
        return true;
    }

    /// <summary>T seen in the battle's keys: the next root 0000:0000 runs the bar unfolded to this menu (Attack, at Throw's list).</summary>
    private int? _unfoldedMenu;

    /// <summary>The pass the menu bar last came up in, and the pass the last T (the Throw list) was read in.</summary>
    public int MenuPass { get; private set; } = -1;

    public int ThrowKeyPass { get; private set; } = -1;

    /// <summary>A menu bar run from the keys (T): the clock stood still meanwhile.</summary>
    private bool _resyncClock;

    /// <summary>F10 seen by the hot keys: the next root 0000:0000 is in keyboard menu mode.</summary>
    private bool _keyboardMenu;

    /// <summary>The bar's loop with the pointer of the screens shown (the battle's own cursor is not drawn meanwhile).</summary>
    // orig: root 0000:0000: `if (mouse-cursor-default != mouse-cursor-now && DS:907A == 1 && DS:9C4C == 1) root 047C:124F(0, 0, ...)` while the bar is up
    // C: root 0000:0000 holds the combat clock (DS:07D3 = 1 at 0x003D, 0 at 0x01CB) while the bar is up; the timer callbacks go on
    // C: the battle's pointer is the driver's (DS:907A == 1: root 047C:124F sets the default pointer shape, root 047C:12C3 hides the pointer and restores what it covered, root 047C:1293 draws the default one at the mouse):
    //   it never stays in the picture under the bar or a box the bar's items show (vis10 session tleads-battle-difficulty; tests: BattleUiTests)
    private void RunMenu(Action run)
    {
        MenuPass = Passes;
        ErasePointer();
        // orig: root 0000:0000+0x0084..0x00C4 as the bar comes up in a battle (DS:907A == 1, DS:9C4C == 1) with a shape other than the hand in
        //   use (DS:A68F != DS:9C42: an edge arrow of R014+0x33A2): pointer-hide, pointer-shape-set(0, 0, DS:A693 while the pause byte DS:A695
        //   is 0, else DS:A691), DS:A68F = DS:9C42, pointer-show; with the hand in use nothing is set. So the driver's pointer over the bar is
        //   the battle's hand at the mouse (the glove running, the order hand paused), until an item's screen sets the gauntlet
        //   (vis10 session uncert-cme-menus: the order hand over the opened Orders menu in the original's screen); tests: BattleUiTests
        // C: DS:9C4C is 1 while the game runs: root 03A1:000A+0x016E sets it from the driver call root 047C:11F6 (1 when it answers 1 or
        //   more) and R000+0x0551 sets it to 1 at start-up; no other writer (scan of the EXE).
        _driverShape = Hand;
        var (shape, hotSpot) = (_ui.PointerShape, _ui.PointerHotSpot);
        ApplyDriverPointer();
        var opened = _ui.Clock.Milliseconds;
        try
        {
            run();
        }
        finally
        {
            (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot) = (false, shape, hotSpot);
        }
        HeldClockTime(_ui.Clock.Milliseconds - opened);
        if (OneTickPerPass && MenuCallbacks is { Count: > 0 } measured)
        {
            var (during, after) = measured.Dequeue();
            CountCallbacks(during);
            if (_battle!.PauseByte != 0)
            {
                // as after a sheet: a running battle's callbacks after the bar raise the clock (the clock jumps list them)
                _afterSheetCallbacks += after;
            }
        }
    }
}
