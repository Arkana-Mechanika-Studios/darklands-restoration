namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityB00": the city barracks, the armory and the guards' headquarters. By day one can inquire about
/// weapons training or look for people to join the party; by night (the text is another one) only the
/// ways out are offered: back to the city square, or out by the main street or a side street.
/// An unfinished card: the training and recruiting lines lead elsewhere or to the "option isn't working
/// yet" card, and the lines meant for asking a particular person (2 to 4) are switched off.
/// </summary>
// orig: R073+0x0D36 (190B:0006) matched; tests: CardSmokeTests
// orig: handler setup R073+0x1276; unused-line handler R073+0x1318 ("screwed up.");
// item pop-ups R073+0x1350/065c/0698/06d4 and saint pop-ups 0710/079a/0824/08ae (standard)
public sealed class CityBarracks : Card
{
    private const int BarracksPicture = 96;
    private const int NotWorkingYet = 0x62;
    private const int CitySquareByDay = 0x12;
    private const int CitySquareByNight = 0x19;
    private const int RecruitCard = 0xAA;

    public override int Number => 0x75;

    public override int Run(CardContext c)
    {
        Show(c, BarracksPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 5, 6, 7);
        if (!c.IsDaylight)
        {
            // At night the other text lists only the ways out.
            menu[0] = menu[1] = OptionState.Hidden;
            menu.Block = 1;
        }
        // The handler setup clears the item and saint lists.
        c.SetCardItems();
        c.SetCardSaints();

        // Every line is rated "Success is assured." for the whole party (hints R073+0x0F76, 0296, 02e0, 032a, 0374, 03ca, 0420, 0476, 04c0, 050a;
        // C: the hint handlers of lines 0 and 9 also assign the leader to r.a, then overwrite it).
        return menu.Run(
            // orig: R073+0x0F62: inquire about weapons training (not written: the city square)
            new MenuLine(_ => c.Next = CitySquare(c), Assured),
            // orig: R073+0x0FB2: look for people to join (the recruiting card, coming back here or to the night square)
            new MenuLine(_ =>
            {
                c.SavedCard = Number;
                c.State.SavedCard2 = CitySquareByNight;
                c.Next = RecruitCard;
            }, Assured),
            // DORMANT: lines 2..4 (card-option-flag-2..4 = 0). orig: R073+0x0FFE, 0318 R073+0x1048, 0362 R073+0x1092
            new MenuLine(_ => LeaveForNotWorking(c), Assured),
            new MenuLine(_ => LeaveForNotWorking(c), Assured),
            new MenuLine(_ => LeaveForNotWorking(c), Assured),
            // orig: R073+0x10DC: back to the city square
            new MenuLine(_ => { c.PassHours(1); c.Next = CitySquare(c); }, Assured),
            // orig: R073+0x1132: out by the main street
            new MenuLine(_ => { c.PassHours(1); c.Next = c.IsDaylight ? 6 : 8; }, Assured),
            // orig: R073+0x1188: out by a side street
            new MenuLine(_ => { c.PassHours(1); c.Next = c.IsDaylight ? 9 : 0x0A; }, Assured),
            // DORMANT: lines 8 and 9 (card-option-flag-8, card-option-flag-9 = 0). orig: R073+0x11DE, 04f8 R073+0x1228
            new MenuLine(_ => LeaveForNotWorking(c), Assured),
            new MenuLine(_ => LeaveForNotWorking(c), Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    private static int CitySquare(CardContext c) => c.IsDaylight ? CitySquareByDay : CitySquareByNight;

    private static void LeaveForNotWorking(CardContext c)
    {
        c.PassHours(0);
        c.Next = NotWorkingYet;
    }
}
