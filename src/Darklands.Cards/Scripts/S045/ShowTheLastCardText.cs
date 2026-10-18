namespace Darklands.Cards.Scripts;

/// <summary>
/// Card 7 (no card of its own): shows the first text of the card on screen again and goes
/// back to the card before.
/// </summary>
// orig: R045+0x162E (199A:000E) matched; tests: CardSmokeTests
public sealed class ShowTheLastCardText : Card
{
    public const int CardNumber = 0x07;

    public override int Number => CardNumber;

    // orig: R045+0x162E (199A:000E) matched (sets DS:EE72 = 7 and DS:EE70 = 0, shows text 0, returns the previous screen)
    public override int Run(CardContext c)
    {
        c.Screen.Text(0);
        return c.State.PreviousCard;
    }
}
