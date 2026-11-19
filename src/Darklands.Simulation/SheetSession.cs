using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Simulation;

/// <summary>What a cursor key or a scroll arrow did in an open list of the character sheet.</summary>
public enum SheetStep
{
    /// <summary>Nothing (the list's end, or its top).</summary>
    None,

    /// <summary>The list scrolled (or is drawn again).</summary>
    Scrolled,

    /// <summary>The light is to move one line: the view recolours the old line, <see cref="SheetSession.Select"/>s, recolours the new one.</summary>
    Select,

    /// <summary>The light moved already (the key's own recolouring follows).</summary>
    Moved,
}

/// <summary>
/// The character sheet's model (seam (a)2): the member shown, the box under the cursor frame, whether a box is open, each list box's
/// first line and lit line per member (kept between visits, as the original's table) and the list's line count, and the rules of
/// the sheet's keys and drags (R031, unchanged). The screen draws from it.
/// </summary>
// orig: member-sheet-screen R031+0x0124, the key handler R031+0x10AE, the list click R031+0x1E42
public sealed class SheetSession
{
    /// <summary>The first visible line and the selected line of each list box, per member.</summary>
    private readonly int[,] _top = new int[19, Party.MaxMembers];
    private readonly int[,] _selected = new int[19, Party.MaxMembers];

    private GameState _state = null!;

    /// <summary>The member shown.</summary>
    public int Who { get; private set; }

    /// <summary>The box under the cursor frame.</summary>
    public int Cursor { get; private set; }

    /// <summary>A box is open (DS:867E).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The line count of the list last drawn (DS:E7A4).</summary>
    public int ListCount { get; private set; }

    public Character Member => _state.Party.Slot(Who);

    /// <summary>The lit line of the box under the cursor, as an index into its list.</summary>
    public int Index => _top[Cursor, Who] + _selected[Cursor, Who];

    public int Top(int box) => _top[box, Who];

    public int Selected(int box) => _selected[box, Who];

    /// <summary>The sheet of <paramref name="who"/> in <paramref name="state"/> (the rows kept).</summary>
    public void Begin(GameState state, int who)
    {
        _state = state;
        Who = who;
    }

    /// <summary>Another member shown, no box open.</summary>
    public void Show(int who)
    {
        Who = who;
        IsOpen = false;
    }

    public void MoveCursor(int box) => Cursor = box;

    public void Opened() => IsOpen = true;

    public void Closed() => IsOpen = false;

    /// <summary>The line count of the list drawn, where the drawing sets it.</summary>
    public void ListShown(int count) => ListCount = count;

    /// <summary>The pack list's rows kept inside the pack before it is drawn.</summary>
    // orig: the pack panel's text (the sheet's panel drawing): top and lit line kept below the pack's count
    public void KeepInPack(int box)
    {
        var count = Member.Pack.Count;
        if (_top[box, Who] >= count)
        {
            _top[box, Who] = Math.Clamp(count - 10, 0, 99);
        }
        if (_selected[box, Who] >= count)
        {
            _selected[box, Who] = count - 1;
        }
    }

    /// <summary>The light one line on (the view recolours around it).</summary>
    public void Select(int step) => _selected[Cursor, Who] += step;

    /// <summary>↑ with a list open.</summary>
    public SheetStep KeyUp()
    {
        if (_selected[Cursor, Who] != 0)
        {
            return SheetStep.Select;
        }
        if (_top[Cursor, Who] == 0)
        {
            return SheetStep.None;
        }
        _top[Cursor, Who]--;
        return SheetStep.Scrolled;
    }

    /// <summary>↓ with a list open (<paramref name="lines"/>: the box's visible lines).</summary>
    public SheetStep KeyDown(int lines)
    {
        _selected[Cursor, Who]++;
        if (_selected[Cursor, Who] >= lines)
        {
            var line = _selected[Cursor, Who];
            _selected[Cursor, Who]--;
            if (_top[Cursor, Who] + line >= ListCount)
            {
                return SheetStep.None;
            }
            _top[Cursor, Who]++;
            return SheetStep.Scrolled;
        }
        if (Index >= ListCount)
        {
            _selected[Cursor, Who]--;
            return SheetStep.None;
        }
        return SheetStep.Moved;
    }

