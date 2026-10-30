namespace Darklands.Cards.Scripts;

/// <summary>"$Drago02": withered, lifeless land under a noxious mist; the party wonders what force did this, and goes on.</summary>
// orig: R082+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R082+0x0068 (1838:0068) matched; R082+0x00A0 (1838:00A0) matched; tests: CardSmokeTests
// DORMANT: the registration and its "screwed up." default are never used (the card shows no menu); text 1 is never shown.
public sealed class BlastedLand : Card
{
    public const int CardNumber = 0x98;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 116);
        c.Next = CardNumber;
        c.SetCardSaints();
        c.SetCardItems();
        c.Screen.Text(0);
        c.State.CurrentLocation = -1;
        // C: the function returns 0x0C directly (following-card keeps the card's number).
        return 0x0C;
    }
}
