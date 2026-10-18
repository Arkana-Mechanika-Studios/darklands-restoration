using System.Reflection;
using Darklands.Cards;

namespace Darklands.Cards.Tests;

/// <summary>
/// Every scripted card runs to its end with each of its first options, in a city and in the
/// wilderness, and leads to a card the game knows.
/// </summary>
public sealed class CardSmokeTests
{
    public static TheoryData<int> ScriptedCards()
    {
        var data = new TheoryData<int>();
        foreach (var card in AllCards())
        {
            data.Add(card.Number);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ScriptedCards))]
    public void CardRunsAndMovesOn(int number)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var card = AllCards().Single(c => c.Number == number);
        foreach (var location in new[] { 3, 120 })
        {
            for (var option = 0; option < 10; option++)
            {
                var state = CardHarness.NewGame(seed: (uint)(option + 7), location: location);
                // the cards that show option flags they never set get them as a city's leaving card leaves them (all offered)
                Array.Fill(state.OptionFlags, 1);
                var (next, _) = CardHarness.Run(card, state, option);
                Assert.True(IsKnownDestination(next), $"card {number} option {option} led to {next}");
            }
        }
    }

    private static bool IsKnownDestination(int next) =>
        next is >= -6 and <= -2 or -0x63 or (>= 1 and <= 365);

    private static IEnumerable<Card> AllCards() =>
        typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!);
}