    /// <summary>One step of the scroll's up arrow held: at the first line the list is drawn again (one up when it can).</summary>
    // orig: R031+0x0286, R031+0x036A
    public SheetStep ArrowUp()
    {
        if (_selected[Cursor, Who] != 0)
        {
            return SheetStep.Select;
        }
        if (_top[Cursor, Who] != 0)
        {
            _top[Cursor, Who]--;
        }
        return SheetStep.Scrolled;
    }

    /// <summary>One step of the scroll's down arrow held; None at the list's end (the loop breaks).</summary>
    // orig: R031+0x03E8, R031+0x04FD
    public SheetStep ArrowDown(int lines)
    {
        if (_selected[Cursor, Who] >= lines - 1)
        {
            if (Index >= ListCount)
            {
                return SheetStep.None;
            }
            _top[Cursor, Who]++;
            return SheetStep.Scrolled;
        }
        return Index >= ListCount - 1 ? SheetStep.None : SheetStep.Select;
    }

    public void PageUp() => _top[Cursor, Who] = _top[Cursor, Who] > 14 ? _top[Cursor, Who] - 14 : 0;

    /// <summary>PgDn; false when nothing changes.</summary>
    public bool PageDown()
    {
        if (_top[Cursor, Who] + 14 >= ListCount)
        {
            return false;
        }
        _top[Cursor, Who] += 14;
        if (Index >= ListCount)
        {
            ToEnd();
        }
        return true;
    }

    public void ToEnd()
    {
        _top[Cursor, Who] = ListCount >= 14 ? ListCount - 14 : 0;
        _selected[Cursor, Who] = ListCount >= 14 ? 13 : ListCount - 1;
    }

    public void ToStart()
    {
        _top[Cursor, Who] = 0;
        _selected[Cursor, Who] = 0;
    }

    /// <summary>Whether line <paramref name="line"/> of box <paramref name="box"/> is in its list.</summary>
    public bool HasLine(int box, int line) => line + _top[box, Who] < ListCount;

    /// <summary>A clicked line lit (in the box under the cursor, as the original writes it).</summary>
    // C: R031+0x1E42 writes the lit line of DS:867C's box (the cursor's), the recolouring reads box k's (the same box: k is the cursor's)
    public void SelectLine(int line) => _selected[Cursor, Who] = line;

    /// <summary>Ctrl-F1..F5: an able member within the party's headcount leads; false (nothing done) otherwise.</summary>
    // orig: R031+0x10AE cases 0x5E00..0x6200: `slot < party-headcount` (DS:A67E, signed) and the status Able
    public bool Lead(int member, int headcount)
    {
        if (member >= headcount || _state.Party.Slots[member].Status != CharacterStatus.Able)
        {
            return false;
        }
        GameCommands.Lead(_state, member);
        return true;
    }

    /// <summary>The name box opened a second time: the member shown leads (no test).</summary>
    // orig: R031+0x264C
    public void LeadShown() => GameCommands.Lead(_state, Who);

    /// <summary>D (Shift: the stack) on the lit pack line; false past the pack's end.</summary>
    // orig: R031+0x185E
    public bool Drop(bool stack)
    {
        if (Index >= Member.Pack.Count)
        {
            return false;
        }
        if (stack)
        {
            Inventory.RemoveStackAt(_state, Who, Index);
        }
        else
        {
            Inventory.TakeAt(_state, Who, Index);
        }
        return true;
    }

    /// <summary>A: the lit pack line readied.</summary>
    // C: R031+0x192E / R031+0x19E2 test only the box (flag 0x10) and that it is open: the line need not be a stack, an entry past the pack's
    // end (Pack: what was last there, e.g. the stack just dropped or given) is readied or unreadied as it is.
    // orig: R031+0x5A02 (18E7:11B2) via PackRules.Equip
    public void Equip() => PackRules.Equip(_state, Who, Index);

    /// <summary>U: the lit pack line unreadied.</summary>
    // orig: R031+0x5B38 (18E7:12E8) via Inventory.Unequip
    public void Unequip() => Inventory.Unequip(_state, Who, Index);

    /// <summary>A list line dropped on the equipment box: that pack stack readied when there is one.</summary>
    // C: any list may be dropped on the equipment box: from Formulae or Saints the line number readies that pack stack.
    public void EquipLine(int index)
    {
        if (index < Member.Pack.Count)
        {
            PackRules.Equip(_state, Who, index);
        }
    }

