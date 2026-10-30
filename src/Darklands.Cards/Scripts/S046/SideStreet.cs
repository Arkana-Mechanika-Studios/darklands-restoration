namespace Darklands.Cards.Scripts;

/// <summary>
/// "$SideS00": a side street by day. From the back streets the party goes to the main street,
/// the city square, the fortress, the market, the church, the business district, the docks,
/// another gate, the other quarters or the city's other places; in a hostile city the watch
/// may spot the party on the way. Afterwards, the city's politics may call the party aside.
/// </summary>
// orig: R046+0x0724 (18AA:0004) matched; tests: CardSmokeTests
public sealed class SideStreet : Card
{
    public const int CardNumber = 0x09;

    private const int Picture = 55, PosterPicture = 34;

    private const int Challenged = 0x01, MainStreet = 0x06, MainStreetAtNight = 0x08, CitySquare = 0x12, Church = 0x13,
        Business = 0x14, Market = 0x15, Fortress = 0x1B, Docks = 0x1F, OtherGate = 0x21, OtherQuarters = 0x7C, Places = 0x3B;

    private const int Politics = 2;

    /// <summary>How likely the watch is to spot the party while the city is hostile (0 when it is not).</summary>
    private int _watchfulness;

    public override int Number => CardNumber;

    // orig: R046+0x0724 (18AA:0004) matched (entry: music, the option flags, the watchfulness, the optional poster and the menu loop; the politics tail is StreetPolitics.AfterTheChoice)
    // orig: R046+0x1130 (18AA:0A10) matched (init helper: empty in the original, it sets no option defaults)
    // DORMANT: the empty init helper installs no default "screwed up." handler and the card has no items or saints, so only option lines 0..9 exist; there is no pop-up handler.
    // C: the original keeps option lines 10..19 untouched (no init loop), so they hold stale values from the last card that set them; the card's MSG has no line for them and the menu cannot select them.
    // C: ten options; hint and action pair up one to one in card-option-handler and the dispatch switch. Each action sets the next card to 1 (Challenged) when the draw is not under the chance and the watchfulness is not 0.
    public override int Run(CardContext c)
    {
        // C: sound-start(6) (root 047C:078A) directly, not through the music setting (music_play): the silence plays even with music off.
        c.PlaySound(6);
        Show(c, Picture);
        c.StandingSeen = c.HereQuery(0);
        _watchfulness = c.Hostile() ? c.LocalPrice(0x0F, 2) : 0;
        OptionState IfThere(int building) => c.HereQuery(building) != 0 ? OptionState.Enabled : OptionState.Hidden;
        var square = IfThere(0x10);
        var fortress = IfThere(0x0F);
        var market = IfThere(0x11);
        var docks = IfThere(0x12);
        if (c.Events.FactValue(Politics, 0, c.Here) == -1)
        {
            ShowPicture(c, PosterPicture);
            c.Screen.Text(1);
            Show(c, Picture);
        }

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        menu[1] = square;
        menu[2] = fortress;
        menu[3] = market;
        menu[6] = docks;
        c.Next = Number;
        var next = menu.Run(
            // orig: R046+0x0B6E (18AA:044E) matched (option 0 action: main street (0x06 by day or 0x08 by night)); hint R046+0x0BB6 (18AA:0496) (chance at least 30, or success assured when the city is not hostile)
            Walk(c, () => c.IsDaylight ? MainStreet : MainStreetAtNight, 30),
            // orig: R046+0x0C0E (18AA:04EE) matched (option 1 action: CitySquare (0x12)); hint R046+0x0C48 (18AA:0528) (chance at least 20, or success assured when the city is not hostile)
            Walk(c, () => CitySquare, 20),
            // orig: R046+0x0CA0 (18AA:0580) matched (option 2 action: Fortress (0x1B)); hint R046+0x0CDA (18AA:05BA) (chance at least 20, or success assured when the city is not hostile)
            Walk(c, () => Fortress, 20),
            // orig: R046+0x0D32 (18AA:0612) matched (option 3 action: Market (0x15)); hint R046+0x0D6C (18AA:064C) (chance at least 30, or success assured when the city is not hostile)
            Walk(c, () => Market, 30),
            // orig: R046+0x0DC4 (18AA:06A4) matched (option 4 action: Church (0x13)); hint R046+0x0DFE (18AA:06DE) (chance at least 30, or success assured when the city is not hostile)
            Walk(c, () => Church, 30),
            // orig: R046+0x0E56 (18AA:0736) matched (option 5 action: Business (0x14)); hint R046+0x0E90 (18AA:0770) (chance at least 40, or success assured when the city is not hostile)
            Walk(c, () => Business, 40),
            // orig: R046+0x0EE8 (18AA:07C8) matched (option 6 action: Docks (0x1F)); hint R046+0x0F22 (18AA:0802) (chance at least 40, or success assured when the city is not hostile)
            Walk(c, () => Docks, 40),
            // orig: R046+0x0F7A (18AA:085A) matched (option 7 action: OtherGate (0x21)); hint R046+0x0FB4 (18AA:0894) (chance at least 40, or success assured when the city is not hostile)
            Walk(c, () => OtherGate, 40),
            // orig: R046+0x100C (18AA:08EC) matched (option 8 action: OtherQuarters (0x7C)); hint R046+0x1046 (18AA:0926) (chance at least 20, or success assured when the city is not hostile)
            Walk(c, () => OtherQuarters, 20),
            // orig: R046+0x109E (18AA:097E) matched (option 9 action: Places (0x3B)); hint R046+0x10D8 (18AA:09B8) (chance at least 40, or success assured when the city is not hostile)
            Walk(c, () => Places, 40));
        return StreetPolitics.AfterTheChoice(c, CardNumber, next, mainStreetByDay: false);
    }

    // orig: action handlers R046+0x0B6E, R046+0x0C0E, R046+0x0CA0, R046+0x0D32, R046+0x0DC4, R046+0x0E56, R046+0x0EE8, R046+0x0F7A, R046+0x100C, R046+0x109E (see the option lines)
    private MenuLine Walk(CardContext c, Func<int> place, int leastChance) =>
        new(() =>
            {
                // (the line's chance is asked here, not rated by the menu: the draw comes first and the line has no pop-up.)
                var noticed = c.Random(100) >= Safety(leastChance).Chance && _watchfulness != 0;
                c.PassHours(0);
                c.Next = noticed ? Challenged : place();
            },
            () => Safety(leastChance));

    /// <summary>Safe in a friendly city; in a hostile one the chance not to be spotted, at least <paramref name="least"/>.</summary>
    // orig: hint handlers R046+0x0BB6, R046+0x0C48, R046+0x0CDA, R046+0x0D6C, R046+0x0DFE, R046+0x0E90, R046+0x0F22, R046+0x0FB4, R046+0x1046, R046+0x10D8 (see the option lines): success assured when DS:57C8 is 0, else limit-to-range(least, 100, 100 - DS:57C8)
    private OptionHint Safety(int least) =>
        _watchfulness == 0
            ? OptionHint.Say(SuccessAssured)
            : OptionHint.ChanceOf(Math.Clamp(100 - _watchfulness, least, 100), OptionHint.WholeParty);
}
