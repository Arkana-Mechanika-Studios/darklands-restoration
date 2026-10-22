namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC11": the party comes back from one of the rooms of the Last Castle of the Apocalypse.
/// The room it came from (the previous card) is marked as overcome in the castle's progress
/// flags, and the card ends with the courtyard fight. The file also holds an option table (ten
/// lines that go to 0x62), but the card never offers a menu: see <see cref="DormantOptions"/>.
/// </summary>
// orig: R130+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   item pop-up: R130+0x0428, R130+0x0464, R130+0x04A0,
//     R130+0x04DC
//   saint pop-up: R130+0x0518, R130+0x05A2, R130+0x062C,
//     R130+0x06B6
//   other: R130+0x00CE, R130+0x00E0, R130+0x010E,
//     R130+0x0120, R130+0x014E, R130+0x0160,
//     R130+0x018E, R130+0x01A0, R130+0x01CE,
//     R130+0x01E0, R130+0x020E, R130+0x0220,
//     R130+0x024E, R130+0x0260, R130+0x028E,
//     R130+0x02A0, R130+0x02CE, R130+0x02E0,
//     R130+0x030E, R130+0x0320, R130+0x034E,
//     R130+0x03F0
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C (generated): some hints return r uninitialised (stale stack): read as their message for the whole party.
public sealed class BackToTheApocalypseCourtyard : Card
{
    public override int Number => 365;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        // C: switch (prev_screen) with no default: any other previous card adds no flag.
        c.LastCastleFlags |= c.State.PreviousCard switch
        {
            345 => 0x01,
            347 => 0x08,
            348 => 0x02,
            350 => 0x04,
            354 => 0x10,
            357 => 0x20,
            358 => 0x40,
            _ => 0,
        };
        LastCastle.Fight(c, LastCastle.CourtyardEnemy);
        return c.Next;
    }

    /// <summary>The option table the card file also holds (ten lines: straight to 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R130+0x00CE R130+0x00E0 R130+0x010E R130+0x0120 R130+0x014E
    //   R130+0x0160 R130+0x018E R130+0x01A0 R130+0x01CE R130+0x01E0
    //   R130+0x020E R130+0x0220 R130+0x024E R130+0x0260 R130+0x028E
    //   R130+0x02A0 R130+0x02CE R130+0x02E0 R130+0x030E R130+0x0320
    //   R130+0x034E R130+0x03F0 R130+0x0428 R130+0x0464 R130+0x04A0
    //   R130+0x04DC R130+0x0518 R130+0x05A2 R130+0x062C R130+0x06B6
    // C: the line hints leave r uninitialised (stale stack): read as "Success is assured." for the whole party. The
    //   item pop-up hints say "Success is assured." too (and set $ChosenOneName to the member in the box).
    internal static MenuLine[] DormantOptions(CardContext c) =>
        [.. Enumerable.Range(0, 10).Select(k => new MenuLine(
            _ => CardMenu.ShowFirstTextAndEnd(c, showFirstText: false), OptionHint.Saying(CardTexts.SuccessAssured),
            () => c.MemberPopupHint(CardTexts.SuccessAssured)))];
}
