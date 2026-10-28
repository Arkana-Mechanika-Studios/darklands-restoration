using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// Card 0x9C, the dragon's worshippers: at the sabbat (the dragon quest's fact 4/4 at 3 or more, options block 4) the card leaves
/// out line 3 (grovel) and keeps line 4 (surrender): R083+0x01CD stores 0 in DS:EE7C, the flag of line 3. Elsewhere all six lines
/// are open. (Oracle: local/batch/cards3 p1, the 12 runs of 09C at fact 4/4 = 5, now in cards-leads.json.gz.)
/// </summary>
public sealed class WitchesOfTheBlightTests
{
    [Theory]
    [InlineData(1, 3, new[] { 1, 1, 1, 1, 1, 1 })]
    [InlineData(3, 4, new[] { 1, 1, 1, 0, 1, 1 })]
    [InlineData(5, 4, new[] { 1, 1, 1, 0, 1, 1 })]
    public void TheSabbatLeavesOutGrovelling(int added, int block, int[] flags)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 7, location: 5);
        var screen = new ScriptedScreen(4);
        var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        // the new game's dragon quest holds fact 4/4 = 1: adding 1 gives 2 (block 3), 3 or 5 the sabbat (block 4)
        c.Events.Update(-1, -1, 0x1C, 4, added, 4, EventUpdate.Add);
        state.CurrentCard = WitchesOfTheBlight.CardNumber;
        new WitchesOfTheBlight().Run(c);
        Assert.True(screen.Log.Contains($"menu {block}"), string.Join(" / ", screen.Log));
        // line 1 (pray) follows the party's saints; the others are the card's own flags
        var got = screen.LastOptions.Take(6).Select((s, i) => i == 1 ? 1 : (int)s).ToArray();
        Assert.Equal(flags, got);
    }
}
