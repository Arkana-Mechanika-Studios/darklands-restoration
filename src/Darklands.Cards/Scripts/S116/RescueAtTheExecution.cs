namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Dunge05": a placeholder scene — "You are at the execution but you rescue your friend."
/// The text is shown on a blank page and the party is back in the city's street. Nobody is
/// actually freed.
/// </summary>
// orig: R116+0x070E (18A8:000E) matched; tests: CardSmokeTests
// orig: R116+0x0A4A (18A8:034A) matched (registration)
// DORMANT: the option flags, items (0x5F, 0x60, 0x65, 0x61) and saints (0, 0x59, 0x61, 0x80) are set up but no menu
// follows; the file's 20 option and pop-up handlers are never called.
public sealed class RescueAtTheExecution : Card
{
    public override int Number => 289;

    public override int Run(CardContext c)
    {
        Show(c, 120);
        c.Next = Number;
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        c.Screen.Text(0, TextFrame.BlankPage);
        // The original returns the card without setting following-card.
        return c.IsDaylight ? 0x12 : 0x19;
    }
}
