using System.Globalization;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Rules;
using Darklands.Platform;

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

    private const string GetLine = "\u008C G\u00FFet an item from pile of loot";
    private const string PutLine = "\u008C P\u00FFut an item into the pile of loot";

    private readonly BuySellPage _page;
    private readonly GameState _state;
    private readonly IList<ItemStack> _pile;
    private readonly int[] _packCount = new int[5];
    private readonly bool[] _usingPile = [true, true, true, true, true];
    private readonly int[] _packTop = new int[5];
    private readonly int[] _pileTop = new int[5];
    private readonly int[] _packRow = new int[5];
    private readonly int[] _pileRow = new int[5];
    private Money _cash;
    private int _member;

    public LootScreen(BuySellPage page, GameState state, IList<ItemStack> pile)
    {
        _page = page;
        _state = state;
        _pile = pile;
    }

    private Party Party => _state.Party;

    /// <summary>The battle's test of D for a slot gone from the battlefield: shows the "can't view" message and answers true (BattleScreens.LootSkipsMember).</summary>
    public Func<int, bool>? CannotView { get; init; }

    private int Member => _member;

    private Pack PackOf(int member) => Party.Slot(member).Pack;

    /// <summary>Runs the screen until the player leaves; returns the caller's cash as the screen left it (florins 0, groschen and pfennigs capped at 10).</summary>
    // orig: loot_distribute R040+0x0000 (1462:0000) and R040+0x01C8 (1462:01C8) draft (both byte-matched); the pile is the caller's list, changed in place (the original's count byte through DS:8DC8)
    // C: the battle code's own cash (the pointer it passed) is what this returns; IGameScreens.Loot is called by the battle code (not modelled here), which does not take it back.
    public Money Run(Money found)
    {
        LootSharing.ReplaceFallenLeader(_state);
        LootSharing.PrepareFoundItems(_pile);
        var leaderBefore = Party.LeaderIndex;
        _member = Party.LeaderIndex;
        _cash = LootSharing.TakeCash(_state, ref found);
        DrawScreen();
        // orig: R040+0x01C8 `while (mouse_buttons & 1);` before the loop
        _page.WaitForRelease();
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
            Party.LeaderIndex = leaderBefore;
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
        if (_page.HeldRow(BuySellPage.LowerRowAt) is { } packRow)
        {
            _usingPile[Member] = false;
            _packRow[Member] = packRow;
            ShowPacking();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.HeldRow(BuySellPage.UpperRowAt) is { } pileRow)
        {
            _usingPile[Member] = true;
            _pileRow[Member] = pileRow;
            ShowGetting();
            _page.Recolor(0x3C, 0x55, 0xFA, 0x78, BuySellPage.Lit, BuySellPage.Ink);
            _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
            _page.WaitForRelease();
        }
        if (_page.Held(BuySellPage.OnLowerUpArrow))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _usingPile[Member] = false;
        }
        if (_page.Held(BuySellPage.OnLowerDownArrow))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _usingPile[Member] = false;
        }
        if (_page.Held(BuySellPage.OnUpperUpArrow))
        {
            _page.WaitForRelease();
            key = Keys.Up;
            _usingPile[Member] = true;
        }
        if (_page.Held(BuySellPage.OnUpperDownArrow))
        {
            _page.WaitForRelease();
            key = Keys.Down;
            _usingPile[Member] = true;
        }
        return key;
    }

    // orig: R040+0x0C22 (1462:0C22) draft (unmatched: cross-jumped tails; behaviour checked against asm); every case kept (Alt-Q, P/p, D/d, G/g, Up, Left/Right, Down); other keys do nothing
    private void HandleKey(int key)
    {
        // C: case 0x1000 (Alt-Q): R040+0x06C8 then quit_to_dos.
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

    // orig: R040+0x0C22 draft, case P; the rule is LootSharing.Put
    private void Put()
    {
        var index = _packTop[Member] + _packRow[Member];
        // C: only the list in use and n_items[who] != 0 are tested: a row past the pack's end puts its stale entry (LootSharing.Put).
        if (_usingPile[Member] || _packCount[Member] == 0)
        {
            return;
        }
        _packCount[Member] = LootSharing.Put(_state, _pile, Member, index);
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
        DrawPileItems();
        DrawPackItems();
        _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
    }

    // orig: R040+0x0C22 draft, case G; the rule is LootSharing.Get
    private void Get()
    {
        var index = _pileTop[Member] + _pileRow[Member];
        // DIVERGENCE: rows past the pile's end are ignored (see LootSharing.Get).
        if (!_usingPile[Member] || _pile.Count == 0 || index >= _pile.Count)
        {
            return;
        }
        _packCount[Member] = LootSharing.Get(_state, _pile, Member, index);
        // C: "top + row + 1 > count" (checked against asm 0EA7..0EB4).
        if (index + 1 > _pile.Count)
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
        DrawPileItems();
        DrawPackItems();
        _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
    }

    /// <summary>D: the next member in the party who is neither dead nor missing.</summary>
    // orig: R040+0x0C22 draft, case D (0x2044/0x2064)
    private void DistributeToTheNextMember()
    {
        _page.DrawParty(Party);
        // C: (DS:8DC4 + 1) % 5, skipping slots ≥ party_size and status 0 or 3 (checked against asm 0D9E..0DD9).
        // orig: R040+0x0DB4..0x0DC8: a slot whose combat floor test (cbt_floor, DS:A6B4, read as words: BattleScreens.LootSkipsMember) says
        //   −99 (gone through an exit) is skipped after thunk 09C0:1D51 = R022+0x04E4 ("You can't view ... at this time.") is called for it,
        //   before the status test; tests: SheetCannotViewTests
        // C: 0C22 against the asm (R040 seg 1462 0C22..123D): every case of the draft matches it (cross-jumped tails only); P, G and D return
        //   without mouse_show, Left/Right on an empty pile returns with the pointer still hidden (the host owns the pointer here).
        // DIVERGENCE: the original loops forever when no member qualifies; here the search stops after five slots.
        for (var tries = 0; tries < Party.MaxMembers; tries++)
        {
            _member = (_member + 1) % Party.MaxMembers;
            if (_member >= Party.Members.Count || CannotView?.Invoke(_member) == true)
            {
                continue;
            }
            if (Party.Slot(_member).Status is not (CharacterStatus.Empty or CharacterStatus.Dead))
            {
                break;
            }
        }
        Redraw();
        _page.Recolor(0x50, 0x2B, 0xC8, 9, BuySellPage.Ink, BuySellPage.Lit);
    }

    // orig: R040+0x0C22 draft, case 0x4800
    private void MoveUp()
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
            DrawPileItems();
            _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
        }
        else if (!_usingPile[Member] && _packRow[Member] < 0 && _packTop[Member] > 0)
        {
            _packRow[Member]++;
            _packTop[Member]--;
            DrawPackItems();
            _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
        }
        else
        {
            _pileRow[Member] = Math.Max(_pileRow[Member], 0);
            _packRow[Member] = Math.Max(_packRow[Member], 0);
            _page.Recolor(0x3C, 0x55, 0x103, 0x69, BuySellPage.Lit, BuySellPage.Ink);
            if (_usingPile[Member])
            {
                _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
            }
            else
            {
                _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
            }
        }
    }

    /// <summary>Down: unlike the other two screens the cursor is only kept within the four rows, not within the list.</summary>
    // orig: R040+0x0C22 draft, case 0x5000
    private void MoveDown()
    {
        if (_usingPile[Member])
        {
            _pileRow[Member]++;
        }
        else
        {
            _packRow[Member]++;
        }
        if (_usingPile[Member] && _pileRow[Member] >= 4 && _pileTop[Member] + _pileRow[Member] < _pile.Count)
        {
            _pileRow[Member]--;
            _pileTop[Member]++;
            DrawPileItems();
            _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
        }
        else if (!_usingPile[Member] && _packRow[Member] >= 4 && _packTop[Member] + _packRow[Member] < _packCount[Member])
        {
            _packRow[Member]--;
            _packTop[Member]++;
            DrawPackItems();
            _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
        }
        else
        {
            _pileRow[Member] = Math.Min(_pileRow[Member], 3);
            _packRow[Member] = Math.Min(_packRow[Member], 3);
            _page.Recolor(0x3C, 0x55, 0x103, 0x6E, BuySellPage.Lit, BuySellPage.Ink);
            if (_usingPile[Member])
            {
                _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
            }
            else
            {
                _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xC8);
            }
        }
    }

    // orig: R040+0x0C22 draft, cases 0x4B00/0x4D00 (no switch to an empty pile)
    private void SwitchLists()
    {
        if (_usingPile[Member])
        {
            ShowPacking();
            _page.Unlight(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
            _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xFA);
            _usingPile[Member] = false;
        }
        else if (_pile.Count != 0)
        {
            ShowGetting();
            _page.Unlight(0x72, _packRow[Member] * 8 + 0x93, 0xB9);
            _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
            _usingPile[Member] = true;
        }
    }

    // ---- drawing ------------------------------------------------------------------------

    private void ShowPacking()
    {
        _page.Text(0x50, 0x1B, GetLine, BuySellPage.Faded);
        _page.Text(0x50, 0x23, PutLine, BuySellPage.Ink);
    }

    private void ShowGetting()
    {
        _page.Text(0x50, 0x23, PutLine, BuySellPage.Faded);
        _page.Text(0x50, 0x1B, GetLine, BuySellPage.Ink);
    }

    // orig: R040+0x06E2 (1462:06E2) draft (byte-matched function)
    private void DrawScreen()
    {
        _page.DrawBackground(Party);
        _page.Text(0x49, 9, CashLine(), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, GetLine, BuySellPage.Ink);
        _page.Text(0x50, 0x23, PutLine, BuySellPage.Faded);
        DrawMenuTail();
        _page.Text(0x6B, 0x45, "The loot contains...", BuySellPage.Ink);
        _page.Text(0x77, 0x83, $"{Party.Slot(Member).ShortName} currently has...", BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = 0; i < 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, 0x93 + i * 8, ItemLine(pack[i]), BuySellPage.Ink);
        }
        for (var i = 0; i < _pile.Count && i < 4; i++)
        {
            // C: the first drawing places the pile's lines with the pack list's top (inv_top) instead of the pile's (both 0 on entry).
            _page.Text(0x55, (i - _packTop[Member]) * 8 + 0x55, ItemLine(_pile[i]), BuySellPage.Ink);
        }
        _page.Light(0x53, 0x55, 0xFA);
        _packCount[Member] = pack.Count;
    }

    // orig: R040+0x0952 (1462:0952) draft (byte-matched function)
    // DIVERGENCE: the original redraws the picture without set_palette; the picture's colours are applied here.
    private void Redraw()
    {
        _page.DrawBackground(Party);
        _page.Text(0x49, 9, CashLine(), BuySellPage.Ink);
        _page.Text(0x50, 0x1B, GetLine, _usingPile[Member] ? BuySellPage.Ink : BuySellPage.Faded);
        _page.Text(0x50, 0x23, PutLine, _usingPile[Member] ? BuySellPage.Faded : BuySellPage.Ink);
        DrawMenuTail();
        // C: the redraw prints this heading at x 0x57, the first drawing at 0x6B.
        _page.Text(0x57, 0x45, "The loot contains...", BuySellPage.Ink);
        _page.Text(0x77, 0x83, $"{Party.Slot(Member).ShortName} has...", BuySellPage.Ink);
        var pack = PackOf(Member);
        for (var i = _packTop[Member]; i < _packTop[Member] + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _packTop[Member]) * 8 + 0x93, ItemLine(pack[i]), BuySellPage.Ink);
        }
        for (var i = _pileTop[Member]; i < _pileTop[Member] + 4 && i < _pile.Count; i++)
        {
            _page.Text(0x55, (i - _pileTop[Member]) * 8 + 0x55, ItemLine(_pile[i]), BuySellPage.Ink);
        }
        if (_usingPile[Member])
        {
            _page.Light(0x52, _pileRow[Member] * 8 + 0x55, 0xFA);
        }
        else
        {
            _page.Light(0x72, _packRow[Member] * 8 + 0x93, 0xB7);
        }
        _packCount[Member] = pack.Count;
    }

    private void DrawMenuTail()
    {
        _page.Text(0x50, 0x2B, "\u008C D\u00FFistribute to a different person", BuySellPage.Ink);
        _page.Text(0x50, 0x33, "\u008C L\u00FFeave", BuySellPage.Ink);
    }

    // orig: R040+0x1372 (1462:1372) draft (byte-matched function)
    private void DrawPackItems()
    {
        _page.Restore(0x72, 0x93, 0xB6, 0x20);
        var pack = PackOf(Member);
        for (var i = _packTop[Member]; i < _packTop[Member] + 4 && i < pack.Count; i++)
        {
            _page.Text(0x75, (i - _packTop[Member]) * 8 + 0x93, ItemLine(pack[i]), BuySellPage.Ink);
        }
    }

    // orig: R040+0x145E (1462:145E) draft (byte-matched function)
    private void DrawPileItems()
    {
        _page.Restore(0x52, 0x55, 0xB6, 0x20);
        for (var i = _pileTop[Member]; i < _pileTop[Member] + 4 && i < _pile.Count; i++)
        {
            _page.Text(0x55, (i - _pileTop[Member]) * 8 + 0x55, ItemLine(_pile[i]), BuySellPage.Ink);
        }
    }

    private string CashLine() =>
        Invariant($"The party finds {_cash.Florins}fl, {_cash.Groschen}gr, {_cash.Pfennigs}pf in cash.");

    /// <summary>"name  (%3d) %2d-Qual".</summary>
    private string ItemLine(ItemStack stack) =>
        Invariant($"{_state.Item(stack.Code & 0xFFF).Name}  ({stack.Count,3}) {stack.Quality,2}-Qual");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
