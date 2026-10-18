namespace Darklands.Cards.Scripts;

/// <summary>
/// "$citys00": the city square by day. From the square the party reads the notices, visits the
/// council hall, the prison, the university, the great churches or the market, or leaves by the
/// main street or a side street. In a hostile city the guards may be waiting; afterwards, the
/// city's politics may call the party aside or an uprising takes over.
/// </summary>
// orig: R048+0x1B18 (19E9:0008) matched; tests: CardSmokeTests
public sealed class CitySquareByDay : Card
{
    public const int CardNumber = 0x12;

    private const int Picture = 107, GuardsPicture = 236;

    private const int Challenged = 0x01, Church = 0x13, Market = 0x15, MarketAtNight = 0x16, MainStreet = 0x06, MainStreetAtNight = 0x08,
        SideStreet = 0x09, SideStreetAtNight = 0x0A, Outskirts = 0x04;

    private const int Pillory = 0x4D, SquareBusy = 0x2F, PoliticsSettled = 0x3E;

    /// <summary>How likely the guards are to be waiting while the city is hostile (0 when it is not).</summary>
    // C: DS:584A is assigned before every use.
    private int _watchfulness;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x0E);
        Show(c, Picture);
        if (c.HereQuery(0x23) != 0 && !c.EventAtHere(SquareBusy))
        {
            c.Next = 0xB2;
            c.EventHere(SquareBusy, 0x18);
            return c.Next;
        }
        c.Next = CardNumber;
        var states = new OptionState[10];
        states[0] = states[1] = states[4] = states[5] = states[6] = states[7] = states[8] = states[9] = OptionState.Enabled;
        if (c.HereQuery(0x18) == 0)
        {
            states[1] = OptionState.Hidden;
        }
        // DORMANT: the barracks (option 3) are never enabled: the flag starts at 0 and is set to 0 again here.
        if (c.HereQuery(0x1A) == 0)
        {
            states[3] = OptionState.Hidden;
        }
        if (c.HereQuery(0x1B) == 0)
        {
            states[4] = OptionState.Hidden;
        }
        if (c.EventAtHere(Pillory))
        {
            states[2] = OptionState.Enabled;
        }
        c.StandingSeen = c.HereQuery(0);
        _watchfulness = c.Hostile() ? c.LocalPrice(0x28, 5) : 0;
        c.Variables.Chosen[0] = c.Leader;
        // C: the draw is made even when the guards cannot be waiting (a watchfulness of 0 never meets it).
        if ((_watchfulness << 1) / 3 > c.Random(100) && _watchfulness > 0)
        {
            ShowPicture(c, GuardsPicture);
            c.Screen.Text(1, TextFrame.OnPicture);
            return 1;
        }
        // DORMANT: R048+0x24CA (19E9:09BA) is empty and R048+0x24CC (19E9:09BC) ("screwed up.") is the default handler of option lines 10..19.

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8);
        for (var k = 0; k <= 8; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        menu.Run(
            new MenuLine(chance => Go(c, chance, 0, 1, () => { c.State.SavedCard = CardNumber; return 0x66; }), Safety),
            new MenuLine(chance => Go(c, chance, 0, 0, () => 0x2B), Safety),
            new MenuLine(chance => Go(c, chance, NoTime, NoTime, () => 0x11F), Safety),
            // DORMANT: option 3 (the barracks) is never enabled.
            new MenuLine(chance => Go(c, chance, 0, 0, () => 0x75), Safety),
            new MenuLine(chance => Go(c, chance, 0, 0, () => 0x31), Safety),
            new MenuLine(chance => Go(c, chance, 0, 1, () => Church), Safety),
            new MenuLine(chance => Go(c, chance, 0, 1, () => c.IsDaylight ? Market : MarketAtNight), Safety),
            new MenuLine(chance => Go(c, chance, 0, 1, () => c.IsDaylight ? MainStreet : MainStreetAtNight), Safety),
            new MenuLine(chance => Go(c, chance, 0, 1, () => c.IsDaylight ? SideStreet : SideStreetAtNight), Safety),
            // DORMANT: option 9 has no line in the card text (nine lines): it would let 0 hours pass and go to the city outskirts.
            // orig: R048+0x247E (19E9:096E) matched
            // orig: R048+0x2490 (19E9:0980) matched
            new MenuLine(() => { c.PassHours(0); c.Next = Outskirts; }, () => OptionHint.ChanceOf(100, c.Leader)));

        var here = c.Here;
        var events = c.Events;
        if (events.QuestAt(RevolutionSupport.Politics, here) && events.FactValue(RevolutionSupport.Politics, 0, here) == -1)
        {
            int chance;
            // DORMANT: the value was just tested to be -1, so the branch for the value 1 never runs.
            if (events.FactValue(RevolutionSupport.Politics, 0, here) == 1)
            {
                chance = c.IsDaylight ? 0x0F : 0x19;
                chance += -(c.PartyMaxAttribute(3) - 0x21);
            }
            else
            {
                chance = 0x21;
                chance -= c.PartyMaxAttribute(3);
            }
            if (c.Random(100) <= chance)
            {
                c.State.SavedCard = CardNumber;
                c.Next = 0x37;
            }
        }
        c.Next = RevolutionSupport.UprisingExits(c, c.Next);
        return c.Next;
    }

    /// <summary>Safe in a friendly city; in a hostile one the chance the guards do not notice the party.</summary>
    // orig: R048+0x1FFC (19E9:04EC) matched
    // orig: R048+0x2080 (19E9:0570) matched
    // orig: R048+0x20F2 (19E9:05E2) matched
    // orig: R048+0x2176 (19E9:0666) matched
    // orig: R048+0x21FA (19E9:06EA) matched
    // orig: R048+0x227E (19E9:076E) matched
    // orig: R048+0x2310 (19E9:0800) matched
    // orig: R048+0x23A2 (19E9:0892) matched
    // orig: R048+0x2434 (19E9:0924) matched
    private OptionHint Safety() =>
        _watchfulness == 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(100 - _watchfulness, OptionHint.WholeParty);

    private const int NoTime = -1;

    /// <summary>
    /// Walks on: if the guards notice the party (the draw is not below the chance, in a hostile
    /// city) they challenge it after <paramref name="failHours"/> hours, else the destination is
    /// reached after <paramref name="successHours"/> hours (<see cref="NoTime"/>: the clock is not touched).
    /// </summary>
    // orig: R048+0x1FBC (19E9:04AC) matched (option 0: notices; remembers the square)
    // orig: R048+0x2046 (19E9:0536) matched (option 1: council hall)
    // orig: R048+0x20CA (19E9:05BA) matched (option 2: prison; no time passes)
    // orig: R048+0x213C (19E9:062C) matched (option 3: barracks)
    // orig: R048+0x21C0 (19E9:06B0) matched (option 4: university)
    // orig: R048+0x2244 (19E9:0734) matched (option 5: churches)
    // orig: R048+0x22C8 (19E9:07B8) matched (option 6: market)
    // orig: R048+0x235A (19E9:084A) matched (option 7: main street)
    // orig: R048+0x23EC (19E9:08DC) matched (option 8: side street)
    // C: the draw is made first; with no watchfulness the second operand fails and the party goes on.
    private void Go(CardContext c, int chance, int failHours, int successHours, Func<int> destination)
    {
        if (c.Random(100) >= chance && _watchfulness != 0)
        {
            if (failHours != NoTime)
            {
                c.PassHours(failHours);
            }
            c.Next = Challenged;
            return;
        }
        if (successHours != NoTime)
        {
            c.PassHours(successHours);
        }
        c.Next = destination();
    }
}
