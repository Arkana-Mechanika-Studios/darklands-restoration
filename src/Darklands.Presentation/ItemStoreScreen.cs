using System.Globalization;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// The item store at an inn: the items left here above, a member's pack below. G gets the lit
/// item, P puts one of the lit stack, C shows the next member, Esc leaves, arrows move and
/// switch lists, D drops one item. One instance serves the whole game: like the original's,
/// the lists' positions and which list is in use are kept per member between visits.
/// </summary>
// orig: item cache overlay R035 = R035 (linked paragraph 1462, payload = offset) matched (11/11); rules in Storing and ItemStores; checked visually with Darklands.Host.Headless screen:store
// C: the per-member arrays are module statics (DGROUP 37BA..37F5) initialised once and never reset: list in use, tops and cursors persist across visits and places.
public sealed class ItemStoreScreen(BuySellPage page, GameState state, IGameScreens screens)
{
    // C: menu rows give G, P, C, Esc; row 4 (below Leave) gives no key. The L key itself is not handled by cache_key (only Esc leaves).
    private static readonly int[] MenuKeys = [0x2247, 0x1950, 0x2E43, Keys.Escape];

    private const string GetLine = "\u008C G\u00FFet an item from cache";
    private const string PutLine = "\u008C P\u00FFut an item into cache";

    // Per member, kept between visits.
    private readonly int[] _packCount = new int[5];
    private readonly bool[] _usingStore = new bool[5];
    private readonly int[] _packTop = new int[5];
    private readonly int[] _storeTop = new int[5];
    private readonly int[] _packRow = new int[5];
    private readonly int[] _storeRow = new int[5];

    private StoreTable _store = new();
    private int _member;

    private Party Party => state.Party;

    private int Member => _member;

    private Pack PackOf(int member) => Party.Slot(member).Pack;

    /// <summary>Runs the screen at the current place until the player leaves; the store is written back.</summary>
    // orig: cache_screen R035+0x0000 (1462:0000) matched; main loop (its argument DS:89F8 is unused)
    public void Run()
    {
        // DIVERGENCE: in the open country the original indexes locations[-1] (cur_loc −1); the screen does not open here.
        if (state.Here is not { } place)
        {
            return;
        }
        var leaderBefore = Party.LeaderIndex;
        _member = Party.LeaderIndex;
        DrawScreen(place);
        // orig: R035+0x0000 `while (mouse_buttons & 1);` before the loop (the click that opened the screen is waited out)
        page.WaitForRelease();
        var lit = -1;
        try
        {
            while (true)
            {
                // DIVERGENCE: root 0000:0000 is GameUi.Poll's (MenuBar, HotKeys).
                var key = page.KeyAfterTheMouse(MouseStep(ref lit));
                if (key == Keys.Escape)
                {
                    return;
                }
                if (key != 0)
                {
                    HandleKey(key);
                }
            }
        }
        finally
        {
            // orig: cache_close R035+0x04BC (1462:04BC) matched: save (or forget an empty store) and leader = DS:89EE; fonts are host state (not modelled).
            state.ItemStores.Close(place, _store, state.Behaviour);
            Party.LeaderIndex = leaderBefore;
        }
    }

    // ---- input ------------------------------------------------------------------------

