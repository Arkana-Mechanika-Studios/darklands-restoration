using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Simulation;

namespace Darklands.Presentation;

/// <summary>
/// Sharing out loot: the pile of found items above, a member's pack below. G gets the lit
/// item, P puts one of the lit stack back on the pile, D shows the next member, L or Esc
/// leaves, arrows move and switch lists. The found cash goes into the purse on arrival; what
/// is left on the pile stays in the caller's list.
/// </summary>
// orig: loot overlay R040 = R040 (linked paragraph 1462, payload = offset) draft (the byte match 9/10); rules in LootSharing; checked visually with Darklands.Host.Headless screen:loot
public sealed class LootScreen
{
    // C: menu rows give G, P, D, Esc; row 4 gives no key. There is no party-column click on this screen.
    private static readonly int[] MenuKeys = [0x2247, 0x1950, 0x2044, Keys.Escape];

    // the menu lines (DS:4C42.. of R040), the key letter after 0x8C in colour 12
    private const string GetLine = "exe:DS:4C42";
    private const string PutLine = "exe:DS:4C64";
    private const string DistributeLine = "exe:DS:4C8A";
    private const string LeaveLine = "exe:DS:4CAE";

    // each drawing function of R040 loads its own copies: the full drawing R040+0x06E2, the redraw R040+0x0952, the list redraws
    //   R040+0x1372 (pack) and R040+0x145E (pile); "The party finds %dfl, %dgr, %dpf in cash.", "The loot contains...", "%s currently has..." /
    //   "%s has...", "%Fs  (%3d) %2d-Qual" for the pack, then for the pile
    private static readonly (string Cash, string Loot, string Has, string PackItem, string PileItem) FullDrawing =
        ("exe:DS:4CC8", "exe:DS:4CF2", "exe:DS:4D07", "exe:DS:4D1B", "exe:DS:4D2F");

    private static readonly (string Cash, string Loot, string Has, string PackItem, string PileItem) RedrawTexts =
        ("exe:DS:4D54", "exe:DS:4D7E", "exe:DS:4D93", "exe:DS:4D9D", "exe:DS:4DB1");

    private const string PackListItem = "exe:DS:4DC5";
    private const string PileListItem = "exe:DS:4DD9";

    private readonly BuySellPage _page;
    private readonly GameState _state;
    private readonly IList<ItemStack> _pile;
    private readonly LootSession _s;

    public LootScreen(BuySellPage page, GameState state, IList<ItemStack> pile)
    {
        _page = page;
        _state = state;
        _pile = pile;
        _s = new LootSession(state, pile);
    }

    private Party Party => _state.Party;

    /// <summary>The battle's test of D for a slot gone from the battlefield: shows the "can't view" message and answers true (BattleScreens.LootSkipsMember).</summary>
    public Func<int, bool>? CannotView { get; init; }

    private int Member => _s.Member;

    private Pack PackOf(int member) => Party.Slot(member).Pack;

    /// <summary>Runs the screen until the player leaves; returns the caller's cash as the screen left it (florins 0, groschen and pfennigs capped at 10).</summary>
    // orig: loot-split-entry R040+0x0000 (1462:0000) and R040+0x01C8 (1462:01C8) draft (both byte-matched); the pile is the caller's list, changed in place (the original's count byte through DS:8DC8)
    // C: the battle code's own cash (the pointer it passed) is what this returns; IGameScreens.Loot is called by the battle code (not modelled here), which does not take it back.
    public Money Run(Money found)
    {
        _s.Start(ref found);
        DrawScreen();
        // orig: R040+0x01C8 `while (mouse-button-state & 1);` before the loop
        _page.WaitForRelease();
        // host comfort (GameUi.SelectionList, the mouse wheel's ↑ / ↓): the pile's and the member's lists
        using var wheel = _page.Ui.UseSelectionList(static () => true);
        var lit = -1;
        try
        {
            while (true)
            {
                // DIVERGENCE: root 0000:0000 is GameUi.Poll's (MenuBar, HotKeys).
                var key = _page.KeyAfterTheMouse(MouseStep(ref lit));
                if (key == Keys.Escape || BuySellPage.Is(key, 'L'))
                {
                    return found;
                }
                if (key != 0)
                {
                    HandleKey(key);
                }
            }
        }
        finally
        {
            // orig: R040+0x06C8 (1462:06C8) draft (byte-matched function): fonts back (host state, not modelled) and leader = DS:8DBA.
            _s.End();
        }
    }

    // ---- input ------------------------------------------------------------------------

