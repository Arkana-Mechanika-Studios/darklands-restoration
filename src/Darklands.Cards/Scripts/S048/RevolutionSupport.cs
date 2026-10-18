namespace Darklands.Cards.Scripts;

/// <summary>
/// What the four city-politics cards of overlay R048 (the rebel/government houses, the city
/// square and the church district) share: the party's side, the city's revolt events and the
/// exits an uprising forces.
/// </summary>
internal static class RevolutionSupport
{
    /// <summary>The quest and fact group of the city's politics.</summary>
    public const int Politics = 2;

    /// <summary>The kind of the revolt events the houses create (the party was seen, refused, caught ...).</summary>
    private const int RevoltEventKind = 0x1C;

    /// <summary>
    /// Which side's house the party stands at. The original reads the word that holds the map
    /// x position (zero = the first side) and the cards that lead here set it, so it is read
    /// from the map position as is.
    /// </summary>
    // C: party_x (DS:E3D0) is the map x position, reused by the political cards as the party's side.
    public static bool AtFirstSidesHouse(CardContext c) => c.State.MapPosition.X == 0;

    /// <summary>A timed event of the revolt for the party's current city (event_new(-2, cur_loc, 0x5a, 0x1c, type, 0, 0, hours, 0, 2)).</summary>
    public static void NewRevoltEvent(CardContext c, int type, int hours) =>
        c.Events.Create(-2, c.Here, 0x5A, RevoltEventKind, type, 0, 0, hours, 0, Politics);

    /// <summary>
    /// The exits the city's politics force once the party leaves the city square or the church
    /// district: a rising or a decided revolt. Returns the card to go to (<paramref name="next"/> unchanged when none).
    /// </summary>
    public static int UprisingExits(CardContext c, int next)
    {
        var events = c.Events;
        var here = c.Here;
        var value = events.FactValue(Politics, 0, here);
        if (events.Fact(Politics, 0x15, here) && value != -1 && !c.EventAtHere(0x3E))
        {
            next = 0xBD;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) != 1 && !c.EventAtHere(0x3E))
        {
            next = 0xBE;
        }
        if (events.Fact(Politics, 0x15, here) && events.FactValue(Politics, 0, here) == -1)
        {
            next = 0x80;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) == 1)
        {
            next = 0x7B;
        }
        return next;
    }
}
