using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Simulation;

/// <summary>
/// The barter screen's model (seam (a)2): the shop, who barters, the day's stock and its listing, each member's list in use, count
/// mirror, pack top and row, the shop list's one top and row, and the rules of the keys (R032, unchanged). The screen draws from it.
/// The prices are not kept: each drawn line prices its item again, as the original's drawing does (a draw of the generator per line),
/// so the view asks <see cref="SellPrice"/> / <see cref="BuyPrice"/> exactly where it draws a line.
/// </summary>
// orig: barter overlay R032: entry R032+0x0000, keys R032+0x0F60, close R032+0x076C
// C: on entry the five members' modes are set to buying, their counts, tops and cursors and the shared shop top/cursor to 0 (the field initialisers).
public sealed class BarterSession(GameState state, int shop, string? name, long categories, int[] listing)
{
    // Per member, as in the original: buying or selling, the stack count, the pack list's top and cursor.
    private readonly bool[] _buying = [true, true, true, true, true];
    private readonly int[] _packCount = new int[5];
    private readonly int[] _packTop = new int[5];
    private readonly int[] _packRow = new int[5];
    private readonly int _leaderBefore = state.Party.LeaderIndex;

    private Party Party => state.Party;

    /// <summary>The shop, opened with the screen (trade-screen-entry R032+0x0000).</summary>
    public Shop Shop { get; } = Trading.Open(state, shop, name, categories);

    /// <summary>The member bartering (the leader at the start).</summary>
    public int Member { get; private set; } = state.Party.LeaderIndex;

    /// <summary>The day's stock as the first drawing listed it.</summary>
    public ShopStock Stock { get; private set; } = new([], []);

    public bool Buying => _buying[Member];
    public int PackTop => _packTop[Member];
    public int PackRow => _packRow[Member];

    /// <summary>The shop list's top and cursor (one for everybody).</summary>
    public int ShopTop { get; private set; }

    public int ShopRow { get; private set; }

    /// <summary>The end: the leader as it was.</summary>
    // orig: trade-screen-close R032+0x076C: leader = DS:884C
    public void End() => GameCommands.Lead(state, _leaderBefore);

    /// <summary>The day's stock listed (it seeds the generator), where the first drawing lists it, and copied into DS:86A2.</summary>
    // orig: trade-screen-paint R032+0x078E, the stock loop (Trading.Stock); R032+0x0ACD: DS:86A2[DS:8850] = k for every item for sale,
    //   the entries past the list's end are left as they were
    public ShopStock ListStock()
    {
        Stock = Trading.Stock(state, Shop);
        for (var i = 0; i < Stock.Codes.Count && i < listing.Length; i++)
        {
            listing[i] = Stock.Codes[i];
        }
        return Stock;
    }

    /// <summary>What the shop pays for one of <paramref name="stack"/>: a draw of the generator, priced where a pack line is drawn.</summary>
    public int SellPrice(ItemStack stack) => Trading.SellPrice(state, stack.Code & 0xFFF, stack.Quality);

    /// <summary>The shop's price of <paramref name="code"/>, priced where a shop line is drawn.</summary>
    public int BuyPrice(int code) => Trading.BuyPrice(state, Shop, code);

    /// <summary>The quality the shop offers of <paramref name="code"/>.</summary>
    public int OfferedQuality(int code) => Trading.OfferedQuality(state, Shop, code);

    /// <summary>The count mirror of the member shown, set where the original's drawing sets it.</summary>
    public void PackShown(int count) => _packCount[Member] = count;

    public void ChoosePackRow(int row)
    {
        _buying[Member] = false;
        _packRow[Member] = row;
    }

    public void ChooseShopRow(int row)
    {
        _buying[Member] = true;
        ShopRow = row;
    }

    public void UseList(bool buying) => _buying[Member] = buying;

    /// <summary>P: the lit item bought for the member; false when nothing is done.</summary>
    // orig: trade-screen-keys R032+0x0F60 case P; the rule is Trading.Buy
    public bool Purchase()
    {
        // C: only the list in use (DS:8842[who]) and the count DS:885C != 0 are tested; the code is DS:86A2[top + row].
        if (!_buying[Member] || Stock.Codes.Count == 0)
        {
            return false;
        }
        var index = ShopTop + ShopRow;
        // BUG: a click lights any of the four shop rows (R032+0x0000: DS:8832 = (y - 0x55) >> 3), also one past a shorter list's end;
        //   P then buys DS:86A2[row], the code an earlier, longer listing of this run of the game left there (item 0 before any),
        //   at this shop's price and quality (local/batch/c2stale/barter_probe.py: with 1 item listed and row 3, R032+0x0F60 prices,
        //   rates and adds DS:86A2[3]).
        var code = index < Stock.Codes.Count ? Stock.Codes[index] : listing[index];
        var count = Trading.Buy(state, Shop, Member, code);
        if (count < 0)
        {
            return false;
        }
        _packCount[Member] = count;
        return true;
    }

