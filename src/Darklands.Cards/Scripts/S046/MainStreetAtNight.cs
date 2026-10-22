namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MainS02": the main street of a city at night. The party chooses where to go, as by day,
/// but anyone abroad at night may meet trouble on the way (a night encounter that then goes
/// on to the chosen place). Afterwards, the city's politics may call the party aside.
/// </summary>
// orig: R046+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class MainStreetAtNight : Card
{
    public const int CardNumber = 0x08;

    private const int Picture = 49;

    private const int SideStreetAtNight = 0x0A, Church = 0x13, Business = 0x14, MarketAtNight = 0x16, CitySquareAtNight = 0x19,
        FortressAtNight = 0x1C, Inn = 0x1D, InnAtNight = 0x1E, DocksAtNight = 0x20, OtherGate = 0x21, OtherGateAtNight = 0x22,
        Places = 0x3A, NightEncounter = 0x3C;

    private const int Closed = 0x51, Politics = 2;

    /// <summary>The chance of trouble on the way, drawn on arrival.</summary>
    private int _danger;

    public override int Number => CardNumber;

    // orig: R046+0x0000 (1838:0000) matched (entry: music, the option flags, the danger draw, the optional notice and the menu loop; the politics tail is StreetPolitics.AfterTheChoice)
    // orig: R046+0x06EA (1838:06EA) matched (init helper: empty in the original, it sets no option defaults)
    // DORMANT: R046+0x06EC (1838:06EC) matched: a "screwed up." default handler that nothing installs (the empty init helper never points opt_fn at it), so it never runs; the card has no items or saints, so there is no pop-up handler either.
    // C: the original keeps option lines 10..19 untouched (no init loop), so they hold stale values from the last card that set them; the card's MSG has no line for them and the menu cannot select them.
    // C: ten options; hint and action pair up one to one in opt_fn and the dispatch switch. Every hint is 100 minus the danger drawn on arrival (the C# Safety).
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
        c.StandingSeen = c.Reputation(c.Here);
        _danger = c.LocalPrice(0x0E, 4);
        if (c.Events.FactValue(Politics, 0, c.Here) == -1)
        {
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
            // orig: R046+0x0422 (1838:0422) matched (option 0 action: CitySquareAtNight (0x19); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x0450 (1838:0450)
            Walk(c, () => CitySquareAtNight),
            // orig: R046+0x0466 (1838:0466) matched (option 1 action: FortressAtNight (0x1C); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x0494 (1838:0494)
            Walk(c, () => FortressAtNight),
            // orig: R046+0x04AA (1838:04AA) matched (option 2 action: MarketAtNight (0x16); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x04D8 (1838:04D8)
            Walk(c, () => MarketAtNight),
            // orig: R046+0x04EE (1838:04EE) matched (option 3 action: Church (0x13); one hour passes when the way is clear; saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x0524 (1838:0524)
            Walk(c, () => Church, passTime: true),
            // orig: R046+0x053A (1838:053A) matched (option 4 action: Business (0x14); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x0568 (1838:0568)
            Walk(c, () => Business),
            // orig: R046+0x057E (1838:057E) matched (option 5 action: Inn (0x1D by day or 0x1E by night); the roll is drawn but both branches go to the inn; one hour passes; saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x05A8 (1838:05A8)
            new MenuLine(() =>
                {
                    // The roll is made, but the party reaches the inn either way.
                    // (the line's chance is asked here, not rated by the menu: the draw comes first and the line has no pop-up.)
                    _ = c.Random(100) <= Safety().Chance;
                    c.PassHours(0);
                    c.Next = c.IsDaylight ? Inn : InnAtNight;
                },
                Safety),
            // orig: R046+0x05BE (1838:05BE) matched (option 6 action: DocksAtNight (0x20); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x05EC (1838:05EC)
            Walk(c, () => DocksAtNight),
            // orig: R046+0x0602 (1838:0602) matched (option 7 action: SideStreetAtNight (0x0A); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x0630 (1838:0630)
            Walk(c, () => SideStreetAtNight),
            // orig: R046+0x0646 (1838:0646) matched (option 8 action: OtherGate (0x21 by day or 0x22 by night); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x0690 (1838:0690)
            Walk(c, () => c.IsDaylight ? OtherGate : OtherGateAtNight),
            // orig: R046+0x06A6 (1838:06A6) matched (option 9 action: Places (0x3A); saves the place and goes to the night encounter 0x3C when the draw is not under the chance and the danger is not 0); hint R046+0x06D4 (1838:06D4)
            Walk(c, () => Places));
        return StreetPolitics.AfterTheChoice(c, CardNumber, next, mainStreetByDay: false);
    }

    // orig: hint handlers R046+0x0450, R046+0x0494, R046+0x04D8, R046+0x0524, R046+0x0568, R046+0x05A8, R046+0x05EC, R046+0x0630, R046+0x0690, R046+0x06D4 (see the option lines): 100 - DS:57BC
    private OptionHint Safety() => OptionHint.ChanceOf(100 - _danger, OptionHint.WholeParty);

    /// <summary>An option that walks to a place; met trouble first leads to the night encounter, which remembers the place.</summary>
    // orig: action handlers R046+0x0422, R046+0x0466, R046+0x04AA, R046+0x04EE, R046+0x053A, R046+0x057E, R046+0x05BE, R046+0x0602, R046+0x0646, R046+0x06A6 (see the option lines); the inn option is written out in Run
    // BUG: the night places are fixed card numbers (only the gate and the inn ask the time), no hour passes on the way
    // (advance_hours(0) at most), and the night encounter keeps the night place to return to after its hours: going between
    // the main street and the side streets at night stays in the night cards at any hour, with the encounters of the night
    // (card oracle at 12:00: 0x08 -> 0x0A -> 0x24 -> 0x08 -> 0x19).
    private MenuLine Walk(CardContext c, Func<int> place, bool passTime = false) =>
        new(chance =>
            {
                if (c.Random(100) >= chance && _danger != 0)
                {
                    c.State.SavedCard = place();
                    c.Next = NightEncounter;
                    return;
                }
                if (passTime)
                {
                    c.PassHours(0);
                }
                c.Next = place();
            },
            Safety);
}
