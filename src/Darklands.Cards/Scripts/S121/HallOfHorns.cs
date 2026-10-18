using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd14": a corridor lined with animal horns. Twisting any set clicks something (−3), but only
/// the stag horns disarm the trap ahead (card 311); a prayer hints at the stag horns or shows what the
/// goat and bull horns would do.
/// </summary>
// orig: R121+0x124A (195C:000A) matched; tests: CardSmokeTests
//   option 0: action R121+0x1480; hint R121+0x148E
//   option 1: action R121+0x14C6; hint R121+0x14E0
//   option 2: action R121+0x1518; hint R121+0x1532
//   option 3: action R121+0x156A; hint R121+0x15A2
//   option 4: action R121+0x15DA; hint R121+0x15F4
//   option 5: action R121+0x162C; hint R121+0x1646
//   option 6: action R121+0x167E; hint R121+0x16D6
//   option 7: action R121+0x170E; hint R121+0x172C
//   option 8: action R121+0x1768; hint R121+0x1786
//   option 9: action R121+0x17C2; hint R121+0x17E0
//   item pop-up: R121+0x18F6, R121+0x1932, R121+0x196E,
//     R121+0x19AA
//   saint pop-up: R121+0x19E6, R121+0x1A6E, R121+0x1AF6,
//     R121+0x1B7E
//   other: R121+0x181C, R121+0x18BE
// DORMANT (generated): option slots 10..19 keep the default handler R121+0x18BE ("screwed up.") and are never shown.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class HallOfHorns : Card
{
    public override int Number => 310;

    private const int Picture = 180;

    /// <summary>The fact (tag 1) that the stag horns have disarmed the tilting floor.</summary>
    private const int TrapDisarmedFact = 0x1B;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        c.SetCardItems();
        c.SetCardSaints(3, 0x45, 0x39, 0x35);
        menu[6] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            // orig: R121+0x1480 matched — walk on.
            new MenuLine(_ => { c.BattleMode = 1; c.Next = -2; }, unknown),
            new MenuLine(_ => TwistTheHorns(c), unknown),
            new MenuLine(_ => TwistTheHorns(c), unknown),
            new MenuLine(_ => TwistTheStagHorns(c), unknown),
            new MenuLine(_ => TwistTheHorns(c), unknown),
            new MenuLine(_ => TwistTheHorns(c), unknown),
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    // orig: R121+0x14C6 / _02d8 / _039a / _03ec matched (identical)
    private static void TwistTheHorns(CardContext c)
    {
        c.Screen.Text(1);
        c.BattleMode = 1;
        c.Next = -3;
    }

    // orig: R121+0x156A matched
    private static void TwistTheStagHorns(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0, 0x1C, TrapDisarmedFact, 0, 0, EventLog.Forever, 0, 1);
        TwistTheHorns(c);
    }

    // orig: R121+0x167E matched
    // C: an int function that falls off its end without a value (except `return 0` after a cancelled prayer); the
    //   menu ignores it. Every outcome stays on the card.
    private static void Pray(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(5);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
            case 1:
                c.Screen.Text(2);
                break;
            case 2:
                c.Screen.Text(3);
                break;
            case 3:
                c.Screen.Text(4);
                break;
        }
    }

    /// <summary>
    /// The handler of option lines 7..9, which the card enables though its text has only seven:
    /// the opening text again, then card 0x62.
    /// </summary>
    // orig: R121+0x170E / _0528 / _0582 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
