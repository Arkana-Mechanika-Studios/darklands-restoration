using Darklands.Cards.Scripts;
using Darklands.Core;
using Darklands.Core.Characters;

namespace Darklands.Cards.Tests;

/// <summary>
/// F-1 set 2 (the user's choices of 2026-11-27), the card fixes: the potion thrown at footpads and the climbers at the city walls by
/// night. The original's behaviour with the flag off, the fixed one with it on (skipped without the game files).
/// </summary>
public sealed class FixedCardsSet2Tests
{
    private static CardContext Context(GameState state, ICardScreen screen) => new(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };

    /// <summary>FixFootpadsPotionUsedUp (B-556a53): the potion thrown at the footpads stays in the pack; fixed, it is used up.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePotionThrownAtTheFootpadsStaysOrIsUsedUp(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(11);
        state.Data = CardHarness.World!;
        state.Behaviour.FixFootpadsPotionUsedUp = fixedGame;
        const int potion = 0x5F;
        var member = Enumerable.Range(0, state.Party.Slots.Count).First(i => state.Party.Slots[i].OccupiesSlot);
        var d = state.Item(potion);
        state.Party.Slots[member].Pack.Clear();
        state.Party.Slots[member].Pack.Add(new ItemStack(potion, d.Type, 30, 1, d.Weight));
        var c = Context(state, new ScriptedScreen(0));
        c.SetCardItems(0x5F, 0x65, 0x61, 0x68);
        c.MenuMember = member;
        c.MenuSlot = 0;
        Footpads.ThrowAPotion(c);
        Assert.Equal(fixedGame ? 0 : 1, state.Party.Slots[member].Pack.Where(st => st.Code == potion).Sum(st => st.Count));
    }

    /// <summary>
    /// FixNightWallsClimbers (B-79145e, B-edab45): at the walls by night the grapple's chance is the stored word, 0, and everybody
    /// climbing is certain (999); fixed, the best able climber's value and the weakest able member's, as by day.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheNightWallsClimbersChances(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(13);
        state.Data = CardHarness.World!;
        state.Behaviour.FixNightWallsClimbers = fixedGame;
        var card = new CityWallsByNight();
        var c = Context(state, new ScriptedScreen(5));         // line 5: fall back (no risk)
        card.Run(c);
        var grapple = card.ClimberChance().Chance;
        var everyone = card.EveryoneChance(c).Chance;
        if (fixedGame)
        {
            Assert.InRange(grapple, 1, 200);
            Assert.InRange(everyone, 1, grapple);
        }
        else
        {
            Assert.Equal(0, grapple);
            Assert.Equal(999, everyone);
        }
    }
}