    /// <summary>P on the pack: the lit potion drunk, the rows kept inside the shorter pack; false when it cannot be drunk.</summary>
    // C: R031+0x1B2C: as for A and U no test of the pack's count: a line past the end drinks the entry left there (the count of the
    // pack, a signed char, goes down all the same: item-erase; -1 from an empty pack), and the list count DS:E7A4 follows it signed.
    public bool Drink()
    {
        if (!PotionEffects.CanDrink(_state, Member.Pack[Index].Code))
        {
            return false;
        }
        PotionEffects.DrinkFromPack(_state, Who, Index);
        if (Member.Pack.Stacks < ListCount)
        {
            if (_top[Cursor, Who] != 0)
            {
                _top[Cursor, Who]--;
            }
            else if (_selected[Cursor, Who] != 0)
            {
                _selected[Cursor, Who]--;
            }
            ListCount = Member.Pack.Stacks;
        }
        return true;
    }

    /// <summary>1..5 (or a Ctrl-F's fall-through) with the pack open: the lit line's item (Shift: the stack) given; false when nothing is given.</summary>
    // orig: R031+0x10AE the 1..5 case
    public bool Give(int key, bool stack)
    {
        var to = (key & 7) - 1;
        // DIVERGENCE: Ctrl-F1 makes to −1, which the original reads as the record before member 0; here nothing is given.
        // C: the original reads record −1's status (DS:9BE9) and count (DS:9C53):
        //   DS:9BE9 is the first word of the 20th line of the battle's Throw sub-menu (DS:9A47 + 19 * 0x16, R019+0x0C80, a 22-byte record
        //   per line), 0 (BSS, read so at start-up) until a battle lists 19 or more Throw lines for a member, then the low byte of a
        //   potion-name pointer (DS:114A / DS:114E[]: 0xB0..0x14, never 0 or 5); while it is 0 the original returns as here. After such a
        //   battle the give goes on when DS:9C53, the last sub-menu's text column (MenuBar), is below 0x40 as a signed byte:
        //   hand-over-one / hand-over-stack(who, −1) add the item to the leader, member 0 (give-item-record-form's −1, R031+0x4850), and take it from the
        //   member even when member 0's pack is full (the item is lost). That history (a Throw list of 19 lines, the menu bar's
        //   geometry) is the same not-modelled UI memory as DS:9C53 elsewhere (SaintBook, TacticalBattle.Dungeon): not reproduced.
        if (to < 0)
        {
            return false;
        }
        var target = _state.Party.Slot(to);
        if (target.Status is CharacterStatus.Empty or CharacterStatus.Away || Index >= Member.Pack.Count)
        {
            return false;
        }
        if (stack)
        {
            // orig: R031+0x48CC (hand-over-stack) R031+0x48CC (18E7:007C) via PackRules.GiveStack
            PackRules.GiveStack(_state, Who, (key & 0xF) - 1, Index);
        }
        else
        {
            // orig: R031+0x4850 (hand-over-one) R031+0x4850 (18E7:0000) via PackRules.GiveOne
            PackRules.GiveOne(_state, Who, (key & 0xF) - 1, Index);
        }
        return true;
    }

    /// <summary>A list line dragged onto member <paramref name="to"/>'s row of the panel: given (Shift: the stack); false for an empty or away member.</summary>
    // orig: R031+0x1E42, the drop on the party panel
    public bool GiveTo(int to, int index, bool stack)
    {
        if (_state.Party.Slot(to).Status is CharacterStatus.Empty or CharacterStatus.Away)
        {
            return false;
        }
        if (stack)
        {
            PackRules.GiveStack(_state, Who, to, index);
        }
        else
        {
            PackRules.GiveOne(_state, Who, to, index);
        }
        return true;
    }

    /// <summary>A pack line dragged to another line of the pack: moved there when that line holds a stack; false otherwise.</summary>
    // orig: R031+0x5922 (item-relocate) R031+0x5922 (18E7:10D2) via PackRules.Move
    public bool MovePackLine(int box, int fromLine, int toLine)
    {
        if (Member.Pack.Count <= toLine)
        {
            return false;
        }
        PackRules.Move(_state, Who, _top[box, Who] + fromLine, _top[box, Who] + toLine);
        return true;
    }
}
