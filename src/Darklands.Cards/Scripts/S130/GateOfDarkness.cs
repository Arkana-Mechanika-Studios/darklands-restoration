namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC03": the fourth gate. While the Fourth Seal holds (no fact 1/0x16) it does not budge
/// and the party turns back. Beyond it lies a blackness that swallows all light: the party
/// marches on (a fight against enemy 0x5B), asks a saint for light first, or goes back.
/// </summary>
// orig: R130+0x18AC (19C2:000C) matched; tests: CardSmokeTests
//   option 0: action R130+0x1A46; hint R130+0x1A68
//   option 1: action R130+0x1AA0; hint R130+0x1B4E
//   option 2: action R130+0x1B86; hint R130+0x1B94
//   saint pop-up: R130+0x1C70, R130+0x1CFA, R130+0x1D84,
//     R130+0x1E0E
//   other: R130+0x1BCC, R130+0x1C38
// DORMANT (generated): option slots 10..19 keep the default handler R130+0x1C38 ("screwed up.") and are never shown.
// C (generated): opt_flag_3/4/5/6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class GateOfDarkness : Card
{
    public override int Number => 347;

    private const int DarknessEnemy = 0x5B;

    public override int Run(CardContext c)
    {
        if (!c.Events.FactAnywhere(1, 0x16))
        {
            // C: this early return runs before the table set-up, so card_items / card_saints keep the last card's values
            //   (nothing reads them before the next card clears them).
            Show(c, 231);
            c.Screen.Text(3);
            c.BattleMode = 1;
            return c.Next = -2;
        }
        Show(c, 288);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.SetCardItems();
        c.SetCardSaints(0x2D, 0x31, 0x57, 0x64);
        menu[1] = c.SaintsUsable();

        return menu.Run(
            // orig: R130+0x1A46 / R130+0x1A68 matched
            new MenuLine(_ => LastCastle.Fight(c, DarknessEnemy), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R130+0x1AA0 / R130+0x1B4E matched; pop-up R130+0x1C70 / _045a / _04e4 / _056e matched
            new MenuLine(_ => PrayForLight(c, menu), OptionHint.Saying(CardTexts.SelectSaint), () => LastCastle.SaintHintForChosen(c)),
            // orig: R130+0x1B86 / R130+0x1B94 matched
            new MenuLine(_ => LastCastle.TurnBack(c), OptionHint.Saying(CardTexts.SuccessAssured)));
    }

    /// <summary>The saint lends a bluish light for the march (text 1), or the heavens stay silent (text 2).</summary>
    // orig: R130+0x1AA0 matched
    // C: returns 0 after a cancelled box (the line stays greyed out), otherwise returns without a value (ignored).
    private void PrayForLight(CardContext c, CardMenu menu)
    {
        menu[1] = OptionState.Shown;
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            c.NameToSlot(1, c.CardSaints[c.MenuSlot], 0);
            // C: card_load (the whole card), not card_load_near.
            Show(c, 289);
            c.Screen.Text(1);
            LastCastle.Fight(c, DarknessEnemy);
            return;
        }
        c.Screen.Text(2);
        c.Next = Number;
    }
}
