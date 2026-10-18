namespace Darklands.Cards.Scripts;

/// <summary>
/// "$AddCh00": the party spends hours renewing acquaintances to find possible companions (an old
/// childhood friend, a mercenary, a thief, a huntsman, a friar, a nun, an alchemist ...). An
/// unfinished card: every line but the last lets no time pass and leads to the "option isn't working
/// yet" card; the last line only returns to the previous card, and only in daylight.
/// </summary>
// orig: R072+0x0B6A (18EE:000A) matched; tests: CardSmokeTests
// orig: handler setup R072+0x10A2; unused-line handler R072+0x1144 ("screwed up.");
// item pop-ups R072+0x117C/0658/0694/06d0 and saint pop-ups 070c/0796/0820/08aa (standard)
public sealed class RenewingAcquaintances : Card
{
    private const int CommonPicture = 0;
    private const int NotWorkingYet = 0x62;

    public override int Number => 0x72;

    public override int Run(CardContext c)
    {
        Show(c, CommonPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The items and saints are listed, but no line is tied to the pop-ups.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 9);

        // Every line is rated "Success is assured." for the whole party (hints R072+0x0DC0, 02ae, 02f8, 0342, 038c, 03d6, 0420, 046a, 04b4, 0506;
        // C: the hint handlers of lines 0 and 9 also assign the leader to r.a, then overwrite it).
        return menu.Run(
            // orig: lines 0..7: R072+0x0DAE, 029c, 02e6, 0330, 037a, 03c4, 040e, 0458
            Line(c), Line(c), Line(c), Line(c), Line(c), Line(c), Line(c), Line(c),
            // DORMANT: line 8 (opt_flag_8 = 0). orig: R072+0x1002
            Line(c),
            // orig: R072+0x104C
            // C: the original function has no return statement and the action is a call with its value dropped;
            // at night nothing changes and the menu is offered again.
            new MenuLine(_ =>
            {
                c.PassHours(0);
                if (c.IsDaylight)
                {
                    c.Next = c.SavedCard;
                }
            }, OptionHint.Saying(SuccessAssured)));
    }

    private static MenuLine Line(CardContext c) => new(_ =>
    {
        c.PassHours(0);
        c.Next = NotWorkingYet;
    }, OptionHint.Saying(SuccessAssured));
}
