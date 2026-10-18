namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MainS01": the main street of a city by day, where the party chooses where to go: the
/// city square, the fortress, the market, the church, the business district, the inn, the
/// docks, a side street, another gate or the other places of the city. In a hostile city
/// every step risks the watch. Afterwards, the city's politics may call the party aside.
/// </summary>
// orig: R045+0x0C40 (18FC:0000) matched; tests: CardSmokeTests
public sealed class MainStreet : Card
{
    public const int CardNumber = 0x06;

    private const int Picture = 14, NoticePicture = 22;

    private const int Challenged = 0x01, SideStreet = 0x09, CitySquare = 0x12, Church = 0x13, Business = 0x14, Market = 0x15,
        Fortress = 0x1B, Inn = 0x1D, InnAtNight = 0x1E, Docks = 0x1F, OtherGate = 0x21, OtherGateAtNight = 0x22,
        Places = 0x3A;

    /// <summary>Events: a closed place (type 0 of kind 0x51).</summary>
    private const int Closed = 0x51;

    /// <summary>The quest and fact group of the city's politics.</summary>
    private const int Politics = 2;

    /// <summary>How likely the watch is to notice the party while the city is hostile (0 when it is not).</summary>
    private int _watchfulness;

    public override int Number => CardNumber;

    // orig: R045+0x0C40 (18FC:0000) matched (entry: music, the option flags, the watchfulness, the optional notice, the menu loop; the politics tail is StreetPolitics.AfterTheChoice)
    // orig: R045+0x162C (18FC:09EC) matched (init helper: empty in the original, it sets no option defaults)
    // DORMANT: the empty init helper installs no default "screwed up." handler and the card has no items or saints, so only option lines 0..9 exist; there is no pop-up handler.
    // UNCERTAIN: the original keeps option lines 10..19 untouched here (no init loop), so they hold stale values from the last card that set them; the card's MSG has no line for them and the menu cannot select them.
    // C: the options use the order square 0, fortress 1, market 2, church 3, business 4, inn 5, docks 6, side street 7, other gate 8, Places 9 in both opt_fn and the dispatch switch; hint and action pair up one to one.
    public override int Run(CardContext c)
    {
        c.PlayMusic(6);
        Show(c, Picture);
        OptionState IfThere(int building) => c.HereQuery(building) != 0 ? OptionState.Enabled : OptionState.Hidden;
        var square = IfThere(0x10);
        var fortress = IfThere(0x0F);
        var market = IfThere(0x11);
        var docks = IfThere(0x12);
        var inn = c.EventTypeAtHere(Closed, 0) ? OptionState.Shown : OptionState.Enabled;
        c.StandingSeen = c.HereQuery(0);
        _watchfulness = c.Hostile() ? c.LocalPrice(0x23, 0x14) : 0;
        c.Variables.Money[0] = Core.Economy.Money.From(0, 0, 1);
        if (c.Events.FactValue(Politics, 0, c.Here) == -1 && c.Random(100) < 35)
        {
            ShowPicture(c, NoticePicture);
            c.Screen.Text(1);
        }

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        menu[0] = square;
        menu[1] = fortress;
        menu[2] = market;
        menu[5] = inn;
        menu[6] = docks;
        c.Next = Number;
        var next = menu.Run(
            // orig: R045+0x10BA (18FC:047A) matched (option 0 action: CitySquare; no time passes); hint R045+0x10E2 (18FC:04A2) (chance at least 10, or success assured when the city is not hostile)
            Walk(c, CitySquare, 10, passTime: false),
            // orig: R045+0x113A (18FC:04FA) matched (option 1 action: Fortress; no time passes); hint R045+0x1162 (18FC:0522) (chance at least 10, or success assured when the city is not hostile)
            Walk(c, Fortress, 10, passTime: false),
            // orig: R045+0x11BA (18FC:057A) matched (option 2 action: Market); hint R045+0x11F4 (18FC:05B4) (chance at least 10, or success assured when the city is not hostile)
            Walk(c, Market, 10),
            // orig: R045+0x124C (18FC:060C) matched (option 3 action: Church); hint R045+0x1286 (18FC:0646) (chance at least 20, or success assured when the city is not hostile)
            Walk(c, Church, 20),
            // orig: R045+0x12DE (18FC:069E) matched (option 4 action: Business); hint R045+0x1318 (18FC:06D8) (chance at least 20, or success assured when the city is not hostile)
            Walk(c, Business, 20),
            // orig: R045+0x1370 (18FC:0730) matched (option 5 action: Inn; inn by day or by night); hint R045+0x13B8 (18FC:0778) (chance at least 30, or success assured when the city is not hostile)
            new MenuLine(chance => GoIfUnseen(c, chance, c.IsDaylight ? Inn : InnAtNight, passTime: true), () => Safety(30)),
            // orig: R045+0x1410 (18FC:07D0) matched (option 6 action: Docks); hint R045+0x144A (18FC:080A) (chance at least 20, or success assured when the city is not hostile)
            Walk(c, Docks, 20),
            // orig: R045+0x14A2 (18FC:0862) matched (option 7 action: side street; no noticing roll); hint R045+0x14B4 (18FC:0874) (chance at least 30, or success assured when the city is not hostile)
            new MenuLine(() => { c.PassHours(0); c.Next = SideStreet; }, () => Safety(30)),
            // orig: R045+0x150C (18FC:08CC) matched (option 8 action: other gate; by day or by night; no time passes); hint R045+0x1542 (18FC:0902) (chance at least 20, or success assured when the city is not hostile)
            new MenuLine(chance => GoIfUnseen(c, chance, c.IsDaylight ? OtherGate : OtherGateAtNight, passTime: false), () => Safety(20)),
            // orig: R045+0x159A (18FC:095A) matched (option 9 action: Places); hint R045+0x15D4 (18FC:0994) (chance at least 30, or success assured when the city is not hostile)
            Walk(c, Places, 30));
        return StreetPolitics.AfterTheChoice(c, CardNumber, next, mainStreetByDay: true);
    }

