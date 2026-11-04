using Darklands.Cards;

namespace Darklands.Cards.Tests;

/// <summary>
/// The card engine's variables are the program's globals: zeros at its start (the EXE's image) and left by a load (b35: every one marked in
/// the original before an Alt-L's saved-game reader was still marked at the next card call).
/// </summary>
public sealed class CardGlobalsTests
{
    [Fact]
    public void TheProgramStartsWithEveryTextVariableZero()
    {
        var v = CardVariables.ProgramStart();
        Assert.Equal([0, 0, 0, 0, 0], v.Chosen);
        Assert.Equal(0, v.Location);
        Assert.Equal(0, v.Location2);
        Assert.Equal(0, v.Item);
        Assert.All(v.Named, n => Assert.Equal("", n));
    }

    [Fact]
    public void TheNextGamesContextTakesTheEngineWordsOfTheLast()
    {
        if (!CardHarness.Available) { return; }
        var first = new CardContext(CardHarness.NewGame(), CardHarness.World!, new ScriptedScreen(-1));
        first.TakeEngineGlobals(null);
        Assert.Equal([0, 0, 0, 0], first.CardItems);
        Assert.Equal([0, 0, 0, 0], first.CardSaints);
        first.SetCardItems(5, 9);
        first.MenuMember = 3;
        first.MenuSlot = 2;
        first.PopupListsAll = true;
        first.OptionsBlock = 7;
        var next = new CardContext(CardHarness.NewGame(), CardHarness.World!, new ScriptedScreen(-1));
        next.TakeEngineGlobals(first);
        Assert.Equal([5, 9, -1, -1], next.CardItems);
        Assert.Equal((3, 2, true, 7), (next.MenuMember, next.MenuSlot, next.PopupListsAll, next.OptionsBlock));
    }
}
