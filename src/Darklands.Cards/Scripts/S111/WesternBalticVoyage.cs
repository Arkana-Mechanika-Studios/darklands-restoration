namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Balti00": aboard ship on the western Baltic, a day after setting out. The party picks a
/// port (Flensburg … Rostock: hours to land, card 0x5A), sails back through the Skagerrak to
/// the North Sea (card 268) or on to the eastern ports (card 270). Sweden cannot be chosen.
/// </summary>
// orig: R111+0x0576 (188F:0006) matched; tests: CardSmokeTests
// orig: R111+0x0AEE (188F:057E) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
// UNCERTAIN: the seven ports all lead to card 0x5A; nothing here records which port was chosen.
public sealed class WesternBalticVoyage : Card
{
    public override int Number => 269;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5, 6, 7, 9);
        menu[8] = OptionState.Shown;
        // A day at sea passes on every arrival at this card.
        c.PassHours(24);

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R111+0x0798 (188F:0228) matched; hint R111+0x07B6
            new MenuLine(_ => Land(c, c.Random(5) + 6)),
            // orig: R111+0x07EE (188F:027E) matched; hint R111+0x080C
            new MenuLine(_ => Land(c, c.Random(5) + 6)),
            // orig: R111+0x0844 (188F:02D4) matched; hint R111+0x0862
            new MenuLine(_ => Land(c, c.Random(5) + 5)),
            // orig: R111+0x089A (188F:032A) matched; hint R111+0x08B8
            new MenuLine(_ => Land(c, c.Random(4) + 4)),
            // orig: R111+0x08F0 (188F:0380) matched; hint R111+0x090E
            new MenuLine(_ => Land(c, c.Random(3) + 3)),
            // orig: R111+0x0946 (188F:03D6) matched; hint R111+0x0964
            new MenuLine(_ => Land(c, c.Random(3) + 3)),
            // orig: R111+0x099C (188F:042C) matched; hint R111+0x09BA
            new MenuLine(_ => Land(c, c.Random(3) + 3)),
            // orig: R111+0x09F2 (188F:0482) matched; hint R111+0x0A10
            new MenuLine(_ => { c.PassHours(c.Random(10) + 24); c.Next = 268; }),
            // orig: R111+0x0A48 (188F:04D8) matched; hint R111+0x0A60
            // DORMANT: line 8 (Sweden) is shown but cannot be chosen; it would only pass most of a day.
            new MenuLine(_ => c.PassHours(c.Random(5) + 20)),
            // orig: R111+0x0A98 (188F:0528) matched; hint R111+0x0AB6
            new MenuLine(_ => { c.PassHours(c.Random(5) + 18); c.Next = 270; }));
    }

    private static void Land(CardContext c, int hours)
    {
        c.PassHours(hours);
        c.Next = 0x5A;
    }
}
