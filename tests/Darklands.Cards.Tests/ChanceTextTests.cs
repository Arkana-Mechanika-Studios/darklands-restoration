using Darklands.Cards;
using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>R029+0x5B62's quality words: the slots next to the seven words point at empty strings in the data image.</summary>
public sealed class ChanceTextTests
{
    [Fact]
    public void AChanceOneSlotPastTheWordsSaysNothing()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        state.Difficulty = Difficulty.Standard;
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(-1));
        Assert.Equal("Chance of success is good to excellent", c.ChanceText(60, 120));
        // 140..159 is slot 7 (DS:6F43, an empty string); −20..−39 slot −1 (DS:0000), −40..−59 slot −2 (DS:7461)
        Assert.Equal("Chance of success is  to excellent", c.ChanceText(145, 150));
        Assert.Equal("Chance of success is  to fair", c.ChanceText(-25, 50));
        Assert.Equal("Chance of success is  to fair", c.ChanceText(-45, 50));
    }
}
