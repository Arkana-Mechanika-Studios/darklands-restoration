using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Simulation;

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
    // C: menu rows give G, P, C, Esc; row 4 (below Leave) gives no key. The L key itself is not handled by stash-screen-keys (only Esc leaves).
    private static readonly int[] MenuKeys = [0x2247, 0x1950, 0x2E43, Keys.Escape];

    // the menu lines of R035, the key letter after 0x8C in colour 12
    private const string GetLine = "exe:DS:37F6";
    private const string PutLine = "exe:DS:3810";

    // each drawing function of R035 loads its own copies: stash-screen-paint R035+0x0510 and stash-screen-repaint R035+0x0782: "Cache another
    //   person's items", "Leave", "%s holds the purse of %dfl, %dgr, %dpf.", "The cache contains...", "%s currently has..." / "%s has...",
    //   "%Fs  (%3d) %2d-Qual" for the pack, then for the store
    private static readonly (string Another, string Leave, string Purse, string Cache, string Has, string PackItem, string StoreItem) FullDrawing =
        ("exe:DS:3863", "exe:DS:3883", "exe:DS:383B", "exe:DS:388C", "exe:DS:38A2", "exe:DS:38B6", "exe:DS:38CA");

    private static readonly (string Another, string Leave, string Purse, string Cache, string Has, string PackItem, string StoreItem) RedrawTexts =
        ("exe:DS:3917", "exe:DS:3937", "exe:DS:38EF", "exe:DS:3940", "exe:DS:3956", "exe:DS:3960", "exe:DS:3974");

    // member-items-paint R035+0x11AC ("%Fs  (%3d) %2dq") and stash-items-paint R035+0x1292
    private const string PackListItem = "exe:DS:3988";
    private const string StoreListItem = "exe:DS:3998";

    // the screen's model: per member, kept between visits (the original's statics)
    private readonly StoreSession _s = new(state);

    private Party Party => state.Party;

    private int Member => _s.Member;

    private Pack PackOf(int member) => Party.Slot(member).Pack;

    /// <summary>Runs the screen at the current place until the player leaves; the store is written back.</summary>
    // orig: stash-screen R035+0x0000 (1462:0000) matched; main loop (its argument DS:89F8 is unused)
    public void Run()
    {
        // DIVERGENCE: in the open country the original indexes locations[-1] (location-now −1); the screen does not open here.
        if (state.Here is not { } place)
        {
            return;
        }
        _s.Start();
        DrawScreen(place);
        // orig: R035+0x0000 `while (mouse-button-state & 1);` before the loop (the click that opened the screen is waited out)
        page.WaitForRelease();
        // host comfort (GameUi.SelectionList, the mouse wheel's ↑ / ↓): the store's and the member's lists
        using var wheel = page.Ui.UseSelectionList(static () => true);
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
            // orig: stash-screen-close R035+0x04BC (1462:04BC) matched: save (or forget an empty store) and leader = DS:89EE; fonts are host state (not modelled).
            _s.End(place);
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
            _s.ChoosePackRow(packRow);
            ShowPacking();
            page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            page.Light(0x72, _s.PackRow * 8 + 0x93, 0xFA);
            page.WaitForRelease();
        }
        if (page.HeldRow(BuySellPage.UpperRowAt) is { } storeRow)
        {
            _s.ChooseStoreRow(storeRow);
            ShowGetting();
            page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xFA);
            page.WaitForRelease();
        }
        if (page.Held(BuySellPage.OnLowerUpArrow))
        {
            page.WaitForRelease();
            key = Keys.Up;
            _s.UseList(store: false);
        }
        if (page.Held(BuySellPage.OnLowerDownArrow))
        {
            page.WaitForRelease();
            key = Keys.Down;
            _s.UseList(store: false);
        }
        if (page.Held(BuySellPage.OnUpperUpArrow))
        {
            page.WaitForRelease();
            key = Keys.Up;
            _s.UseList(store: true);
        }
        if (page.Held(BuySellPage.OnUpperDownArrow))
        {
            page.WaitForRelease();
            key = Keys.Down;
            _s.UseList(store: true);
        }
        // C: the original loops `row = pointer-row / 0x28; while (row != -1) { row = member-sheet-overlay-entry(row); panel redrawn }`; member-sheet-screen returns -1 on every
        //   path (its only two returns give -1), the member shown changing inside the sheet: the loop runs once, as here, and IGameScreens.CharacterSheet
        //   needs no result.
        if (page.HeldRow(BuySellPage.MemberAt) is { } member)
        {
            screens.CharacterSheet(member);
            Redraw();
            page.WaitForRelease();
        }
        return key;
    }

    // orig: stash-screen-keys R035+0x0A46 (1462:0A46) matched; every case kept (Alt-Q, P/p, D/d, G/g, C/c, Up, Left/Right, Down); other keys do nothing
    private void HandleKey(int key)
    {
        // C: case 0x1000 (Alt-Q): stash-screen-close then exit-to-system (the finally block writes the store back).
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

    // orig: stash-screen-keys R035+0x0A46 matched, case P (StoreSession.Put)
    private void Put()
    {
        switch (_s.Put(state.Here!))
        {
            case StorePut.NoRoom:
                page.ShowMessage(Storing.NoRoomMessage);
                return;
            case StorePut.Nothing:
                return;
        }
        DrawStoreItems();
        DrawPackItems();
        page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
    }

    /// <summary>D: one item of the <em>leader's</em> stack at the member's lit row is thrown away (StoreSession.DropFromLeader).</summary>
    // orig: stash-screen-keys R035+0x0A46 matched, case D (0x2044/0x2064)
    private void DropFromLeader()
    {
        if (!_s.DropFromLeader())
        {
            return;
        }
        DrawPackItems();
        page.Light(0x50, _s.PackRow * 8 + 0x1B, 0x5F);
    }

    // orig: stash-screen-keys R035+0x0A46 matched, case G (StoreSession.Get)
    private void Get()
    {
        if (!_s.Get())
        {
            return;
        }
        DrawStoreItems();
        DrawPackItems();
        page.Light(0x52, _s.StoreRow * 8 + 0x55, 0x5F);
    }

    /// <summary>C: the next present member (StoreSession.NextMember).</summary>
    // orig: stash-screen-keys R035+0x0A46 matched, case C (0x2E43/0x2E63): the party drawn first
    private void ShowTheNextMember()
    {
        page.DrawParty(Party);
        _s.NextMember();
        Redraw();
        // C: copied from the barter screen: the highlight lands on the Leave line (0x33), not on "Cache another person's items" (0x2B).
        page.Recolor(0x50, 0x33, 0xC8, 9, BuySellPage.Ink, BuySellPage.Lit);
    }

    // orig: stash-screen-keys R035+0x0A46 matched, case 0x4800 (StoreSession.MoveUp)
    private void MoveUp()
    {
        switch (_s.MoveUp())
        {
            case ListMove.UpperScrolled:
                DrawStoreItems();
                page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xB9);
                break;
            case ListMove.LowerScrolled:
                DrawPackItems();
                page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                break;
            default:
                page.Recolor(0x3C, 0x55, 0x103, 0x69, BuySellPage.Lit, BuySellPage.Ink);
                if (_s.UsingStore)
                {
                    page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xC3);
                }
                else
                {
                    page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                }
                break;
        }
    }

    // orig: stash-screen-keys R035+0x0A46 matched, case 0x5000 (both cursors are kept inside their lists; StoreSession.MoveDown)
    private void MoveDown()
    {
        switch (_s.MoveDown())
        {
            case ListMove.UpperScrolled:
                DrawStoreItems();
                page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xB9);
                break;
            case ListMove.LowerScrolled:
                DrawPackItems();
                page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                break;
            default:
                page.Recolor(0x3C, 0x55, 0x103, 0x6E, BuySellPage.Lit, BuySellPage.Ink);
                if (_s.UsingStore)
                {
                    page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xC8);
                }
                else
                {
                    page.Light(0x72, _s.PackRow * 8 + 0x93, 0xC8);
                }
                break;
        }
    }

    // orig: stash-screen-keys R035+0x0A46 matched, cases 0x4B00/0x4D00 (no switch to an empty store; StoreSession.SwitchLists)
    private void SwitchLists()
    {
        switch (_s.SwitchLists())
        {
            case ListSwitch.ToLower:
                ShowPacking();
                page.Unlight(0x52, _s.StoreRow * 8 + 0x55, 0xB9);
                page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                break;
            case ListSwitch.ToUpper:
                ShowGetting();
                page.Unlight(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xB9);
                break;
        }
    }

    // ---- drawing ------------------------------------------------------------------------

    /// <summary>The menu with Put dark (the pack in use).</summary>
    private void ShowPacking()
    {
        page.Text(0x50, 0x1B, Words(GetLine), BuySellPage.Faded);
        page.Text(0x50, 0x23, Words(PutLine), BuySellPage.Ink);
    }

    /// <summary>The menu with Get dark (the store in use).</summary>
    private void ShowGetting()
    {
        page.Text(0x50, 0x23, Words(PutLine), BuySellPage.Faded);
        page.Text(0x50, 0x1B, Words(GetLine), BuySellPage.Ink);
    }

    /// <summary>The first drawing; the store is read here.</summary>
    // orig: stash-screen-paint R035+0x0510 (1462:0510) matched; reads the store (stash-load)
    private void DrawScreen(Core.World.Location place)
    {
        page.DrawBackground(Party);
        page.Text(0x49, 9, PurseLine(FullDrawing.Purse), BuySellPage.Ink);
        page.Text(0x50, 0x1B, Words(GetLine), BuySellPage.Faded);
        page.Text(0x50, 0x23, Words(PutLine), BuySellPage.Ink);
        DrawMenuTail(FullDrawing.Another, FullDrawing.Leave);
        page.Text(0x6B, 0x45, Words(FullDrawing.Cache), BuySellPage.Ink);
        page.Text(0x77, 0x83, page.Ui.Format(FullDrawing.Has, Party.Slot(Member).ShortName), BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            page.Text(0x75, 0x93 + i * 8, ItemLine(FullDrawing.PackItem, pack[i].Code & 0xFFF, pack[i].Count, pack[i].Quality), BuySellPage.Ink);
        }
        // C: the pack lines are drawn from row 0 (not from the kept top) and the store's lines at (i − kept top), as the original does with its persistent statics.
        _s.Open(place);
        for (var i = 0; i < _s.Store.Count && i < 4; i++)
        {
            page.Text(0x55, (i - _s.StoreTop) * 8 + 0x55, StoreLine(FullDrawing.StoreItem, _s.Store[i]), BuySellPage.Ink);
        }
        page.Light(0x73, 0x93, 0xB7);
        _s.PackShown(pack.Count);
    }

    /// <summary>The whole page again (another member, or after a character sheet).</summary>
    // orig: stash-screen-repaint R035+0x0782 (1462:0782) matched
    private void Redraw()
    {
        page.DrawBackground(Party);
        page.Text(0x49, 9, PurseLine(RedrawTexts.Purse), BuySellPage.Ink);
        page.Text(0x50, 0x1B, Words(GetLine), _s.UsingStore ? BuySellPage.Ink : BuySellPage.Faded);
        page.Text(0x50, 0x23, Words(PutLine), _s.UsingStore ? BuySellPage.Faded : BuySellPage.Ink);
        DrawMenuTail(RedrawTexts.Another, RedrawTexts.Leave);
        // C: the redraw prints this heading at x 0x57, the first drawing at 0x6B.
        page.Text(0x57, 0x45, Words(RedrawTexts.Cache), BuySellPage.Ink);
        page.Text(0x77, 0x83, page.Ui.Format(RedrawTexts.Has, Party.Slot(Member).ShortName), BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            page.Text(0x75, 0x93 + i * 8, ItemLine(RedrawTexts.PackItem, pack[i].Code & 0xFFF, pack[i].Count, pack[i].Quality), BuySellPage.Ink);
        }
        for (var i = 0; i < _s.Store.Count && i < 4; i++)
        {
            page.Text(0x55, (i - _s.StoreTop) * 8 + 0x55, StoreLine(RedrawTexts.StoreItem, _s.Store[i]), BuySellPage.Ink);
        }
        if (_s.UsingStore)
        {
            page.Light(0x52, _s.StoreRow * 8 + 0x55, 0xB7);
        }
        else
        {
            page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB7);
        }
        _s.PackShown(pack.Count);
    }

    private void DrawMenuTail(string another, string leave)
    {
        page.Text(0x50, 0x2B, Words(another), BuySellPage.Ink);
        page.Text(0x50, 0x33, Words(leave), BuySellPage.Ink);
    }

    /// <summary>Four of the member's stacks (here the quality reads "q", not "-Qual").</summary>
    // orig: member-items-paint R035+0x11AC (1462:11AC) matched (its lines say "q", the others "-Qual")
    private void DrawPackItems()
    {
        page.Restore(0x72, 0x93, 0xB6, 0x20);
        var pack = PackOf(Member);
        for (var i = _s.PackTop; i < _s.PackTop + 4 && i < pack.Count; i++)
        {
            page.Text(0x75, (i - _s.PackTop) * 8 + 0x93, ItemLine(PackListItem, pack[i].Code & 0xFFF, pack[i].Count, pack[i].Quality), BuySellPage.Ink);
        }
    }

    // orig: stash-items-paint R035+0x1292 (1462:1292) matched
    private void DrawStoreItems()
    {
        page.Restore(0x52, 0x55, 0xB6, 0x20);
        for (var i = _s.StoreTop; i < _s.StoreTop + 4 && i < _s.Store.Count; i++)
        {
            page.Text(0x55, (i - _s.StoreTop) * 8 + 0x55, StoreLine(StoreListItem, _s.Store[i]), BuySellPage.Ink);
        }
    }

    private string Words(string id) => page.Ui.Words(id);

    private string PurseLine(string format)
    {
        var purse = Party.Purse;
        return page.Ui.Format(format, Party.Slot(Party.LeaderIndex).ShortName, purse.Florins, purse.Groschen, purse.Pfennigs);
    }

    // C: the store's qual and cnt are signed chars printed with %d (a count of 200 prints −56).
    private string StoreLine(string format, StoredItem entry) => ItemLine(format, entry.Code, (sbyte)entry.Count, (sbyte)entry.Quality);   // the store keeps them as signed bytes

    /// <summary>"%Fs  (%3d) %2d-Qual" (or "...%2dq"): the item's name, the count, the quality.</summary>
    private string ItemLine(string format, int code, int count, int quality) => page.Ui.Format(format, page.Ui.ItemName(code, state.Item(code).Name), count, quality);
}
