using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Economy;

namespace Darklands.Cards.Tests;

/// <summary>The bank notes (DS:9072 triple) in the cards that use them: 16-bit arithmetic, florins-only resets.</summary>
public sealed class BankNotesTests
{
    private static CardContext Context(out Darklands.Core.GameState state)
    {
        state = CardHarness.NewGame();
        return new CardContext(state, CardHarness.World!, new ScriptedScreen(-1));
    }

    [Fact]
    public void RedeemingReturnsTheFloinsLessSixPfennigsEach()
    {
        if (!CardHarness.Available) { return; }
        var c = Context(out var state);
        state.Party.Purse = Money.From(10, 0, 0);
        state.Party.BankNotes = Money.From(100, 5, 7);
        BankingHouses.RedeemLetters(c);
        // 110 florins less 600 pfennigs (2 florins 10 groschen).
        Assert.Equal(Money.From(10, 0, 0).Add(100, 0, 0).Subtract(0, 0, 600), state.Party.Purse);
        Assert.Equal(Money.From(0, 5, 7), state.Party.BankNotes); // DS:9072 = 0 clears only the florins word
    }

    [Fact]
    public void TheRedemptionFeeWrapsAt16Bits()
    {
        if (!CardHarness.Available) { return; }
        var c = Context(out var state);
        state.Party.BankNotes = Money.From(6000, 0, 0);
        BankingHouses.RedeemLetters(c);
        // 6 * 6000 = 36000 still fits an unsigned pfennig word; 6 * 12000 = 72000 wraps to 6464.
        Assert.Equal(Money.FromPfennigs(36000), c.Variables.Money[1]);
        state.Party.BankNotes = Money.From(12000, 0, 0);
        BankingHouses.RedeemLetters(c);
        Assert.Equal(Money.FromPfennigs(6464), c.Variables.Money[1]);
    }

    [Fact]
    public void DepositsAddToTheFloinsWordAndWrapAt16Bits()
    {
        if (!CardHarness.Available) { return; }
        var c = Context(out var state);
        c.TextInput = (_, _, _) => "500";
        state.Party.Purse = Money.From(1000, 0, 0);
        state.Party.BankNotes = Money.From(32500, 3, 4);
        Assert.True(BankingHouses.DepositFlorins(c));
        Assert.Equal(Money.From(500, 0, 0), state.Party.Purse);
        Assert.Equal(unchecked((short)33000), state.Party.BankNotes.Florins);
        Assert.Equal((short)3, state.Party.BankNotes.Groschen);
        Assert.Equal((short)4, state.Party.BankNotes.Pfennigs);
    }

    [Fact]
    public void DismissingAMemberTakesAFifthOfTheWholeTriple()
    {
        // The share is (unsigned)purse-in-pfennigs / 5, subtracted with purse-subtract-parts(0, 0, share).
        var notes = Money.From(40, 10, 6);
        var share = (ushort)notes.TotalPfennigs / 5;
        var left = notes.Subtract(0, 0, share);
        Assert.Equal(notes.TotalPfennigs - share, left.TotalPfennigs);
    }
}
