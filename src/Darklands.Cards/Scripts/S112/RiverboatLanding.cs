namespace Darklands.Cards.Scripts;

/// <summary>
/// "$River00": a riverboat landing (on the Weichsel, the Danube or the Maas). The script is
/// unfinished: none of the river passages is offered (two would go to card 0x5A), only the
/// overland route back to the map; the fare ($Money1) is never set.
/// </summary>
// orig: R112+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R112+0x0464 (1838:0464) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
// C: the loop calls card_menu without card_options or card_text (card_load draws only the picture), so the menu works on the option
// C: lines the screen last laid out (the previous card's last text) with this card's states and handlers (CardMenu.DrawsOptions = false).
// C: the card's three blocks (one per river) are never shown; which river is meant is not chosen by the card.
public sealed class RiverboatLanding : Card
{
    public override int Number => 272;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.DrawsOptions = false;
        menu[9] = OptionState.Enabled;

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // DORMANT: lines 0..8 are never offered. Their actions: R112+0x020E, R112+0x0288,
            // R112+0x0302 .. R112+0x03EA are empty; R112+0x0248 and R112+0x02C2 go to card 0x5A.
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => c.Next = 0x5A, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => c.Next = 0x5A, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            // orig: R112+0x0424 (1838:0424) matched; hint R112+0x042C (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard, Unknown));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");
}
