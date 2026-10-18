namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Other00": back streets of a city in political turmoil. The party may call on the rebel
/// leader or the government's man (when it knows where they live), and so take a side, or
/// go back to the main street or on to the side streets.
/// </summary>
// orig: R075+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R075+0x0636 (1838:0636) matched; R075+0x06D8 (1838:06D8) matched; R075+0x0710 (1838:0710) matched; R075+0x074C (1838:074C) matched; R075+0x0788 (1838:0788) matched; R075+0x07C4 (1838:07C4) matched; R075+0x0800 (1838:0800) matched; R075+0x088A (1838:088A) matched; R075+0x0914 (1838:0914) matched; R075+0x099E (1838:099E) matched; tests: CardSmokeTests
// DORMANT: the card sets up items, saints and their pop-up hints, but no line of its text is
// an item or saint line; the "screwed up." default of lines 10..19 is never offered.
public sealed class FactionLeadersHomes : Card
{
    public const int CardNumber = 0x7C;

    /// <summary>The planning talk with the leader of the side the party has joined.</summary>
    private const int PlanningSession = 0x79;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 34);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(8, 9);
        if (c.Events.QuestAt(2, c.Here))
        {
            if (c.Events.Fact(2, 0x1A, c.Here))
            {
                menu[0] = OptionState.Enabled;
            }
            if (c.Events.Fact(2, 0x19, c.Here))
            {
                menu[1] = OptionState.Enabled;
            }
        }
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        c.RandomName(c.City.Ordinal + 0x16, 2, 1);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        // DORMANT: the original also installs the usual item pop-up hint (c.MemberPopupHint) and saint pop-up
        // hint (c.SaintPopupHint) here; no line is an item or saint line, so no line carries them.

        // orig: R075+0x03CA (1838:03CA) matched; R075+0x03DC (1838:03DC) matched; R075+0x0414 (1838:0414) matched; R075+0x0426 (1838:0426) matched; R075+0x045E (1838:045E) matched; R075+0x0470 (1838:0470) matched; R075+0x04A8 (1838:04A8) matched; R075+0x04BA (1838:04BA) matched; R075+0x04F2 (1838:04F2) matched; R075+0x0504 (1838:0504) matched; R075+0x053C (1838:053C) matched; R075+0x054E (1838:054E) matched; tests: CardSmokeTests
        // DORMANT: lines 2..7 are placeholders the game never offers; they would end the game.
        var placeholder = new MenuLine(() => { c.PassHours(0); c.Next = 0x62; }, OptionHint.Saying(SuccessAssured));
        return menu.Run(
            // orig: R075+0x02D8 (1838:02D8) matched; R075+0x031C (1838:031C) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); JoinSide(c, 0, -1); }, OptionHint.Saying(SuccessAssured)),
            // orig: R075+0x0358 (1838:0358) matched; R075+0x0392 (1838:0392) matched; tests: CardSmokeTests
            new MenuLine(() => JoinSide(c, 1, 1), OptionHint.Saying(SuccessAssured)),
            placeholder, placeholder, placeholder, placeholder, placeholder, placeholder,
            // orig: R075+0x0586 (1838:0586) matched; R075+0x05A4 (1838:05A4) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = c.IsDaylight ? 6 : 8; }, OptionHint.Saying(SuccessAssured)),
            // orig: R075+0x05DC (1838:05DC) matched; R075+0x05FA (1838:05FA) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = c.IsDaylight ? 9 : 0x0A; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>
    /// Calling on a leader puts the party on his side. When the party already serves that side
    /// (the city's fact value), they sit down to plan; otherwise the leader's offer follows.
    /// </summary>
    // UNCERTAIN: the side kept in party_x (CardContext.CityMark).
    private static void JoinSide(CardContext c, int side, int servesThatSide)
    {
        c.CityMark = side;
        c.Next = c.Events.FactValue(2, 0, c.Here) == servesThatSide ? PlanningSession : c.IsDaylight ? 0x10 : 0x11;
    }
}
