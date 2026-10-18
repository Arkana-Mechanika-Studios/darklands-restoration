using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd19": in a corridor of the fortress the party overhears the Preceptor recruiting
/// someone. With sharp ears (the best Perception × 1.5 against a d100) they learn that the
/// fortress and the High Sabbats guard the seals of the wards around their master's castle;
/// otherwise they hear only murmurs. Then back to the fortress (−2). No choices.
/// </summary>
// orig: R122+0x1FE0 (1A36:0000) matched; tests: CardSmokeTests
//   item pop-up: R122+0x24E0, R122+0x251C, R122+0x2558,
//     R122+0x2594
//   saint pop-up: R122+0x25D0, R122+0x2658, R122+0x26E0,
//     R122+0x2768
//   other: R122+0x2082, R122+0x20A0, R122+0x20DC,
//     R122+0x20FA, R122+0x2136, R122+0x2154,
//     R122+0x2190, R122+0x21AE, R122+0x21EA,
//     R122+0x2208, R122+0x2244, R122+0x2262,
//     R122+0x229E, R122+0x22BC, R122+0x22F8,
//     R122+0x2316, R122+0x2352, R122+0x2370,
//     R122+0x23AC, R122+0x23CA, R122+0x2406,
//     R122+0x24A8
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class OverheardPreceptor : Card
{
    public override int Number => 315;

    private const int HallPicture = 178;

    public override int Run(CardContext c)
    {
        // This card has no menu and, unlike the others, leaves the item and saint lists alone.
        Show(c, HallPicture);
        // C: the two operands of >= are independent (party_max_attr only sets best_member), so their order does not matter.
        var perception = c.PartyMaxAttribute((int)Attribute.Perception);
        if (perception * 3 / 2 >= c.Random(100))
        {
            // UNCERTAIN: card_text(k, 2): frame mode 2 read as drawing like mode 0.
            c.Screen.Text(0);
            c.Screen.Text(1);
            c.Screen.Text(2);
        }
        else
        {
            c.Screen.Text(3);
        }
        c.BattleMode = 1;
        return -2;
    }

    /// <summary>The option table the card file also holds (ten lines: the first text again, then 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R122+0x2082 R122+0x20A0 R122+0x20DC R122+0x20FA R122+0x2136
    //   R122+0x2154 R122+0x2190 R122+0x21AE R122+0x21EA R122+0x2208
    //   R122+0x2244 R122+0x2262 R122+0x229E R122+0x22BC R122+0x22F8
    //   R122+0x2316 R122+0x2352 R122+0x2370 R122+0x23AC R122+0x23CA
    //   R122+0x2406 R122+0x24A8 R122+0x24E0 R122+0x251C R122+0x2558
    //   R122+0x2594 R122+0x25D0 R122+0x2658 R122+0x26E0 R122+0x2768
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c);
}
