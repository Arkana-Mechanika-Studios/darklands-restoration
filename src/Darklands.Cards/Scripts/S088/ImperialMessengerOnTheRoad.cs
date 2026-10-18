namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetI00.msg": on the road a splendidly dressed royal messenger of the Emperor asks the
/// party to escort him to the nearest city until he finds a proper escort. The party may agree
/// or refuse. (The card is unfinished in the original: agreeing only shows his reply and the
/// card stays up.)
/// </summary>
// orig: R088+0x0FB6 (1933:0006) matched; tests: CardSmokeTests
public sealed class ImperialMessengerOnTheRoad : Card
{
    public const int CardNumber = 0xB3;

    private const int Picture = 0;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // orig: R088+0x1142 (1933:0192) matched; R088+0x117A (1933:01CA) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered).
        c.SetCardItems();
        c.SetCardSaints();
        // C: the start-up cannot leave this card, so the menu loop test is always true.
        var menu = Menu(c);
        menu.Enable(0, 1);
        return menu.Run(
            // orig: R088+0x10BC (1933:010C) matched; R088+0x10CA (1933:011A) matched; tests: CardSmokeTests
            // C: only the messenger's reply is shown; Next stays on this card, so the lines are offered again.
            new MenuLine(() => c.Screen.Text(1, TextFrame.BlankPage), OptionHint.Saying(SuccessAssured)),
            // orig: R088+0x1102 (1933:0152) matched; R088+0x110A (1933:015A) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x0C, OptionHint.Saying(SuccessAssured)));
    }
}
