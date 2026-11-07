namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd41": a cell of Baphomet's fortress behind an oak door with an iron grating; it is
/// empty and holds nothing of interest. The party goes back to the fortress (−2, battle mode 1).
/// </summary>
// orig: R134+0x1EDA (1A25:000A) matched; tests: CardSmokeTests
//   item pop-up: R134+0x2294, R134+0x22D0, R134+0x230C,
//     R134+0x2348
//   saint pop-up: R134+0x2384, R134+0x240E, R134+0x2498,
//     R134+0x2522
//   other: R134+0x1F3A, R134+0x1F4C, R134+0x1F7A,
//     R134+0x1F8C, R134+0x1FBA, R134+0x1FCC,
//     R134+0x1FFA, R134+0x200C, R134+0x203A,
//     R134+0x204C, R134+0x207A, R134+0x208C,
//     R134+0x20BA, R134+0x20CC, R134+0x20FA,
//     R134+0x210C, R134+0x213A, R134+0x214C,
//     R134+0x217A, R134+0x218C, R134+0x21BA,
//     R134+0x225C
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C (generated): some hints return r uninitialised (stale stack): read as their message for the whole party.
public sealed class EmptyPrisonCell : Card
{
    public override int Number => 363;

    private const int Picture = 0;

    public override int Run(CardContext c)
    {
        // No options: the original leaves the item and saint lists as they are, and shows the
        // text as an option list without lines (which also waits for the player).
        Show(c, Picture);
        c.Next = Number;
        // orig: R134+0x1F26 card-option-lines(0) R043+0x0F2A and no card-choice-menu: the block is drawn as an option list, waited for
        //   (press-to-continue R043+0x32B6 at R043+0x10D0, mode 1) and the picture fades as before a choice; then the battle. vis10 session
        //   D-99119d: the original takes the Enter in that wait (key 1C0D read under R134+0x1EDA) and fades the picture before the battle
        c.Screen.OptionLines(0);
        c.BattleMode = 1;
        return -2;
    }

    /// <summary>The option table the card file also holds (ten lines: straight to 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R134+0x1F3A R134+0x1F4C R134+0x1F7A R134+0x1F8C R134+0x1FBA
    //   R134+0x1FCC R134+0x1FFA R134+0x200C R134+0x203A R134+0x204C
    //   R134+0x207A R134+0x208C R134+0x20BA R134+0x20CC R134+0x20FA
    //   R134+0x210C R134+0x213A R134+0x214C R134+0x217A R134+0x218C
    //   R134+0x21BA R134+0x225C R134+0x2294 R134+0x22D0 R134+0x230C
    //   R134+0x2348 R134+0x2384 R134+0x240E R134+0x2498 R134+0x2522
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c, CardTexts.SuccessAssured, showFirstText: false);
}
