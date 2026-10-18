namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol07": in a panic, the party flees through the city in turmoil towards the nearest
/// gate, the city wall, or (in a port) the docks.
/// </summary>
// orig: R076+0x09FE (18D7:000E) matched; tests: CardSmokeTests
// orig: R076+0x0C42 (18D7:0252) matched; R076+0x0C64 (18D7:0274) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
public sealed class PanickedFlightFromTheCity : Card
{
    public const int CardNumber = 0x82;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1);
        menu[2] = c.HereQuery(0x12) != 0 ? OptionState.Hidden : OptionState.Enabled;
        return menu.Run(
            // orig: R076+0x0B76 (18D7:0186) matched; R076+0x0B7E (18D7:018E) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x3A, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R076+0x0BB6 (18D7:01C6) matched; R076+0x0BBE (18D7:01CE) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x3B, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R076+0x0BF6 (18D7:0206) matched; R076+0x0C0A (18D7:021A) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = c.IsDaylight ? 0x1F : 0x20, OptionHint.Saying(CardTexts.Unknown)));
    }
}
