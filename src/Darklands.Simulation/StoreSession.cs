using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Simulation;

/// <summary>What P did on the item store screen.</summary>
public enum StorePut
{
    /// <summary>Nothing (the store's list in use, or an empty pack).</summary>
    Nothing,

    /// <summary>Refused: the store cannot hold it (OriginalBehaviour.FixCacheStoreLimit); the screen says so.</summary>
    NoRoom,

    /// <summary>Put into the store.</summary>
    Done,
}

/// <summary>
/// The item store screen's model (seam (a)2): whose pack is shown, the store's working copy, each member's two lists' tops and rows,
/// the list in use and the count mirror the original keeps, and the rules of its keys (R035, unchanged). The screen draws from it.
/// The rows are kept between visits, as the original's statics; the store is read where the original's first drawing reads it.
/// </summary>
// orig: stash-screen R035+0x0000, the keys R035+0x0A46, close R035+0x04BC
public sealed class StoreSession(GameState state)
{
    private readonly int[] _packCount = new int[5];
    private readonly bool[] _usingStore = new bool[5];
    private readonly int[] _packTop = new int[5];
    private readonly int[] _storeTop = new int[5];
    private readonly int[] _packRow = new int[5];
    private readonly int[] _storeRow = new int[5];
    private int _leaderBefore;

    private Party Party => state.Party;

    /// <summary>The member whose pack is shown.</summary>
    public int Member { get; private set; }

    /// <summary>The store's working copy (written back at <see cref="End"/>).</summary>
    public StoreTable Store { get; private set; } = new();

    public bool UsingStore => _usingStore[Member];
    public int PackTop => _packTop[Member];
    public int StoreTop => _storeTop[Member];
    public int PackRow => _packRow[Member];
    public int StoreRow => _storeRow[Member];

    /// <summary>The start: the leader kept to put back, the leader's pack shown.</summary>
    public void Start()
    {
        _leaderBefore = Party.LeaderIndex;
        Member = Party.LeaderIndex;
    }

    /// <summary>The store of <paramref name="place"/> read, where the first drawing reads it (stash-load in R035+0x0510).</summary>
    public void Open(Location place) => Store = state.ItemStores.Open(place.CacheHandle, state.StoreTable);

    /// <summary>The end: the store written back (or forgotten when empty), the leader as it was.</summary>
    // orig: stash-screen-close R035+0x04BC: save (or forget an empty store) and leader = DS:89EE
    public void End(Location place)
    {
        state.ItemStores.Close(place, Store, state.Behaviour, state.Locations);
        GameCommands.Lead(state, _leaderBefore);
    }

    /// <summary>The count mirror of the member shown, set where the original's drawing sets it.</summary>
    public void PackShown(int count) => _packCount[Member] = count;

    public void ChoosePackRow(int row)
    {
        _usingStore[Member] = false;
        _packRow[Member] = row;
    }

    public void ChooseStoreRow(int row)
    {
        _usingStore[Member] = true;
        _storeRow[Member] = row;
    }

    public void UseList(bool store) => _usingStore[Member] = store;

    /// <summary>P: the stack under the member's cursor into the store.</summary>
    // orig: stash-screen-keys R035+0x0A46 case P; the rule is Storing.Put
    public StorePut Put(Location place)
    {
        var index = _packRow[Member] + _packTop[Member];
        // C: only the list in use and `party[who].nitems == 0` are tested: a row past the pack's end puts its stale entry (Storing.Put).
        if (_usingStore[Member] || Party.Slot(Member).Pack.Stacks == 0)
        {
            return StorePut.Nothing;
        }
        // DIVERGENCE: data safety (OriginalBehaviour.FixCacheStoreLimit): a Put the compact CACHE.TMP layout cannot hold is refused here, with a message, the item staying in the pack.
        if (!Storing.CanPut(state, place, Store, Member, index))
        {
            return StorePut.NoRoom;
        }
        _packCount[Member] = Storing.Put(state, Store, Member, index);
        if (index >= _packCount[Member])
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
        return StorePut.Done;
    }

