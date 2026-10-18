namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Soldi00": the street of the soldiers' camp-followers, crowded with push-cart vendors. A
/// half-finished card: the first line is the arms outfitter's shop, every other line only
/// shows the street's first text and sends the party to the city entry card.
/// </summary>
// orig: R063+0x0D40 (190C:0000) matched; tests: CardSmokeTests
// orig: handler setup R063+0x12C8; unused-line handler R063+0x136A ("screwed up.");
// item pop-ups R063+0x13A2/069e/06da/0716 and saint pop-ups 0752/07dc/0866/08f0 (standard)
public sealed class SoldiersStreet : Card
{
    private const int CommonPicture = 0;
    private const int CityStreets = 0x17;
    private const int CityEntry = 4;

    public override int Number => 0x4D;

    public override int Run(CardContext c)
    {
        Show(c, CommonPicture);
        // C: two globals (DS:EE70 = 0, DS:EE72 = 7) are set here and never read by this card.
        c.Next = Number;
        // The items and saints are listed, but no line is tied to the pop-ups (as in the other unfinished cards).
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        return menu.Run(
            // orig: R063+0x0F86 (the leader at 100%)
            new MenuLine(_ => VisitTheArmsOutfitter(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R063+0x11E2 (the leader at 100%)
            new MenuLine(_ => TalkAndLeave(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndLeave(c), OptionHint.Saying(SuccessAssured)));
    }

    // orig: R063+0x0F60
    private static void VisitTheArmsOutfitter(CardContext c)
    {
        c.OpenShop(1, "Arms Outfitter", 0xFF, 0x400);
        c.PassHours(1);
        c.Next = CityStreets;
    }

    // orig: R063+0x0FC0 (and the eight like it, 02d6 .. 0532)
    private static void TalkAndLeave(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CityEntry;
    }
}
