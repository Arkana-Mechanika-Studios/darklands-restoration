using Darklands.Cards;
using Darklands.Content.Formats.Text.Strings;
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
// orig: barter overlay R032 = R032 (linked paragraph 1462, payload = offset) draft (the byte match 8/11); the lists and keys in Simulation.BarterSession, rules in Trading; checked visually with Darklands.Host.Headless screen:barter
public sealed class BarterScreen
{
    // C: a click on menu row 2 (the blank line) gives 0x1E41 ('A'), which no case handles: kept as a no-op key.
    private static readonly int[] MenuKeys = [0x1950, 0x1F53, 0x1E41, 0x3042, Keys.Escape];

    // the menu lines of R032 (one copy each), the key letter after 0x8C in colour 12
    private const string PurchaseLine = "exe:DS:2F4A";
    private const string SellLine = "exe:DS:2F3A";

    /// <summary>The texts one drawing function of R032 loads: its own copies.</summary>
    private sealed record PageTexts(string Purse, string He, string She, string Himself, string Herself, string BartersFor, string BartersForSelf,
        string Another, string Leave, string Offers, string Has, string PackItem, string ShopItem);

    // trade-screen-paint R032+0x078E: "%s holds the purse of %dfl, %dgr, %dpf (%lupf).", "He", "She", "himself", "herself", "%s barters for %s to
    //   the %Fs" (another member, then the leader for himself), "Barter for another person", "Leave", "The %Fs offers...", "%s currently has...",
    //   "%5upf  %Fs  (%3d) %2dq", "%5upf  %Fs  %3dq  %3dlbs"
    private static readonly PageTexts FullDrawing = new("exe:DS:2F71", "exe:DS:2FA1", "exe:DS:2FAC", "exe:DS:2FA4", "exe:DS:2FB0", "exe:DS:2FB8",
        "exe:DS:2FD5", "exe:DS:2FF2", "exe:DS:300F", "exe:DS:3018", "exe:DS:302A", "exe:DS:303E", "exe:DS:3055");

    // trade-screen-repaint R032+0x0B30 (its "%s has...")
    private static readonly PageTexts RedrawTexts = new("exe:DS:307F", "exe:DS:30AF", "exe:DS:30BA", "exe:DS:30B2", "exe:DS:30BE", "exe:DS:30C6",
        "exe:DS:30E3", "exe:DS:3100", "exe:DS:311D", "exe:DS:3126", "exe:DS:3138", "exe:DS:3142", "exe:DS:3159");

    // trade-screen-keys R032+0x0F60: the purse line after P (DS:3172) and after S (DS:31A2); member-items-paint R032+0x1770 ("%5upf  %Fs  (%3d) %2dq",
    //   DS:31D2); shop-items-paint R032+0x1870 ("%3dpf  %Fs  %3dq  %3dlbs", DS:31E9)
    private const string PurseAfterPurchase = "exe:DS:3172";
    private const string PurseAfterSale = "exe:DS:31A2";
    private const string PackListItem = "exe:DS:31D2";
    private const string ShopListItem = "exe:DS:31E9";
    private const string BlankLine = " ";

    private readonly BuySellPage _page;
    private readonly GameState _state;
    private readonly IGameScreens _screens;
    private readonly Simulation.BarterSession _s;

    public BarterScreen(BuySellPage page, GameState state, IGameScreens screens, int shop, string? name, long categories)
    {
        _page = page;
        _state = state;
        _screens = screens;
        _s = new Simulation.BarterSession(state, shop, name, categories, page.BarterListing);
    }

    private Core.Characters.Party Party => _state.Party;

    // orig: trade-screen-entry R032+0x0058: DS:869C = name ? name : R136+0xA70F[shop] (OriginalWords.ShopNames)
    /// <summary>The shop's name: the one it was opened under, else its entry of the name table.</summary>
    private string ShopName => _s.Shop.Name ?? _page.Ui.Word(OriginalWords.ShopNames, _s.Shop.Number);

    private int Leader => Party.LeaderIndex;

