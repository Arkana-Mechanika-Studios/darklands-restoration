namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityN00": eyes and ears open in the city: read the official notices, ask about affairs
/// elsewhere in the Empire, gossip about the situation here, look for special jobs, or (when the
/// politics quest runs here) discuss the political situation. The lines lead on to the cards that
/// do the real work and come back here.
/// </summary>
// orig: R069+0x1250 (195D:0000) matched; tests: CardSmokeTests
// orig: handler setup R069+0x1720 is empty (the previous card's item/saint handlers stay: no line here uses them);
// unused-line handler R069+0x1722 ("screwed up.")
public sealed class CityNews : Card
{
    private const int NoticeBoardPicture = 22;
    private const int PoliticsQuest = 2;
    private const int ReadNotices = 0x6D;
    private const int AskAboutTheEmpire = 0x6E;
    private const int SpecialJobs = 0x67;
    private const int Politics = 0xAE;

    public override int Number => 0x66;

    public override int Run(CardContext c)
    {
        Show(c, NoticeBoardPicture);
        c.Next = Number;
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 9);
        if (c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            menu[4] = OptionState.Enabled;
        }

        // Every line is rated "Success is assured." for the whole party
        // (hints R069+0x1494, 0294, 02da, 0320, 0366, 03a0, 03da, 0414, 044e, 0498).
        return menu.Run(
            new MenuLine(_ => GoOn(c, ReadNotices, letTimePass: true), Assured),
            new MenuLine(_ => GoOn(c, AskAboutTheEmpire, letTimePass: true), Assured),
            // orig: R069+0x151C: gossip about this city is the politics card; no time passes here.
            new MenuLine(_ => GoOn(c, Politics, letTimePass: false), Assured),
            // orig: R069+0x1562: look for special jobs
            new MenuLine(_ => GoOn(c, SpecialJobs, letTimePass: false), Assured),
            // orig: R069+0x15A8: discuss the politics (the same card as the gossip)
            new MenuLine(_ => GoOn(c, Politics, letTimePass: false), Assured),
            // DORMANT: lines 5..8 (shown as 5 to 8 in the text; opt_flag_5..8 = 0): empty actions.
            // orig: R069+0x15EE, 03d8 R069+0x1628, 0412 R069+0x1662, 044c R069+0x169C
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            // orig: R069+0x16D6: back to the card the party came from
            new MenuLine(_ => { c.PassHours(0); c.Next = c.SavedCard; }, Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>Leaves for another card, which comes back here afterwards (saved_card2 remembers this card).</summary>
    // orig: R069+0x147C (read the notices) and R069+0x14CC (ask about the Empire)
    // let 0 hours pass first; the other three lines do not.
    private void GoOn(CardContext c, int card, bool letTimePass)
    {
        if (letTimePass)
        {
            c.PassHours(0);
        }
        c.State.SavedCard2 = Number;
        c.Next = card;
    }
}
