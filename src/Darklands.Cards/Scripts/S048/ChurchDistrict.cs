namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Churc00": the church district, by day under the gothic spires, at night in their shadow.
/// From here the party visits the cathedral, a church, the monastery or the university, or goes
/// on to the city square, the fortress, the main street or a side street. In a hostile city the
/// watch may be waiting; afterwards the city's politics may call the party aside or an uprising
/// takes over.
/// </summary>
// orig: R048+0x2504 (1A88:0004) matched; tests: CardSmokeTests
public sealed class ChurchDistrict : Card
{
    public const int CardNumber = 0x13;

    private const int DayPicture = 92, NightPicture = 93;

    private const int Challenged = 0x01, CitySquare = 0x12, CitySquareAtNight = 0x19, Fortress = 0x1B, FortressAtNight = 0x1C,
        MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A;

    /// <summary>How likely the watch is to be waiting while the city is hostile (0 when it is not).</summary>
    // C: DS:5856 is assigned at the start of every run.
    private int _watchfulness;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        _watchfulness = c.Hostile() ? c.LocalPrice(0x14, 3) : 0;
        if (c.Random(100) < _watchfulness)
        {
            // The watch stops the party at once, before the card is even shown.
            return c.Next = Challenged;
        }
        c.PlayMusic(8);
        Show(c, c.IsDaylight ? DayPicture : NightPicture);
        var states = new OptionState[9];
        states[0] = states[1] = states[3] = states[4] = states[5] = states[6] = states[7] = states[8] = OptionState.Enabled;
        if (c.HereQuery(0x1D) == 0)
        {
            states[0] = OptionState.Hidden;
        }
        if (c.HereQuery(0x1C) == 0)
        {
            states[3] = OptionState.Hidden;
        }
        if (c.HereQuery(0x1B) == 0)
        {
            states[4] = OptionState.Hidden;
        }
        if (c.HereQuery(0x0F) == 0)
        {
            states[6] = OptionState.Hidden;
        }
        c.StandingSeen = c.HereQuery(0);
        // DORMANT: R048+0x2CC2 (1A88:07C2) is empty and R048+0x2CC4 (1A88:07C4) ("screwed up.") is the default handler of
        // the option lines the card does not use.

        // C: the original picks the text block (day 0, night 1) on every round; only the hidden
        // option 2 stays on the card without moving the clock, so the block cannot change.
        var menu = Menu(c, c.IsDaylight ? 0 : 1);
        menu.Enable(0, 1, 3, 4, 5, 6, 7, 8);
        menu[0] = states[0];
        menu[1] = states[1];
        menu[3] = states[3];
        menu[4] = states[4];
        menu[5] = states[5];
        menu[6] = states[6];
        menu[7] = states[7];
        menu[8] = states[8];
        c.Next = Number;
        menu.Run(
            // orig: R048+0x2916 (1A88:0416) matched
            // orig: R048+0x2934 (1A88:0434) matched
            new MenuLine(() => { c.PassHours(0); c.Next = c.IsDaylight ? 0x32 : 0x33; }, OptionHint.Saying(NoRisk)),
            // orig: R048+0x296C (1A88:046C) matched
            // orig: R048+0x298A (1A88:048A) matched
            new MenuLine(() => { c.PassHours(0); c.Next = c.IsDaylight ? 0x34 : 0x35; }, OptionHint.Saying(NoRisk)),
            // DORMANT: option 2 (it has no text line) is never enabled; it would let 0 hours pass and stay on the card.
            // orig: R048+0x29C2 (1A88:04C2) matched
            // orig: R048+0x29CE (1A88:04CE) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(NoRisk)),
            // orig: R048+0x2A06 (1A88:0506) matched
            // orig: R048+0x2A18 (1A88:0518) matched
            new MenuLine(() => { c.PassHours(0); c.Next = 0x36; }, OptionHint.Saying(NoRisk)),
            // orig: R048+0x2A50 (1A88:0550) matched
            // orig: R048+0x2A62 (1A88:0562) matched
            new MenuLine(() => { c.PassHours(0); c.Next = 0x31; }, OptionHint.Saying(NoRisk)),
            // orig: R048+0x2A9A (1A88:059A) matched
            // orig: R048+0x2ADA (1A88:05DA) matched
            new MenuLine(() => Walk(c, 1, () => c.IsDaylight ? CitySquare : CitySquareAtNight), Safety),
            // orig: R048+0x2B24 (1A88:0624) matched
            // orig: R048+0x2B64 (1A88:0664) matched
            new MenuLine(() => Walk(c, 1, () => c.IsDaylight ? Fortress : FortressAtNight), Safety),
            // orig: R048+0x2BAE (1A88:06AE) matched
            // orig: R048+0x2BEE (1A88:06EE) matched
            new MenuLine(() => Walk(c, 1, () => c.IsDaylight ? MainStreet : MainStreetAtNight), Safety),
            // orig: R048+0x2C38 (1A88:0738) matched
            // orig: R048+0x2C78 (1A88:0778) matched
            new MenuLine(() => Walk(c, 2, () => c.IsDaylight ? SideStreet : SideStreetAtNight), Safety));

        var here = c.Here;
        var events = c.Events;
        if (events.QuestAt(RevolutionSupport.Politics, here) && events.FactValue(RevolutionSupport.Politics, 0, here) == -1)
        {
            var chance = c.IsDaylight ? 5 : 0x0F;
            chance += -(c.PartyMaxAttribute(3) - 0x21);
            if (events.Fact(RevolutionSupport.Politics, 6, here))
            {
                chance += 10;
            }
            if (c.Random(100) <= chance)
            {
                c.State.SavedCard = 0x12;
                c.Next = 0x37;
            }
        }
        c.Next = RevolutionSupport.UprisingExits(c, c.Next);
        return c.Next;
    }

    /// <summary>Safe in a friendly city; in a hostile one the chance the watch does not stop the party.</summary>
    private OptionHint Safety() =>
        _watchfulness == 0 ? OptionHint.Say(NoRisk) : OptionHint.ChanceOf(100 - _watchfulness, OptionHint.WholeParty);

    /// <summary>The watch stops the party when its draw is below the watchfulness; else it walks on after <paramref name="hours"/> hours.</summary>
    private void Walk(CardContext c, int hours, Func<int> destination)
    {
        if (c.Random(100) < _watchfulness)
        {
            c.PassHours(0);
            c.Next = Challenged;
            return;
        }
        c.PassHours(hours);
        c.Next = destination();
    }
}
