namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd11": a stiff Templar guard opens the door and demands the password. The party may say
/// one (card 308), attack, bluff, or pray to read the guard's thoughts; a prayer that fails gives
/// the party away (−4).
/// </summary>
// orig: R120+0x2018 (1A39:0008) matched; tests: CardSmokeTests
//   option 0: action R120+0x2252; hint R120+0x225A
//   option 1: action R120+0x2292; hint R120+0x22A4
//   option 2: action R120+0x22DC; hint R120+0x2306
//   option 3: action R120+0x233E; hint R120+0x23BA
//   option 4: action R120+0x23F2; hint R120+0x2400
//   option 5: action R120+0x2438; hint R120+0x2452
//   option 6: action R120+0x248A; hint R120+0x24A4
//   option 7: action R120+0x24DC; hint R120+0x24FA
//   option 8: action R120+0x2536; hint R120+0x2554
//   option 9: action R120+0x2590; hint R120+0x25AE
//   item pop-up: R120+0x26C4, R120+0x2700, R120+0x273C,
//     R120+0x2778
//   saint pop-up: R120+0x27B4, R120+0x283C, R120+0x28C4,
//     R120+0x294C
//   other: R120+0x25EA, R120+0x268C
// DORMANT (generated): option slots 10..19 keep the default handler R120+0x268C ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class TemplarDoorGuard : Card
{
    public override int Number => 307;

    private const int Picture = 168;
    private const int AlarmPicture = 167;

    private const int Password = 0x134;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems();
        c.SetCardSaints(0x6A, 0x2E, 0x35, 0x4E);
        menu[3] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        // DORMANT: in text block 2 the option lines 0..3 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => c.Next = Password, unknown),
            new MenuLine(_ => { c.CombatFlags |= 8; Leave(c, -3); }, unknown),
            new MenuLine(_ => menu.Offer(2, 4, 5, 6), unknown),
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => Leave(c, -2), unknown),
            new MenuLine(_ => { c.Screen.Text(3); Leave(c, -2); }, unknown),
            new MenuLine(_ => { c.Screen.Text(3); Leave(c, -2); }, unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    // orig: the common tail of R120+0x2292 / _032e / _03e2 / _0428 / _047a matched
    private static void Leave(CardContext c, int next)
    {
        c.BattleMode = 1;
        c.Next = next;
    }

    /// <summary>A heard prayer lets the party hear the guard's thoughts; a failed one starts a fight.</summary>
    // orig: R120+0x233E matched
    // C: an int function: `return 0` after a cancelled prayer, `return;` without a value after text 4, and it falls
    //   off its end after the alarm; the menu ignores the value.
    private static void Pray(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            c.Screen.Text(4);
            return;
        }
        ShowPicture(c, AlarmPicture);
        c.Screen.Text(5);
        c.CombatFlags |= 8;
        Leave(c, -4);
    }

    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    // orig: R120+0x24DC / _0526 / _0580 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