    /// <summary>The mouse tests of one pass of the loop, in their order, on the button's state; the key they give (0: none).</summary>
    // orig: R040+0x01C8 draft (byte-matched), the mouse tests in loop order: the menu (BuySellPage.MenuStep); a row of either list
    // lights at once and the button is then waited out; an arrow waits the button out first, then gives Up/Down for its list
    // (no party column here); tests: BuySellInputTests
    private int MouseStep(ref int lit)
    {
        var key = _page.MenuStep(ref lit, MenuKeys, 8);
        if (_page.HeldRow(_page.LowerRow) is { } packRow)
        {
            _s.ChoosePackRow(packRow);
            ShowPacking();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.HeldRow(_page.UpperRow) is { } pileRow)
        {
            _s.ChoosePileRow(pileRow);
            ShowGetting();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x52, _s.PileRow * 8 + 0x55, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.Held(_page.LowerUp))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _s.UseList(pile: false);
        }
        if (_page.Held(_page.LowerDown))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _s.UseList(pile: false);
        }
        if (_page.Held(_page.UpperUp))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _s.UseList(pile: true);
        }
        if (_page.Held(_page.UpperDown))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _s.UseList(pile: true);
        }
        return key;
    }

    // orig: R040+0x0C22 (1462:0C22) draft (unmatched: cross-jumped tails; behaviour checked against asm); every case kept (Alt-Q, P/p, D/d, G/g, Up, Left/Right, Down); other keys do nothing
    private void HandleKey(int key)
    {
        // C: case 0x1000 (Alt-Q): R040+0x06C8 then exit-to-system.
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
            DistributeToTheNextMember();
        }
        else if (BuySellPage.Is(key, 'G'))
        {
            Get();
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

    // orig: R040+0x0C22 draft, case P (LootSession.Put)
    private void Put()
    {
        if (!_s.Put())
        {
            return;
        }
        DrawPileItems();
        DrawPackItems();
        _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
    }

    // orig: R040+0x0C22 draft, case G (LootSession.Get)
    private void Get()
    {
        if (!_s.Get())
        {
            return;
        }
        DrawPileItems();
        DrawPackItems();
        _page.Light(0x52, _s.PileRow * 8 + 0x55, 0xFA);
    }

    /// <summary>D: the next member in the party who is neither dead nor missing (LootSession.NextMember).</summary>
    // orig: R040+0x0C22 draft, case D (0x2044/0x2064): the party drawn first, then the search, the page again
    private void DistributeToTheNextMember()
    {
        _page.DrawParty(Party);
        _s.NextMember(CannotView);
        Redraw();
        _page.Recolor(0x50, 0x2B, 0xC8, 9, BuySellPage.Ink, BuySellPage.Lit);
    }

    // orig: R040+0x0C22 draft, case 0x4800 (LootSession.MoveUp)
    private void MoveUp() => DrawMove(_s.MoveUp(), 0x69, 0xB9);

    /// <summary>Down: unlike the other two screens the cursor is only kept within the four rows, not within the list.</summary>
    // orig: R040+0x0C22 draft, case 0x5000 (LootSession.MoveDown)
    private void MoveDown() => DrawMove(_s.MoveDown(), 0x6E, 0xC8);

    // what a cursor key draws: the list that scrolled with its light, or the lights again (the clear's height and the pack light's width
    //   differ between Up and Down, as the original's cases)
    private void DrawMove(ListMove move, int clearHeight, int packLightWidth)
    {
        switch (move)
        {
            case ListMove.UpperScrolled:
                DrawPileItems();
                _page.Light(0x52, _s.PileRow * 8 + 0x55, 0xFA);
                break;
            case ListMove.LowerScrolled:
                DrawPackItems();
                _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                break;
            default:
                _page.Recolor(0x3C, 0x55, 0x103, clearHeight, BuySellPage.Lit, BuySellPage.Ink);
                if (_s.UsingPile)
                {
                    _page.Light(0x52, _s.PileRow * 8 + 0x55, 0xFA);
                }
                else
                {
                    _page.Light(0x72, _s.PackRow * 8 + 0x93, packLightWidth);
                }
                break;
        }
    }

    // orig: R040+0x0C22 draft, cases 0x4B00/0x4D00 (no switch to an empty pile; LootSession.SwitchLists)
    private void SwitchLists()
    {
        switch (_s.SwitchLists())
        {
            case ListSwitch.ToLower:
                ShowPacking();
                _page.Unlight(0x52, _s.PileRow * 8 + 0x55, 0xFA);
                _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xFA);
                break;
            case ListSwitch.ToUpper:
                ShowGetting();
                _page.Unlight(0x72, _s.PackRow * 8 + 0x93, 0xB9);
                _page.Light(0x52, _s.PileRow * 8 + 0x55, 0xFA);
                break;
        }
    }

    // ---- drawing ------------------------------------------------------------------------

    private void ShowPacking()
    {
        _page.Text(0x50, 0x1B, Words(GetLine), BuySellPage.Faded);
        _page.Text(0x50, 0x23, Words(PutLine), BuySellPage.Ink);
    }

    private void ShowGetting()
    {
        _page.Text(0x50, 0x23, Words(PutLine), BuySellPage.Faded);
        _page.Text(0x50, 0x1B, Words(GetLine), BuySellPage.Ink);
    }

    // orig: R040+0x06E2 (1462:06E2) draft (byte-matched function)
    // C: R040 calls the panel's frames and rows (root 0000:1588 / 0x1716) only for D (R040+0x0D8B, R040+0x0D96): the screen and its
    //   redraw copy the picture's part right of the column, and the party column stays as the screen before drew it (over a battle: the battle's
    //   panel, its order letters and a dead member's hatching down to the frame; vis10 session D-56a675)
    private void DrawScreen()
    {
        _page.DrawBackground(Party, drawParty: false);
        _page.Text(0x49, 9, CashLine(FullDrawing.Cash), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, Words(GetLine), BuySellPage.Ink);
        _page.Text(0x50, 0x23, Words(PutLine), BuySellPage.Faded);
        DrawMenuTail();
        _page.Text(0x6B, 0x45, Words(FullDrawing.Loot), BuySellPage.Ink);
        _page.Text(0x77, 0x83, _page.Ui.Format(FullDrawing.Has, Party.Slot(Member).ShortName), BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, 0x93 + i * 8, ItemLine(FullDrawing.PackItem, pack[i]), BuySellPage.Ink);
        }
        for (var i = 0; i < _pile.Count && i < 4; i++)
        {
            // C: the first drawing places the pile's lines with the pack list's top (pack-top-row) instead of the pile's (both 0 on entry).
            _page.Text(0x55, (i - _s.PackTop) * 8 + 0x55, ItemLine(FullDrawing.PileItem, _pile[i]), BuySellPage.Ink);
        }
        _page.Light(0x53, 0x55, 0xFA);
        _s.PackShown(pack.Count);
    }

    // orig: R040+0x0952 (1462:0952) draft (byte-matched function)
    // C: R040+0x0952 loads the picture without its palette, but its only caller is D (R040+0x0DE1, after the panel R040+0x0D88 and at
    //   most the notice R022+0x04E4 for a member gone through an exit), and nothing changes the palette while the loot page is up: the
    //   page's full drawing R040+0x06E2 set it (R040+0x0703), the party information is off in a battle (DS:987F = 2, R002+0x0223..0x0258)
    //   and the menu titles and DS:9893 are 2 (R014+0xE422..0xE42D). vis10 session D-56a675: the palettes agree after D (0 entries).
    private void Redraw()
    {
        _page.DrawBackground(Party, drawParty: false);
        _page.Text(0x49, 9, CashLine(RedrawTexts.Cash), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, Words(GetLine), _s.UsingPile ? BuySellPage.Ink : BuySellPage.Faded);
        _page.Text(0x50, 0x23, Words(PutLine), _s.UsingPile ? BuySellPage.Faded : BuySellPage.Ink);
        DrawMenuTail();
        // C: the redraw prints this heading at x 0x57, the first drawing at 0x6B.
        _page.Text(0x57, 0x45, Words(RedrawTexts.Loot), BuySellPage.Ink);
        _page.Text(0x77, 0x83, _page.Ui.Format(RedrawTexts.Has, Party.Slot(Member).ShortName), BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = _s.PackTop; i < _s.PackTop + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _s.PackTop) * 8 + 0x93, ItemLine(RedrawTexts.PackItem, pack[i]), BuySellPage.Ink);
        }
        for (var i = _s.PileTop; i < _s.PileTop + 4 && i < _pile.Count; i++)
        {
            _page.Text(0x55, (i - _s.PileTop) * 8 + 0x55, ItemLine(RedrawTexts.PileItem, _pile[i]), BuySellPage.Ink);
        }
        if (_s.UsingPile)
        {
            _page.Light(0x52, _s.PileRow * 8 + 0x55, 0xFA);
        }
        else
        {
            _page.Light(0x72, _s.PackRow * 8 + 0x93, 0xB7);
        }
        _s.PackShown(pack.Count);
    }

    private void DrawMenuTail()
    {
        _page.Text(0x50, 0x2B, Words(DistributeLine), BuySellPage.Ink);
        _page.Text(0x50, 0x33, Words(LeaveLine), BuySellPage.Ink);
    }

    // orig: R040+0x1372 (1462:1372) draft (byte-matched function)
    private void DrawPackItems()
    {
        _page.Restore(0x72, 0x93, 0xB6, 0x20);
        var pack = PackOf(Member);
        for (var i = _s.PackTop; i < _s.PackTop + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _s.PackTop) * 8 + 0x93, ItemLine(PackListItem, pack[i]), BuySellPage.Ink);
        }
    }

    // orig: R040+0x145E (1462:145E) draft (byte-matched function)
    private void DrawPileItems()
    {
        _page.Restore(0x52, 0x55, 0xB6, 0x20);
        for (var i = _s.PileTop; i < _s.PileTop + 4 && i < _pile.Count; i++)
        {
            _page.Text(0x55, (i - _s.PileTop) * 8 + 0x55, ItemLine(PileListItem, _pile[i]), BuySellPage.Ink);
        }
    }

    private string Words(string id) => _page.Ui.Words(id);

    private string CashLine(string format) => _page.Ui.Format(format, _s.Cash.Florins, _s.Cash.Groschen, _s.Cash.Pfennigs);

    /// <summary>"%Fs  (%3d) %2d-Qual": the item's name, the count, the quality.</summary>
    private string ItemLine(string format, ItemStack stack) =>
        _page.Ui.Format(format, _page.Ui.ItemNameArg(stack.Code & 0xFFF, _state.Item(stack.Code & 0xFFF).Name), stack.Count, stack.Quality);
}
