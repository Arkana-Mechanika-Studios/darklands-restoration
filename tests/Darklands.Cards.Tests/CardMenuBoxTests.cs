using Darklands.Cards;

namespace Darklands.Cards.Tests;

/// <summary>
/// A choice in an item or saint pop-up box (card_menu R043+0x1BD2 / card_mouse R043+0x21D4): the box's handler table rates it,
/// whatever line the box belongs to; a choice there leaves the box's layout as the menu's (its handlers in opt_fn, its entries'
/// states in opt_on) until the card installs its own; Enter there rates the line with opt_fn[k], the box's handler for slot k % 4.
/// And options_block: the block of the cards that show card_options(options_block) is the game's CardContext.OptionsBlock.
/// </summary>
public sealed class CardMenuBoxTests
{
    /// <summary>A screen whose first menu is chosen in a box (by a click, or by Enter), the others choose line <paramref name="then"/>.</summary>
    private sealed class BoxScreen(int line, PopupBox box, int member, int slot, bool click, int then) : ICardScreen
    {
        public CardContext? Context { get; set; }

        public List<IReadOnlyList<OptionState>> Menus { get; } = [];

        public List<OptionHint> ShiftHints { get; } = [];

        public void Show(string cardName, string picture)
        {
        }

        public void ShowPicture(string picture)
        {
        }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture)
        {
        }

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null) =>
            Choose(block, options, hint, null);

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint)
        {
            Menus.Add(options.ToArray());
            var c = Context!;
            if (Menus.Count > 1)
            {
                return then;
            }
            // the pointer in the box: a Shift hint there asks the box's handler for the entry
            c.MenuBox = box;
            c.MenuMember = member;
            c.MenuSlot = slot;
            if (popupHint is not null)
            {
                ShiftHints.Add(popupHint(line));
            }
            var states = new OptionState[CardMenu.MaxLines];
            states[member * 4 + slot] = OptionState.Enabled;
            states.CopyTo(c.MenuBoxStates, 0);
            c.MenuPopup = click;
            return line;
        }

        public void Tell(string message, int member = OptionHint.WholeParty)
        {
        }

        public void PassTime(int hours)
        {
        }
    }

    private static (CardContext C, BoxScreen Screen) Context(int line, PopupBox box, int member, int slot, bool click, int then = 0)
    {
        var state = CardHarness.NewGame(seed: 5, location: 3);
        var screen = new BoxScreen(line, box, member, slot, click, then);
        var c = new CardContext(state, CardHarness.World!, screen) { Next = 1 };
        screen.Context = c;
        return (c, screen);
    }

    [Fact]
    public void AClickInABoxIsRatedByTheBoxsTableWhateverTheLine()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // line 1 has no pop-up of its own (076's sally port under the item box): the item table rates the choice
        var (c, screen) = Context(line: 1, PopupBox.Items, member: 2, slot: 1, click: true);
        var menu = new CardMenu(c, 1) { ItemBox = () => OptionHint.ChanceOf(30 + c.MenuSlot, c.MenuMember) };
        menu.Enable(0, 1);
        var got = -9;
        menu.Run(new MenuLine(_ => { }), new MenuLine(chance => { got = chance; c.Next = 2; }, OptionHint.Saying("line 1")));
        Assert.Equal(31, got);
        Assert.Equal(31, screen.ShiftHints.Single().Chance);

        // the saint box over the same line: the saint table
        (c, _) = Context(line: 1, PopupBox.Saints, member: 0, slot: 3, click: true);
        menu = new CardMenu(c, 1) { ItemBox = () => OptionHint.ChanceOf(30, 0), SaintBox = () => OptionHint.ChanceOf(50 + c.MenuSlot, 0) };
        menu.Enable(0, 1);
        menu.Run(new MenuLine(_ => { }), new MenuLine(chance => { got = chance; c.Next = 2; }));
        Assert.Equal(53, got);

        // a card whose loop calls item_fn[0][menu_slot] (R105, R111, R126, R127, R128, R132, R134): the item table even for the saint box
        (c, _) = Context(line: 1, PopupBox.Saints, member: 0, slot: 2, click: true);
        menu = new CardMenu(c, 1) { ItemBox = () => OptionHint.ChanceOf(30 + c.MenuSlot, 0), SaintBox = () => OptionHint.ChanceOf(50, 0), PopupRatedByItems = true };
        menu.Enable(0, 1);
        menu.Run(new MenuLine(_ => { }), new MenuLine(chance => { got = chance; c.Next = 2; }));
        Assert.Equal(32, got);
    }

    [Fact]
    public void WithoutABoxTableTheLinesPopupStillRatesIt()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context(line: 0, PopupBox.Items, member: 0, slot: 0, click: true);
        var menu = new CardMenu(c, 1);
        menu.Enable(0);
        var got = -9;
        menu.Run(new MenuLine(chance => { got = chance; c.Next = 2; }, OptionHint.Saying("hint"), () => OptionHint.ChanceOf(7, 0)));
        Assert.Equal(7, got);
    }

    [Fact]
    public void EnterInABoxRatesTheLineWithTheBoxsHandlerForSlotLineModFour()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // Enter on member 1's slot 0 entry of the item box over line 2: menu_popup stays 0, the card calls opt_fn[2], which is the box's
        // handler for slot 2 (item_fn[0][2]); menu_member and menu_slot are the entry's
        var (c, _) = Context(line: 2, PopupBox.Items, member: 1, slot: 0, click: false);
        var menu = new CardMenu(c, 1) { ItemBox = () => OptionHint.ChanceOf(40 + c.MenuSlot, c.MenuMember) };
        menu.Enable(0, 1, 2);
        var got = (-9, -9, -9);
        menu.Run(new MenuLine(_ => { }), new MenuLine(_ => { }),
            new MenuLine(chance => { got = (chance, c.MenuMember, c.MenuSlot); c.Next = 2; }, OptionHint.Saying("line 2")));
        Assert.Equal((42, 1, 0), got);
    }

    [Fact]
    public void EnterInABoxRunsSlotLineModFoursHandlerOnTheEntrysSlot()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // Enter on member 0's slot 1 entry of the item box over line 2: the loop calls opt_fn[2], the handler for slot 2 (e.g. R112+0x391A),
        // which reads card_items[menu_slot] with menu_slot still the entry's 1 (card_menu R043+0x1CAE)
        var (c, _) = Context(line: 2, PopupBox.Items, member: 0, slot: 1, click: false);
        var menu = new CardMenu(c, 1) { ItemBox = () => OptionHint.ChanceOf(10 * c.MenuSlot + c.MenuEntrySlot, c.MenuMember) };
        menu.Enable(0, 1, 2);
        var got = (-9, -9, -9);
        menu.Run(new MenuLine(_ => { }), new MenuLine(_ => { }),
            new MenuLine(chance => { got = (chance, c.MenuSlot, c.MenuEntrySlot); c.Next = 2; }, OptionHint.Saying("line 2")));
        Assert.Equal((21, 1, 1), got);
    }

    [Fact]
    public void ABareRetfBoxHandlerGivesTheValueLeftInAx()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // item_fn slots 2/3 bare retf (R112+0x1438 / 0x143A): Enter in the item box over line 2 rates it with slot 2's, a click on slot 0 with slot 0's
        foreach (var (click, expected) in new[] { (false, 0), (true, 55) })
        {
            var (c, _) = Context(line: 2, PopupBox.Items, member: 0, slot: 0, click: click);
            var menu = new CardMenu(c, 1) { ItemBox = () => c.MenuSlot < 2 ? OptionHint.ChanceOf(55, c.MenuMember) : c.EmptyBoxHandler() };
            menu.Enable(0, 1, 2);
            var got = -9;
            menu.Run(new MenuLine(_ => { }), new MenuLine(_ => { }), new MenuLine(chance => { got = chance; c.Next = 2; }, OptionHint.Saying("line 2")));
            Assert.Equal(expected, got);
        }
    }

    [Fact]
    public void AfterAChoiceInABoxTheUnlistedLinesHaveTheBoxsHandlersAndStates()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // the card lists lines 0..1; its reset enabled the others (as 076's); the choice in the box (member 1, slot 2: entry 6)
        // leaves opt_on[k] = the box's entry states and opt_fn[k] = the box's handler for slot k % 4 for every line it does not install
        var (c, screen) = Context(line: 0, PopupBox.Saints, member: 1, slot: 2, click: true, then: 6);
        var menu = new CardMenu(c, 1) { SaintBox = () => { c.Variables.Chosen[0] = c.MenuMember; return OptionHint.ChanceOf(60 + c.MenuSlot, 0); } };
        menu.Enable(Enumerable.Range(0, 20).ToArray());
        var rounds = 0;
        menu.EachRound = () => { if (++rounds == 3) { c.Next = 2; } };
        menu.Run(new MenuLine(_ => { }), new MenuLine(_ => { }));
        // round 2: lines 0, 1 stay the card's (their states are not copied flags here: they too take the box's states)
        Assert.Equal(OptionState.Enabled, screen.Menus[1][6]);
        Assert.Equal(OptionState.Hidden, screen.Menus[1][5]);
        Assert.Equal(OptionState.Hidden, screen.Menus[1][2]);
        // line 6 has no action, but its handler (the box's for slot 2) was called on the choice: $ChosenOneName = menu_member
        Assert.Equal(62, c.OptionHandlers[6]!().Chance);
        Assert.Equal(1, c.Variables.Chosen[0]);
    }

    [Fact]
    public void CopiedLinesKeepTheirFlagsAfterAChoiceInABox()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, screen) = Context(line: 0, PopupBox.Items, member: 0, slot: 0, click: true, then: 1);
        var menu = new CardMenu(c, 1, 0, copiedFlags: 0b11);
        menu.Enable(0, 1);
        var rounds = 0;
        menu.EachRound = () => { if (++rounds == 3) { c.Next = 2; } };
        menu.Run(new MenuLine(_ => { }), new MenuLine(_ => { }));
        // opt_on[0..1] = opt_flag_0..1 every round: the box's states do not stay
        Assert.Equal(OptionState.Enabled, screen.Menus[1][0]);
        Assert.Equal(OptionState.Enabled, screen.Menus[1][1]);
    }

    [Fact]
    public void AMenuOnOptionsBlockIsTheGamesValue()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context(line: 0, PopupBox.None, member: 0, slot: 0, click: false);
        c.OptionsBlock = 0x2B;
        var kept = new CardMenu(c, 1, block: null, onOptionsBlock: true);
        Assert.Equal(0x2B, kept.Block);
        var written = new CardMenu(c, 1, block: 3, onOptionsBlock: true);
        Assert.Equal(3, c.OptionsBlock);
        written.Block = 5;
        Assert.Equal(5, c.OptionsBlock);
        var own = new CardMenu(c, 1, block: 7);
        own.Block = 8;
        Assert.Equal(5, c.OptionsBlock);
    }
}
