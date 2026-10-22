namespace Darklands.Cards.Scripts;

/// <summary>
/// "$North00": aboard ship on the North Sea. The party picks a port (Hamburg, Bremen, Leer,
/// Zwolle, Elburg: a few hours to land, card 0x5A) or sails through the Skagerrak into the
/// Baltic (card 269). England, Scotland and Norway are shown but cannot be chosen.
/// </summary>
// orig: R111+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R111+0x053C (1838:053C) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
// C: the loop calls card_menu without card_options or card_text (card_load draws only the picture), so the menu works on the option
// C: lines the screen last laid out (the previous card's last text) with this card's states and handlers (CardMenu.DrawsOptions = false).
// C: the five ports all lead to card 0x5A; nothing here records which port was chosen.
public sealed class NorthSeaVoyage : Card
{
    public override int Number => 268;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.DrawsOptions = false;
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5);
        menu.Set(OptionState.Shown, 6, 7, 8);

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R111+0x020E (1838:020E) matched; hint R111+0x022C
            new MenuLine(_ => Land(c)),
            // orig: R111+0x0264 (1838:0264) matched; hint R111+0x0282
            new MenuLine(_ => Land(c)),
            // orig: R111+0x02BA (1838:02BA) matched; hint R111+0x02D8
            new MenuLine(_ => Land(c)),
            // orig: R111+0x0310 (1838:0310) matched; hint R111+0x032E
            new MenuLine(_ => Land(c)),
            // orig: R111+0x0366 (1838:0366) matched; hint R111+0x0384
            new MenuLine(_ => Land(c)),
            // orig: R111+0x03BC (1838:03BC) matched; hint R111+0x03DA
            new MenuLine(_ => { c.PassHours(c.Random(6) + 18); c.Next = 269; }),
            // orig: R111+0x0412 (1838:0412) matched; hint R111+0x042A
            // DORMANT: lines 6..8 are shown but cannot be chosen; they would only pass a day at sea.
            new MenuLine(_ => c.PassHours(c.Random(8) + 24)),
            // orig: R111+0x0462 (1838:0462) matched; hint R111+0x047A
            new MenuLine(_ => c.PassHours(c.Random(8) + 22)),
            // orig: R111+0x04B2 (1838:04B2) matched; hint R111+0x04CA
            new MenuLine(_ => c.PassHours(c.Random(8) + 22)),
            // orig: R111+0x0502 (1838:0502) matched (empty); hint R111+0x0504
            // DORMANT: line 9 is never offered.
            new MenuLine(_ => { }));
    }

    private static void Land(CardContext c)
    {
        c.PassHours(c.Random(3) + 3);
        c.Next = 0x5A;
    }
}