    private int Member => _s.Member;

    private Core.Characters.Pack PackOf(int member) => Party.Slot(member).Pack;

    /// <summary>Runs the screen until the player leaves.</summary>
    // orig: trade-screen-entry R032+0x0000 (1462:0000) draft (unmatched: registers/slots); main loop
    // C: on entry the five members' modes are set to buying, their counts, tops and cursors and the shared shop top/cursor to 0 (BarterSession).
    public void Run()
    {
        DrawScreen();
        // orig: R032+0x00F7 `while (mouse-button-state & 1);` before the loop (checked against the asm)
        _page.WaitForRelease();
        // host comfort (GameUi.SelectionList, the mouse wheel's ↑ / ↓): the shop's and the member's lists
        using var wheel = _page.Ui.UseSelectionList(static () => true);
        var lit = -1;
        try
        {
            while (true)
            {
                // orig: R032+0x06B2: menu-bar-loop(DS:9419, 0) (GameUi.Poll's: MenuBar, HotKeys); R032+0x06BF: if DS:9C46 { DS:9C46 = 0; the picture 0x2F29
                //   reloaded into DS:E48C and shown, the panel (root 0000:1588 / 0000:1716 with 0x100), R032+0x0B30 (Redraw) } (the pass's key kept); a waiting key
                var key = _page.KeyAfterTheMouse(MouseStep(ref lit), Redraw);
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
            // orig: trade-screen-close R032+0x076C (1462:076C) draft (byte-matched function): restores the fonts, the panel mode flags DS:9920/DS:9934 and leader = DS:884C.
            // DIVERGENCE: fonts and panel mode flags are host state not modelled here; only the leader is restored.
            _s.End();
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
            _s.ChoosePackRow(packRow);
            ShowSelling();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.HeldRow(BuySellPage.UpperRowAt) is { } shopRow)
        {
            _s.ChooseShopRow(shopRow);
            ShowBuying();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.Held(BuySellPage.OnLowerUpArrow))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _s.UseList(buying: false);
            ShowSelling();
        }
        if (_page.Held(BuySellPage.OnLowerDownArrow))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _s.UseList(buying: false);
            ShowSelling();
        }
        if (_page.Held(BuySellPage.OnUpperUpArrow))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _s.UseList(buying: true);
            ShowBuying();
        }
        if (_page.Held(BuySellPage.OnUpperDownArrow))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _s.UseList(buying: true);
            ShowBuying();
        }
        // C: member-sheet-overlay-entry(row) returns the member to show next; barter ignores it and redraws (pic, palette, panel, the lists).
        if (_page.HeldRow(BuySellPage.MemberAt) is { } member)
        {
            _screens.CharacterSheet(member);
            Redraw();
            _page.WaitForRelease();
        }
        return key;
    }

    // orig: trade-screen-keys R032+0x0F60 (1462:0F60) draft (byte-matched function); every case kept (Alt-Q, P/p, S/s, D/d, L/l in Run, B/b, Up, Down, Ctrl-F1..F5 falling into Left/Right); other keys do nothing
    private void HandleKey(int key)
    {
        // C: case 0x1000 (Alt-Q) calls trade-screen-close then exit-to-system; the finally block restores the leader.
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

    // orig: trade-screen-keys R032+0x0F60 draft, case P (BarterSession.Purchase)
    private void Purchase()
    {
        if (!_s.Purchase())
        {
            return;
        }
        RedrawPurse(PurseAfterPurchase);
        DrawPackItems();
        _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0x5F);
    }

    // orig: trade-screen-keys R032+0x0F60 draft, case S (BarterSession.Sell)
    private void Sell()
    {
        if (!_s.Sell())
        {
            return;
        }
        RedrawPurse(PurseAfterSale);
        DrawPackItems();
        _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
    }

    /// <summary>D: one item of the <em>leader's</em> stack at the bartering member's lit row is thrown away (BarterSession.DropFromLeader).</summary>
    // orig: trade-screen-keys R032+0x0F60 draft, case D (0x2044/0x2064): a 0x5F-wide highlight in the menu area
    private void DropFromLeader()
    {
        if (!_s.DropFromLeader())
        {
            return;
        }
        DrawPackItems();
        _page.Light(0x50, _s.PackRow * 8 + 0x1B, 0x5F);
    }

    // orig: trade-screen-keys R032+0x0F60 draft, case B (0x3042/0x3062) (BarterSession.NextMember)
    private void BarterForTheNextMember()
    {
        _page.DrawParty(Party);
        _s.NextMember();
        Redraw();
        _page.Recolor(0x50, 0x33, 0xC8, 9, BuySellPage.Ink, BuySellPage.Lit);
    }

    // orig: trade-screen-keys R032+0x0F60 draft, case 0x4800 (BarterSession.MoveUp)
    private void MoveUp()
    {
        switch (_s.MoveUp())
        {
            case Simulation.ListMove.UpperScrolled:
                DrawShopItems();
                _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xB9);
                break;
            case Simulation.ListMove.LowerScrolled:
                DrawPackItems();
                _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                break;
            default:
                _page.Recolor(0x3C, 0x55, 0x103, 0x69, BuySellPage.Lit, BuySellPage.Ink);
                if (_s.Buying)
                {
                    _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xC3);
                }
                else
                {
                    _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                }
                break;
        }
    }

    // orig: trade-screen-keys R032+0x0F60 draft, case 0x5000 (BarterSession.MoveDown)
    private void MoveDown()
    {
        switch (_s.MoveDown())
        {
            case Simulation.ListMove.UpperScrolled:
                DrawShopItems();
                _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xB9);
                break;
            case Simulation.ListMove.LowerScrolled:
                DrawPackItems();
                _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                break;
            default:
                _page.Recolor(0x3C, 0x55, 0x103, 0x6E, BuySellPage.Lit, BuySellPage.Ink);
                if (_s.Buying)
                {
                    _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xC8);
                }
                else
                {
                    _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xC8);
                }
                break;
        }
    }

    /// <summary>Ctrl-F1..F5: a present member leads the haggling; then the lists switch, as the original falls through.</summary>
    // orig: trade-screen-keys R032+0x0F60 draft, cases 0x5E00..0x6200 (BarterSession.ChooseLeader); prices on screen are not recomputed
    private void ChooseLeader(int member)
    {
        if (!_s.ChooseLeader(member))
        {
            return;
        }
        _page.DrawParty(Party);
        SwitchLists();
    }

    // orig: trade-screen-keys R032+0x0F60 draft, cases 0x4B00/0x4D00 (BarterSession.SwitchLists; the rows stay)
    private void SwitchLists()
    {
        if (_s.SwitchLists() == Simulation.ListSwitch.ToLower)
        {
            ShowSelling();
            _page.Unlight(0x52, _s.ShopRow * 8 + 0x55, 0xB9);
            _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
        }
        else
        {
            ShowBuying();
            _page.Unlight(0x72, _s.PackRow * 8 + 0x93, 0xB9);
            _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xB9);
        }
    }

    // ---- drawing ------------------------------------------------------------------------

    /// <summary>The menu with Purchase dark (the shop list in use).</summary>
    private void ShowBuying()
    {
        _page.Text(0x50, 0x23, Words(SellLine), BuySellPage.Faded);
        _page.Text(0x50, 0x1B, Words(PurchaseLine), BuySellPage.Ink);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
    }

    /// <summary>The menu with Sell dark (the pack list in use).</summary>
    private void ShowSelling()
    {
        _page.Text(0x50, 0x1B, Words(PurchaseLine), BuySellPage.Faded);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
        _page.Text(0x50, 0x23, Words(SellLine), BuySellPage.Ink);
    }

    /// <summary>The first drawing: the page, both lists, and the day's stock (which seeds the generator).</summary>
    // orig: trade-screen-paint R032+0x078E (1462:078E) draft (byte-matched function); the stock loop is Trading.Stock
    private void DrawScreen()
    {
        _page.DrawBackground(Party);
        _page.Text(0x49, 9, PurseLine(FullDrawing.Purse), BuySellPage.Ink);
        _page.Text(0x49, 0x12, BartersLine(FullDrawing), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, Words(PurchaseLine), BuySellPage.Ink);
        _page.Text(0x50, 0x23, Words(SellLine), BuySellPage.Faded);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
        DrawMenuTail(FullDrawing);
        _page.Text(0x81, 0x83, _page.Ui.Format(FullDrawing.Has, Party.Slot(Member).ShortName), BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, 0x93 + i * 8, PackLine(FullDrawing.PackItem, pack[i]), BuySellPage.Ink);
        }
        // C: the pack lines above were priced before the stock listing seeds the generator (price-to-sell draws under the clock seed, in a flag-8 place).
        // orig: R032+0x0ACD: DS:86A2[DS:8850] = k for every item for sale (BarterSession.ListStock)
        var stock = _s.ListStock();
        for (var i = 0; i < stock.ListedPrices.Count; i++)
        {
            var code = stock.Codes[i];
            // C: the first listing places its lines with the pack list's top DS:8852[DS:884E] instead of the shop list's (both 0 on entry).
            _page.Text(0x55, (i - _s.PackTop) * 8 + 0x55, ShopLine(FullDrawing.ShopItem, code, stock.ListedPrices[i]), BuySellPage.Ink);
        }
        _page.Light(0x53, 0x55, 0xB8);
        _s.PackShown(pack.Count);
    }

    /// <summary>The whole page again (after another member is chosen or a character sheet).</summary>
    // orig: trade-screen-repaint R032+0x0B30 (1462:0B30) draft (byte-matched function)
    // C: R032+0x0B30 loads the picture without its palette, but none of its three callers leaves another palette on: after a sheet
    //   (a click on a member's row, R032+0x064D) R032+0x0657..0x066F loads the picture and its palette (root 047C:058B) first; after
    //   the menu bar with the party icons stale (DS:9C46, set by the party information's close R034+0x119C: F6 or the Party menu)
    //   R032+0x06CC..0x06E4 does the same; D (R032+0x1384..0x13CB) follows nothing. The bar's other screens over the barter draw boxes
    //   only (Quit to DOS, the difficulty notice, the marching order's box) and Save / Load are off (DS:9920 / 9934 = 2). vis10
    //   session D-af566f: the original's palette after the sheet is the barter's (0 entries differ from before it), as here.
    private void Redraw()
    {
        _page.DrawBackground(Party);
        _page.Text(0x49, 9, PurseLine(RedrawTexts.Purse), BuySellPage.Ink);
        // C: the redraw prints this line at x 0x4E, the first drawing at 0x49.
        _page.Text(0x4E, 0x12, BartersLine(RedrawTexts), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, Words(PurchaseLine), _s.Buying ? BuySellPage.Ink : BuySellPage.Faded);
        _page.Text(0x50, 0x23, Words(SellLine), _s.Buying ? BuySellPage.Faded : BuySellPage.Ink);
        _page.Text(0x50, 0x2B, BlankLine, BuySellPage.Faded);
        DrawMenuTail(RedrawTexts);
        _page.Text(0x81, 0x83, _page.Ui.Format(RedrawTexts.Has, Party.Slot(Member).ShortName), BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = _s.PackTop; i < _s.PackTop + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _s.PackTop) * 8 + 0x93, PackLine(RedrawTexts.PackItem, pack[i]), BuySellPage.Ink);
        }
        for (var i = _s.ShopTop; i < _s.ShopTop + 4 && i < _s.Stock.Codes.Count; i++)
        {
            var code = _s.Stock.Codes[i];
            _page.Text(0x55, (i - _s.ShopTop) * 8 + 0x55, ShopLine(RedrawTexts.ShopItem, code, _s.BuyPrice(code)), BuySellPage.Ink);
        }
        if (_s.Buying)
        {
            _page.Light(0x52, _s.ShopRow * 8 + 0x55, 0xB9);
        }
        else
        {
            _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
        }
        _s.PackShown(pack.Count);
    }

    private void DrawMenuTail(PageTexts texts)
    {
        _page.Text(0x50, 0x33, Words(texts.Another), BuySellPage.Ink);
        _page.Text(0x50, 0x3B, Words(texts.Leave), BuySellPage.Ink);
        _page.Text(0x7A, 0x45, _page.Ui.Format(texts.Offers, ShopName), BuySellPage.Ink);
    }

    /// <summary>Four of the member's stacks with what the shop pays.</summary>
    // orig: member-items-paint R032+0x1770 (1462:1770) draft (byte-matched function)
    private void DrawPackItems()
    {
        _page.Restore(0x72, 0x93, 0xB6, 0x20);
        var pack = PackOf(Member);
        for (var i = _s.PackTop; i < _s.PackTop + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _s.PackTop) * 8 + 0x93, PackLine(PackListItem, pack[i]), BuySellPage.Ink);
        }
    }

    /// <summary>Four of the goods with their prices (the original prints the price only three wide here).</summary>
    // orig: shop-items-paint R032+0x1870 (1462:1870) draft (byte-matched function)
    private void DrawShopItems()
    {
        _page.Restore(0x52, 0x55, 0xB9, 0x20);
        for (var i = _s.ShopTop; i < _s.ShopTop + 4 && i < _s.Stock.Codes.Count; i++)
        {
            var code = _s.Stock.Codes[i];
            _page.Text(0x55, (i - _s.ShopTop) * 8 + 0x55, ShopLine(ShopListItem, code, _s.BuyPrice(code)), BuySellPage.Ink);
        }
    }

    // orig: trade-screen-keys R032+0x0F60 draft: gfx-rect-copy(backdrop-window, 0x49, 9, 0xEE, 9) and the purse line again, after P and S
    private void RedrawPurse(string format)
    {
        _page.Restore(0x49, 9, 0xEE, 9);
        _page.Text(0x49, 9, PurseLine(format), BuySellPage.Ink);
    }

    private string Words(string id) => _page.Ui.Words(id);

    private string PurseLine(string format)
    {
        var purse = Party.Purse;
        return _page.Ui.Format(format, Party.Slot(Leader).ShortName, purse.Florins, purse.Groschen, purse.Pfennigs, purse.TotalPfennigs);
    }

    // C: he/she and himself/herself follow the leader's sex (party[leader].female) even when naming another member.
    private string BartersLine(PageTexts texts)
    {
        var leader = Party.Slot(Leader);
        var member = Party.Slot(Member).ShortName;
        return Leader != Member
            ? _page.Ui.Format(texts.BartersFor, Words(leader.IsFemale ? texts.She : texts.He), member, ShopName)
            : _page.Ui.Format(texts.BartersForSelf, member, Words(leader.IsFemale ? texts.Herself : texts.Himself), ShopName);
    }

    /// <summary>"%5upf  %Fs  (%3d) %2dq": what the shop pays for one, the name, the count and the quality.</summary>
    private string PackLine(string format, Core.Characters.ItemStack stack)
    {
        var code = stack.Code & 0xFFF;
        // C: sprintf arguments right to left: quality, count, name, then price-to-sell (a flag-8 draw per line).
        var price = _s.SellPrice(stack);
        return _page.Ui.Format(format, price, _page.Ui.ItemNameArg(code, _state.Item(code).Name), stack.Count, stack.Quality);
    }

    /// <summary>"%5upf  %Fs  %3dq  %3dlbs" (shop-items-paint: "%3dpf ..."): the price, the name, the quality offered and the weight.</summary>
    // C: "%5upf" prints the price word unsigned; shop-items-paint's "%3dpf" signed, three wide (OriginalFormat takes the low 16 bits).
    private string ShopLine(string format, int code, int price)
    {
        var d = _state.Item(code);
        return _page.Ui.Format(format, price, _page.Ui.ItemNameArg(code, d.Name), _s.OfferedQuality(code), (sbyte)d.Weight);
    }
}
