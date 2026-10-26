using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// The stranger's mob (R128): with the flying ointment (quest 1, fact 0x0D) each wave is preceded by the ointment's offer, whose option
/// lines are never switched on (R128+0x0806 clears opt_on). The original's card_menu then waits for ever (direct call j_menu.py,
/// local/batch/evid1o); the offer reaches the screen with no line enabled.
/// </summary>
public sealed class EmptyOfferTests
{
    [Fact]
    public void TheOintmentIsOfferedWithNoLineToChoose()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 3, location: 5);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0)) { Battle = (_, _, _) => 0 };
        c.Events.Update(-1, -1, 0x1C, 0x0D, 1, 1, EventUpdate.Or);
        Assert.True(c.Events.FactAnywhere(1, 0x0D));
        var e = Assert.Throws<InvalidOperationException>(() => new StrangerBringsAMob().Run(c));
        Assert.Equal("Menu 4 offers no option.", e.Message);
    }
}
