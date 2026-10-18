namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Consi00": the party tallies up the city's attitudes and discusses with its leader whether to put
/// the plan (the rebels' march or the arrest of the rebels) into effect. An unfinished card: of the
/// ten lines only the last one offered in the text is open ("not implement the plan quite yet");
/// it passes an hour, shows the closing text and returns to the card that sent the party. The other
/// lines (reviewing the plan, putting it into effect) are switched off.
/// </summary>
// orig: R072+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R072+0x0584 (only the option lines are reset: no item or saint pop-ups are installed);
// unused-line handler R072+0x05A6 ("screwed up.")
public sealed class ConsiderTheRebelPlan : Card
{
    private const int PlanPicture = 356;
    private const int NotWorkingYet = 0x62;

    public override int Number => 0x70;

    public override int Run(CardContext c)
    {
        Show(c, PlanPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // Only line 6 is open.
        var menu = Menu(c, 1);
        menu.Enable(6);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        return menu.Run(
            // DORMANT: lines 0..5 (opt_flag_0..5 = 0): review the plans and put them into effect.
            // orig: R072+0x0244 (stay), 0284 (text 4, stay), 02d0 (the leader chosen, back), 0318 (the leader chosen, text 4, back);
            // hints 024c, 0298 ("No risk."), 02de, 0332 ("You can always encounter the unknown", the leader)
            new MenuLine(_ => c.Next = Number, OptionHint.Saying(NoRisk)),
            new MenuLine(_ => { c.Screen.Text(4); c.Next = Number; }, OptionHint.Saying(NoRisk)),
            new MenuLine(_ =>
            {
                c.Variables.Chosen[0] = c.Leader;
                c.Next = c.SavedCard;
            }, () => new OptionHint(-1, CardTexts.Unknown, c.Leader)),
            new MenuLine(_ =>
            {
                c.Variables.Chosen[0] = c.Leader;
                c.Screen.Text(4);
                c.Next = c.SavedCard;
            }, () => new OptionHint(-1, CardTexts.Unknown, c.Leader)),
            // DORMANT: lines 4 and 5 (flags 0): text 0, no time, the "option isn't working yet" card.
            // orig: R072+0x036C, 03c6 R072+0x03C6; hints 038a, 03e4
            // C: their hint handlers assign the leader to r.b and then overwrite it with the party.
            new MenuLine(_ => ShowTheTallyAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => ShowTheTallyAndLeave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R072+0x0420 (hint 043e "No risk.")
            new MenuLine(_ => NotImplementThePlanYet(c), OptionHint.Saying(NoRisk)),
            // DORMANT: lines 7..9 (flags 0). orig: R072+0x0476, 04d0, 052a; hints 0494, 04ee, 0548
            new MenuLine(_ => ShowTheTallyAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => ShowTheTallyAndLeave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => ShowTheTallyAndLeave(c), OptionHint.Saying(SuccessAssured)));
    }

    private static void ShowTheTallyAndLeave(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = NotWorkingYet;
    }

    private static void NotImplementThePlanYet(CardContext c)
    {
        c.PassHours(1);
        c.Screen.Text(8);
        c.Next = c.SavedCard;
    }
}