    /// <summary>S: one of the lit stack sold; false when nothing is done.</summary>
    // orig: trade-screen-keys R032+0x0F60 case S; the rule is Trading.Sell
    public bool Sell()
    {
        var index = _packRow[Member] + _packTop[Member];
        // C: only the list in use and the stack count DS:8838 != 0 are tested: a lit row past the pack's end sells its stale entry (Trading.Sell).
        if (_buying[Member] || _packCount[Member] == 0)
        {
            return false;
        }
        var count = Trading.Sell(state, Member, index);
        if (count < 0)
        {
            return false;
        }
        _packCount[Member] = count;
        if (index >= count)
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

    /// <summary>D: one item of the <em>leader's</em> stack at the bartering member's lit row thrown away; false when the shop list is in use.</summary>
    // orig: trade-screen-keys R032+0x0F60 case D (0x2044/0x2064): item-take-single-overlay-entry(leader, row + top), the count stored for the bartering member
    public bool DropFromLeader()
    {
        var index = _packRow[Member] + _packTop[Member];
        // C: no count test: past the end of the leader's pack the stale entry there loses an item (Inventory.TakeAt).
        if (_buying[Member])
        {
            return false;
        }
        _packCount[Member] = Inventory.TakeAt(state, Party.LeaderIndex, index);
        return true;
    }

    /// <summary>B: the next present member barters.</summary>
    // orig: trade-screen-keys R032+0x0F60 case B (0x3042/0x3062)
    public void NextMember()
    {
        var member = Member;
        do
        {
            member = (member + 1) % 5;
        }
        // C: (DS:884E + 1) % 5 until party[].present: loops forever if nobody is present (impossible while the party exists).
        while (Party.OccupiedSlot(member) is null);
        Member = member;
    }

    // orig: trade-screen-keys R032+0x0F60 case 0x4800
    public ListMove MoveUp()
    {
        if (_buying[Member])
        {
            ShopRow--;
        }
        else
        {
            _packRow[Member]--;
        }
        if (_buying[Member] && ShopRow < 0 && ShopTop > 0)
        {
            ShopRow++;
            ShopTop--;
            return ListMove.UpperScrolled;
        }
        if (!_buying[Member] && _packRow[Member] < 0 && _packTop[Member] > 0)
        {
            _packRow[Member]++;
            _packTop[Member]--;
            return ListMove.LowerScrolled;
        }
        ShopRow = Math.Max(ShopRow, 0);
        _packRow[Member] = Math.Max(_packRow[Member], 0);
        return ListMove.LightOnly;
    }

    // orig: trade-screen-keys R032+0x0F60 case 0x5000
    // C: the shop cursor is kept inside the list (one step back) but the pack cursor only inside the four rows.
    public ListMove MoveDown()
    {
        if (_buying[Member])
        {
            ShopRow++;
        }
        else
        {
            _packRow[Member]++;
        }
        if (_buying[Member] && ShopRow >= 4 && ShopTop + ShopRow < Stock.Codes.Count)
        {
            ShopRow--;
            ShopTop++;
            return ListMove.UpperScrolled;
        }
        if (!_buying[Member] && _packRow[Member] >= 4 && _packRow[Member] + _packTop[Member] < _packCount[Member])
        {
            _packRow[Member]--;
            _packTop[Member]++;
            return ListMove.LowerScrolled;
        }
        if (ShopRow >= Stock.Codes.Count)
        {
            ShopRow--;
        }
        ShopRow = Math.Min(ShopRow, 3);
        _packRow[Member] = Math.Min(_packRow[Member], 3);
        return ListMove.LightOnly;
    }

    /// <summary>Ctrl-F1..F5: a present member, an unconscious one too, leads the haggling; false (nothing done) for an empty slot.</summary>
    // orig: trade-screen-keys R032+0x0F60 cases 0x5E00..0x6200 (then falls through into the list switch); prices on screen are not recomputed
    public bool ChooseLeader(int member)
    {
        if (Party.OccupiedSlot(member) is null)
        {
            return false;
        }
        GameCommands.Lead(state, member);
        return true;
    }

    /// <summary>Left / Right: the other list (always, an empty shop list too).</summary>
    // orig: trade-screen-keys R032+0x0F60 cases 0x4B00/0x4D00
    public ListSwitch SwitchLists()
    {
        _buying[Member] = !_buying[Member];
        return _buying[Member] ? ListSwitch.ToUpper : ListSwitch.ToLower;
    }
}
