using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// The dragon's blighted land (R081+0x1848): when the ruined outlaws' battle keeps the card, its menu shows options_block as the card
/// before left it. The original's direct call (local/batch/evid1o j_dragon.py): DS:EE53 = 0x2B, what = 7, run_battle 5 -> card_options(0x2B);
/// on the dragon's ways in the card stores 0x0A / 0x0B / 0x0C there itself.
/// </summary>
public sealed class DragonRetainedBlockTests
{
    [Fact]
    public void TheRuinedOutlawsKeepTheCardWithTheEarlierCardsBlock()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var reached = false;
        for (var seed = 1u; seed < 400 && !reached; seed++)
        {
            var state = CardHarness.NewGame(seed: seed, location: 5);
            var screen = new ScriptedScreen(0);
            var c = new CardContext(state, CardHarness.World!, screen) { Battle = (_, _, _) => 5, OptionsBlock = 0x2B };
            c.Events.Update(-1, -1, 0x1C, 1, 3, 4, EventUpdate.Add);
            new DragonBlightedLand().Run(c);
            if (screen.Log.Contains("text 7") && screen.Log.Any(l => l.StartsWith("menu ", StringComparison.Ordinal)))
            {
                reached = true;
                Assert.Contains("menu 43", screen.Log);
                Assert.Equal(0x2B, c.OptionsBlock);
            }
            else if (screen.Log.Any(l => l.StartsWith("menu ", StringComparison.Ordinal)))
            {
                // a dragon's way in: the card's own block, stored in the global
                Assert.Contains(c.OptionsBlock, new[] { 0x0A, 0x0B, 0x0C });
                Assert.Contains($"menu {c.OptionsBlock}", screen.Log);
            }
        }
        Assert.True(reached);
    }
}
