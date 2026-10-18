namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Villa00": a hamlet abandoned near a diabolic worship site. Look for signs of life, or
/// leave. A village that was destroyed (city flag 0x20) only shows its ruins.
/// </summary>
// orig: R078+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R078+0x01D4 (1838:01D4) matched; R078+0x020C (1838:020C) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered);
// text 2 (a blasted ruin with the same choices) is never shown by the script.
public sealed class AbandonedHamlet : Card
{
    public const int CardNumber = 0x88;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 128);
        c.Next = CardNumber;
        // C: the flag byte is read as a signed char before the 0x20 test.
        if (((sbyte)c.LocationQuery(c.Here, 0x20) & 0x20) != 0)
        {
            c.Screen.Text(3);
            c.State.CurrentLocation = -1;
            return c.Next = 0x0C;
        }
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1);
        return menu.Run(
            // orig: R078+0x0134 (1838:0134) matched; R078+0x0152 (1838:0152) matched; tests: CardSmokeTests
            new MenuLine(() => { c.Screen.Text(1); c.PassHours(4); c.Next = 0x0C; }, OptionHint.Saying(NoRisk)),
            // orig: R078+0x018A (1838:018A) matched; R078+0x019C (1838:019C) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)));
    }
}
