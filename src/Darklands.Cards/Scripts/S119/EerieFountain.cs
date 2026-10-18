using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd04": a fountain of cascading colours glows with nerve-wracking magic. The first four
/// members may drink from it: while its power lasts, each drink greatly strengthens Strength and
/// Endurance at a cost in Virtue. A successful prayer drains the fountain for good and teaches the
/// party a little Virtue. Only avoiding the place leaves (−2, battle mode 1).
/// </summary>
// orig: R119+0x0980 (18D0:0000) matched; tests: CardSmokeTests
//   option 0: action R119+0x0BF0; hint R119+0x0C88
//   option 1: action R119+0x0CC0; hint R119+0x0CCE
//   option 2: action R119+0x0D06; hint R119+0x0D7A
//   option 3: action R119+0x0DB2; hint R119+0x0E2A
//   option 4: action R119+0x0E62; hint R119+0x0EDC
//   option 5: action R119+0x0F14; hint R119+0x0F8E
//   option 6: action R119+0x0FC6; hint R119+0x0FE4
//   option 7: action R119+0x101C; hint R119+0x103A
//   option 8: action R119+0x1072; hint R119+0x1090
//   option 9: action R119+0x10C8; hint R119+0x10E6
//   item pop-up: R119+0x11FC, R119+0x1238, R119+0x1274,
//     R119+0x12B0
//   saint pop-up: R119+0x12EC, R119+0x1374, R119+0x13FC,
//     R119+0x1484
//   other: R119+0x1122, R119+0x11C4
// DORMANT (generated): option slots 10..19 keep the default handler R119+0x11C4 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// DIVERGENCE (generated): party_panel_rows(0x100) (root 0000:1716, a party-panel redraw) is left out; the screen redraws itself.
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class EerieFountain : Card
{
    public override int Number => 301;

    private const int Picture = 173;

    /// <summary>The fact (tag 1) that the fountain has been drained.</summary>
    private const int DrainedFact = 0x13;

    /// <summary>Where the handlers of option lines the text does not have lead.</summary>
    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        // C: the C tests the status bytes of slots 0..3 directly (party[k][0] != 0), not party_size.
        for (var who = 0; who < 4; who++)
        {
            // Only the first four slots get a line (the fifth stays hidden).
            menu[2 + who] = c.Status(who) != CharacterStatus.Empty ? OptionState.Enabled : OptionState.Hidden;
        }
        c.SetCardItems();
        c.SetCardSaints(0x0F, 0x6F, 0x35, 0x39);
        menu[0] = c.SaintsUsable();
        for (var k = 0; k < 4; k++)
        {
            c.Variables.Chosen[k] = k;
        }

        // DORMANT: the item pop-up handlers (R119+0x11FC..0x12B0) are never used: card_items stays all −1 and no line opens an item box.
        // DORMANT: R119+0x11C4 ("screwed up.") is the handler of unset slots 10..19, which are never shown.
        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            new MenuLine(_ => PrayOverTheFountain(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => { c.BattleMode = 1; c.Next = -2; }, unknown),
            new MenuLine(_ => Drink(c, 0), unknown),
            new MenuLine(_ => Drink(c, 1), unknown),
            new MenuLine(_ => Drink(c, 2), unknown),
            new MenuLine(_ => Drink(c, 3), unknown),
            // DORMANT: lines 6..9 stay hidden (their flags are never set); their handlers show text 0 and go to 0x62.
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            // C: the hint of line 9 sets r.b = leader and then −2; only −2 remains.
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    /// <summary>A heard prayer clears the water (text 1 for the first two saints, 2 for the others) and drains it for ever.</summary>
    // orig: R119+0x0BF0 (18D0:0270) matched
    private static void PrayOverTheFountain(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(3);
            return;
        }
        // DORMANT: a menu_slot outside 0..3 shows no text (impossible: the box has four saints).
        if (c.MenuSlot is 0 or 1)
        {
            c.Screen.Text(1);
        }
        else if (c.MenuSlot is 2 or 3)
        {
            c.Screen.Text(2);
        }
        c.Events.Create(Who.Everybody, c.Here, 0, 0x1C, DrainedFact, 0, 0, EventLog.Forever, 0, 1);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 0);
        // DIVERGENCE: the C then redraws the party panel (party_panel_rows(0x100)), also after a failed prayer; our
        //   screen redraws itself. The handler falls off its end without a value (ignored).
    }

    /// <summary>A member drinks: +50 Strength and Endurance, −1..4 Virtue while the fountain still works.</summary>
    // orig: R119+0x0D06 / _0432 / _04e2 / _0594 R119+0x0D06 / 0x0DB2 / 0x0E62 / 0x0F14 matched (one per member)
    private static void Drink(CardContext c, int who)
    {
        c.Variables.Chosen[0] = who;
        if (!c.Events.FactAnywhere(1, DrainedFact))
        {
            c.AddAttribute(who, (int)Core.Characters.Attribute.Strength, 50);
            c.AddAttribute(who, (int)Core.Characters.Attribute.Endurance, 50);
            c.AddSkill(who, (int)Skill.Virtue, -(c.Random(4) + 1));
            // DIVERGENCE: party_panel_rows(0x100) (panel redraw) is left out.
            c.Screen.Text(4);
        }
        else
        {
            c.Screen.Text(5);
        }
        // C: member 0's handler (0386) does not reset $ChosenOneName afterwards, but it is already 0: the same result.
        c.Variables.Chosen[0] = 0;
    }

    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    // orig: R119+0x0FC6 / _069c / _06f2 / _0748 R119+0x0FC6 / 0x101C / 0x1072 / 0x10C8 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
