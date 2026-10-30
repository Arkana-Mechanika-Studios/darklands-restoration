namespace Darklands.Cards.Scripts;

/// <summary>
/// What the city's politics (quest and fact group 2) do when the party leaves a street card:
/// an agent may take the party aside, and an uprising under way or decided takes over.
/// </summary>
// orig: tail of R045+0x0C40 (18FC:0000) matched (main street by day, saved card 6; mainStreetByDay: true); tests: CardSmokeTests
// orig: tail of R046+0x0000 (1838:0000) matched (main street at night, saved card 8; mainStreetByDay: false)
// orig: tail of R046+0x0724 (18AA:0004) matched (side street, saved card 9; mainStreetByDay: false)
// C: the three tails are the same code with the sides of the fact tests swapped in the day main street (fact 6 is tested negated, fact value -1 and 1 exchanged); the C# folds them into one method with mainStreetByDay.
internal static class StreetPolitics
{
    private const int Politics = 2, PoliticsSettled = 0x3E;

    private const int PoliticsCalls = 0x37, Uprising = 0xBD, Uprising2 = 0xBE, RevolutionLost = 0x80, RevolutionWon = 0x7B;

    /// <summary>
    /// Returns the card to go to instead of <paramref name="next"/> (and sets it). An agent
    /// calls more likely at night and on a weak party (low Strength), and remembers the street
    /// in <c>SavedCard</c>. The main street by day tests the sides the other way round from the
    /// other streets (as the original does): there the agent is likelier before the faction's
    /// fact 6 is known, and the side that rises is the other one.
    /// </summary>
    // orig: the post-menu politics block of R045+0x0C40, R046+0x0000 and R046+0x0724: open-quest-at(2) and fact value -1 -> chance 5 by day or 15 by night, minus (max Strength - 33), +10 for fact 6; a draw at most that calls card 0x37 with the street saved; then facts 0x15/0x16 pick the uprising cards 0xBD/0xBE (unless event 0x3E) and 0x80/0x7B.
    public static int AfterTheChoice(CardContext c, int street, int next, bool mainStreetByDay)
    {
        var here = c.Here;
        var events = c.Events;
        if (events.QuestAt(Politics, here) && events.FactValue(Politics, 0, here) == -1)
        {
            var chance = c.IsDaylight ? 5 : 15;
            chance += -(c.PartyMaxAttribute(3) - 33);
            if (events.Fact(Politics, 6, here) != mainStreetByDay)
            {
                chance += 10;
            }
            if (c.Random(100) <= chance)
            {
                c.State.SavedCard = street;
                next = PoliticsCalls;
            }
        }
        if (events.Fact(Politics, 0x15, here) && (mainStreetByDay ? events.FactValue(Politics, 0, here) != -1 : events.FactValue(Politics, 0, here) == 1) && !c.EventAtHere(PoliticsSettled))
        {
            next = Uprising;
        }
        if (events.Fact(Politics, 0x16, here) && (mainStreetByDay ? events.FactValue(Politics, 0, here) != 1 : events.FactValue(Politics, 0, here) == -1) && !c.EventAtHere(PoliticsSettled))
        {
            next = Uprising2;
        }
        if (events.Fact(Politics, 0x15, here) && events.FactValue(Politics, 0, here) == (mainStreetByDay ? -1 : 1))
        {
            next = RevolutionLost;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) == (mainStreetByDay ? 1 : -1))
        {
            next = RevolutionWon;
        }
        c.Next = next;
        return next;
    }
}
