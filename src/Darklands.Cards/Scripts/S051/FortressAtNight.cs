namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityF01": the fortress of the city lord at night. The gate is shut: the party can only
/// walk back to the main street or the side street (the same scene as the fortress by day,
/// with nothing to ask for).
/// </summary>
// orig: R051+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R051+0x052C (1838:052C) matched (init helper: enables all 20 option lines, installs the "screwed up." default and the item and saint pop-up handlers, clears the item and saint slots; the card then fills 4 items and 4 saints)
// DORMANT: R051+0x05CE (1838:05CE) matched: the "screwed up." default handler of option lines 10..19; the card's menu has no such lines.
// DORMANT: R051+0x0606 (1838:0606), R051+0x0642 (1838:0642), R051+0x067E (1838:067E), R051+0x06BA (1838:06BA):
//   the item pop-up handlers of slots 0..3 ("Success is assured." for the chosen member); no option opens the item pop-up (options 2 and 3 are closed and idle).
// DORMANT: R051+0x06F6 (1838:06F6), R051+0x0780 (1838:0780), R051+0x080A (1838:080A), R051+0x0894 (1838:0894):
//   the saint pop-up handlers of slots 0..3 (the chance text of the saint); no option opens the saint pop-up.
public sealed class FortressAtNight : Card
{
    public const int CardNumber = 0x1C;

    private const int Picture = 267;

    private const int MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        CitySquare = 0x12, CitySquareAtNight = 0x19;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R051+0x0244 (1838:0244) + R051+0x0262 (1838:0262) R051 seg 1838 matched
            // DORMANT: option 0 has flag 0 (closed); its action would let no time pass and go to the main street; its hint is a 100% chance for the leader.
            new MenuLine(() => { c.PassHours(0); c.Next = StreetForTheHour(c, MainStreet, MainStreetAtNight); },
                () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R051+0x029C (1838:029C) + R051+0x02BA (1838:02BA) R051 seg 1838 matched
            // DORMANT: option 1 has flag 0 (closed); it would go to the side street.
            new MenuLine(() => { c.PassHours(0); c.Next = StreetForTheHour(c, SideStreet, SideStreetAtNight); },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x02F2 (1838:02F2) + R051+0x02FE (1838:02FE) R051 seg 1838 matched
            // DORMANT: options 2..6 have flag 0 (closed); their actions only let no time pass and stay.
            Idle(c),
            // orig: R051+0x0336 (1838:0336) + R051+0x0342 (1838:0342) R051 seg 1838 matched
            Idle(c),
            // orig: R051+0x037A (1838:037A) + R051+0x0386 (1838:0386) R051 seg 1838 matched
            Idle(c),
            // orig: R051+0x03BE (1838:03BE) + R051+0x03CA (1838:03CA) R051 seg 1838 matched
            Idle(c),
            // orig: R051+0x0402 (1838:0402) + R051+0x040E (1838:040E) R051 seg 1838 matched
            Idle(c),
            // orig: R051+0x0446 (1838:0446) + R051+0x045A (1838:045A) R051 seg 1838 matched
            // DORMANT: option 7 has flag 0 (closed); it would go to the city square; its hint is a 100% chance for the leader.
            new MenuLine(() => c.Next = StreetForTheHour(c, CitySquare, CitySquareAtNight),
                () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R051+0x0494 (1838:0494) + R051+0x04A8 (1838:04A8) R051 seg 1838 matched
            new MenuLine(() => c.Next = StreetForTheHour(c, MainStreet, MainStreetAtNight), OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x04E0 (1838:04E0) + R051+0x04F4 (1838:04F4) R051 seg 1838 matched
            new MenuLine(() => c.Next = StreetForTheHour(c, SideStreet, SideStreetAtNight), OptionHint.Saying(SuccessAssured)));
    }

    private static int StreetForTheHour(CardContext c, int byDay, int byNight) => c.IsDaylight ? byDay : byNight;

    private static MenuLine Idle(CardContext c) =>
        new(() => c.PassHours(0), OptionHint.Saying(SuccessAssured));
}
