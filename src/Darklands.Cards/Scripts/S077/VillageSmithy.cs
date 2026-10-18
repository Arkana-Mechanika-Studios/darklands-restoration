namespace Darklands.Cards.Scripts;

/// <summary>"$Villa03": the village smithy. Trade with the blacksmith, or be on your way.</summary>
// orig: R077+0x212A (1A4A:000A) matched; tests: CardSmokeTests
// orig: R077+0x22D6 (1A4A:01B6) matched; R077+0x230E (1A4A:01EE) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered).
public sealed class VillageSmithy : Card
{
    public const int CardNumber = 0x87;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 345);
        c.Next = CardNumber;
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1);
        return menu.Run(
            // orig: R077+0x222E (1A4A:010E) matched; R077+0x2254 (1A4A:0134) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                // C: the categories are two words, 0x00FF and 0x0400 (low word first).
                c.OpenShop(-1, "Blacksmith", 0xFF, 0x400);
                c.PassHours(1);
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R077+0x228C (1A4A:016C) matched; R077+0x229E (1A4A:017E) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x85; }, OptionHint.Saying(SuccessAssured)));
    }
}
