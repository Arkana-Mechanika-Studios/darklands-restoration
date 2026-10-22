using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd39": a darkly dressed man in a broad-brimmed cap has been following the party and
/// accuses them of being up to something. Ignoring him, protesting innocence or admitting it
/// lightly all send him away, but the witches' suspicion (fact 7 of tag 0x1C) grows by one when
/// the party protests and falls by one when it admits. Fact 0x11 marks the meeting for three
/// days. The party then goes back to the card it came from.
/// </summary>
// orig: R127+0x21C0 (1A54:0000) matched; tests: CardSmokeTests
//   option 0: action R127+0x23EE; hint R127+0x2402
//   option 1: action R127+0x243A; hint R127+0x2464
//   option 2: action R127+0x249C; hint R127+0x24C6
//   option 3: action R127+0x24FE; hint R127+0x251C
//   option 4: action R127+0x2558; hint R127+0x2576
//   option 5: action R127+0x25B2; hint R127+0x25D0
//   option 6: action R127+0x260C; hint R127+0x262A
//   option 7: action R127+0x2666; hint R127+0x2684
//   option 8: action R127+0x26C0; hint R127+0x26DE
//   option 9: action R127+0x271A; hint R127+0x2738
//   item pop-up: R127+0x284E, R127+0x288A, R127+0x28C6,
//     R127+0x2902
//   saint pop-up: R127+0x293E, R127+0x29C6, R127+0x2A4E,
//     R127+0x2AD6
//   other: R127+0x2774, R127+0x2816
// DORMANT (generated): option slots 10..19 keep the default handler R127+0x2816 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 3, 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class SuspiciousStranger : Card
{
    public override int Number => 335;

    private const int Picture = 156;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.Events.Create(Who.Everybody, c.Here, 0, 0x1C, 0x11, 0, 0, 72, 0, 1);
        c.SetCardItems();
        c.SetCardSaints();

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: lines 3..9 R127+0x24FE, R127+0x2558, R127+0x25B2, R127+0x260C, R127+0x2666, R127+0x26C0, R127+0x271A
        //   (hints R127+0x251C, R127+0x2576, R127+0x25D0, R127+0x262A, R127+0x2684, R127+0x26DE, R127+0x2738) matched
        // DORMANT: lines 3..9 are never enabled; each shows text 0, passes 0 hours and goes to 0x62.
        Action<int> stayAWhile = _ =>
        {
            c.Screen.Text(0);
            c.PassHours(0);
            c.Next = 0x62;
        };
        return menu.Run(
            // orig: R127+0x23EE / R127+0x2402 matched
            new MenuLine(_ => GoOn(c, 1, 0), assured),
            // orig: R127+0x243A / R127+0x2464 matched
            new MenuLine(_ => GoOn(c, 2, 1), assured),
            // orig: R127+0x249C / R127+0x24C6 matched
            new MenuLine(_ => GoOn(c, 3, -1), assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured));
    }

    /// <summary>Shows the answer's text, changes the suspicion, and returns to the card the party came from.</summary>
    // orig: R127+0x23EE / R127+0x243A / R127+0x249C matched (suspicion 0: the first one changes nothing)
    private static void GoOn(CardContext c, int block, int suspicion)
    {
        c.Screen.Text(block);
        if (suspicion != 0)
        {
            c.Events.Update(-1, -1, 0x1C, 7, suspicion, 1, EventUpdate.Add);
        }
        c.Next = c.State.SavedCard;
    }
}
