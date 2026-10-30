using Darklands.Cards;

namespace Darklands.Cards.Tests;

/// <summary>The option menu every card runs (CardMenu): states, rating of the choice, pop-up hints, the loop.</summary>
public sealed class CardMenuTests
{
    private static (CardContext C, ScriptedScreen Screen) Context(int option = 0)
    {
        var state = CardHarness.NewGame(seed: 5, location: 3);
        var screen = new ScriptedScreen(option);
        return (new CardContext(state, CardHarness.World!, screen) { Next = 1 }, screen);
    }

    [Fact]
    public void StatesChangedByAChoiceAreOfferedNextRound()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, screen) = Context();
        var menu = new CardMenu(c, 1);
        menu.Enable(0, 1);
        var rounds = 0;
        menu.Run(new MenuLine(() => { rounds++; menu[0] = OptionState.Shown; }), new MenuLine(() => c.Next = 2));
        Assert.Equal(1, rounds);
        Assert.Equal(OptionState.Shown, screen.LastOptions[0]);
        Assert.Equal(2, c.Next);
    }

    [Fact]
    public void TheChosenLinesHandlerIsAlwaysCalledAndALineTakingTheChanceGetsIt()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context();
        var menu = new CardMenu(c, 1);
        menu.Enable(0);
        var asked = 0;
        var got = 0;
        menu.Run(new MenuLine(chance => { got = chance; c.Next = 2; }, () => { asked++; return OptionHint.ChanceOf(42, 0); }));
        // Once by the screen, once on the choice.
        Assert.Equal((2, 42), (asked, got));

        // local-minus-0a = (*card-option-handler[k])() runs whether the action uses it or not.
        c.Next = 1;
        asked = 0;
        menu.Run(new MenuLine(() => c.Next = 2, () => { asked++; return OptionHint.ChanceOf(42, 0); }));
        Assert.Equal(2, asked);
    }

    [Fact]
    public void APopupChoiceIsRatedByTheLinesPopupThenTheMenusThenTheLinesHint()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context();
        c.MenuPopup = true;
        int Rate(MenuLine line, Func<OptionHint>? menuPopup)
        {
            c.Next = 1;
            var menu = new CardMenu(c, 1) { Popup = menuPopup };
            menu.Enable(0);
            var got = 0;
            menu.Run(new MenuLine(chance => { got = chance; c.Next = 2; }, line.Hint, line.Popup));
            return got;
        }
        var hint = () => OptionHint.ChanceOf(1, 0);
        var own = () => OptionHint.ChanceOf(2, 0);
        var shared = () => OptionHint.ChanceOf(3, 0);
        Assert.Equal(2, Rate(new MenuLine(_ => { }, hint, own), shared));
        Assert.Equal(3, Rate(new MenuLine(_ => { }, hint), shared));
        Assert.Equal(1, Rate(new MenuLine(_ => { }, hint), null));
    }

    [Fact]
    public void ALineWithoutAHandlerSaysScrewedUpAndDoesNothing()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context(option: 1);
        var menu = new CardMenu(c, 1);
        menu.Enable(0, 1);
        var hints = new List<string>();
        var rounds = 0;
        menu.EachRound = () => { if (++rounds == 3) { c.Next = 2; } };
        menu.Run(new MenuLine(() => { }, () => { hints.Add("line 0"); return OptionHint.Say("line 0"); }));
        // Round 1 chooses line 1 (no handler), round 2 line 0 (asked by the screen and called on the choice); round 3 ends before a menu.
        Assert.Equal(3, rounds);
        Assert.Equal(2, c.Next);
        Assert.Equal(["line 0", "line 0"], hints);
    }

    [Fact]
    public void NothingIsOfferedWhenTheCardHasAlreadyMovedOn()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, screen) = Context();
        c.Next = 7;
        var menu = new CardMenu(c, 1);
        menu.Enable(0);
        Assert.Equal(7, menu.Run(new MenuLine(() => c.Next = 2)));
        Assert.Empty(screen.Log);
    }

    [Fact]
    public void LinesCopiedFromTheGlobalFlagsShowWhatTheCardBeforeLeft()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context();
        c.State.OptionFlags[3] = 2;
        c.State.OptionFlags[4] = 1;
        // lines 3 and 4 are copied from card-option-flag-3/4; the card never assigns 4 (stale), so 3 starts at 0
        var menu = new CardMenu(c, 1, 0, copiedFlags: 0b11000, staleFlags: 0b10000);
        Assert.Equal(OptionState.Hidden, menu[3]);
        Assert.Equal(OptionState.Enabled, menu[4]);
        menu[3] = OptionState.Shown;
        Assert.Equal(2, c.State.OptionFlags[3]);       // the card's assignment is the global's
        menu[5] = OptionState.Enabled;                 // not copied: the menu's own line
        Assert.Equal(0, c.State.OptionFlags[5]);
    }

    [Fact]
    public void ACardWithoutAResetKeepsTheHandlersAnEarlierCardLeft()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (c, _) = Context();
        c.OptionHandlers[5] = () => OptionHint.Say("left over");
        _ = new CardMenu(c, 1, resetsHandlers: false);
        Assert.Equal("left over", c.OptionHandlers[5]!().Message);
        _ = new CardMenu(c, 1);
        Assert.Equal(CardTexts.ScrewedUp, c.OptionHandlers[5]!().Message);
    }
}
