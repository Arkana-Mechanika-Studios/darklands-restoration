using Darklands.Cards.Scripts;
using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>
/// options_block (DS:EE53) is one global of the card engine: the cards that show card_options(options_block) keep it as their menu's
/// block (OptionsBlockCards), a few others store into it, and a card that reads it before writing it sees what the last writer left:
/// the alchemy mishap after the slum lodging (R089+0x0743), the blighted land after the ruined outlaws (R081+0x21D9) and the witches'
/// sabbat test of the dragon card (R083+0x01CD).
/// </summary>
public sealed class OptionsBlockTests
{
    private static (CardContext C, ScriptedScreen Screen) Context(GameState state, int option = -1)
    {
        var screen = new ScriptedScreen(option);
        return (new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames }, screen);
    }

    [Fact]
    public void ACardShowingOptionsBlockLeavesItsBlockThere()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // the Medici bank shows card_options(options_block) with 0 or 2 (R061+0x0978); a card that shows a constant block does not touch it
        var state = CardHarness.NewGame(seed: 3, location: 3);
        var (c, screen) = Context(state);
        c.OptionsBlock = 0x2B;
        state.CurrentCard = 0x45;
        c.Next = 0x45;
        new MediciBank().Run(c);
        Assert.Contains(screen.Log, l => l is "menu 0" or "menu 2");
        Assert.True(c.OptionsBlock is 0 or 2, $"options_block {c.OptionsBlock}");
    }

    [Fact]
    public void TheMishapAfterTheSlumLodgingShowsTheBlockTheLastWriterLeft()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        foreach (var left in new[] { 0, 2 })
        {
            var state = CardHarness.NewGame(seed: 3, location: 3);
            state.PreviousCard = 0x23;
            var (c, screen) = Context(state);
            // e.g. an inn mishap left 2, any card that opens with options_block = 0 left 0
            c.OptionsBlock = left;
            state.CurrentCard = 0xB5;
            new AlchemyMishap().Run(c);
            Assert.Equal($"menu {left}", screen.Log.First(l => l.StartsWith("menu ", StringComparison.Ordinal)));
            Assert.Equal(left, c.OptionsBlock);
        }
    }
}
