using Darklands.Cards;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

// The battle screen: what the rules ask of the screen (ITacticalView): messages, sounds, the panel's rows, the view's centre, loot. The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    public int CameraX => _view.CameraX;

    public int CameraY => _view.CameraY;

    public bool HasHotSpot(int i) => HotSpot(i) is not null;

    /// <summary>The renderer's clip test of the combatant's sprite box, 4 pixels round it, against the view (0..0x103 × 0..0xC7).</summary>
    // orig: R014+0x9C8C (14F7:347C) draft, called by R014+0x719E with (scrx − 4, scry − 4, w + 8, h + 8)
    public bool CombatantInView(int i)
    {
        if (SpriteBox(i) is not { } box)
        {
            return false;
        }
        int x = box.Left - BattlefieldView.Left - 4, y = box.Top - 4, w = box.Width + 8, h = box.Height + 8;
        const int Right = 0x103, Bottom = 0xC7;
        if (x < 0)
        {
            w += x;
            x = 0;
            if (w <= 0)
            {
                return false;
            }
        }
        if (w + x > Right && Right - x + 1 <= 0)
        {
            return false;
        }
        if (y < 0)
        {
            h += y;
            y = 0;
            if (h <= 0)
            {
                return false;
            }
        }
        return !(h + y > Bottom && Bottom - y + 1 <= 0);
    }

    /// <summary>Where combatant i's sprite is drawn (screen pixels) and its size, null without an animation.</summary>
    private (int Left, int Top, int Width, int Height, int FootX, int FootY, Content.Formats.Battles.CombatAnimation Animation)? SpriteBox(int i)
    {
        var tb = _battle!;
        var c = tb.Battle[i];
        var (code, posture, weapon) = tb.ShownFile(i);
        var animation = _library.Drawn(c.AnimationSlot, code, posture, weapon)
            ?? _library.Drawn(c.AnimationSlot, code, "WK", weapon);
        if (animation is null)
        {
            return null;
        }
        var facing = c.SpriteFacing & 7;
        var (fx, fy) = _view.ToScreen(c.Position.X, c.Position.Y);
        var left = fx - animation.FootX[facing];
        var top = animation.BaseY - animation.Height + fy;
        return (left, top, animation.Widths[facing], animation.Height, fx, fy, animation);
    }

    /// <summary>A sound effect, when the sound options allow it (sound on and n ≥ 10, or 0x12, or music on and 7..9).</summary>
    // orig: R014+0x465E (0E76:465E) draft (asm-verified per spec D §1.11)
    public void Sound(int n) => Darklands.Audio.GameSounds.PlayBattleSound(_ui.Audio, n, _ui.Options.SoundEffects, _ui.Options.Music);

    // orig: R024+0x07A2 (1EF8:07A2) draft (the load and the tile's sprite and offsets; see BattlefieldTiles.Vary)
    public FurnitureShape? VaryFurniture(int tile, string set, int record) =>
        _tiles?.Vary(_assets.TileSets, set, tile, record) is { } f ? new FurnitureShape(f.Footprint, f.Height, f.BoxX, f.BoxY) : null;

    public void Message(int text, int ticks) => ShowMessage(_assets.Executable.String(text), ticks);

    public void FarMessage(int text, int ticks) => ShowMessage(_owner.FarText(text), ticks);

    public void Message(int text, int ticks, int member)
    {
        var c = _battle!.Battle[member];
        ShowMessage((c.Member?.Name ?? "") + _assets.Executable.String(text), ticks);
    }

    /// <summary>notice-display: the box over the battle for <paramref name="ticks"/> timer ticks, the battle's loop waiting meanwhile; then the box is gone.</summary>
    // orig: notice-display root 0000:24BC
    // C: pointer-hide (047C:12C3), the framed box (0000:23E6, which saves the rectangle it covers), wait-ticks(ticks) (0410:0302: a loop
    // C: until the byte DS:07D2 has risen by ticks), the rectangle put back (03EC:000E), pointer-show (047C:1293);
    // C: nothing of the battle runs meanwhile (the callers go on after it: the "USE STAIRS" message at 19CD:0A1C, 19CD:0E70 and R016's
    // C: arrow keys), no key or click is taken (they wait in the BIOS buffer and the driver's counts); the timer callback 047C:000A counts
    // C: every tick: DS:07D1 / 07D2 / 07D6, and the combat clock DS:07D4 unless DS:07D3 holds it. Direct calls of command-mode-end with
    // C: the party split: DS:07D1 +150 at the delay's return, the clock +150 when running (castle-p600) and unmoved when paused.
    // tests: BattleUiTests.AMessageWaitsItsTicksWithTheTimerRunning, BattleCallsTests (R014+0xBCA8 jobs)
    private void ShowMessage(string text, int ticks)
    {
        _message = text;
        _messageUntil = long.MaxValue;
        _messageWaiting = true;
        try
        {
            if (!OneTickPerPass)
            {
                Draw();
                _ui.Delay(ticks * 1000 / 60);
            }
        }
        finally
        {
            _messageWaiting = false;
        }
        _message = null;
        WaitedTicks(ticks);
        if (OneTickPerPass && NoticeCallbacks is { Count: > 0 } measured)
        {
            // C: the host has no time of its own in OneTickPerPass: paused, the timer callbacks after the box up to the next pass (the
            //   paused tick the wait made due and that tick's draw, after a box a full one) come at the pass's end, as for the hot keys' boxes
            var (_, after) = measured.Dequeue();
            if (_battle!.PauseByte != 0)
            {
                _afterSheetCallbacks += after;
            }
        }
    }

    /// <summary>Timer ticks spent inside a call that waits on the timer (notice-display's wait-ticks): the callbacks count them, the clock too
    /// unless it is held; the loop's own time starts again after them.</summary>
    // orig: root 047C:000A (see ShowMessage); the clock stands while the pause byte is not 0, as Advance
    private void WaitedTicks(int ticks)
    {
        _resyncClock = true;
        if (_battle!.PauseByte == 0 && OneTickPerPass && (ClockJumps is not null || SetUpTicks is not null))
        {
            // C: a debugging aid: the tester's measured time of a running battle (the clock jumps of its passes, the set-up's late ticks,
            // DARKLANDS_BATTLE_CLOCK_JUMPS / DARKLANDS_BATTLE_SETUP_TICKS) holds the wait's callbacks already
            return;
        }
        CountCallbacks(ticks);
        if (_battle.PauseByte == 0)
        {
            _battle.Battle.Clock = (ushort)(_battle.Battle.Clock + ticks);
        }
    }

    /// <summary>
    /// What member-gauge-bars put in each party row when the row was last drawn: a place note ("near a door", DS:0049 ...) in place of the
    /// bars, or null (the bars). The row keeps it until it is drawn again.
    /// </summary>
    // orig: member-gauge-bars root 0000:1A9C, root 0000:1BB7..1CD4 (checked against the asm): when fighter-flags[i] (DS:A696) has bit 0x08 and
    // genuine-fight (DS:A6C1) is 0, the text at the near pointer DS:01A8 (bit 0x02, in a doorway), DS:01AA (0x01, on stairs), DS:01AC (0x04,
    // at a chest), DS:01AE (0x20, in an exit zone) or DS:01B0 (0x40, left the field), the first bit set in that order, replaces the three
    // bars (the values are still drawn); with none of them, the bars. Bit 0x08 is set only while party-place-table (R014+0xDC4E..0xDD17)
    // draws the row of a member who entered a place and while floor-view-paint (R017+0x001E..0x004C) draws all the rows, so the note
    // comes when a member steps into a place (or the view is drawn anew) and stays until the row is drawn again.
    private readonly string?[] _placeNotes = new string?[Combatant.PartySlots];

    /// <summary>
    /// The order letter R016+0x15BE drew alone in each party row when an order was chosen ('\0': the patch without a letter), or null: the
    /// row keeps it until the row is drawn again.
    /// </summary>
    private readonly char?[] _orderLetters = new char?[Combatant.PartySlots];

    /// <summary>
    /// The order letter each party row shows: member-status-glyph's letter as it was when the row was last drawn (null: none), kept until the
    /// row is drawn again; <see cref="_rowLetterDrawn"/> false: not drawn since the battle began.
    /// </summary>
    // orig: member-status-glyph root 0000:18FA has one caller, party-panel-row-set root 0000:1716 (its call at 0000:1844), which draws only
    //   the rows of the bits DS:01E4[which] (all five for 0x100); a member's order changed without it keeps the row's old letter on the
    //   screen: the arrow keys of fight-keys (R016+0x02D6..0x02ED: DS:E079 = 1, the action word +0x10 = 1, DS:CA30[i] = the step) set
    //   walking and return (R016+0x09B8) with no row drawn, so the letter stays '-' while the member walks (vis10 run
    //   scratch\leads-mine\panel-letter of synth/DKSAVE2-stonebox). The rows are drawn where the engine says RowChanged, at floor-view-paint
    //   (R017+0x002C..0x003A, FloorViewPanel), after the won battle's loot (R014+0x0D30..0x0D33), after the combat sheet
    //   (R022+0x01D0..0x01D3), before the loot screen (R014+0xE5AF..0xE5B2) and after each skill-change message (R014+0x0694's calls
    //   R014+0x07DC .. 0x08DE).
    private readonly char?[] _rowLetters = new char?[Combatant.PartySlots];

    private readonly bool[] _rowLetterDrawn = new bool[Combatant.PartySlots];

    /// <summary>The order letter party row <paramref name="row"/> shows now (as of its last drawing), or null.</summary>
    public char? RowLetterShown(int row) => _rowLetterDrawn[row] ? _rowLetters[row] : StatusLetter(row);

    /// <summary>party-panel-row-set drew row <paramref name="row"/> (or all rows for -1): its letter is taken from the state now.</summary>
    private void RowLetterDrawn(int row)
    {
        if (_battle is null)
        {
            return;
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (row == -1 || row == i)
            {
                _rowLetters[i] = StatusLetter(i);
                _rowLetterDrawn[i] = true;
            }
        }
    }

    /// <summary>The order letter R016+0x15BE drew in a party row, still on the screen (null: none; '\0': the patch without a letter).</summary>
    public char? OrderLetterShown(int row) => _orderLetters[row];

    /// <summary>R016+0x15BE ran with nobody selected: the rows of its cell (the patch, no letter) still on the screen from y = 0 (7, then 1).</summary>
    private int _orderCellRows;

    /// <summary>The rows of the cell R016+0x15BE drew with nobody selected still on the screen at (6..15, 0..): 7, 1 once the first row was drawn again, or 0.</summary>
    public int OrderCellRowsShown => _orderCellRows;

    /// <summary>The panel page's (6..15, 1..6), which the cell copies to the screen, holds the first row as drawn (not the patch's pixels any more).</summary>
    private bool _pageCellHoldsRows;

    // C: after party-panel-frame-set(-1) (FramesOfNobodyDrawn) the cell R016+0x15BE copies from the panel page (DS:0168, page 1) to the screen
    //   brings the first row's own pixels at y 1..6 and the patch's top line at y 0 (vis10 session uncert-order-letter-none, the screen read
    //   before and after R016+0x15BE: after Enter, Enter, H only y = 0 is the patch's).
    public void FramesOfNobodyDrawn() => _pageCellHoldsRows = true;

    // orig: R016+0x15BE (1EF8:15BE) draft (checked against the asm)
    // C: the end of every order choice (TacticalBattle.ChooseOrder, ChoosePlace, ChooseThrow): the letter of DS:A6B0 into DS:9085 (1: 'F' when the Following bit 0x1000 of the member's flags is set, else 'W'; 6 'B', 0x0A 'V', 0x12 'A',
    //   0x22 'P', 0x40 'R', 0x80 'M', 0x100 'T'; 0x400: the place action DS:A6D1 through the table at R016+0x1664: 1, 2 'U', 3, 6 'P', 4, 7 'D',
    //   5 'O', 8 'X', 10 'H'), DS:9086 = 0; the patch DS:A6C2 at (6, 0x28 x DS:A6B2 + 0x1F) of the page DS:0168, the letter there in colour
    //   0xFF (the window colour put back after), the cell's 10 x font-height pixels copied to the screen DS:0190 (root 047C:0553), then the
    //   Following bit cleared. Nothing else draws the row until the panel rows (root 0000:1716) draw it again (vis10 session visleads-order-letter:
    //   each order key's letter white in the row, the same letter after a key that chose nothing; after Enter the rows' own letter)
    // DIVERGENCE: any other order or place action (in the game: place action 9, Loot Bodies, R016+0x13CA) leaves DS:9085 as its last writer
    //   left it, a text buffer shared by many screens, and draws its first byte; here the patch only.
    // C: with nobody selected (DS:A6B2 = 0xFF: H with Halt on outside order mode, reachable in normal play, see ChoosePlace) the cell's y is
    //   0x28 x -1 + 0x1F = -9: the driver does not wrap it but puts the patch and the copied 10 x 7 cell at y = 0, and the 'H' does not show:
    //   the row background's pixels (3..12, 0x1E..0x24) stand at (6..15, 0..6), over the top of the first row (vis10 run of
    //   uncert-order-letter-none: 70 pixels at (6..15, 0..6) after each H, the letter absent); drawing the first row again (from y = 1)
    //   leaves its top line (y = 0) there (seen after Enter, Enter and a later H); the battle's start clears it here.
    public void OrderLetterDrawn()
    {
        if (_battle is { Selected: -1 })
        {
            _orderCellRows = _pageCellHoldsRows ? Math.Max(_orderCellRows, 1) : PartyPanel.LetterPatchHeight;
            return;
        }
        if (_battle is not { } tb || tb.Selected is < 0 or >= Combatant.PartySlots)
        {
            return;
        }
        var c = tb.Battle[tb.Selected];
        _orderLetters[tb.Selected] = tb.OrderAction == 0x400
            ? tb.PlaceOrder switch
            {
                1 or 2 => 'U',
                3 or 6 => 'P',
                4 or 7 => 'D',
                5 => 'O',
                8 => 'X',
                10 => 'H',
                _ => '\0',
            }
            : tb.OrderAction switch
            {
                1 => (c.Flags & TacticalFlags.Following) != 0 ? 'F' : 'W',
                6 => 'B',
                0x0A => 'V',
                0x12 => 'A',
                0x22 => 'P',
                0x40 => 'R',
                0x80 => 'M',
                0x100 => 'T',
                _ => '\0',
            };
    }

    // orig: party-panel-row-set root 0000:1716 (which): the rows of the bits DS:01E4[which] (1 << which), all five for 0x100; each through
    // member-gauge-bars, which chooses the note from the flags as they are at that moment.
    public void RowChanged(int member)
    {
        if (member == -1)
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                DrawRowNote(i, 0);
            }
        }
        else if (member is >= 0 and < Combatant.PartySlots)
        {
            DrawRowNote(member, 0);
        }
    }

    // orig: floor-view-paint R017+0x0000..0x0058: bit 0x08 set in fighter-flags[0..4], party-panel-frame-set(0x100) and party-panel-row-set(0x100),
    // then bit 0x08 cleared again: every member who stands in a place shows its note.
    private void FloorViewPanel()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            DrawRowNote(i, TacticalFlags.PlaceChanged);
        }
    }

    private void DrawRowNote(int i, TacticalFlags extra)
    {
        if (_battle is not { } tb)
        {
            return;
        }
        var flags = tb.Battle[i].Flags | extra;
        int? at = null;
        if ((flags & TacticalFlags.PlaceChanged) != 0 && !tb.Battle.RealCombat)
        {
            at = (flags & TacticalFlags.InDoorway) != 0 ? 0x01A8
                : (flags & TacticalFlags.OnStairs) != 0 ? 0x01AA
                : (flags & TacticalFlags.AtChest) != 0 ? 0x01AC
                : (flags & TacticalFlags.InExitZone) != 0 ? 0x01AE
                : (flags & TacticalFlags.LeftField) != 0 ? 0x01B0
                : null;
        }
        _placeNotes[i] = at is { } pointer
            ? _assets.Executable.String(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_assets.Executable.DataSegment(pointer, 2)))
            : null;
        _orderLetters[i] = null;
        RowLetterDrawn(i);
        if (i == 0 && _orderCellRows > 1)
        {
            // C: the row is drawn from y = 1: the cell's top line stays (vis10 run uncert-order-letter-none, after Enter)
            _orderCellRows = 1;
        }
        PanelTrace?.Invoke($"row {i} clock {tb.Battle.Clock} {_placeNotes[i] ?? "bars"}");
    }

    // orig: the timer callback root 047C:000A counts the combat clock (DS:07D4) while the set-up of fight-main-loop R014+0x0000 loads its
    // files: here the clock moves on by the real time the set-up took (next to nothing)
    public void SetUpTime(int stage)
    {
        var now = _ui.Clock.Milliseconds;
        if (stage == 0)
        {
            _setUpMark = now;
            // the first battle has no entry, unless it is taken up from a saved game loaded from the start screen (lockstep gives it then:
            // local/batch/b32/a7-king); every later one takes its own, a battle taken up from a saved game uses it
            if ((_setUpsSeen++ > 0 || _resuming) && ResumeClocks is { Count: > 0 } clocks && clocks.Dequeue() is var first && _resuming)
            {
                // the restore's and the set-up's time comes after the set-up's clock reads (the original's first pass checks the
                // activations at once: R014+0x05B4), up to the first pass
                _setUpTicks = (0, 0, 0, 0);
                _resumeLate = (ushort)(first - _battle!.Battle.Clock);
                return;
            }
            if (SetUpTicks is { Count: > 0 } given)
            {
                _setUpTicks = given.Count > 1 ? given.Dequeue() : given.Peek();
            }
            return;
        }
        if (SetUpTicks is not null || (_resuming && ResumeClocks is not null))
        {
            _clockFraction = stage switch
            {
                1 => _setUpTicks.Loads,
                2 => _setUpTicks.FirstFrame,
                3 => _setUpTicks.BeforeActivations,
                _ => _setUpTicks.Late - _setUpTicks.BeforeActivations,
            };
            Advance(0);
        }
        else if (stage >= 3)
        {
            return;
        }
        else
        {
            Advance(now - _setUpMark);
        }
        _setUpMark = now;
    }

    // orig: floor-view-paint R017+0x0000 (1EF8:0000) draft with who −1: the view around the centre kept (DS:C866 / DS:C886, which a
    // placement may have set); DS:E160 = 1, so the next fight-draw forgets the last frame's logged sprites (DS:E176 = 0)
    public void MapChanged()
    {
        ApplyViewOrigin();
        _viewRedrawn = true;
        _screenLeftover = null;
        _framesOverHatching = false;
        _battle!.Effects.ForgetCloudBoxes();
        FloorViewPanel();
    }

    // orig: floor-view-paint R017+0x0000 draft with who ≥ 0: the centre becomes the combatant's cell (DS:E233 = x >> 5, DS:E235 = y >> 4)
    public void Recentre(int who)
    {
        var c = _battle!.Battle[who];
        _battle.ViewOrigin = null;
        _view.CentreColumn = c.Position.Column;
        _view.CentreRow = c.Position.Row;
        _viewRedrawn = true;
        _screenLeftover = null;
        _framesOverHatching = false;
        _battle!.Effects.ForgetCloudBoxes();
        FloorViewPanel();
    }

    // orig: fight-tick R014+0x1065..0x10A7 (checked against the asm): scry < 0x14 or > 0xA0, or scrx < 0x14 or > 0xDC, of DS:17B0, as
    // fighter-screen-position R014+0x99F6 left them in the last frame (the sprite's top left in the view)
    public bool KeepInView(int who)
    {
        var (sx, sy) = _tickPlaces[who] ?? _battle!.Battle[who].ScreenPlace;
        if (sy < 0x14 || sy > 0xA0 || sx < 0x14 || sx > 0xDC)
        {
            Recentre(who);
            return true;
        }
        return false;
    }

    /// <summary>The loot screen over the battle (the party records are the combatants): what the party leaves stays in the list.</summary>
    // orig: loot-view R014+0xE3C0 (19CD:2E50) unmatched → R040 loot-split-entry (the
    // combat buffers freed and reloaded around it are the original's memory management)
    public void OpenLoot(int member, List<ItemStack> list, Money money)
    {
        // C: the pointer is the driver's: the loot screen keeps the battle's party column (R040), never the battle's pointer drawn over it
        ErasePointer();
        if (LootTicks is not null && LootTicks.Remove(JumpKey(_battle!.Battle.Clock), out var ticks))
        {
            _clockFraction = ticks;
            Advance(0);
        }
        // orig: loot-view: palette-keep(0, DS:CA55) before loot-split-entry (R040, which shows BUYSELL.PIC and sets its palette),
        // palette-reload(DS:CA53, DS:CA55) after it; then loot-pile-opened R014+0xE278 redraws the battle:
        // floor-view-paint-overlay-entry(DS:17B0) (the view centred on the active member) and fight-draw.
        var palette = (uint[])_ui.Canvas.Screen.Palette.Clone();
        // orig: loot-view R014+0xE4F6: DS:07D8 = 0 (the loot screen's palette), R014+0xE6BB: DS:07D8 = 1 after the palette restore
        _ui.RunCycles(false);
        _ui.PaletteSet();
        // orig: the party records are the combatants, so the loot screen (R040) and its panel see the wounds and skills of the fight at once (as the
        // character sheet does, see CharacterSheet)
        _battle!.MembersFromCombat();
        PacksToMembers();
        // orig: R014+0xE5AF..0xE5B2: the panel rows (root 0000:1716) with 0x100 before the loot screen, the frames not: each row's 0x26 lines are
        //   copied to the screen, its last line and the frames stay as the battle drew them (vis10 session D-56a675: the shields, the order letters
        //   and the battle's hatching on the frame line)
        RowLetterDrawn(-1);
        new PartyPanel(_ui).DrawRowsAsCopied(_ui.Canvas, _state.Party);
        // orig: loot-view R014+0xE410..0xE42D: the menu titles and DS:9893 are 2 while the loot screen is up, put back at
        //   R014+0xE716..0xE72B (as around the combat sheet)
        var menus = GreyMenusForAScreen();
        // orig: loot-view R014+0xE584..0xE5A3 (DS:9C4C == 1): pointer-hide, pointer-shape-set(0, 0, DS:9C42: the gauntlet), DS:A68F = DS:9C42
        //   before the loot screen; R014+0xE67A..0xE6A7 after it: the battle's hand again (DS:A693 running / DS:A691 paused). loot-split-entry
        //   shows the driver's pointer (R040+0x0194, root 047C:1293) and its page redraws hide and show it again (R040+0x06FB / 0x0948,
        //   0x096B / 0x0C19): the gauntlet at the mouse over the loot page (vis10 session D-56a675: the original's loot screen has it, the
        //   C#'s had no pointer); tests: BattleUiTests
        var (visible, shape, hotSpot) = (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot);
        (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot) = (true, null, (0, 0));
        try
        {
            _owner.TakeLoot(list, money);
        }
        finally
        {
            (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot) = (visible, shape, hotSpot);
            menus();
        }
        // orig: the party record is the combatant record: what the screen changed in a member comes back whole, not only the pack: an item
        // put on the pile that was the equipped one is unequipped (R031+0x5B38 through item-take-single) in the record itself (+0x22, +0x51,
        // +0x4B/+0x4C, +0x5C and the encumbrance +0x49), which the battle reads afterwards
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (_battle!.Battle[i].Member is { } m)
            {
                CombatantSetup.Refresh(_battle.Battle[i], m);
            }
        }
        _battle!.TakeUpEnemyAnimations();
        // orig: R014+0xE5E3: the releases made on the loot screen are taken from the driver and forgotten
        _ui.TakeLeftReleases();
        _releases = 0;
        palette.CopyTo(_ui.Canvas.Screen.Palette, 0);
        _ui.PaletteSet();
        _ui.RunCycles(true);
        _underPointer = null;
        var c = _battle!.Battle[_battle.Battle.ActiveMember];
        _view.CentreColumn = c.Position.Column;
        _view.CentreRow = c.Position.Row;
        Draw();
        // orig: R014+0xE67A..0xE6A7: the battle's hand set again after the loot screen (the driver's shape for what follows: a member's
        //   message, R021+0x0298)
        _driverShape = Hand;
        ErasePointer();
        DrawPointer(_ui.Canvas, _driverShape);
    }

    /// <summary>The screen's frame lines were drawn by party-panel-frame-set after the last full paint (the view's page flip).</summary>
    private bool _framesOverHatching;

    // orig: R014+0x0D25..0x0D38 after the won battle's loot screen: party-panel-frame-set(0x100) draws the frames on both pages, the
    //   bottom line of row i at y = 0x28 x i + 0x27 (root 0000:1601 / 0x16EA); party-panel-row-set(0x100) draws the rows on the page
    //   DS:0168 (a dead member's hatching clipped to 0x27 lines, root 0000:1852..0x1888) and copies only 0x26 lines of each to the screen
    //   (root 0000:18AC..0x18C3), so the screen keeps the frame line under the hatching until the next floor-view-paint copies the page
    //   (vis10 session leftovers-msg, write watch on (10,39): the flip writes 07, then the frame-set 82; hooks: frame-set and row-set
    //   from R014+0x0D2D / 0x0D38 after the loot)
    // C: the battle draws the whole panel each frame: a dead member's hatching keeps off the frame line until the next full paint
    //   (MapChanged, Recentre, ViewScrolled); tests: BattleUiTests
    public void PanelFramesRedrawn()
    {
        _framesOverHatching = true;
        if (_battle is null)
        {
            return;
        }
        // orig: R014+0x0D30..0x0D33: party-panel-row-set(0x100) after the frames: every row's letter as the state is now
        RowLetterDrawn(-1);
        ErasePointer();
        DrawPanel(_ui.Canvas);
        DrawPointer(_ui.Canvas, _driverShape);
    }

    /// <summary>A report in its member's panel row (or the whole column for −2) in the original's words: good news in colour 15, bad news in 12.</summary>
    public void MemberMessage(GameMessage message) =>
        MemberMessage(message.Member, new OriginalMessageText(_assets.Executable).Text(message), message.Good ? 15 : 12);

    /// <summary>A text in a member's panel row (or the whole column for −2) in colour <paramref name="color"/>, waiting for a key or a left click.</summary>
    // orig: R021+0x01E2 (1EF8:01E2) matched: the row background DS:A626 at (3, row × 40 + 1)
    // (the column: the strips DS:A628 and y 0x51), the text wrapped in 0x37 pixels from y + 4, then `while (!key-waiting-test() && !(mouse-button-state & 1))
    // menu-bar-loop(...)` and the button waited out
    // orig: R021+0x01F9 pointer-hide (root 047C:12C3) before the row is drawn, R021+0x0298 pointer-show (root 047C:1293) after it, no
    //   shape set: the driver's pointer waits in the battle's last shape (the hand after the loot screen, R014+0xE67A..0xE6A7; the bar
    //   opened meanwhile sets the hand too, root 0000:0000+0x0084), not the gauntlet; tests: BattleUiTests
    private void MemberMessage(int member, string text, int color)
    {
        ErasePointer();
        var canvas = _ui.Canvas;
        var sprites = _ui.Assets.CommonSprites;
        int y;
        if (member == -2)
        {
            var column = new Clip(3, 1, 3 + 0x36, 1 + 0xC6);
            for (var v = 0; v < 0xC6; v += 0xB)
            {
                SpriteDrawing.Draw(canvas, sprites.ColumnBackground, 3, v, column);
            }
            y = 0x51;
        }
        else
        {
            y = member * 40 + 1;
            SpriteDrawing.Draw(canvas, sprites.RowBackground, 3, y);
        }
        var layout = new CardTextLayout(_ui.Text);
        layout.Draw(canvas, TextPainter.Encode(text), 3, y + 4, 0x37,
            new CardTextLayout.Settings { Color = (byte)color, Leading = 0, Alignment = TextAlignment.Center });
        var restore = ShowDriverPointer();
        // orig: R021+0x02A6 matched C: `while (!root 047C:073D() && !(mouse-button-state & 1)) root 0000:0000(&DS:9419, 0);` (the key stays
        // waiting for the battle's keys), then R021+0x02C3 `while (mouse-button-state & 1);` (the release is counted: the next pass of the
        // mouse handler takes it as a click); tests: BattleUiTests
        try
        {
            while (!_ui.KeyWaiting && !_ui.Mouse.Left)
            {
                _ui.Present();
                if (_ui.Mouse.Right)
                {
                    RunMenu(() => _menuBar!.Run(keyboard: false));
                    // the bar leaves the driver's pointer shown in the hand it set
                    ApplyDriverPointer();
                }
                _ui.Idle();
            }
            _ui.WaitForButtonRelease();
        }
        finally
        {
            restore();
        }
        // orig: R014+0x0694's skill notices (its calls R014+0x07DC .. 0x08DE): each message, then party-panel-row-set(member): the row's letter
        //   as the state is now
        if (member >= 0)
        {
            RowLetterDrawn(member);
        }
    }

    public int ScreenTileOf(int cell)
    {
        for (var k = 0; k < BattlefieldView.Rows * BattlefieldView.Columns; k++)
        {
            var (dx, dy) = BattlefieldView.SlotOffset(k / BattlefieldView.Columns, k % BattlefieldView.Columns);
            if ((_view.CentreColumn + dx) * 256 + _view.CentreRow + dy == cell)
            {
                return k;
            }
        }
        return 0x8C;
    }
}
