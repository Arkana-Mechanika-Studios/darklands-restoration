namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol16": a day of besieging the city lord in the fortress (after 0xBA) or the council
/// hall, until word comes that knights from the countryside have arrived. The party may go
/// back to the siege, or give it up and choose where to go: the docks (if the city has any),
/// or the cards 0x3A and 0x3B.
/// </summary>
// orig: R091+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class SiegeOfTheCityLord : Card
{
    private const int Picture = 0;

    public override int Number => 0xBC;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        // C: the block is the script's global DS:8E22.
        var withMercenaries = c.HereQuery(0x29) != 2;

        var menu = Menu(c);
        menu.Enable(0, 1);
        menu.Block = c.State.PreviousCard == 0xBA ? (withMercenaries ? 2 : 0) : (withMercenaries ? 4 : 3);

        // orig: R091+0x056A (1838:056A) matched (resets the handlers only; the lists are left)
        return menu.Run(
            // orig: R091+0x0276 (1838:0276); hint R091+0x027E matched
            new MenuLine(() => c.Next = c.CardBefore, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R091+0x02B6 (1838:02B6); hint R091+0x02F4 matched
            new MenuLine(() =>
                {
                    // The siege is given up: the city's side wins.
                    menu[0] = OptionState.Hidden;
                    menu[1] = OptionState.Hidden;
                    menu.Block = 1;
                    menu[3] = OptionState.Enabled;
                    menu[4] = OptionState.Enabled;
                    if (c.HereQuery(0x12) != 0)
                    {
                        menu[2] = OptionState.Enabled;
                    }
                    c.ConflictOutcome(1, -1, 1);
                    c.Next = Number;
                }),
            // orig: R091+0x032C; R091+0x0382; R091+0x03CC matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 0x1F : 0x20; }),
            new MenuLine(() => { c.PassHours(1); c.Next = 0x3A; }),
            new MenuLine(() => { c.PassHours(1); c.Next = 0x3B; }),
            // orig: R091+0x0416 and the four like it, matched
            // DORMANT: lines 5..9 are hidden (flags 0); they would only let no time pass.
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)));
    }
}
