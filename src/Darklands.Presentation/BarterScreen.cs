using System.Globalization;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// The barter screen: the shop's goods above, the bartering member's pack below. P buys the
/// lit item, S sells one of the lit stack, B barters for the next member, L or Esc leaves,
/// arrows move and switch lists, Ctrl-F1..F5 choose who leads the haggling (Charisma counts),
/// D drops one item. Draws and redraws the parts the original redraws, when it does.
/// </summary>
// orig: barter overlay R032 = R032 (linked paragraph 1462, payload = offset) draft (the byte match 8/11); rules in Trading; checked visually with Darklands.Host.Headless screen:barter
public sealed class BarterScreen
{
    // C: a click on menu row 2 (the blank line) gives 0x1E41 ('A'), which no case handles: kept as a no-op key.
    private static readonly int[] MenuKeys = [0x1950, 0x1F53, 0x1E41, 0x3042, Keys.Escape];

    private const string PurchaseLine = "\u008C P\u00FFurchase an item";
    private const string SellLine = "\u008C S\u00FFell an item";
    private const string BlankLine = " ";

    private readonly BuySellPage _page;
    private readonly GameState _state;
    private readonly IGameScreens _screens;
    private readonly Shop _shop;
    private readonly int _leaderBefore;

    private ShopStock _stock = new([], []);
    private int _member;

    // Per member, as in the original: buying or selling, the stack count, the pack list's top and cursor.
    private readonly bool[] _buying = [true, true, true, true, true];
    private readonly int[] _packCount = new int[5];
    private readonly int[] _packTop = new int[5];
    private readonly int[] _packRow = new int[5];

    // The shop list's top and cursor (one for everybody).
    private int _shopTop;
    private int _shopRow;

    public BarterScreen(BuySellPage page, GameState state, IGameScreens screens, int shop, string? name, long categories)
    {
        _page = page;
        _state = state;
        _screens = screens;
        _shop = Trading.Open(state, shop, name, categories);
        _leaderBefore = state.Party.LeaderIndex;
        _member = state.Party.LeaderIndex;
    }

    private Core.Characters.Party Party => _state.Party;

    private int Leader => Party.LeaderIndex;

    private Core.Characters.Pack PackOf(int member) => Party.Slot(member).Pack;

