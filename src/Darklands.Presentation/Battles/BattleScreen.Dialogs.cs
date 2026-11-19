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

// The battle screen: the screens a battle opens (character sheet, surrender, enemy information, marching order). The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    /// <summary>"You can't view … at this time.": a message box for 150 ticks, the battle waiting.</summary>
    // orig: R022+0x04E4 matched: sprintf(buf, "%Fs%s%Fs", 286B:0212, name, 286B:0222); root 0000:24BC(buf, 0x96) (root 0000:24BC, a wait
    // on the timer that nothing shortens)
    // C: fight-character-sheet comes here (its test of the floor byte 0x9D) before it holds the clock (inc DS:07D3): the clock runs in the wait
    private void CannotView(int member) => ShowMessage(_owner.CannotView(_battle!.Battle[member].Member?.Name ?? ""), 0x96);

    /// <summary>F1..F5 or a click on a portrait: the member's character sheet over the battle (only for an able member still on the field).</summary>
    // orig: fight-character-sheet R022+0x0000 (1EF8:0000) draft
    // C: CannotView is R022+0x04E4's message (the "You can't view … at this time." message)
    private void CharacterSheet(int member)
    {
        var tb = _battle!;
        var c = tb.Battle[member];
        if (c.Status == CharacterStatus.Able && c.Level == BattleEnd.LeftThroughExit || c.Status == CharacterStatus.Unconscious)
        {
            CannotView(member);
            return;
        }
        if (c.Status != CharacterStatus.Able || _owner.CharacterSheet is not { } sheet)
        {
            return;
        }
        var saved = _ui.Canvas.SaveScreen();
        // orig: R022+0x0066: inc DS:07D3 (the combat clock held) before the sheet, R022+0x03C6: dec DS:07D3 at its end
        var opened = _ui.Clock.Milliseconds;
        // orig: R022+0x0167: DS:07D8 = 0 before the sheet (its own palette then); R022+0x0240: DS:07D8 = 1 after the battle's palette is back
        _ui.RunCycles(false);
        _ui.PaletteSet();
        tb.MembersFromCombat();
        _ui.PointerVisible = true;
        // orig: R022+0x007A..0x009D: the Orders, Attack and Party titles (DS:9429, DS:9433, DS:943D) and Change Marching Order (DS:9893,
        //   Alt-O) are 2 (greyed, refused) while the sheet is up, put back at R022+0x02A2..0x02B7; tests: HotKeyFlagsTests
        var menus = GreyMenusForAScreen();
        try
        {
            sheet(member);
        }
        finally
        {
            menus();
        }
        // C: a click on the member's own row closes the sheet on its press (R031+0x0635 to R031+0x08E1, no wait for the button); R022 then
        // reloads the battle's pictures and level (R022+0x01EA..0x03BE) before it reads the release count, so the closing click's release
        // falls inside the reload and is taken and forgotten at R022+0x03BE (lockstep session orders-sheetclick: the original is back in
        // the battle, R014+0x3198 does not open the sheet again). The reload takes no time here: the button is waited out in its place.
        // DIVERGENCE: a button held longer than the original's reload would give the battle a release (the sheet again); the reload's time
        //   is the machine's (disk reads of the battle's pictures and level, R022+0x01EA..0x03BE), not a value of the EXE: here the reload
        //   always outlasts the press (an ordinary click on a PC of the time), so a long press does not reopen the sheet.
        _ui.WaitForButtonRelease();
        // orig: R022+0x01EA after the sheet: the releases made on it taken from the driver and forgotten (DS:C62A = 0)
        _ui.TakeLeftReleases();
        _releases = 0;
        _ui.PointerVisible = false;
        tb.MembersBackToCombat();
        // orig: R022+0x01D0..0x01D3: party-panel-row-set(0x100) after the sheet: every row's letter as the state is now
        RowLetterDrawn(-1);
        _ui.Canvas.Restore(saved);
        SetPalette(tb.Field.Terrain.BackgroundPalette);
        _paletteApplied.Clear();
        _ui.RunCycles(true);
        // orig: R022+0x03BE at the end: the release count read once more (DS:C62A untouched)
        _ui.TakeLeftReleases();
        HeldClockTime(_ui.Clock.Milliseconds - opened);
        if (OneTickPerPass && SheetCallbacks is { Count: > 0 } measured)
        {
            var (during, after) = measured.Dequeue();
            CountCallbacks(during);
            if (tb.PauseByte != 0)
            {
                // a running battle's callbacks after the sheet raise the clock: the clock jumps list them
                _afterSheetCallbacks += after;
            }
        }
    }

    /// <summary>S: "Do you really want to surrender?" in real combat; yes ends the battle (or, with members gone, only for those left).</summary>
    // orig: yield-action R022+0x03E4 (1EF8:03E4) matched
    private void Surrender()
    {
        var tb = _battle!;
        if (!tb.Battle.RealCombat)
        {
            return;
        }
        // C: yes-no-prompt (root 0000:2522) hides the driver's pointer (the battle's shape) while the question waits: the drawn one comes off
        ErasePointer();
        if (new MessageBox(_ui).Ask(_owner.SurrenderQuestion))
        {
            tb.Surrender();
        }
    }

    /// <summary>E: the enemy information: the portraits, names, armour and weapons (with skill words below expert) of up to two kinds.</summary>
    // orig: enemy-details R021+0x02CE (1EF8:02CE) and R021+0x03FE (1EF8:03FE) matched
    private void EnemyInfo()
    {
        var tb = _battle!;
        var kinds = tb.EnemyInfoKinds();
        if (kinds.Count == 0)
        {
            return;
        }
        var canvas = _ui.Canvas;
        ErasePointer();
        var panel = canvas.Save(0, 0, PartyPanel.Width, Canvas.Height);
        var common = _assets.Common;
        for (var y = 0; y < 0xC6; y += 0x0B)
        {
            // DS:A628: the panel's background piece of COMMONSP.IMG.
            SpriteDrawing.Draw(canvas, common.ColumnBackground, 3, y, new Clip(0, 0, PartyPanel.Width, Canvas.Height));
        }
        for (var k = 0; k < kinds.Count; k++)
        {
            DrawEnemyInfo(canvas, kinds[k], kinds.Count == 1 ? 50 : k * 100);
        }
        // orig: R021+0x033D pointer-hide (root 047C:12C3) before the window, R021+0x03A1 pointer-show (root 047C:1293) after it: the driver's
        //   pointer, in the shape the battle's last pass set (pointer-shape-set in R014+0x33A2), stands over the window and the field while it
        //   waits, and moves with the mouse (the driver's); no shape is set meanwhile. checked against the original's screen (vis10 session
        //   tester-einfo-gate: the glove at the mouse in both shots); tests: BattleUiTests
        var shape = PointerShape(_ui.Mouse.X, _ui.Mouse.Y);
        var at = (_ui.Mouse.X, _ui.Mouse.Y);
        var inWindow = _ui.PointerInWindow;
        DrawPointer(canvas, shape);
        _ui.Present();
        // orig: R021+0x03AF matched C: `while (!root 047C:073D() && !(mouse-button-state & 1));` (no menu bar), root 0000:1384, then R021+0x03BB
        // `while (mouse-button-state & 1);`; tests: BattleUiTests
        while (!_ui.KeyWaiting && !_ui.Mouse.Left)
        {
            // (the host's pointer leaving or entering the window also redraws: host enhancement IInput.PointerInWindow)
            if ((_ui.Mouse.X, _ui.Mouse.Y) != at || _ui.PointerInWindow != inWindow)
            {
                ErasePointer();
                at = (_ui.Mouse.X, _ui.Mouse.Y);
                inWindow = _ui.PointerInWindow;
                DrawPointer(canvas, shape);
            }
            _ui.Present();
            _ui.Idle();
        }
        _ui.FlushKeys();
        _ui.WaitForButtonRelease();
        // C: the original leaves its pointer on (the next pass draws it again); here the drawn one comes off before the panel is put back
        ErasePointer();
        canvas.Restore(panel);
        // orig: R021+0x03E3..0x03F6: party-panel-frame-set(0x100), party-panel-row-set(0x100) as the screen closes
        RowChanged(-1);
    }

    private void DrawEnemyInfo(Canvas canvas, int m, int x)
    {
        var tb = _battle!;
        var c = tb.Battle[m];
        // orig: R021+0x03FE draws in the side window (DS:0104 -> DS:00F2), whose font is slot 1 (font 0 of FONTS.FNT, 6 pixels high; no code
        //   writes the record's font word +0x10): the measure root 0265:084E and text-paint root 0265:006E take it. checked against the DS
        //   bytes and the original's screen (vis10 session tester-einfo-gate); tests: BattleUiTests
        var small = _ui.Text;
        var code = tb.SlotCodes[c.AnimationSlot] + tb.SlotWeapons[c.AnimationSlot] + "st.pic";
        if (code[0] > 0x60)
        {
            code = char.ToUpperInvariant(code[0]) + code[1..];
        }
        if (_assets.EnemyPortrait(code) is { } pic)
        {
            canvas.Draw(pic.Image, 0x0E, x, transparent: true);
        }
        var y = x + 0x20;
        var name = tb.Field.Forces.Kinds[c.Level < 0 ? 0 : c.Level].ElementAtOrDefault(tb.EnemyKindIndex(c.AnimationSlot))?.Name ?? "";
        while (name.Length > 0 && small.Measure(name) > 0x36)
        {
            name = name[..^1];
        }
        small.Draw(canvas, (0x36 - small.Measure(name)) / 2 + 3, y, name, 0x0E);
        y += 8;
        var expert = _state.Difficulty == Difficulty.Expert;
        string Word(int v) => _assets.Executable.String(QualityWordAt(v));
        if (code[0] == 'E')
        {
            var plus = (c.Protection & 0x7F) > 0 ? "\x8e+\x8f" : "";
            small.Draw(canvas, 4, y, TypeName(0x01F7, c.ArmourType[0]) + plus, 0x0F);
            y += 8;
            small.Draw(canvas, 4, y, TypeName(0x01F7, c.ArmourType[1]) + plus, 0x0F);
            y += 8;
            if (c.ShieldType != -1)
            {
                small.Draw(canvas, 4, y, TypeName(0x01CF, c.ShieldType), 0x0F);
                y += 8;
            }
            var t = c.DefaultWeapon == -1 ? 9 : c.DefaultWeapon;
            var bonus = c.WeaponBonus != 0 ? "\x8e+\x8f" : "";
            if (t <= 0x21)
            {
                small.Draw(canvas, 4, y, TypeName(0x0277, t) + bonus, 0x0F);
                y += 8;
                if (!expert)
                {
                    small.Draw(canvas, 4, --y, " - " + Word(Math.Clamp((int)c.Skills[0], 0, 100) / 20), 0x0F);
                    y += 8;
                }
            }
            t = c.MissileWeapon != -1 ? c.MissileWeapon : c.EnemyClass == 2 ? 0x22 : -1;
            if (t != -1)
            {
                small.Draw(canvas, 4, y, TypeName(0x0277, t) + bonus, 0x0F);
                y += 8;
                if (!expert)
                {
                    small.Draw(canvas, 4, y - 1, " - " + Word(Math.Clamp((int)c.Skills[5], 0, 100) / 20), 0x0F);
                }
            }
            return;
        }
        small.Draw(canvas, 4, y, "Natrl Armr", 0x0F);
        y += 8;
        if (!expert)
        {
            small.Draw(canvas, 4, --y, " - " + Word(Math.Clamp(c.WeaponClass & 0x0F, 0, 6)), 0x0F);
            y += 8;
        }
        small.Draw(canvas, 4, y, "Natrl Weapn", 0x0F);
        y += 8;
        if (!expert)
        {
            small.Draw(canvas, 4, y - 1, " - " + Word(Math.Clamp((int)c.Skills[0], 0, 100) / 20), 0x0F);
        }
    }

    /// <summary>grade-words (DS:4FCE): "very poor" … "superb".</summary>
    private int QualityWordAt(int k)
    {
        var table = _assets.Executable.DataSegment(0x4FCE, 14);
        k = Math.Clamp(k, 0, 6);
        return table[2 * k] | table[2 * k + 1] << 8;
    }

    /// <summary>The short name of a weapon, armour or shield type: far pointer table at 286B:base + 4 × type (one table, three bases).</summary>
    // orig: R136+0x7ED7 (weapons), R136+0x7E57 (armour), R136+0x7E2F (shields): the same far pointer table seen from three bases
    private string TypeName(int tableBase, int type)
    {
        var exe = _assets.Executable;
        var at = tableBase + 4 * type;
        if (at < 0 || at > 0x400)
        {
            return "";
        }
        var p = exe.Far(0x286B, at, 4);
        int off = p[0] | p[1] << 8, seg = p[2] | p[3] << 8;
        if (seg != 0x286B)
        {
            return "";
        }
        var bytes = exe.Far(seg, off, 40);
        var end = bytes.IndexOf((byte)0);
        return Darklands.Core.GameTextCodec.Decode(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>Change Marching Order (Party menu, Alt-O): the members are picked in order with F1..F5 or clicks.</summary>
    // orig: R022+0x051C (1EF8:051C) draft through MarchingOrderChoice (the one core of the battle and the shell, R022+0x07AA draft (bytes
    // match)); the battle's loop R022+0x0655..0x0682, checked against the asm: the keys are read raw (`if (root 047C:073D()) switch
    // (root 047C:0744())`: exactly 0x3B00..0x3F00 pick members 0..4, whatever their state); else a release from the driver's count
    // (DS:C62A / DS:C4FE): over the panel the row's member if able and still marked, in the view (DS:907A == 1, the battle) every marked
    // member hot spot under it, each taken in turn; tests: BattleUiTests, MarchingOrderTests
    // C: a key picks the member even when absent or already picked (R022+0x07AA does not check); several hot spots under one release are
    // all taken, so more than one member can be picked for one ordinal; a sixth entry is the length's own byte (MarchingOrderChoice).
    // DIVERGENCE: the prompt ("Select first" ...) is the battle screen's message line, not a text box over the card window (MarchingOrderScreen
    // draws the box outside a battle).
    private void ChooseMarchingOrder()
    {
        var tb = _battle!;
        var lengthAt = Darklands.Core.Rules.MarchingOrderChoice.LengthAt;
        var order = new byte[lengthAt + 1];
        tb.StoreMarchingOrder(order);
        var choice = new Darklands.Core.Rules.MarchingOrderChoice(order, i => tb.Battle[i].IsAble);
        for (var k = 0; k < choice.Prompts; k++)
        {
            _message = _owner.SelectOrdinal(k);
            _messageUntil = long.MaxValue;
            Draw();
            var more = true;
            do
            {
                _ui.Present();
                if (_ui.TryReadKey(out var key))
                {
                    if (key is Keys.F1 or Keys.F2 or Keys.F3 or Keys.F4 or Keys.F5)
                    {
                        choice.Take((key - Keys.F1) >> 8);
                        more = false;
                    }
                    continue;
                }
                _ui.Idle();
                TakeReleases();
                if (_releases > 0)
                {
                    var (x, y) = (_releaseX, _releaseY);
                    _releases = 0;
                    if (x < BattlefieldView.Left)
                    {
                        var i = y / PartyPanel.RowHeight;
                        if (choice.CanClick(i, tb.Battle[i].IsAble))
                        {
                            choice.Take(i);
                            more = false;
                        }
                    }
                    else
                    {
                        for (var j = _hotSpots.Count - 1; j >= 0; j--)
                        {
                            var h = _hotSpots[j];
                            if (h.Who < Combatant.PartySlots && h.Left <= x && h.Right >= x && h.Top <= y && h.Bottom >= y && choice.Selected[h.Who])
                            {
                                choice.Take(h.Who);
                                more = false;
                            }
                        }
                    }
                }
            }
            while (more);
        }
        choice.Finish();
        _message = null;
        tb.SetMarchingOrder(Darklands.Core.Rules.MarchingOrderChoice.Entries(order));
    }
}
