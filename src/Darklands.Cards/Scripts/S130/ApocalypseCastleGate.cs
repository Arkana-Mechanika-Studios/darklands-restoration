namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC00": a dark castle squats on the horizon. While the First Seal is intact (no fact
/// 1/1) a floating mask turns the party back. Once it is broken they reach the colossal doors
/// of the Castle of the Apocalypse: the castle's progress fact (1/0x1A) is set up, its value
/// (without the low byte) becomes the progress flags, and the party may enter the courtyard,
/// pray for guidance or leave.
/// </summary>
// orig: R130+0x0740 (18AC:0000) matched; tests: CardSmokeTests
//   option 0: action R130+0x0980; hint R130+0x09AE
//   option 1: action R130+0x09E6; hint R130+0x0A98
//   option 2: action R130+0x0AD0; hint R130+0x0AD8
//   saint pop-up: R130+0x0BAA, R130+0x0C34, R130+0x0CBE
//   other: R130+0x0B10, R130+0x0B72
// DORMANT (generated): option slots 10..19 keep the default handler R130+0x0B72 ("screwed up.") and are never shown.
// C (generated): opt_flag_3/4/5/6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class ApocalypseCastleGate : Card
{
    public override int Number => 344;

    private const int DoorsPicture = 345;

    public override int Run(CardContext c)
    {
        Show(c, 258);
        c.Next = Number;
        var menu = Menu(c, 3);
        menu.Enable(0, 1, 2);
        if (!c.Events.FactAnywhere(1, 1))
        {
            c.Screen.Text(0);
            ShowPicture(c, 346);
            c.Screen.Text(1);
            return c.Next = 12;
        }
        c.Screen.Text(0);
        ShowPicture(c, 284);
        c.Screen.Text(2);
        ShowPicture(c, DoorsPicture);
        if (!c.Events.FactAnywhere(1, 0x1A))
        {
            c.Events.Create(-2, c.Here, 0, 0x1C, 0x1A, 0, 0, 0x870, 0, 1);
        }
        // C: DS:160C = loc_fact_value_any(1, 0x1A); (char)DS:160C = 0; clears the low byte of the 16-bit word.
        // C: the fact's value is a 16-bit word; if the C# value ever exceeded 16 bits, the high bits would stay.
        c.LastCastleFlags = c.Events.FactValueAnywhere(1, 0x1A) & ~0xFF;
        c.SetCardItems();
        c.SetCardSaints(0x10, 0x42, 0x43);
        menu[1] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(_ => EnterTheCourtyard(c), OptionHint.Saying(CardTexts.SuccessAssured)),
            new MenuLine(_ => PrayForGuidance(c), OptionHint.Saying(CardTexts.SelectSaint), () => LastCastle.SaintHintForChosen(c)),
            new MenuLine(_ => c.Next = 12, OptionHint.Saying(CardTexts.NoRisk)));
    }

    // orig: R130+0x0980 (18AC:0240) matched
    private static void EnterTheCourtyard(CardContext c)
    {
        c.EventHere(0x64, 1);
        LastCastle.Fight(c, LastCastle.CourtyardEnemy);
    }

    // orig: R130+0x09E6 matched
    /// <summary>The saint shows a dire vision (text 4) or stays silent (text 5); the gate's picture comes back.</summary>
    private static void PrayForGuidance(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            c.NameToSlot(1, c.CardSaints[c.MenuEntrySlot], 0);
            ShowPicture(c, 344);
            c.Screen.Text(4);
        }
        else
        {
            ShowPicture(c, 311);
            c.Screen.Text(5);
        }
        ShowPicture(c, DoorsPicture);
    }
}