    /// <summary>Runs the screen until the player leaves.</summary>
    // orig: barter_screen R032+0x0000 (1462:0000) draft (unmatched: registers/slots); main loop
    // C: on entry the five members' modes are set to buying, their counts, tops and cursors and the shared shop top/cursor to 0 (the field initialisers).
    public void Run()
    {
        DrawScreen();
        // orig: R032+0x00F7 `while (mouse_buttons & 1);` before the loop (checked against the asm)
        _page.WaitForRelease();
        var lit = -1;
        try
        {
            while (true)
            {
                // DIVERGENCE: menu_bar_run(DS:9419, 0) is GameUi.Poll's (MenuBar, HotKeys); the party_icons_dirty redraw is not here: the page is not redrawn after them.
                var key = _page.KeyAfterTheMouse(MouseStep(ref lit));
                if (key == Keys.Escape || BuySellPage.Is(key, 'L'))
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
            // orig: barter_close R032+0x076C (1462:076C) draft (byte-matched function): restores the fonts, the panel mode flags DS:9920/DS:9934 and leader = DS:884C.
            // DIVERGENCE: fonts and panel mode flags are host state not modelled here; only the leader is restored.
            Party.LeaderIndex = _leaderBefore;
        }
    }

    // ---- input ------------------------------------------------------------------------

    /// <summary>The mouse tests of one pass of the loop, in their order, on the button's state; the key they give (0: none).</summary>
    // orig: R032+0x00FE..0x0323 and on, draft checked against the asm, the mouse tests in loop order: the menu (BuySellPage.MenuStep,
    // the leaving un-highlight 9 rows high here); a row of either list lights at once and the button is then waited out; an arrow
    // waits the button out first, then gives Up/Down and redraws the menu for its list; the party column opens the sheet, redraws the
    // page, then the button is waited out; tests: BuySellInputTests
    // C: each test reads the button again, so after a wait the later tests see it up.
    private int MouseStep(ref int lit)
    {
        var key = _page.MenuStep(ref lit, MenuKeys, 9);
        if (_page.HeldRow(BuySellPage.LowerRowAt) is { } packRow)
        {
            _buying[_member] = false;
            _packRow[_member] = packRow;
            ShowSelling();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.HeldRow(BuySellPage.UpperRowAt) is { } shopRow)
        {
            _buying[_member] = true;
            _shopRow = shopRow;
            ShowBuying();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x52, _shopRow * 8 + 0x55, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.Held(BuySellPage.OnLowerUpArrow))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _buying[_member] = false;
            ShowSelling();
        }
        if (_page.Held(BuySellPage.OnLowerDownArrow))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _buying[_member] = false;
            ShowSelling();
        }
        if (_page.Held(BuySellPage.OnUpperUpArrow))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _buying[_member] = true;
            ShowBuying();
        }
        if (_page.Held(BuySellPage.OnUpperDownArrow))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _buying[_member] = true;
            ShowBuying();
        }
        // C: member_sheet_vec(row) returns the member to show next; barter ignores it and redraws (pic, palette, panel, the lists).
        if (_page.HeldRow(BuySellPage.MemberAt) is { } member)
        {
            _screens.CharacterSheet(member);
            Redraw();
            _page.WaitForRelease();
        }
        return key;
    }

    // orig: barter_key R032+0x0F60 (1462:0F60) draft (byte-matched function); every case kept (Alt-Q, P/p, S/s, D/d, L/l in Run, B/b, Up, Down, Ctrl-F1..F5 falling into Left/Right); other keys do nothing
    private void HandleKey(int key)
    {
        // C: case 0x1000 (Alt-Q) calls barter_close then quit_to_dos; the finally block restores the leader.
        // DORMANT: with a GameUi.GameKey hook installed, HotKeys takes Alt-Q first (and asks); reached only without the hook.
        if (key == Keys.AltQ)
        {
            throw new QuitGameException();
        }
        if (BuySellPage.Is(key, 'P'))
        {
            Purchase();
        }
        else if (BuySellPage.Is(key, 'S'))
        {
            Sell();
        }
        else if (BuySellPage.Is(key, 'D'))
        {
            DropFromLeader();
        }
        else if (BuySellPage.Is(key, 'B'))
        {
            BarterForTheNextMember();
        }
        else if (key == Keys.Up)
        {
            MoveUp();
        }
        else if (key == Keys.Down)
        {
            MoveDown();
        }
        // C: Ctrl-F1..F5 are 0x5E00..0x6200; key = (key >> 8) - 0x5E.
        else if (key is >= 0x5E00 and <= 0x6200 && (key & 0xFF) == 0)
        {
            ChooseLeader((key >> 8) - 0x5E);
        }
        else if (key is Keys.Left or Keys.Right)
        {
            SwitchLists();
        }
    }

    // orig: barter_key R032+0x0F60 draft, case P; the rule is Trading.Buy
    private void Purchase()
    {
        // DIVERGENCE: a click can light a shop row past the list's end; the original then buys DS:86A2[row] left from an earlier listing (the array is never cleared). Nothing happens here.
        if (!_buying[_member] || _stock.Codes.Count == 0 || (uint)(_shopTop + _shopRow) >= _stock.Codes.Count)
        {
            return;
        }
        var count = Trading.Buy(_state, _shop, _member, _stock.Codes[_shopTop + _shopRow]);
        if (count < 0)
        {
            return;
        }
        RedrawPurse();
        _packCount[_member] = count;
        DrawPackItems();
        _page.Light(0x52, _shopRow * 8 + 0x55, 0x5F);
    }

    // orig: barter_key R032+0x0F60 draft, case S; the rule is Trading.Sell
    private void Sell()
    {
        var index = _packRow[_member] + _packTop[_member];
        // C: only the list in use and the stack count DS:8838 != 0 are tested: a lit row past the pack's end sells its stale entry (Trading.Sell).
        if (_buying[_member] || _packCount[_member] == 0)
        {
            return;
        }
        var count = Trading.Sell(_state, _member, index);
        if (count < 0)
        {
            return;
        }
        RedrawPurse();
        _packCount[_member] = count;
        if (index >= count)
        {
            if (_packTop[_member] != 0)
            {
                _packTop[_member]--;
            }
            else if (_packRow[_member] != 0)
            {
                _packRow[_member]--;
            }
        }
        DrawPackItems();
        _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
    }

    /// <summary>D: one item of the <em>leader's</em> stack at the bartering member's lit row is thrown away.</summary>
    // orig: barter_key R032+0x0F60 draft, case D (0x2044/0x2064): item_take_one_vec(leader, row + top), the count stored for the bartering member, a 0x5F-wide highlight in the menu area
    private void DropFromLeader()
    {
        var index = _packRow[_member] + _packTop[_member];
        // C: no count test: past the end of the leader's pack the stale entry there loses an item (Inventory.TakeAt).
        if (_buying[_member])
        {
            return;
        }
        _packCount[_member] = Core.Rules.Inventory.TakeAt(_state, Leader, index);
        DrawPackItems();
        _page.Light(0x50, _packRow[_member] * 8 + 0x1B, 0x5F);
    }

    // orig: barter_key R032+0x0F60 draft, case B (0x3042/0x3062)
    private void BarterForTheNextMember()
    {
        _page.DrawParty(Party);
        do
        {
            _member = (_member + 1) % 5;
        }
        // C: (DS:884E + 1) % 5 until party[].present: loops forever if nobody is present (impossible while the party exists).
        while (Party.PresentMember(_member) is null);
        Redraw();
        _page.Recolor(0x50, 0x33, 0xC8, 9, BuySellPage.Ink, BuySellPage.Lit);
    }

    // orig: barter_key R032+0x0F60 draft, case 0x4800
    private void MoveUp()
    {
        if (_buying[_member])
        {
            _shopRow--;
        }
        else
        {
            _packRow[_member]--;
        }
        if (_buying[_member] && _shopRow < 0 && _shopTop > 0)
        {
            _shopRow++;
            _shopTop--;
            DrawShopItems();
            _page.Light(0x52, _shopRow * 8 + 0x55, 0xB9);
        }
        else if (!_buying[_member] && _packRow[_member] < 0 && _packTop[_member] > 0)
        {
            _packRow[_member]++;
            _packTop[_member]--;
            DrawPackItems();
            _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
        }
        else
        {
            _shopRow = Math.Max(_shopRow, 0);
            _packRow[_member] = Math.Max(_packRow[_member], 0);
            _page.Recolor(0x3C, 0x55, 0x103, 0x69, BuySellPage.Lit, BuySellPage.Ink);
            if (_buying[_member])
            {
                _page.Light(0x52, _shopRow * 8 + 0x55, 0xC3);
            }
            else
            {
                _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
            }
        }
    }

    // orig: barter_key R032+0x0F60 draft, case 0x5000
    // C: the shop cursor is kept inside the list (one step back) but the pack cursor only inside the four rows.
    private void MoveDown()
    {
        if (_buying[_member])
        {
            _shopRow++;
        }
        else
        {
            _packRow[_member]++;
        }
        if (_buying[_member] && _shopRow >= 4 && _shopTop + _shopRow < _stock.Codes.Count)
        {
            _shopRow--;
            _shopTop++;
            DrawShopItems();
            _page.Light(0x52, _shopRow * 8 + 0x55, 0xB9);
        }
        else if (!_buying[_member] && _packRow[_member] >= 4 && _packRow[_member] + _packTop[_member] < _packCount[_member])
        {
            _packRow[_member]--;
            _packTop[_member]++;
            DrawPackItems();
            _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
        }
        else
        {
            if (_shopRow >= _stock.Codes.Count)
            {
                _shopRow--;
            }
            _shopRow = Math.Min(_shopRow, 3);
            _packRow[_member] = Math.Min(_packRow[_member], 3);
            _page.Recolor(0x3C, 0x55, 0x103, 0x6E, BuySellPage.Lit, BuySellPage.Ink);
            if (_buying[_member])
            {
                _page.Light(0x52, _shopRow * 8 + 0x55, 0xC8);
            }
            else
            {
                _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xC8);
            }
        }
    }

    /// <summary>Ctrl-F1..F5: a present member leads the haggling; then the lists switch, as the original falls through.</summary>
    // orig: barter_key R032+0x0F60 draft, cases 0x5E00..0x6200 (falls through into the list switch); prices on screen are not recomputed
    private void ChooseLeader(int member)
    {
        if (Party.PresentMember(member) is null)
        {
            return;
        }
        Party.LeaderIndex = member;
        _page.DrawParty(Party);
        SwitchLists();
    }

    // orig: barter_key R032+0x0F60 draft, cases 0x4B00/0x4D00
    private void SwitchLists()
    {
        if (_buying[_member])
        {
            ShowSelling();
            _page.Unlight(0x52, _shopRow * 8 + 0x55, 0xB9);
            _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
            _buying[_member] = false;
        }
        else
        {
            ShowBuying();
            _page.Unlight(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
            _page.Light(0x52, _shopRow * 8 + 0x55, 0xB9);
            _buying[_member] = true;
        }
    }

    // ---- drawing ------------------------------------------------------------------------

    /// <summary>The menu with Purchase dark (the shop list in use).</summary>
    private void ShowBuying()
    {
        _page.Text(0x50, 0x23, SellLine, BuySellPage.Faded);
        _page.Text(0x50, 0x1B, PurchaseLine, BuySellPage.Ink);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
    }

    /// <summary>The menu with Sell dark (the pack list in use).</summary>
    private void ShowSelling()
    {
        _page.Text(0x50, 0x1B, PurchaseLine, BuySellPage.Faded);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
        _page.Text(0x50, 0x23, SellLine, BuySellPage.Ink);
    }

    /// <summary>The first drawing: the page, both lists, and the day's stock (which seeds the generator).</summary>
    // orig: draw_barter_screen R032+0x078E (1462:078E) draft (byte-matched function); the stock loop is Trading.Stock
    private void DrawScreen()
    {
        _page.DrawBackground(Party);
        _page.Text(0x49, 9, PurseLine(), BuySellPage.Ink);
        _page.Text(0x49, 0x12, BartersLine(), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, PurchaseLine, BuySellPage.Ink);
        _page.Text(0x50, 0x23, SellLine, BuySellPage.Faded);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
        DrawMenuTail();
        _page.Text(0x81, 0x83, $"{Party.Slot(_member).ShortName} currently has...", BuySellPage.Ink);
        var pack = PackOf(_member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, 0x93 + i * 8, PackLine(pack[i]), BuySellPage.Ink);
        }
        // C: the pack lines above were priced before the stock listing seeds the generator (sell_price draws under the clock seed, in a flag-8 place).
        _stock = Trading.Stock(_state, _shop);
        for (var i = 0; i < _stock.ListedPrices.Count; i++)
        {
            var code = _stock.Codes[i];
            // C: the first listing places its lines with the pack list's top DS:8852[DS:884E] instead of the shop list's (both 0 on entry).
            _page.Text(0x55, (i - _packTop[_member]) * 8 + 0x55, ShopLine(code, _stock.ListedPrices[i], wide: true), BuySellPage.Ink);
        }
        _page.Light(0x53, 0x55, 0xB8);
        _packCount[_member] = pack.Count;
    }

    /// <summary>The whole page again (after another member is chosen or a character sheet).</summary>
    // orig: redraw_barter_screen R032+0x0B30 (1462:0B30) draft (byte-matched function)
    // DIVERGENCE: the original redraws the picture without set_palette (the palette left by a character sheet stays); the picture's colours are applied here.
    private void Redraw()
    {
        _page.DrawBackground(Party);
        _page.Text(0x49, 9, PurseLine(), BuySellPage.Ink);
        // C: the redraw prints this line at x 0x4E, the first drawing at 0x49.
        _page.Text(0x4E, 0x12, BartersLine(), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, PurchaseLine, _buying[_member] ? BuySellPage.Ink : BuySellPage.Faded);
        _page.Text(0x50, 0x23, SellLine, _buying[_member] ? BuySellPage.Faded : BuySellPage.Ink);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
        DrawMenuTail();
        _page.Text(0x81, 0x83, $"{Party.Slot(_member).ShortName} has...", BuySellPage.Ink);
        var pack = PackOf(_member);
        for (var i = _packTop[_member]; i < _packTop[_member] + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _packTop[_member]) * 8 + 0x93, PackLine(pack[i]), BuySellPage.Ink);
        }
        for (var i = _shopTop; i < _shopTop + 4 && i < _stock.Codes.Count; i++)
        {
            var code = _stock.Codes[i];
            _page.Text(0x55, (i - _shopTop) * 8 + 0x55, ShopLine(code, Trading.BuyPrice(_state, _shop, code), wide: true), BuySellPage.Ink);
        }
        if (_buying[_member])
        {
            _page.Light(0x52, _shopRow * 8 + 0x55, 0xB9);
        }
        else
        {
            _page.Light(0x72, _packRow[_member] * 8 + 0x93, 0xB9);
        }
        _packCount[_member] = pack.Count;
    }

    private void DrawMenuTail()
    {
        _page.Text(0x50, 0x33, "\u008C B\u00FFarter for another person", BuySellPage.Ink);
        _page.Text(0x50, 0x3B, "\u008C L\u00FFeave", BuySellPage.Ink);
        _page.Text(0x7A, 0x45, $"The {_shop.Name} offers...", BuySellPage.Ink);
    }

    /// <summary>Four of the member's stacks with what the shop pays.</summary>
    // orig: draw_member_items R032+0x1770 (1462:1770) draft (byte-matched function)
    private void DrawPackItems()
    {
        _page.Restore(0x72, 0x93, 0xB6, 0x20);
        var pack = PackOf(_member);
        for (var i = _packTop[_member]; i < _packTop[_member] + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _packTop[_member]) * 8 + 0x93, PackLine(pack[i]), BuySellPage.Ink);
        }
    }

    /// <summary>Four of the goods with their prices (the original prints the price only three wide here).</summary>
    // orig: draw_shop_items R032+0x1870 (1462:1870) draft (byte-matched function)
    private void DrawShopItems()
    {
        _page.Restore(0x52, 0x55, 0xB9, 0x20);
        for (var i = _shopTop; i < _shopTop + 4 && i < _stock.Codes.Count; i++)
        {
            var code = _stock.Codes[i];
            _page.Text(0x55, (i - _shopTop) * 8 + 0x55, ShopLine(code, Trading.BuyPrice(_state, _shop, code), wide: false), BuySellPage.Ink);
        }
    }

    // orig: barter_key R032+0x0F60 draft: gfx_copy_rect(back_win, 0x49, 9, 0xEE, 9) and the purse line again, after P and S
    private void RedrawPurse()
    {
        _page.Restore(0x49, 9, 0xEE, 9);
        _page.Text(0x49, 9, PurseLine(), BuySellPage.Ink);
    }

    private string PurseLine()
    {
        var purse = Party.Purse;
        return Invariant($"{Party.Slot(Leader).ShortName} holds the purse of {purse.Florins}fl, {purse.Groschen}gr, {purse.Pfennigs}pf ({(uint)purse.TotalPfennigs}pf).");
    }

    // C: he/she and himself/herself follow the leader's sex (party[leader].female) even when naming another member.
    private string BartersLine()
    {
        var leader = Party.Slot(Leader);
        var member = Party.Slot(_member).ShortName;
        return Leader != _member
            ? $"{(leader.IsFemale ? "She" : "He")} barters for {member} to the {_shop.Name}"
            : $"{member} barters for {(leader.IsFemale ? "herself" : "himself")} to the {_shop.Name}";
    }

    /// <summary>"%5upf  name  (%3d) %2dq": what the shop pays for one, the name, the count and the quality.</summary>
    private string PackLine(Core.Characters.ItemStack stack)
    {
        var code = stack.Code & 0xFFF;
        // C: sprintf arguments right to left: quality, count, name, then sell_price (a flag-8 draw per line).
        var price = (ushort)Trading.SellPrice(_state, code, stack.Quality);
        return Invariant($"{price,5}pf  {_state.Item(code).Name}  ({stack.Count,3}) {stack.Quality,2}q");
    }

    /// <summary>"%5upf  name  %3dq  %3dlbs" (or "%3dpf ..." when not <paramref name="wide"/>): the price, the name, the quality offered and the weight.</summary>
    private string ShopLine(int code, int price, bool wide)
    {
        var d = _state.Item(code);
        // C: "%5upf" prints the price word unsigned; draw_shop_items uses "%3dpf" (signed, three wide).
        var shown = wide ? Invariant($"{(ushort)price,5}") : Invariant($"{(short)price,3}");
        return Invariant($"{shown}pf  {d.Name}  {Trading.OfferedQuality(_state, _shop, code),3}q  {(sbyte)d.Weight,3}lbs");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
