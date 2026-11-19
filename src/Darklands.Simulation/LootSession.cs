using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Rules;

namespace Darklands.Simulation;

/// <summary>How a cursor key changed a list screen's lists (loot, item store: what the view draws again).</summary>
public enum ListMove
{
    /// <summary>The upper list (the pile, the store) scrolled by one.</summary>
    UpperScrolled,

    /// <summary>The member's list scrolled by one.</summary>
    LowerScrolled,

    /// <summary>Only the light moved (or stayed, kept within the rows).</summary>
    LightOnly,
}

/// <summary>Which list a list screen's Left / Right turned to.</summary>
public enum ListSwitch
{
    /// <summary>Nothing (the pile is empty).</summary>
    None,

    /// <summary>The member's list.</summary>
    ToLower,

    /// <summary>The upper list (the pile, the store).</summary>
    ToUpper,
}

/// <summary>
/// The loot screen's model (seam (a)2): who is sharing, the found cash, each member's two lists' tops and rows, the list in use and
/// the count mirror the original keeps (item-count-local), and the rules of its keys. The screen draws from it and sends it the
/// player's keys; the choices of the rules are the original's (R040), unchanged.
/// </summary>
// orig: loot-split-entry R040+0x0000 and R040+0x01C8, the keys R040+0x0C22 (the statics of R040 kept per member)
public sealed class LootSession(GameState state, IList<ItemStack> pile)
{
    private readonly int[] _packCount = new int[5];
    private readonly bool[] _usingPile = [true, true, true, true, true];
    private readonly int[] _packTop = new int[5];
    private readonly int[] _pileTop = new int[5];
    private readonly int[] _packRow = new int[5];
    private readonly int[] _pileRow = new int[5];
    private int _leaderBefore;

    private Party Party => state.Party;

    /// <summary>The member whose pack is shown.</summary>
    public int Member { get; private set; }

    /// <summary>The cash the party found, taken at the start.</summary>
    public Money Cash { get; private set; }

    /// <summary>The pile of found items (the caller's list, changed in place).</summary>
    public IList<ItemStack> Pile => pile;

    public bool UsingPile => _usingPile[Member];
    public int PackTop => _packTop[Member];
    public int PileTop => _pileTop[Member];
    public int PackRow => _packRow[Member];
    public int PileRow => _pileRow[Member];

    /// <summary>The start: a fallen leader replaced, the found items prepared, the leader kept to put back, the cash taken.</summary>
    // orig: R040+0x0000 / R040+0x01C8 before the loop
    public void Start(ref Money found)
    {
        LootSharing.ReplaceFallenLeader(state);
        LootSharing.PrepareFoundItems(pile);
        _leaderBefore = Party.LeaderIndex;
        Member = Party.LeaderIndex;
        Cash = LootSharing.TakeCash(state, ref found);
    }

    /// <summary>The end: the leader as it was.</summary>
    // orig: R040+0x06C8: leader = DS:8DBA
    public void End() => GameCommands.Lead(state, _leaderBefore);

    /// <summary>The count mirror of the member shown, set where the original's drawing sets it (item-count-local[who]).</summary>
    public void PackShown(int count) => _packCount[Member] = count;

    /// <summary>A row of the member's list chosen with the mouse.</summary>
    public void ChoosePackRow(int row)
    {
        _usingPile[Member] = false;
        _packRow[Member] = row;
    }

    /// <summary>A row of the pile chosen with the mouse.</summary>
    public void ChoosePileRow(int row)
    {
        _usingPile[Member] = true;
        _pileRow[Member] = row;
    }

    /// <summary>An arrow of a list held: that list is the one in use.</summary>
    public void UseList(bool pile) => _usingPile[Member] = pile;

    /// <summary>P: puts the stack under the member's cursor on the pile; false when nothing is done.</summary>
    // orig: R040+0x0C22 case P; the rule is LootSharing.Put
    public bool Put()
    {
        var index = _packTop[Member] + _packRow[Member];
        // C: only the list in use and item-count-local[who] != 0 are tested: a row past the pack's end puts its stale entry (LootSharing.Put).
        if (_usingPile[Member] || _packCount[Member] == 0)
        {
            return false;
        }
        _packCount[Member] = LootSharing.Put(state, pile, Member, index);
        // C: "top + row + 1 >= count" (checked against asm 0D19..0D26): unlike the other screens the cursor also steps back when the stack is still there on the last row.
        if (index + 1 >= _packCount[Member])
        {
            if (_packTop[Member] != 0)
            {
                _packTop[Member]--;
            }
            else if (_packRow[Member] != 0)
            {
                _packRow[Member]--;
            }
        }
        return true;
    }

