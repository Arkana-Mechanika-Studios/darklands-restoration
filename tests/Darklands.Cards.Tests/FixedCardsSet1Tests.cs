using Darklands.Cards.Scripts;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests;

/// <summary>
/// F-1 set 1 (the user's choices of 2026-11-25), the card fixes: the alchemist's formulas, the Hussite band's friendship and the peat
/// bog's rope. The original's behaviour with the flag off, the fixed one with it on (skipped without the game files).
/// </summary>
public sealed class FixedCardsSet1Tests
{
    private static CardContext Context(GameState state, ICardScreen screen) => new(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };

    /// <summary>FixAlchemistFormulaRange (B-10641a): every alchemist offers formulas 0..21 only; fixed, the whole 0..65.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlchemistsOfferTheFirstFormulasOrTheWholeRange(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(3);
        state.Behaviour.FixAlchemistFormulaRange = fixedGame;
        var offered = new HashSet<int>();
        for (var city = 0; city < Core.World.Location.CityCount; city++)
        {
            state.CurrentLocation = city;
            var c = Context(state, new ScriptedScreen(0));
            for (var slot = 0; slot < 4; slot++)
            {
                offered.Add(AlchemistFormulaSeller.FormulaOnOffer(c, slot));
            }
        }
        Assert.All(offered, f => Assert.InRange(f, 0, fixedGame ? 65 : 21));
        Assert.Equal(fixedGame, offered.Max() > 21);
    }

    /// <summary>FixHussiteFriendship (B-7bfead): friendship only on a roll equal to the chance; fixed, at or below it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HussiteFriendshipNeedsTheExactRollOrOneAtOrBelow(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(5);
        state.Behaviour.FixHussiteFriendship = fixedGame;
        // the roll the claim will draw, read from a copy of the generator
        var roll = new MscRandom(((MscRandom)state.Random).State).Below(100);
        var chance = Math.Min(99, roll + 10);
        Assert.NotEqual(roll, chance);
        var screen = new ScriptedScreen(0);
        try
        {
            HussiteBand.ClaimFriendship(Context(state, screen), chance);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or NullReferenceException)
        {
            // the failed claim goes on into the fight, which this test's screen does not run
        }
        // text 2: the band lets the party go; text 3: the claim fails
        Assert.Contains(fixedGame ? "text 2" : "text 3", screen.Log);
        Assert.DoesNotContain(fixedGame ? "text 3" : "text 2", screen.Log);
    }

    /// <summary>
    /// FixPeatBogRope (B-03246c): the rope line tests the Rope's item type as a flag mask, so a party with a rope alone cannot use it;
    /// fixed, the Rope's flag.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APartyWithOnlyARopeCanOrCannotThrowIt(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(7);
        state.Data = CardHarness.World!;
        state.Behaviour.FixPeatBogRope = fixedGame;
        // the Rope: item type 0x64, flag 0x100 (none of the flags 0x64 tests)
        var rope = Enumerable.Range(0, CardHarness.World!.ItemDefinitions.Count).First(code => state.Item(code).Type == 0x64);
        Assert.Equal(0x100u, state.Item(rope).Flags & 0x164u);
        foreach (var member in state.Party.Slots)
        {
            member.Pack.Clear();
        }
        var leader = state.Party.Slots.First(m => m.OccupiesSlot);
        leader.Pack.Add(new ItemStack(rope, state.Item(rope).Type, 20, 1, state.Item(rope).Weight));
        var screen = new ScriptedScreen(5);
        try
        {
            new PeatBog().Run(Context(state, screen));
        }
        catch (InvalidOperationException)
        {
            // only the first menu is looked at
        }
        Assert.NotNull(screen.FirstOptions);
        Assert.Equal(fixedGame ? OptionState.Enabled : OptionState.Shown, screen.FirstOptions![0]);
    }
}
