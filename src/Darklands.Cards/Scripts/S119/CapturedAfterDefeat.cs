namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd06": beaten in the fortress, the party sinks into blackness as rough hands carry them
/// off; the story goes on in the dungeon cell (card 305). The card has no menu (its option
/// handlers are never used) and, unlike the others, does not reset the card's items and saints.
/// </summary>
// orig: R119+0x238E (1A70:000E) matched; tests: CardSmokeTests
//   item pop-up: R119+0x2842, R119+0x287E, R119+0x28BA,
//     R119+0x28F6
//   saint pop-up: R119+0x2932, R119+0x29BA, R119+0x2A42,
//     R119+0x2ACA
//   other: R119+0x23E4, R119+0x2402, R119+0x243E,
//     R119+0x245C, R119+0x2498, R119+0x24B6,
//     R119+0x24F2, R119+0x2510, R119+0x254C,
//     R119+0x256A, R119+0x25A6, R119+0x25C4,
//     R119+0x2600, R119+0x261E, R119+0x265A,
//     R119+0x2678, R119+0x26B4, R119+0x26D2,
//     R119+0x270E, R119+0x272C, R119+0x2768,
//     R119+0x280A
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class CapturedAfterDefeat : Card
{
    public override int Number => 303;

    private const int Picture = 177;

    private const int DungeonCell = 0x131;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        // UNCERTAIN: the original passes frame mode 2 to card_text; read as drawing like mode 0 (on the picture).
        c.Screen.Text(0);
        // C: the card function returns 0x131 directly: it never sets next_card, never fills the option table and
        //   never runs a menu.
        return DungeonCell;
    }

    /// <summary>
    /// The option table the file also holds: ten lines that each show the opening text again and end the game
    /// (0x62), with "You can always encounter the unknown" as hint, plus the usual pop-up handlers.
    /// </summary>
    // DORMANT: nothing calls these: the card returns before any menu (and before R119+0x2768, the table
    //   set-up). Kept as they would run if a menu were offered on block 0 with every line enabled.
    // orig: R119+0x23E4 / _00be / _0118 / _0172 / _01cc / _0226 / _0280 / _02da / _0334 / _038e matched (actions)
    // orig: R119+0x2402 / _00dc / _0136 / _0190 / _01ea / _0244 / _029e / _02f8 / _0352 / _03ac matched (hints)
    // orig: R119+0x2768 (table set-up), R119+0x280A ("screwed up." default),
    //   R119+0x2842 / _04fe / _053a / _0576 (item pop-up), R119+0x2932 / _063a / _06c2 / _074a (saint pop-up) matched
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c);
}
