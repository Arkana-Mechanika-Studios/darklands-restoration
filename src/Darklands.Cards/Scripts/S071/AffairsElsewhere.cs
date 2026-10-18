namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Affai00": travellers' news of affairs elsewhere. It has no menu: it tells the news that
/// applies (a robber knight's or a mine's trouble, a city's recent uprising or put-down rebellion,
/// unrest in a city with the politics quest, or "nothing of interest") and then goes back to
/// the card that sent the party (saved_card2).
/// </summary>
// orig: R071+0x15A4 (1992:0004) matched; tests: CardSmokeTests
// orig: handler setup R071+0x1BC4 (never called from the card: it has no menu loop)
public sealed class AffairsElsewhere : Card
{
    private const int NewsPicture = 193;
    private const int RobberKnightQuest = 4;
    private const int MineQuest = 0x0C;
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int PutDownRebellionEvent = 0x42;

    public override int Number => 0x6E;

    public override int Run(CardContext c)
    {
        Show(c, NewsPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        var events = c.Events;
        if (events.QuestExists(RobberKnightQuest))
        {
            c.Variables.Location = events.FindLocation(FactKind, -1, -1, RobberKnightQuest, -1);
            c.Screen.Text(5);
        }
        if (events.QuestExists(MineQuest))
        {
            c.Variables.Location = events.FindLocation(FactKind, -1, -1, MineQuest, -1);
            c.Variables.Location2 = NearestCityTo(c, c.Variables.Location);
            c.Screen.Text(0x11);
        }
        if (c.Here != -1)
        {
            var city = NearestCityTo(c, c.Here);
            if (Uprising(c, city))
            {
                c.Screen.Text(1);
            }
            else if (events.At(PutDownRebellionEvent, NearestCityTo(c, c.Here)))
            {
                c.Screen.Text(2);
            }
            else if (events.QuestExists(PoliticsQuest))
            {
                TellOfTheUnrest(c);
            }
            else if (!events.QuestExists(RobberKnightQuest) && !events.QuestExists(MineQuest))
            {
                c.Screen.Text(3);
            }
        }
        else
        {
            // The same in the wilderness, seen from the party's map position.
            if (Uprising(c, NearestCityToParty(c)))
            {
                c.Screen.Text(1);
            }
            else if (events.At(PutDownRebellionEvent, NearestCityToParty(c)))
            {
                c.Screen.Text(2);
            }
            else if (events.QuestExists(PoliticsQuest))
            {
                TellOfTheUnrest(c);
            }
            // BUG: outside a city the news test misses a negation: unlike the branch above, this one tests the mine
            // quest without it, so the "nothing of interest" text appears here only while the mine quest runs (and not
            // the robber knight's).
            else if (!events.QuestExists(RobberKnightQuest) && events.QuestExists(MineQuest))
            {
                c.Screen.Text(3);
            }
        }
        c.Next = c.State.SavedCard2;
        return c.Next;
        // DORMANT: below the card's code the file holds an older version of the same news (R071+0x17BC
        // R071+0x17BC: without the mine news, always ending with text 3 when nothing else applies, then going to card 0x6E)
        // and a menu that nothing runs: R071+0x192E shows text 3 and goes to card 0x6E;
        // R071+0x197A, 0424, 046e, 04b8, 0502, 054c, 0596 let no time pass and go to card 0x62;
        // R071+0x1B80 returns to saved_card2; their hints (0356 .. 05e8) all say "Success is assured.".
    }

    /// <summary>Whether the (nearest) city has the uprising flag.</summary>
    // C: (char) value & -0x80 is non-zero only when the sign bit of the byte is set.
    private static bool Uprising(CardContext c, int city) => (sbyte)c.LocationQuery(city, 0x20) < 0;

    /// <summary>A rumour of unrest in the city with the politics quest; if the party is there already, a dull week instead.</summary>
    private static void TellOfTheUnrest(CardContext c)
    {
        c.Variables.Location = c.Events.FindLocation(FactKind, -1, -1, PoliticsQuest, -1);
        // C: in the wilderness cur_loc is -1, so the quest's city is never "here".
        c.Screen.Text(c.Variables.Location != c.Here ? 4 : 3);
    }

    /// <summary>The nearest city to a location's map position (the original reads the location record).</summary>
    // DIVERGENCE: a location number outside the table reads garbage in the original; here it is position 0, 0.
    private static int NearestCityTo(CardContext c, int location)
    {
        var (x, y) = location >= 0 && location < c.State.Locations.Count
            ? (c.State.Locations[location].X, c.State.Locations[location].Y)
            : (0, 0);
        return c.NearestCity(x, y);
    }

    private static int NearestCityToParty(CardContext c) => c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);
}
