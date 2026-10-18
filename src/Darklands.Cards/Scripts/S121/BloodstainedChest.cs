namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd13": an empty chest with a single bloodstain; the party goes back to the fortress
/// (−2, battle mode 1). The card offers no choice and does not reset the card's items and saints.
/// </summary>
// orig: R121+0x0A56 (18DD:0006) matched; tests: CardSmokeTests
//   item pop-up: R121+0x0F3A, R121+0x0F76, R121+0x0FB2,
//     R121+0x0FEE
//   saint pop-up: R121+0x102A, R121+0x10B2, R121+0x113A,
//     R121+0x11C2
//   other: R121+0x0ADC, R121+0x0AFA, R121+0x0B36,
//     R121+0x0B54, R121+0x0B90, R121+0x0BAE,
//     R121+0x0BEA, R121+0x0C08, R121+0x0C44,
//     R121+0x0C62, R121+0x0C9E, R121+0x0CBC,
//     R121+0x0CF8, R121+0x0D16, R121+0x0D52,
//     R121+0x0D70, R121+0x0DAC, R121+0x0DCA,
//     R121+0x0E06, R121+0x0E24, R121+0x0E60,
//     R121+0x0F02
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class BloodstainedChest : Card
{
    public override int Number => 309;

    private const int Picture = 169;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Screen.Text(1);
        c.BattleMode = 1;
        return c.Next = -2;
    }

    /// <summary>The option table the card file also holds (ten lines: the first text again, then 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R121+0x0ADC R121+0x0AFA R121+0x0B36 R121+0x0B54 R121+0x0B90
    //   R121+0x0BAE R121+0x0BEA R121+0x0C08 R121+0x0C44 R121+0x0C62
    //   R121+0x0C9E R121+0x0CBC R121+0x0CF8 R121+0x0D16 R121+0x0D52
    //   R121+0x0D70 R121+0x0DAC R121+0x0DCA R121+0x0E06 R121+0x0E24
    //   R121+0x0E60 R121+0x0F02 R121+0x0F3A R121+0x0F76 R121+0x0FB2
    //   R121+0x0FEE R121+0x102A R121+0x10B2 R121+0x113A R121+0x11C2
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c);
}
