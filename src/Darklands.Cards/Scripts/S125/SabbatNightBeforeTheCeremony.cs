using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd31": night falls over the sabbat field. A day passes while the party rests in a quiet
/// corner, dreams of a saint who sends them to find the fortress monastery, and wakes for the
/// day of the great ceremony (card 0x148). The count of sights visited starts again.
/// </summary>
/// <remarks>
/// The original card has no menu (it never sets up option lines or clears the card's items and
/// saints); its option handlers are never used.
/// </remarks>
// orig: R125+0x25FE (1A97:000E) matched; tests: CardSmokeTests
//   item pop-up: R125+0x2B58, R125+0x2B94, R125+0x2BD0,
//     R125+0x2C0C
//   saint pop-up: R125+0x2C48, R125+0x2CD0, R125+0x2D58,
//     R125+0x2DE0
//   other: R125+0x26FA, R125+0x2718, R125+0x2754,
//     R125+0x2772, R125+0x27AE, R125+0x27CC,
//     R125+0x2808, R125+0x2826, R125+0x2862,
//     R125+0x2880, R125+0x28BC, R125+0x28DA,
//     R125+0x2916, R125+0x2934, R125+0x2970,
//     R125+0x298E, R125+0x29CA, R125+0x29E8,
//     R125+0x2A24, R125+0x2A42, R125+0x2A7E,
//     R125+0x2B20
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class SabbatNightBeforeTheCeremony : Card
{
    public override int Number => 327;

    private const int Picture = 144;
    private const int DreamPicture = 170;
    private const int MorningPicture = 88;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.PassHours(24);
        c.Screen.Text(0);
        ShowPicture(c, DreamPicture);
        c.Screen.Text(1);
        ShowPicture(c, MorningPicture);
        c.Screen.Text(2);
        Clear(c, SightsVisited);
        Note(c, NightPassed, 48);
        return 0x148;
    }

    /// <summary>The option table the card file also holds (ten lines: the first text again, then 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R125+0x26FA R125+0x2718 R125+0x2754 R125+0x2772 R125+0x27AE
    //   R125+0x27CC R125+0x2808 R125+0x2826 R125+0x2862 R125+0x2880
    //   R125+0x28BC R125+0x28DA R125+0x2916 R125+0x2934 R125+0x2970
    //   R125+0x298E R125+0x29CA R125+0x29E8 R125+0x2A24 R125+0x2A42
    //   R125+0x2A7E R125+0x2B20 R125+0x2B58 R125+0x2B94 R125+0x2BD0
    //   R125+0x2C0C R125+0x2C48 R125+0x2CD0 R125+0x2D58 R125+0x2DE0
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c, CardTexts.SuccessAssured);
}
