namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Speci00": a few casual conversations in the city reveal who is hiring for special jobs. In
/// a city whose number is a multiple of twenty (with no such event running) the first line leads on
/// to the card of the job itself; once the politics quest's side is known, two more lines chase the
/// rumours about the aristocrat who hires "easy" jobs and the one who hires against the city lord.
/// </summary>
// orig: R069+0x175A (19AD:000A) matched; tests: CardSmokeTests
// orig: handler setup R069+0x1CDE; unused-line handler R069+0x1D80 ("screwed up.");
// item pop-ups R069+0x1DB8/06a4/06e0/071c and saint pop-ups 0758/07e2/086c/08f6 (standard)
public sealed class SpecialJobRumors : Card
{
    private const int CommonPicture = 0;
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int JobEventKind = 0x65;
    private const int JobCard = 0x168;

    public override int Number => 0x67;

    public override int Run(CardContext c)
    {
        Show(c, CommonPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // Line 0 starts "shown only" (opt_flag_0 = 2); line 9 is always open.
        var menu = Menu(c, 0);
        menu[0] = OptionState.Shown;
        menu[9] = OptionState.Enabled;
        if (c.HereQuery(0x21) % 20 == 0 && !c.Events.At(JobEventKind, c.Here))
        {
            menu[0] = OptionState.Enabled;
        }
        if (c.Events.Fact(PoliticsQuest, 0, c.Here))
        {
            menu[1] = menu[2] = OptionState.Enabled;
        }
        // The names go to $NamedTwoName (slot 1: a woman in one city of five) and $NamedOneName (slot 0).
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 1);
        c.RandomName(c.City.Ordinal + 0x16, 2, 0);

        // Every line is rated "Success is assured." for the whole party
        // (hints R069+0x1A10, 032e, 03ac, 03e6, 0424, 045e, 0498, 04d2, 050c, 0556).
        return menu.Run(
            // orig: R069+0x1A08
            new MenuLine(_ => c.Next = JobCard, Assured),
            new MenuLine(_ => RumourOfTheAristocrat(c), Assured),
            new MenuLine(_ => RumourOfTheEnemy(c), Assured),
            // DORMANT: lines 3..8 (opt_flag_3..8 = 0): empty actions.
            // orig: R069+0x1B34, 0422 R069+0x1B72, 045c R069+0x1BAC, 0496 R069+0x1BE6, 04d0 R069+0x1C20, 050a R069+0x1C5A
            // C: the hint handler of line 3 (R069+0x1B36) also assigns the leader to r.a, then overwrites it: it is rated like the others.
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            // orig: R069+0x1C94: back to the card the party came from (saved_card2)
            new MenuLine(_ => { c.PassHours(1); c.Next = c.State.SavedCard2; }, Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>The aristocrat hires for "easy" jobs: the rumour starts a fact (type 0x19) that lasts 20 days.</summary>
    // orig: R069+0x1A48
    private static void RumourOfTheAristocrat(CardContext c)
    {
        c.Screen.Text(2);
        c.Events.Create(-2, c.Here, 0x5A, FactKind, 0x19, 0, 0, 0x1E0, 0, PoliticsQuest);
        c.PassHours(0);
    }

    /// <summary>The "enemy" of the city lord: once the first rumour was followed nobody talks about him; otherwise a second fact (type 0x1A) starts.</summary>
    // orig: R069+0x1AB6
    private static void RumourOfTheEnemy(CardContext c)
    {
        if (c.Events.Fact(PoliticsQuest, 0x19, c.Here))
        {
            c.Screen.Text(4, TextFrame.BlankPage);
            return;
        }
        c.Events.Create(-2, c.Here, 0x5A, FactKind, 0x1A, 0, 0, 0x1E0, 0, PoliticsQuest);
        c.Screen.Text(3, TextFrame.BlankPage);
    }
}