    /// <summary>An option that walks to a place, unless the watch notices the party on the way.</summary>
    // (shared helper for the Walk options above; each option's pair of functions is cited at its line)
    private MenuLine Walk(CardContext c, int place, int leastChance, bool passTime = true) =>
        new(chance => GoIfUnseen(c, chance, place, passTime), () => Safety(leastChance));

    /// <summary>Safe in a friendly city; in a hostile one the chance not to be noticed, at least <paramref name="least"/>.</summary>
    // orig: hint handlers R045+0x10E2, R045+0x1162, R045+0x11F4, R045+0x1286, R045+0x1318, R045+0x13B8, R045+0x144A, R045+0x14B4, R045+0x1542, R045+0x15D4 (see the option lines): success assured when DS:57B0 is 0, else clamp_range(least, 100, 100 - DS:57B0)
    private OptionHint Safety(int least) =>
        _watchfulness == 0
            ? OptionHint.Say(SuccessAssured)
            : OptionHint.ChanceOf(Math.Clamp(100 - _watchfulness, least, 100), OptionHint.WholeParty);

    // orig: action handlers R045+0x10BA, R045+0x113A, R045+0x11BA, R045+0x124C, R045+0x12DE, R045+0x1370, R045+0x1410, R045+0x14A2, R045+0x150C, R045+0x159A (see the option lines): the roll is rand_mod(100) >= chance and DS:57B0, else the place; the side street action R045+0x14A2 skips the roll
    // C: the original draws the number first and tests it before DS:57B0, the C# keeps that order (the draw is always made).
    private void GoIfUnseen(CardContext c, int chance, int place, bool passTime)
    {
        var noticed = c.Random(100) >= chance && _watchfulness != 0;
        if (passTime)
        {
            c.PassHours(0);
        }
        c.Next = noticed ? Challenged : place;
    }
}