    /// <summary>G: gives the pile's stack under the cursor to the member; false when nothing is done.</summary>
    // orig: R040+0x0C22 case G; the rule is LootSharing.Get
    public bool Get()
    {
        var index = _pileTop[Member] + _pileRow[Member];
        // DIVERGENCE: rows past the pile's end are ignored (see LootSharing.Get).
        // C: the original's G gives the entry there, a leftover of the DOS block the battle hands over: undefined memory (LootSharing.Get).
        if (!_usingPile[Member] || pile.Count == 0 || index >= pile.Count)
        {
            return false;
        }
        _packCount[Member] = LootSharing.Get(state, pile, Member, index);
        // C: "top + row + 1 > count" (checked against asm 0EA7..0EB4).
        if (index + 1 > pile.Count)
        {
            if (_pileTop[Member] != 0)
            {
                _pileTop[Member]--;
            }
            else if (_pileRow[Member] != 0)
            {
                _pileRow[Member]--;
            }
        }
        return true;
    }

    /// <summary>D: the next member in the party who is neither dead nor missing (<paramref name="cannotView"/>: a slot gone from the battlefield, told and skipped).</summary>
    // orig: R040+0x0C22 case D (0x2044/0x2064)
    // C: (DS:8DC4 + 1) % 5, skipping slots ≥ party-headcount and status 0 or 3 (checked against asm 0D9E..0DD9).
    // orig: R040+0x0DB4..0x0DC8: a slot whose combat floor test (fighter-floor-level, DS:A6B4, read as words: BattleScreens.LootSkipsMember) says
    //   −99 (gone through an exit) is skipped after thunk 09C0:1D51 = R022+0x04E4 ("You can't view ... at this time.") is called for it,
    //   before the status test; tests: SheetCannotViewTests
    // DIVERGENCE: the original loops forever when no member qualifies; here the search stops after five slots.
    public void NextMember(Func<int, bool>? cannotView)
    {
        var member = Member;
        for (var tries = 0; tries < Party.MaxMembers; tries++)
        {
            member = (member + 1) % Party.MaxMembers;
            if (member >= Party.Slots.Count || cannotView?.Invoke(member) == true)
            {
                continue;
            }
            if (Party.Slot(member).Status is not (CharacterStatus.Empty or CharacterStatus.Dead))
            {
                break;
            }
        }
        Member = member;
    }

    /// <summary>Up in the list in use.</summary>
    // orig: R040+0x0C22 case 0x4800
    public ListMove MoveUp()
    {
        if (_usingPile[Member])
        {
            _pileRow[Member]--;
        }
        else
        {
            _packRow[Member]--;
        }
        if (_usingPile[Member] && _pileRow[Member] < 0 && _pileTop[Member] > 0)
        {
            _pileRow[Member]++;
            _pileTop[Member]--;
            return ListMove.UpperScrolled;
        }
        if (!_usingPile[Member] && _packRow[Member] < 0 && _packTop[Member] > 0)
        {
            _packRow[Member]++;
            _packTop[Member]--;
            return ListMove.LowerScrolled;
        }
        _pileRow[Member] = Math.Max(_pileRow[Member], 0);
        _packRow[Member] = Math.Max(_packRow[Member], 0);
        return ListMove.LightOnly;
    }

    /// <summary>Down in the list in use: unlike the other two screens the cursor is only kept within the four rows, not within the list.</summary>
    // orig: R040+0x0C22 case 0x5000
    public ListMove MoveDown()
    {
        if (_usingPile[Member])
        {
            _pileRow[Member]++;
        }
        else
        {
            _packRow[Member]++;
        }
        if (_usingPile[Member] && _pileRow[Member] >= 4 && _pileTop[Member] + _pileRow[Member] < pile.Count)
        {
            _pileRow[Member]--;
            _pileTop[Member]++;
            return ListMove.UpperScrolled;
        }
        if (!_usingPile[Member] && _packRow[Member] >= 4 && _packTop[Member] + _packRow[Member] < _packCount[Member])
        {
            _packRow[Member]--;
            _packTop[Member]++;
            return ListMove.LowerScrolled;
        }
        _pileRow[Member] = Math.Min(_pileRow[Member], 3);
        _packRow[Member] = Math.Min(_packRow[Member], 3);
        return ListMove.LightOnly;
    }

    /// <summary>Left / Right: the other list (never to an empty pile).</summary>
    // orig: R040+0x0C22 cases 0x4B00/0x4D00
    public ListSwitch SwitchLists()
    {
        if (_usingPile[Member])
        {
            _usingPile[Member] = false;
            return ListSwitch.ToLower;
        }
        if (pile.Count != 0)
        {
            _usingPile[Member] = true;
            return ListSwitch.ToUpper;
        }
        return ListSwitch.None;
    }
}
