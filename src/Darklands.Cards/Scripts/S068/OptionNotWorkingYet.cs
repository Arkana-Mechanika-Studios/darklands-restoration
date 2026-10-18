namespace Darklands.Cards.Scripts;

/// <summary>
/// "$NoGo00.msg": the card the game leads to when a scene is not finished: "Sorry, this option
/// isn't working yet." It offers to go back to the previous scene, to the city's main street or
/// side street, or to the outside of the city (the other lines say the same and go back).
/// </summary>
// orig: R068+0x128C (1960:000C) matched; tests: CardSmokeTests
// orig: handler setup R068+0x1822; unused-line handler R068+0x18C4 ("screwed up.");
// item pop-ups R068+0x18FC/06b8/06f4/0730 and saint pop-ups 076c/07f6/0880/090a (standard)
public sealed class OptionNotWorkingYet : Card
{
    private const int CommonPicture = 0;
    private const int CityEntry = 4;

    // The card table calls this card "$NoGo00.msg", with the extension (the card names itself the same way).
    public override int Number => 0x62;

    public override int Run(CardContext c)
    {
        Show(c, CommonPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The items and saints are listed, but no line is tied to the pop-ups.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        return menu.Run(
            // orig: R068+0x14CE, hint 0260 (the leader)
            new MenuLine(_ => { c.PassHours(0); c.Next = c.PreviousCard; }, () => new OptionHint(-1, SuccessAssured, c.Leader)),
            // orig: R068+0x151A, hint 02b8 (the leader)
            new MenuLine(_ => { c.PassHours(0); c.Next = c.IsDaylight ? 6 : 8; }, () => new OptionHint(-1, SuccessAssured, c.Leader)),
            // orig: R068+0x1572, hint 0310 (100%, the leader)
            new MenuLine(_ => { c.PassHours(0); c.Next = c.IsDaylight ? 9 : 0x0A; }, () => new OptionHint(100, SuccessAssured, c.Leader)),
            // orig: R068+0x15CA, hint 035c
            new MenuLine(_ => { c.PassHours(0); c.Next = CityEntry; }, OptionHint.Saying(SuccessAssured)),
            // Lines 4..9 apologise once more and go back. orig: R068+0x1614, 03ec, 0442, 049a, 04f2, 054a
            // (hints 03b2: leader; 040a: whole party; 0460, 04b8, 0510, 0568: 100%, the leader)
            new MenuLine(_ => SorryAndBack(c), () => new OptionHint(-1, SuccessAssured, c.Leader)),
            new MenuLine(_ => SorryAndBack(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => SorryAndBack(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => SorryAndBack(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => SorryAndBack(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => SorryAndBack(c), () => new OptionHint(100, SuccessAssured, c.Leader)));
    }

    private static void SorryAndBack(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = c.PreviousCard;
    }
}
