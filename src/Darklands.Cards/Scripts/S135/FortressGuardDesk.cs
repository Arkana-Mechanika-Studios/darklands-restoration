namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd42": the guard's desk of Baphomet's fortress monastery, with today's passwords and
/// papers about the "glorious leader" it defends. The party goes back to the fortress (−3,
/// battle mode 1).
/// </summary>
// orig: R135+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   other: R135+0x0082, R135+0x0094, R135+0x00C2,
//     R135+0x00D4, R135+0x0102, R135+0x0114,
//     R135+0x0142, R135+0x0154, R135+0x0182,
//     R135+0x0194, R135+0x01C2, R135+0x01D4,
//     R135+0x0202, R135+0x0214, R135+0x0242,
//     R135+0x0254, R135+0x0282, R135+0x0294,
//     R135+0x02C2, R135+0x02D4, R135+0x0302,
//     R135+0x03A4, R135+0x03DC, R135+0x0418,
//     R135+0x0454, R135+0x0490, R135+0x04CC,
//     R135+0x0556, R135+0x05E0, R135+0x066A
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C (generated): some hints return r uninitialised (stale stack): read as their message for the whole party.
public sealed class FortressGuardDesk : Card
{
    public override int Number => 364;

    private const int Picture = 0;

    public override int Run(CardContext c)
    {
        // No options: the original leaves the item and saint lists as they are.
        Show(c, Picture);
        c.Next = Number;
        c.Screen.Text(0);
        c.BattleMode = 1;
        return -3;
    }

    /// <summary>The option table the card file also holds (ten lines: straight to 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R135+0x0082 R135+0x0094 R135+0x00C2 R135+0x00D4 R135+0x0102
    //   R135+0x0114 R135+0x0142 R135+0x0154 R135+0x0182 R135+0x0194
    //   R135+0x01C2 R135+0x01D4 R135+0x0202 R135+0x0214 R135+0x0242
    //   R135+0x0254 R135+0x0282 R135+0x0294 R135+0x02C2 R135+0x02D4
    //   R135+0x0302 R135+0x03A4 R135+0x03DC R135+0x0418 R135+0x0454
    //   R135+0x0490 R135+0x04CC R135+0x0556 R135+0x05E0 R135+0x066A
    // C: the line hints leave r uninitialised (stale stack): read as "Success is assured." for the whole party. The
    //   item pop-up hints say "Success is assured." too (and set $ChosenOneName to the member in the box).
    // C: the reference file names them act_/hlp_/opt_/register_ + offset (card-script-R135-0000 = R135+0x0000).
    internal static MenuLine[] DormantOptions(CardContext c) =>
        [.. Enumerable.Range(0, 10).Select(k => new MenuLine(
            _ => CardMenu.ShowFirstTextAndEnd(c, showFirstText: false), OptionHint.Saying(CardTexts.SuccessAssured),
            () => c.MemberPopupHint(CardTexts.SuccessAssured)))];
}
