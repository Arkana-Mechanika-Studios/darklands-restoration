namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Devil00": a village by rapids has a fine stone bridge that nobody uses: a devil built
/// it twenty years ago and claims the soul of the first to cross. The party may cross it
/// (to the devil) or take the long way round (six hours).
/// </summary>
// orig: R088+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class DevilsBridgeVillage : Card
{
    public const int CardNumber = 0xB0;

    private const int Picture = 111;

    /// <summary>The devil on the bridge.</summary>
    private const int DevilOnTheBridge = 0xB1;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // orig: R088+0x01A0 (1838:01A0) matched; R088+0x01D8 (1838:01D8) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered).
        c.SetCardItems();
        c.SetCardSaints();
        c.Screen.Text(0);
        // C: the start-up cannot leave this card, so the menu loop test is always true.
        // The two lines are in text block 1.
        var menu = Menu(c, 1);
        menu.Enable(0, 1);
        return menu.Run(
            // orig: R088+0x0110 (1838:0110) matched; R088+0x0118 (1838:0118) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = DevilOnTheBridge, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R088+0x0150 (1838:0150) matched; R088+0x0168 (1838:0168) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.PassHours(6);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
            }, OptionHint.Saying(SuccessAssured)));
    }
}
