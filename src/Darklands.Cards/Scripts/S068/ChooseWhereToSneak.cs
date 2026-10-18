namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Selec02": moving silently through the streets by night, the party picks the place to break
/// into: the merchants' stalls, the foreign traders' wagons, the pharmacist, the Fugger and
/// Medici offices, the Hanseatic League, the pawnshop, or a quiet side street. A half-finished
/// card: most lines only let an hour of nothing pass and ask again; two lines lead on.
/// </summary>
// orig: R068+0x097E (18CF:000E) matched; tests: CardSmokeTests
// orig: handler setup R068+0x0E9A; unused-line handler R068+0x0F3C ("screwed up.");
// item pop-ups R068+0x0F74/0640/067c/06b8 and saint pop-ups 06f4/077e/0808/0892 (standard)
public sealed class ChooseWhereToSneak : Card
{
    private const int MainPicture = 49;
    private const int CityEntry = 4;
    private const int QuietSideStreet = 0x0A;

    public override int Number => 0x61;

    public override int Run(CardContext c)
    {
        Show(c, MainPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The items and saints are listed, but no line is tied to the pop-ups.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        return menu.Run(
            // Lines 0..6: the locked places are not finished: nothing happens (c.Next is unchanged, the question is asked again).
            // orig: R068+0x0BC2, 0296, 02dc, 0320, 0364, 03a8, 03ec (advance_hours(0) only); hints 025e, 02a2 (leader), 02e8, 032c, 0370, 03b4, 03f8 (100%, whole party)
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), () => new OptionHint(-1, SuccessAssured, c.Leader)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), () => new OptionHint(100, SuccessAssured, OptionHint.WholeParty)),
            // Line 7, the quiet side street. orig: R068+0x0DA0, hint 0442
            new MenuLine(_ => { c.PassHours(0); c.Next = QuietSideStreet; }, OptionHint.Saying(SuccessAssured)),
            // Lines 8 and 9. orig: R068+0x0DEA and 04d2 (hints 0498 and 04f0: 100%, the leader)
            new MenuLine(_ => TalkAndGo(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => TalkAndGo(c), () => new OptionHint(100, SuccessAssured, c.Leader)));
    }

    private static void TalkAndGo(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CityEntry;
    }
}
