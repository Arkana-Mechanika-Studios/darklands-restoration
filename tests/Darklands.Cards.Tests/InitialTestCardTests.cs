using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Economy;
using Darklands.Core.Rules;

namespace Darklands.Cards.Tests;

/// <summary>Card 0, "$Initi00" (R044+0x0000): the developers' test card.</summary>
public sealed class InitialTestCardTests
{
    [Fact]
    public void TheCardGivesTwoHundredFlorinsAndEntersTheCity()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 3, location: 3);
        var (next, screen) = CardHarness.Run(new InitialTestCard(), state, option: 0);
        Assert.Equal(4, next);
        Assert.Equal(Money.From(200, 0, 0), state.Party.Purse);
        Assert.Equal(0, state.PreviousCard);
        Assert.Contains("show $Initi00 xhouse3", screen.Log);
        // C: opt_flag_2 = 2 (shown), opt_flag_4 = 0 (hidden), the others 1.
        Assert.Equal(OptionState.Shown, screen.LastOptions[2]);
        Assert.Equal(OptionState.Hidden, screen.LastOptions[4]);
    }

    [Fact]
    public void TheStandingLinesStayOnTheCard()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 3, location: 3);
        // The scripted screen chooses line 8 (+99), then moves on to line 9 (the end of the game).
        var (next, _) = CardHarness.Run(new InitialTestCard(), state, option: 8);
        Assert.Equal(0x62, next);
        Assert.Equal(0x63, LocationRules.Query(state, 3, 0));
    }
}