    /// <summary>The mouse tests of one pass of the loop, in their order, on the button's state; the key they give (0: none).</summary>
    // orig: R035+0x0000 matched C, the mouse tests in loop order: the menu (BuySellPage.MenuStep); a row of either list lights at
    // once and the button is then waited out; an arrow waits the button out first, then gives Up/Down for its list (the list in
    // use changes without redrawing the menu); the party column opens the sheet, then the button is waited out; tests: BuySellInputTests
    // C: each test reads the button again, so after a wait the later tests see it up.
    private int MouseStep(ref int lit)
    {
        var key = page.MenuStep(ref lit, MenuKeys, 8);
        if (page.HeldRow(BuySellPage.LowerRowAt) is { } packRow)
        {
            _usingStore[Member] = false;
            _packRow[Member] = packRow;
            ShowPacking();
            page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xFA);
            page.WaitForRelease();
        }
        if (page.HeldRow(BuySellPage.UpperRowAt) is { } storeRow)
        {
            _usingStore[Member] = true;
            _storeRow[Member] = storeRow;
            ShowGetting();
            page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xFA);
            page.WaitForRelease();
        }
        if (page.Held(BuySellPage.OnLowerUpArrow))
        {
            page.WaitForRelease();
            key = Keys.Up;
            _usingStore[Member] = false;
        }
        if (page.Held(BuySellPage.OnLowerDownArrow))
        {
            page.WaitForRelease();
            key = Keys.Down;
            _usingStore[Member] = false;
        }
        if (page.Held(BuySellPage.OnUpperUpArrow))
        {
            page.WaitForRelease();
            key = Keys.Up;
            _usingStore[Member] = true;
        }
        if (page.Held(BuySellPage.OnUpperDownArrow))
        {
            page.WaitForRelease();
            key = Keys.Down;
            _usingStore[Member] = true;
        }
        // DIVERGENCE: the original shows sheets while member_sheet_vec returns another member (row != -1); IGameScreens.CharacterSheet returns nothing, so one sheet is shown.
        if (page.HeldRow(BuySellPage.MemberAt) is { } member)
        {
            screens.CharacterSheet(member);
            Redraw();
            page.WaitForRelease();
        }
        return key;
    }

    // orig: cache_key R035+0x0A46 (1462:0A46) matched; every case kept (Alt-Q, P/p, D/d, G/g, C/c, Up, Left/Right, Down); other keys do nothing
    private void HandleKey(int key)
    {
        // C: case 0x1000 (Alt-Q): cache_close then quit_to_dos (the finally block writes the store back).
        // DORMANT: with a GameUi.GameKey hook installed, HotKeys takes Alt-Q first; reached only without the hook.
        if (key == Keys.AltQ)
        {
            throw new QuitGameException();
        }
        if (BuySellPage.Is(key, 'P'))
        {
            Put();
        }
        else if (BuySellPage.Is(key, 'D'))
        {
            DropFromLeader();
        }
        else if (BuySellPage.Is(key, 'G'))
        {
            Get();
        }
        else if (BuySellPage.Is(key, 'C'))
        {
            ShowTheNextMember();
        }
        else if (key == Keys.Up)
        {
            MoveUp();
        }
        else if (key is Keys.Left or Keys.Right)
        {
            SwitchLists();
        }
        else if (key == Keys.Down)
        {
            MoveDown();
        }
    }

    // orig: cache_key R035+0x0A46 matched, case P; the rule is Storing.Put
    private void Put()
    {
        var index = _packRow[Member] + _packTop[Member];
        // C: only the list in use and `party[who].nitems == 0` are tested: a row past the pack's end puts its stale entry (Storing.Put).
        if (_usingStore[Member] || PackOf(Member).Stacks == 0)
        {
            return;
        }
        _packCount[Member] = Storing.Put(state, _store, Member, index);
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
        DrawStoreItems();
        DrawPackItems();
        page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
    }

    /// <summary>D: one item of the <em>leader's</em> stack at the member's lit row is thrown away.</summary>
    // orig: cache_key R035+0x0A46 matched, case D (0x2044/0x2064): item_take_one_vec(leader, row + top)
    private void DropFromLeader()
    {
        var index = _packRow[Member] + _packTop[Member];
        // C: no count test: past the end of the leader's pack the stale entry there loses an item (Inventory.TakeAt).
        if (_usingStore[Member])
        {
            return;
        }
        _packCount[Member] = Core.Rules.Inventory.TakeAt(state, Party.LeaderIndex, index);
        DrawPackItems();
        page.Light(0x50, _packRow[Member] * 8 + 0x1B, 0x5F);
    }

    // orig: cache_key R035+0x0A46 matched, case G; the rule is Storing.Get
    private void Get()
    {
        var index = _storeTop[Member] + _storeRow[Member];
        // C: only the list in use and an empty store are tested: a row past the store's end hands out its stale entry (Storing.Get).
        if (!_usingStore[Member] || _store.Count == 0)
        {
            return;
        }
        _packCount[Member] = Storing.Get(state, _store, Member, index);
        if (index >= _store.Count)
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
        DrawStoreItems();
        DrawPackItems();
        page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0x5F);
    }

    /// <summary>C: the next present member (counting up to the party size).</summary>
    // orig: cache_key R035+0x0A46 matched, case C (0x2E43/0x2E63)
    private void ShowTheNextMember()
    {
        page.DrawParty(Party);
        var size = Math.Max(1, Party.Members.Count);
        do
        {
            _member = (_member + 1) % size;
        }
        // C: (DS:89F2 + 1) % party_size (not % 5 as on the barter screen).
        while (Party.PresentMember(_member) is null);
        Redraw();
        // C: copied from the barter screen: the highlight lands on the Leave line (0x33), not on "Cache another person's items" (0x2B).
        page.Recolor(0x50, 0x33, 0xC8, 9, BuySellPage.Ink, BuySellPage.Lit);
    }

    // orig: cache_key R035+0x0A46 matched, case 0x4800
    private void MoveUp()
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
            DrawStoreItems();
            page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xB9);
        }
        else if (!_usingStore[Member] && _packRow[Member] < 0 && _packTop[Member] > 0)
        {
            _packRow[Member]++;
            _packTop[Member]--;
            DrawPackItems();
            page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
        }
        else
        {
            _storeRow[Member] = Math.Max(_storeRow[Member], 0);
            _packRow[Member] = Math.Max(_packRow[Member], 0);
            page.Recolor(0x3C, 0x55, 0x103, 0x69, BuySellPage.Lit, BuySellPage.Ink);
            if (_usingStore[Member])
            {
                page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xC3);
            }
            else
            {
                page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
            }
        }
    }

    // orig: cache_key R035+0x0A46 matched, case 0x5000 (both cursors are kept inside their lists)
    private void MoveDown()
    {
        if (_usingStore[Member])
        {
            _storeRow[Member]++;
        }
        else
        {
            _packRow[Member]++;
        }
        if (_usingStore[Member] && _storeRow[Member] >= 4 && _storeTop[Member] + _storeRow[Member] < _store.Count)
        {
            _storeRow[Member]--;
            _storeTop[Member]++;
            DrawStoreItems();
            page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xB9);
        }
        else if (!_usingStore[Member] && _packRow[Member] >= 4 && _packTop[Member] + _packRow[Member] < _packCount[Member])
        {
            _packRow[Member]--;
            _packTop[Member]++;
            DrawPackItems();
            page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
        }
        else
        {
            if (_storeRow[Member] >= _store.Count)
            {
                _storeRow[Member]--;
            }
            if (_packCount[Member] <= _packRow[Member])
            {
                _packRow[Member]--;
            }
            _storeRow[Member] = Math.Min(_storeRow[Member], 3);
            _packRow[Member] = Math.Min(_packRow[Member], 3);
            page.Recolor(0x3C, 0x55, 0x103, 0x6E, BuySellPage.Lit, BuySellPage.Ink);
            if (_usingStore[Member])
            {
                page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xC8);
            }
            else
            {
                page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xC8);
            }
        }
    }

    // orig: cache_key R035+0x0A46 matched, cases 0x4B00/0x4D00 (no switch to an empty store)
    private void SwitchLists()
    {
        if (_usingStore[Member])
        {
            ShowPacking();
            page.Unlight(0x52, _storeRow[Member] * 8 + 0x55, 0xB9);
            page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
            _usingStore[Member] = false;
        }
        else if (_store.Count != 0)
        {
            ShowGetting();
            page.Unlight(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
            page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xB9);
            _usingStore[Member] = true;
        }
    }

    // ---- drawing ------------------------------------------------------------------------

    /// <summary>The menu with Put dark (the pack in use).</summary>
    private void ShowPacking()
    {
        page.Text(0x50, 0x1B, GetLine, BuySellPage.Faded);
        page.Text(0x50, 0x23, PutLine, BuySellPage.Ink);
    }

    /// <summary>The menu with Get dark (the store in use).</summary>
    private void ShowGetting()
    {
        page.Text(0x50, 0x23, PutLine, BuySellPage.Faded);
        page.Text(0x50, 0x1B, GetLine, BuySellPage.Ink);
    }

    /// <summary>The first drawing; the store is read here.</summary>
    // orig: draw_cache_screen R035+0x0510 (1462:0510) matched; reads the store (cache_load)
    private void DrawScreen(Core.World.Location place)
    {
        page.DrawBackground(Party);
        page.Text(0x49, 9, PurseLine(), BuySellPage.Ink);
        page.Text(0x50, 0x1B, GetLine, BuySellPage.Faded);
        page.Text(0x50, 0x23, PutLine, BuySellPage.Ink);
        DrawMenuTail();
        page.Text(0x6B, 0x45, "The cache contains...", BuySellPage.Ink);
        page.Text(0x77, 0x83, $"{Party.Slot(Member).ShortName} currently has...", BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            page.Text(0x75, 0x93 + i * 8, ItemLine(pack[i].Code & 0xFFF, pack[i].Count, pack[i].Quality, "-Qual"), BuySellPage.Ink);
        }
        // C: the pack lines are drawn from row 0 (not from the kept top) and the store's lines at (i − kept top), as the original does with its persistent statics.
        _store = state.ItemStores.Open(place.CacheHandle, state.StoreTable);
        for (var i = 0; i < _store.Count && i < 4; i++)
        {
            page.Text(0x55, (i - _storeTop[Member]) * 8 + 0x55, StoreLine(_store[i]), BuySellPage.Ink);
        }
        page.Light(0x73, 0x93, 0xB7);
        _packCount[Member] = pack.Count;
    }

    /// <summary>The whole page again (another member, or after a character sheet).</summary>
    // orig: redraw_cache_screen R035+0x0782 (1462:0782) matched
    private void Redraw()
    {
        page.DrawBackground(Party);
        page.Text(0x49, 9, PurseLine(), BuySellPage.Ink);
        page.Text(0x50, 0x1B, GetLine, _usingStore[Member] ? BuySellPage.Ink : BuySellPage.Faded);
        page.Text(0x50, 0x23, PutLine, _usingStore[Member] ? BuySellPage.Faded : BuySellPage.Ink);
        DrawMenuTail();
        // C: the redraw prints this heading at x 0x57, the first drawing at 0x6B.
        page.Text(0x57, 0x45, "The cache contains...", BuySellPage.Ink);
        page.Text(0x77, 0x83, $"{Party.Slot(Member).ShortName} has...", BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            page.Text(0x75, 0x93 + i * 8, ItemLine(pack[i].Code & 0xFFF, pack[i].Count, pack[i].Quality, "-Qual"), BuySellPage.Ink);
        }
        for (var i = 0; i < _store.Count && i < 4; i++)
        {
            page.Text(0x55, (i - _storeTop[Member]) * 8 + 0x55, StoreLine(_store[i]), BuySellPage.Ink);
        }
        if (_usingStore[Member])
        {
            page.Light(0x52, _storeRow[Member] * 8 + 0x55, 0xB7);
        }
        else
        {
            page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB7);
        }
        _packCount[Member] = pack.Count;
    }

    private void DrawMenuTail()
    {
        page.Text(0x50, 0x2B, "\u008C C\u00FFache another person's items", BuySellPage.Ink);
        page.Text(0x50, 0x33, "\u008C L\u00FFeave", BuySellPage.Ink);
    }

    /// <summary>Four of the member's stacks (here the quality reads "q", not "-Qual").</summary>
    // orig: draw_member_items R035+0x11AC (1462:11AC) matched (its lines say "q", the others "-Qual")
    private void DrawPackItems()
    {
        page.Restore(0x72, 0x93, 0xB6, 0x20);
        var pack = PackOf(Member);
        for (var i = _packTop[Member]; i < _packTop[Member] + 4 && i < pack.Count; i++)
        {
            page.Text(0x75, (i - _packTop[Member]) * 8 + 0x93, ItemLine(pack[i].Code & 0xFFF, pack[i].Count, pack[i].Quality, "q"), BuySellPage.Ink);
        }
    }

    // orig: draw_cache_items R035+0x1292 (1462:1292) matched
    private void DrawStoreItems()
    {
        page.Restore(0x52, 0x55, 0xB6, 0x20);
        for (var i = _storeTop[Member]; i < _storeTop[Member] + 4 && i < _store.Count; i++)
        {
            page.Text(0x55, (i - _storeTop[Member]) * 8 + 0x55, StoreLine(_store[i]), BuySellPage.Ink);
        }
    }

    private string PurseLine()
    {
        var purse = Party.Purse;
        return Invariant($"{Party.Slot(Party.LeaderIndex).ShortName} holds the purse of {purse.Florins}fl, {purse.Groschen}gr, {purse.Pfennigs}pf.");
    }

    // C: the store's qual and cnt are signed chars printed with %d (a count of 200 prints −56).
    private string StoreLine(StoredItem entry) => ItemLine(entry.Code, (sbyte)entry.Count, (sbyte)entry.Quality, "-Qual");   // the store keeps them as signed bytes

    /// <summary>"name  (%3d) %2d-Qual".</summary>
    private string ItemLine(int code, int count, int quality, string qualityWord) =>
        Invariant($"{state.Item(code).Name}  ({count,3}) {quality,2}{qualityWord}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
