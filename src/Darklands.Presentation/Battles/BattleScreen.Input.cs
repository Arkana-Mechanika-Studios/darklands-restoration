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

// The battle screen: the player's keys and mouse. The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    /// <summary>root 0000:0CAA for a waiting key: a key it knows is taken and acted on, any other stays waiting for the battle's keys.</summary>
    private void HotKeysForWaitingKey()
    {
        if (!_ui.KeyWaiting || !_ui.TryReadKey(out var key))
        {
            return;
        }
        if (key == Keys.F12 && _ui.ScreenshotFolder is not null)
        {
            _ui.SaveScreenshot();
            return;
        }
        if (key == Keys.F10)
        {
            _keyboardMenu = true;
            return;
        }
        // C: the driver's pointer is never in the picture a hot key's box or screen is drawn over (vis10 session D-b797f7: the Alt-S comment
        //   dialog shows none); the pass's own draw puts it back when the key turns out to be the battle's
        ErasePointer();
        if (!BattleHotKey(key))
        {
            _ui.PushBackKey(key);
        }
    }

    /// <summary>
    /// The battle's keys, once a pass when a key waits or a direction key is held: the hot keys first, then the key; with no key
    /// waiting, the direction of the held key as an arrow (Home/PgUp/End/PgDn for the diagonals).
    /// </summary>
    // orig: R016+0x0000 draft, checked against the asm for the arrows and Enter: `if (root 047C:073D()) { root 0000:0CAA(); if
    // (root 047C:073D()) k = root 047C:0744(); } else k = the arrow of the signs of DS:0880 / DS:0881`; tests: BattleUiTests
    private void CombatKeys()
    {
        var k = 0;
        if (_ui.KeyWaiting)
        {
            HotKeysForWaitingKey();
            if (_ui.KeyWaiting && _ui.TryReadKey(out var key))
            {
                k = key;
            }
        }
        else
        {
            k = ArrowOf(_ui.Directions.X, _ui.Directions.Y);
        }
        if (k != 0)
        {
            Key(k);
        }
    }

    /// <summary>The arrow key of a direction (the signs of x and y), 0 for none.</summary>
    private static int ArrowOf(int x, int y) => (x, y) switch
    {
        ( < 0, < 0) => Keys.Home,
        ( < 0, > 0) => Keys.End,
        ( > 0, < 0) => Keys.PageUp,
        ( > 0, > 0) => Keys.PageDown,
        ( < 0, _) => Keys.Left,
        ( > 0, _) => Keys.Right,
        (_, < 0) => Keys.Up,
        (_, > 0) => Keys.Down,
        _ => 0,
    };

    /// <summary>DS:E094: the combat tick the pointer last moved in. The hover (DS:D728), the keyboard's click (DS:D73B), the click when
    /// the key is let go (DS:D947) and the timer callbacks since the keys were last let go (DS:07D6) are the program's CursorKeys.</summary>
    private int _pointerTick = -1;

    /// <summary>
    /// The cursor keys move the pointer (order mode, travelling as a group): once a combat tick while a direction key is held, the
    /// pointer moves 1, 3, 5 or 10 pixels the longer it is held (within x 0x3D..0x13F, y 0..0xC7) and the hover acts as with the
    /// button held; letting the key go is a click at the pointer (not when travelling as a group). A waiting key is eaten when the
    /// pointer moved.
    /// </summary>
    // orig: R014+0x35FC draft, checked against the asm (its jump table and clamps): t = DS:07D6 - DS:D945 (is 0): speed 1 up to 0x2D,
    // 3 up to 0x4B, 5 up to 0x5A, else 10; root 047C:1266; `if (root 047C:073D()) root 047C:0744();`; tests: BattleUiTests
    // C: DS:07D6 counts every timer callback of the program (paused too, outside the battles too) and only this function clears it, so a
    // key already held when order mode begins starts at the speed of the time since the last let-go, in this battle or an earlier one
    // (or since the program's start): t is a signed int, so a count past 0x7FFF is slow again.
    private void CursorKeys()
    {
        var tb = _battle!;
        var keys = tb.CursorKeys;
        var (dx, dy) = (_ui.Directions.X, _ui.Directions.Y);
        if (dx == 0 && dy == 0)
        {
            keys.Hover = false;
            if (keys.ClickOnRelease)
            {
                keys.Click = true;
                keys.ClickOnRelease = false;
            }
            // C: R014+0x38AC..0x38B1: DS:07D6 = DS:D945 = 0
            keys.HeldCallbacks = 0;
            keys.ReleasedAt = 0;
            return;
        }
        var key = 0;
        if (_pointerTick != tb.Ticks)
        {
            _pointerTick = tb.Ticks;
            keys.Hover = true;
            if (tb.MoveMode != 1)
            {
                keys.ClickOnRelease = true;
            }
            key = ArrowOf(dx, dy);
        }
        var t = (short)(keys.HeldCallbacks - keys.ReleasedAt);
        var speed = t <= 0x2D ? 1 : t <= 0x4B ? 3 : t <= 0x5A ? 5 : 10;
        var (mx, my) = (_ui.Mouse.X, _ui.Mouse.Y);
        int Up() => my - speed >= 0 ? my - speed : 0;
        int Down() => speed + my > 0xC7 ? 0xC7 : speed + my;
        int LeftX() => mx - speed >= 0x3D ? mx - speed : 0x3D;
        int RightX() => speed + mx > 0x13F ? 0x13F : speed + mx;
        switch (key)
        {
            case Keys.Up: _ui.MovePointer(mx, Up()); break;
            case Keys.Down: _ui.MovePointer(mx, Down()); break;
            case Keys.Left: _ui.MovePointer(LeftX(), my); break;
            case Keys.Right: _ui.MovePointer(RightX(), my); break;
            case Keys.Home: _ui.MovePointer(LeftX(), Up()); break;
            case Keys.End: _ui.MovePointer(LeftX(), Down()); break;
            case Keys.PageDown: _ui.MovePointer(RightX(), Down()); break;
            case Keys.PageUp: _ui.MovePointer(RightX(), Up()); break;
            default: return;
        }
        if (_ui.KeyWaiting)
        {
            _ui.TryReadKey(out _);
        }
    }

    /// <summary>A key of the battle.</summary>
    // orig: fight-keys R016+0x0000 (1EF8:0000) draft (each case with its enable test)
    private void Key(int key)
    {
        var tb = _battle!;
        var m = tb.Menus;
        // orig: R016+0x00FC.. (and R016+0x00B5.. with the party split) switch on the whole key word (checked against the asm, every case): a
        // letter is its key's two words (scan code with the small and the capital letter, as Keys.ForCharacter makes them), F1..F5 and
        // Ctrl-F1..F5 their own words, 1..5 the top row's (0x0231 .. 0x0635); the keypad's digits (Num Lock on, or Shift with it off:
        // 0x4737 .. 0x5133) go to the scrolling part of the arrows' cases without the Shift test (R016+0x02A0, 0x0304, 0x0360, 0x03BC, 0x0418,
        // 0x04C2, 0x04C7, 0x02A4), its 5 (0x4C35) is no case.
        var scroll = key switch
        {
            0x4838 => 0,
            0x4939 => 1,
            0x4D36 => 2,
            0x5133 => 3,
            0x5032 => 4,
            0x4F31 => 5,
            0x4B34 => 6,
            0x4737 => 7,
            _ => -1,
        };
        var function = (key & 0xFF) == 0 && key is >= Keys.F1 and <= Keys.F5;
        var ctrlFunction = (key & 0xFF) == 0 && key is >= Keys.CtrlF1 and <= Keys.CtrlF5;
        var digit = key is >= 0x0231 and <= 0x0635 && Keys.ForCharacter(Keys.CharacterOf(key)) == key;
        var letter = char.IsAsciiLetter(Keys.CharacterOf(key)) && Keys.ForCharacter(Keys.CharacterOf(key)) == key ? char.ToUpperInvariant(Keys.CharacterOf(key)) : ' ';
        if (tb.Battle.PartyLevel != tb.Battle.LevelShown)
        {
            // The party is split over two levels (R016+0x00AC: DS:C623 != DS:11CA): every arrow word, the keypad's digits too and Shift or not,
            // gives the message (R016+0x00EA), U takes the stairs back (R016+0x014E), a few keys go on to the switch.
            if (scroll >= 0 || key is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
            {
                Message(TacticalBattle.UseStairsText, 0x96);
                return;
            }
            if (letter == 'U')
            {
                tb.UseKey(tb.Selected);
                return;
            }
            // DORMANT: 0x1000 (Alt-Q) asks "Quit to DOS?" here (R016+0x0194, root 0000:13A0): the hot keys take Alt-Q first.
            if (!(key is Keys.Escape or Keys.Enter or Keys.Space || function || ctrlFunction || digit))
            {
                return;
            }
        }
        if (scroll >= 0)
        {
            ScrollView(scroll);
            return;
        }
        switch (key)
        {
            case Keys.Up:
                Arrow(0);
                return;
            case Keys.PageUp:
                Arrow(1);
                return;
            case Keys.Right:
                Arrow(2);
                return;
            case Keys.PageDown:
                Arrow(3);
                return;
            case Keys.Down:
                Arrow(4);
                return;
            case Keys.End:
                Arrow(5);
                return;
            case Keys.Left:
                Arrow(6);
                return;
            case Keys.Home:
                Arrow(7);
                return;
            // orig: R014+0x0F6B..0x0F85: fight-tick still runs every 8 timer callbacks (DS:07D1) while the battle is paused and its fight-draw
            //   re-rolls the clouds' puffs and edges and the animated pieces.
            // DIVERGENCE: host timing (the former U-00dcd4): how many timer callbacks each paused pass of the original takes is its CPU time against the
            //   timer, so it varies with the machine; the host's paused passes are not tied to that timer, so the C# draws the clouds' re-rolled
            //   jitter frame on another pass than the original in a paused shot (3,497 px in the cloud boxes, vis10 shots c and d of D-09fbc1).
            //   The re-roll itself, its random sequence and the unpaused frames are the original's (lockstep sessions with a pause agree); the
            //   saved game is unaffected.
            case Keys.Space:
                tb.TogglePause();
                return;
            case Keys.Escape:
                if (tb.Paused)
                {
                    tb.CancelOrder();
                }
                return;
            case Keys.Enter:
                // orig: R016+0x0734 (checked against the asm): `if (DS:D7FE == 1 && travel-mode == 1) DS:D73B = 1; else if (travel-mode == 0)
                // R014+0xBCA8();`: travelling as a group, Enter clicks at the pointer
                if (tb.CursorKeysOn && tb.MoveMode == 1)
                {
                    tb.CursorKeys.Click = true;
                }
                else if (tb.MoveMode == 0)
                {
                    tb.LeaveOrderMode();
                }
                return;
            case Keys.AltO:
                // C: 0x1800 (Alt-O) in the battle's own switch: everybody stops and real combat ends (the hot key takes it first when allowed).
                tb.StopEverything();
                return;
        }
        if (function)
        {
            CharacterSheet((key - Keys.F1) >> 8);
            return;
        }
        if (ctrlFunction)
        {
            tb.ActivateMember((key - Keys.CtrlF1) >> 8);
            return;
        }
        if (digit)
        {
            tb.SelectMember(Keys.CharacterOf(key) - '1');
            return;
        }
        var selected = tb.Selected != -1;
        switch (letter)
        {
            case 'A':
                if (m.On(AttackItem.Standard) && selected)
                {
                    tb.ChooseOrder(0x12);
                }
                return;
            case 'B':
                if (m.On(AttackItem.Berserk) && selected)
                {
                    tb.ChooseOrder(0x06);
                }
                return;
            case 'D':
                tb.DissolveKey(tb.Selected);
                return;
            case 'E':
                if (m.On(OrdersItem.EnemyInfo))
                {
                    EnemyInfo();
                }
                return;
            case 'F':
                if (tb.Paused && m.On(OrdersItem.Flee) && selected)
                {
                    tb.ChooseOrder(1, flee: true);
                }
                return;
            case 'G':
                if (m.On(OrdersItem.Group))
                {
                    tb.ToggleGroup();
                }
                return;
            case 'H':
                tb.Halt(tb.Battle.Leader);
                return;
            case 'M':
                if (m.On(AttackItem.Missile))
                {
                    tb.ChooseOrder(0x80);
                }
                return;
            case 'O':
                tb.OpenKey(tb.Selected);
                return;
            case 'P':
                if (tb.PickKey(tb.Selected))
                {
                    tb.ChooseOrder(0x22);
                }
                return;
            case 'Q':
                if (m.On(OrdersItem.SingleFile))
                {
                    tb.ToggleSingleFile();
                }
                return;
            case 'S':
                Surrender();
                return;
            case 'T':
                if (m.On(AttackItem.Throw))
                {
                    // orig: case 0x1454 (R016+0x068A..0x06A1): DS:9C26 = 1, DS:9C2F = 2, DS:9C3C = DS:9C41 = -1, DS:A394 = 3 (the Attack menu
                    // unfolded to Throw's list) and nothing else: the bar comes up at the next pass's root 0000:0000 (MenuBarRun), after that pass's
                    // head, so the timer callbacks it holds the clock for are seen by that pass's combat tick, not this one's
                    _unfoldedMenu = 2;
                    ThrowKeyPass = Passes;
                }
                return;
            case 'U':
                tb.UseKey(tb.Selected);
                return;
            case 'V':
                if (m.On(AttackItem.Vulnerable) && selected)
                {
                    tb.ChooseOrder(0x0A);
                }
                return;
            case 'W':
                if (tb.Paused && m.On(OrdersItem.Walk) && selected)
                {
                    tb.ChooseOrder(1);
                }
                return;
            case 'X':
                tb.ExitKey();
                return;
        }
    }

    /// <summary>An arrow: the active member steps (Shift, BIOS 0040:0017 bit 0 or 1: the view scrolls by a cell, two sideways).</summary>
    private void Arrow(int direction)
    {
        if (_ui.ShiftHeld)
        {
            ScrollView(direction);
            return;
        }
        _battle!.StepKey(direction);
    }

    // orig: R016+0x02A0.. (the arrows' scrolling parts): DS:C866 / DS:C886 moved, then R016+0x0B92 (1EF8:0B92) matched: the view centre kept
    // within 1..0x27, then floor-view-paint
    private void ScrollView(int direction)
    {
        int[] dx = [-1, 0, 1, 2, 1, 0, -1, -2], dy = [-1, -1, 0, 1, 1, 1, 0, -1];
        _view.CentreColumn = Math.Clamp(_view.CentreColumn + dx[direction], 1, 0x27);
        _view.CentreRow = Math.Clamp(_view.CentreRow + dy[direction], 1, 0x27);
        ViewScrolled();
    }

    /// <summary>The front-most combatant (any, and enemy) whose hot spot holds the point, −1 none.</summary>
    // orig: the TOPMOST macro and the enemy loops of R014+0xC044 / R014+0xC33C draft, checked
    // against asm (R014+0xC342..0xC3A5, the TOPMOST loop of the click): the hot spots (DS:D8F1 x0, DS:D8D7 x1, DS:D8BD y0, DS:D8A3 y1, inclusive) are scanned from the last
    // drawn to the first; "any" is the one of greatest map y (`by < y`, by starting at 0, so the front-most of equal y), "enemy" the first
    // hit of a combatant ≥ 5 in that order (the front-most drawn), whatever its y.
    private (int Any, int Enemy) HotSpotsAt(int x, int y)
    {
        int best = -1, bestY = 0, enemy = -1;
        for (var k = _hotSpots.Count - 1; k >= 0; k--)
        {
            var h = _hotSpots[k];
            if (x < h.Left || x > h.Right || y < h.Top || y > h.Bottom)
            {
                continue;
            }
            if (bestY < h.MapY)
            {
                best = h.Who;
                bestY = h.MapY;
            }
            if (enemy < 0 && h.Who >= Combatant.PartySlots)
            {
                enemy = h.Who;
            }
        }
        return (best, enemy);
    }

    /// <summary>The map point under the pointer (DS:D742/DS:D744, DS:E1B4/DS:E1B6): the inverse of the projection, so the ground cursor is drawn under the pointer.</summary>
    // orig: fight-mouse R014+0xB576 (19CD:0006) draft, see BattlefieldView.ToMap (its − 16 is already in the formula: a second
    // − 16 put the spot, and the ghost drawn at it, 16 pixels left of the click)
    private MapPoint GroundAt(int x, int y)
    {
        var (mx, my) = _view.ToMap(x, y);
        return new MapPoint(mx, my);
    }

    /// <summary>The front-most party member whose hot spot holds the point (the hot spots scanned from the last drawn), −1 none.</summary>
    // orig: the member loops of the mouse handler (R014+0xB632.., R014+0xB87E..): `for (k = mouse-zone-total - 1; k >= 0; k--) if (mouse-zone-fighter[k] < 5 && HIT(k, x, y))`
    private int MemberHotSpotAt(int x, int y)
    {
        for (var k = _hotSpots.Count - 1; k >= 0; k--)
        {
            var h = _hotSpots[k];
            if (h.Who < Combatant.PartySlots && x >= h.Left && x <= h.Right && y >= h.Top && y <= h.Bottom)
            {
                return h.Who;
            }
        }
        return -1;
    }

    /// <summary>
    /// The mouse handler, once a pass: while the left button is held over the view (Ctrl up) the cursor follows (outside order
    /// mode the member under the pointer is selected, in order mode the order cursor, in a formation the group cursor); then a
    /// release taken from the mouse driver's count is a click at the place of the release (Ctrl: the member there is activated).
    /// A release over the party panel is left for <see cref="PanelRelease"/>.
    /// </summary>
    // orig: R014+0xB576 (19CD:0006) draft, checked against the asm: the hover R014+0xB632.., the release count R014+0xB814 (INT 33h
    // function 6 into DS:C4FE, its count into DS:C62A when none is pending), the keyboard click DS:D73B (the pointer where it is now),
    // `ex -= 60; if (ex < 0) return;` before DS:C62A = 0, the Ctrl-click R014+0xB87E..; tests: BattleUiTests
    // C: the menu bar shown (DS:9409, F10's keyboard mode) returns at once: the releases stay counted.
    private void CombatMouse()
    {
        var tb = _battle!;
        if (_menuBar!.Shown)
        {
            return;
        }
        tb.GroupCursorTimeout();
        var mouse = _ui.Mouse;
        var mx = mouse.X - BattlefieldView.Left;
        if ((tb.CursorKeys.Hover || mouse.Left) && mx >= 0 && mx < 0x104 && !_ui.CtrlHeld)
        {
            if (tb.MoveMode == 0)
            {
                if (!tb.InOrderMode)
                {
                    tb.PreviewMember(MemberHotSpotAt(mouse.X, mouse.Y));
                }
                else
                {
                    var (any, enemy) = HotSpotsAt(mouse.X, mouse.Y);
                    tb.Hover(any, enemy, GroundAt(mouse.X, mouse.Y));
                }
            }
            else
            {
                tb.GroupCursor(GroundAt(mouse.X, mouse.Y), tb.CursorKeys.OnForTheGroupCursor);
            }
        }
        TakeReleases();
        if (_releases <= 0 && !tb.CursorKeys.Click)
        {
            return;
        }
        int ex, ey;
        if (tb.CursorKeys.Click)
        {
            if (tb.MoveMode != 0)
            {
                tb.KeepGroupCursor();
            }
            tb.CursorKeys.Click = false;
            (ex, ey) = (_ui.Mouse.X, _ui.Mouse.Y);
        }
        else
        {
            (ex, ey) = (_releaseX, _releaseY);
        }
        if (ex < BattlefieldView.Left)
        {
            return;
        }
        _releases = 0;
        if (_ui.CtrlHeld)
        {
            if (MemberHotSpotAt(ex, ey) is var member and >= 0)
            {
                tb.ActivateMember(member);
            }
            return;
        }
        Click(ex, ey);
    }

    /// <summary>DS:C62A: when no release is pending, the mouse driver's count and the place of the last release are taken.</summary>
    // orig: `if (DS:C62A == 0) { DS:C4FE = root 047C:1320(6, 0); DS:C62A = DS:C4FE[1]; }`
    private void TakeReleases()
    {
        if (_releases == 0)
        {
            (_releases, _releaseX, _releaseY) = _ui.TakeLeftReleases();
        }
    }

    /// <summary>A click in the view: outside order mode a member enters order mode, in order mode the order's target or spot, in a formation the group moves.</summary>
    // orig: R014+0xB87E.. (the click of the mouse handler) draft, checked against the asm
    private void Click(int x, int y)
    {
        var tb = _battle!;
        if (tb.MoveMode == 0)
        {
            if (!tb.InOrderMode)
            {
                // C: chosen-member = -1, then the last-drawn member hot spot under the release enters order mode
                var member = MemberHotSpotAt(x, y);
                tb.PreviewMember(member);
                if (member >= 0)
                {
                    tb.EnterOrderMode(member);
                }
                return;
            }
            var (any, enemy) = HotSpotsAt(x, y);
            tb.OrderClick(any, enemy, GroundAt(x, y));
            return;
        }
        tb.GroupMove(GroundAt(x, y));
    }

    /// <summary>
    /// A release over the party panel (x &lt; 60): the sheet of the member of the release's row when that member stands able on
    /// the field (every able member then readies his weapon again), else "You can't view …"; with Ctrl the member is activated.
    /// </summary>
    // orig: R014+0x3198..0x3238 (R014+0x3166) draft, checked against the asm: `if (DS:C62A > 0 && DS:C4FE[2] < 0x3c) { if (!(KBFLAGS & 4))
    // { i = y / 40; if (status == 1 && fighter-floor-level[i] != -99) { R022+0x0000(y); for (...) if (status == 1) R019+0x0B9E(i); } else
    // R022+0x04E4(i); } else R016+0x09BC(y / 40); DS:C62A = 0; }`; tests: BattleUiTests
    // C: an empty, dead or absent member's row also gives the message (with its record's name).
    private void PanelRelease()
    {
        var tb = _battle!;
        TakeReleases();
        if (_releases <= 0 || _releaseX >= BattlefieldView.Left)
        {
            return;
        }
        var row = _releaseY / PartyPanel.RowHeight;
        if (!_ui.CtrlHeld)
        {
            var c = tb.Battle[row];
            if (c.Status == CharacterStatus.Able && c.Level != BattleEnd.LeftThroughExit)
            {
                CharacterSheet(row);
                tb.ReReadyAbleMembers();
            }
            else
            {
                CannotView(row);
            }
        }
        else
        {
            tb.ActivateMember(row);
        }
        _releases = 0;
    }

    /// <summary>Shift with the pointer at an edge of the view: the view scrolls toward it (a cell, two at the corners' sides).</summary>
    // orig: R014+0x323E.. (R014+0x3166) draft: the zones of the edge cursors (KBFLAGS & 3), DS:C866 / DS:C886 changed, then R016+0x0B92
    // (R016+0x0B92: the centre kept within 1..0x27, the view redrawn); tests: BattleUiTests
    // DIVERGENCE: the original scrolls once per pass of its loop (R014+0x3166, no time gate before R016+0x0B92), which runs as fast as the
    //   machine draws, its whole view redrawn after each step (a few steps a second on the machines of its day). With
    //   GameUi.EdgeScrollMilliseconds 0 (the default: the oracle tools, the headless host) here too, once a pass; the SDL host sets a
    //   pace (at most one step each 100 ms of the host's clock, whatever the frame rate: a step every frame, 60 a second, was far faster
    //   than the original's). Shift with an arrow scrolls at the keyboard's repeat either way.
    private void EdgeScroll()
    {
        if (!_ui.ShiftHeld)
        {
            return;
        }
        var now = _ui.Clock.Milliseconds;
        if (_ui.EdgeScrollMilliseconds > 0 && now - _edgeScrolledAt < _ui.EdgeScrollMilliseconds)
        {
            return;
        }
        var (x, y) = (_ui.Mouse.X, _ui.Mouse.Y);
        int dc, dr;
        if (x > 0x7D && x < 0xFF && y < 8)
        {
            (dc, dr) = (-1, -1);
        }
        else if (x > 0x7D && x < 0xFF && y > 0xBF)
        {
            (dc, dr) = (1, 1);
        }
        else if (x >= 0x3C && x < 0x44 && y > 0x32 && y < 0x96)
        {
            (dc, dr) = (-1, 0);
        }
        else if (x >= 0x137 && y > 0x32 && y < 0x96)
        {
            (dc, dr) = (1, 0);
        }
        else if ((x >= 0x3C && x <= 0x7D && y < 8) || (x >= 0x3C && x < 0x44 && y <= 0x32))
        {
            (dc, dr) = (-2, -1);
        }
        else if ((x >= 0xFF && y < 8) || (x >= 0x137 && y <= 0x32))
        {
            (dc, dr) = (0, -1);
        }
        else if ((x >= 0x3C && x <= 0x7D && y > 0xBF) || (x >= 0x3C && x < 0x44 && y >= 0x96))
        {
            (dc, dr) = (0, 1);
        }
        else if ((x >= 0xFF && y > 0xBF) || (x >= 0x137 && y >= 0x96))
        {
            (dc, dr) = (2, 1);
        }
        else
        {
            return;
        }
        _edgeScrolledAt = now;
        ScrollView(dc, dr);
    }

    /// <summary>The view's centre moves (kept within 1..0x27) and the view no longer follows the active member.</summary>
    // orig: R016+0x0B92 (1EF8:0B92) matched: the view centre DS:C866/DS:C886 kept within 1..0x27, then R017+0x0000
    private void ScrollView(int dc, int dr)
    {
        _view.CentreColumn = Math.Clamp(_view.CentreColumn + dc, 1, 0x27);
        _view.CentreRow = Math.Clamp(_view.CentreRow + dr, 1, 0x27);
        ViewScrolled();
    }

    /// <summary>R016+0x0B92's end: floor-view-paint (DS:E160) and DS:11B1 = 0, the view no longer follows the active member.</summary>
    private void ViewScrolled()
    {
        _battle!.ViewOrigin = null;
        _battle.FollowsActive = false;
        _viewRedrawn = true;
        _screenLeftover = null;
        _framesOverHatching = false;
        _battle!.Effects.ForgetCloudBoxes();
        FloorViewPanel();
    }

    /// <summary>The Orders, Attack and Party titles and Change Marching Order (DS:9893) set to 2; returns what puts them back.</summary>
    private Action GreyMenusForAScreen()
    {
        var m = _battle!.Menus;
        var (orders, attack, party, marching) = (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder);
        m.OrdersMenu = m.AttackMenu = m.PartyMenu = m.MarchingOrder = 2;
        return () => (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder) = (orders, attack, party, marching);
    }
}