    /// <summary>D: one item of the <em>leader's</em> stack at the member's lit row is thrown away; false when the store's list is in use.</summary>
    // orig: stash-screen-keys R035+0x0A46 case D (0x2044/0x2064): item-take-single-overlay-entry(leader, row + top)
    public bool DropFromLeader()
    {
        var index = _packRow[Member] + _packTop[Member];
        // C: no count test: past the end of the leader's pack the stale entry there loses an item (Inventory.TakeAt).
        if (_usingStore[Member])
        {
            return false;
        }
        _packCount[Member] = Inventory.TakeAt(state, Party.LeaderIndex, index);
        return true;
    }

    /// <summary>G: the store's entry under the cursor to the member; false when nothing is done.</summary>
    // orig: stash-screen-keys R035+0x0A46 case G; the rule is Storing.Get
    public bool Get()
    {
        var index = _storeTop[Member] + _storeRow[Member];
        // C: only the list in use and an empty store are tested: a row past the store's end hands out its stale entry (Storing.Get).
        if (!_usingStore[Member] || Store.Count == 0)
        {
            return false;
        }
        _packCount[Member] = Storing.Get(state, Store, Member, index);
        if (index >= Store.Count)
        {
            if (_storeTop[Member] != 0)
            {
                _storeTop[Member]--;
            }
            else if (_storeRow[Member] != 0)
            {
                _storeRow[Member]--;
            }
        }
        return true;
    }

    /// <summary>C: the next present member (counting up to the party size).</summary>
    // orig: stash-screen-keys R035+0x0A46 case C (0x2E43/0x2E63)
    // C: (DS:89F2 + 1) % party-headcount (not % 5 as on the barter screen).
    public void NextMember()
    {
        var size = Math.Max(1, Party.Slots.Count);
        var member = Member;
        do
        {
            member = (member + 1) % size;
        }
        while (Party.OccupiedSlot(member) is null);
        Member = member;
    }

    // orig: stash-screen-keys R035+0x0A46 case 0x4800
    public ListMove MoveUp()
    {
        if (_usingStore[Member])
        {
            _storeRow[Member]--;
        }
        else
        {
            _packRow[Member]--;
        }
        if (_usingStore[Member] && _storeRow[Member] < 0 && _storeTop[Member] > 0)
        {
            _storeRow[Member]++;
            _storeTop[Member]--;
            return ListMove.UpperScrolled;
        }
        if (!_usingStore[Member] && _packRow[Member] < 0 && _packTop[Member] > 0)
        {
            _packRow[Member]++;
            _packTop[Member]--;
            return ListMove.LowerScrolled;
        }
        _storeRow[Member] = Math.Max(_storeRow[Member], 0);
        _packRow[Member] = Math.Max(_packRow[Member], 0);
        return ListMove.LightOnly;
    }

    // orig: stash-screen-keys R035+0x0A46 case 0x5000 (both cursors are kept inside their lists)
    public ListMove MoveDown()
    {
        if (_usingStore[Member])
        {
            _storeRow[Member]++;
        }
        else
        {
            _packRow[Member]++;
        }
        if (_usingStore[Member] && _storeRow[Member] >= 4 && _storeTop[Member] + _storeRow[Member] < Store.Count)
        {
            _storeRow[Member]--;
            _storeTop[Member]++;
            return ListMove.UpperScrolled;
        }
        if (!_usingStore[Member] && _packRow[Member] >= 4 && _packTop[Member] + _packRow[Member] < _packCount[Member])
        {
            _packRow[Member]--;
            _packTop[Member]++;
            return ListMove.LowerScrolled;
        }
        if (_storeRow[Member] >= Store.Count)
        {
            _storeRow[Member]--;
        }
        if (_packCount[Member] <= _packRow[Member])
        {
            _packRow[Member]--;
        }
        _storeRow[Member] = Math.Min(_storeRow[Member], 3);
        _packRow[Member] = Math.Min(_packRow[Member], 3);
        return ListMove.LightOnly;
    }

    // orig: stash-screen-keys R035+0x0A46 cases 0x4B00/0x4D00 (no switch to an empty store)
    public ListSwitch SwitchLists()
    {
        if (_usingStore[Member])
        {
            _usingStore[Member] = false;
            return ListSwitch.ToLower;
        }
        if (Store.Count != 0)
        {
            _usingStore[Member] = true;
            return ListSwitch.ToUpper;
        }
        return ListSwitch.None;
    }
}
