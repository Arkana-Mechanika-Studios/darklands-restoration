namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubr00": the burnt-out shell of a raubritter's keep. Searching the ruin takes an hour and
/// finds nothing (the party may search again); or the party goes on its way (the map).
/// </summary>
// orig: R129+0x10A4 (1942:0004) matched; tests: CardSmokeTests
//   option 0: action R129+0x12BC; hint R129+0x12D4
//   option 1: action R129+0x130C; hint R129+0x1314
//   option 2: action R129+0x1350; hint R129+0x136E
//   option 3: action R129+0x13AA; hint R129+0x13C8
//   option 4: action R129+0x1404; hint R129+0x1422
//   option 5: action R129+0x145E; hint R129+0x147C
//   option 6: action R129+0x14B8; hint R129+0x14D6
//   option 7: action R129+0x1512; hint R129+0x1530
//   option 8: action R129+0x156C; hint R129+0x158A
//   option 9: action R129+0x15C6; hint R129+0x15E4
//   item pop-up: R129+0x16FA, R129+0x1736, R129+0x1772,
//     R129+0x17AE
//   saint pop-up: R129+0x17EA, R129+0x1872, R129+0x18FA,
//     R129+0x1982
//   other: R129+0x1620, R129+0x16C2
// DORMANT (generated): option slots 10..19 keep the default handler R129+0x16C2 ("screwed up.") and are never shown.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class BurntOutKeep : Card
{
    public override int Number => 343;

    private const int Picture = 0;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // All ten lines are switched on; the text has only the first two.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        c.SetCardItems();
        c.SetCardSaints();

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: lines 2..9 R129+0x1350, R129+0x13AA, R129+0x1404, R129+0x145E, R129+0x14B8, R129+0x1512, R129+0x156C, R129+0x15C6
        //   (hints R129+0x136E, R129+0x13C8, R129+0x1422, R129+0x147C, R129+0x14D6, R129+0x1530, R129+0x158A, R129+0x15E4) matched
        // DORMANT: lines 2..9 are enabled but block 0 has no text for them, so the menu never shows them; each would
        //   show text 0, pass 0 hours and go to 0x62.
        Action<int> stayAWhile = _ =>
        {
            c.Screen.Text(0);
            c.PassHours(0);
            c.Next = 0x62;
        };
        return menu.Run(
            // orig: R129+0x12BC / R129+0x12D4 matched — search: the card stays (next_card unchanged).
            new MenuLine(_ =>
            {
                c.Screen.Text(1);
                c.PassHours(1);
            }, assured),
            // orig: R129+0x130C / R129+0x1314 matched
            new MenuLine(_ => c.Next = 0x0C, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured));
    }
}
