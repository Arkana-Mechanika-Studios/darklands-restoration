using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd15": a corridor whose floor tilts into a chute. If the stag horns were twisted (card 310)
/// it only shifts a little. Otherwise the party may leap (the least agile member decides), disarm
/// it (half the best Artifice), jam it with a potion or pray; failing, they slide down into the
/// dungeon cell (card 305).
/// </summary>
// orig: R121+0x1C06 (19F8:0006) matched; tests: CardSmokeTests
//   option 0: action R121+0x1E9E; hint R121+0x1F00
//   option 1: action R121+0x1F1C; hint R121+0x1F7E
//   option 2: action R121+0x1FA2; hint R121+0x1FFA
//   option 3: action R121+0x2032; hint R121+0x20AE
//   option 4: action R121+0x20E6; hint R121+0x2104
//   option 5: action R121+0x2140; hint R121+0x215E
//   option 6: action R121+0x219A; hint R121+0x21B8
//   option 7: action R121+0x21F4; hint R121+0x2212
//   option 8: action R121+0x224E; hint R121+0x226C
//   option 9: action R121+0x22A8; hint R121+0x22C6
//   item pop-up: R121+0x23DC, R121+0x2418, R121+0x2454,
//     R121+0x2490
//   saint pop-up: R121+0x24CC, R121+0x2554, R121+0x25DC,
//     R121+0x2664
//   other: R121+0x2302, R121+0x23A4
// DORMANT (generated): option slots 10..19 keep the default handler R121+0x23A4 ("screwed up.") and are never shown.
// C (generated): opt_flag_5/6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card_text(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card_text R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
public sealed class TiltingFloorTrap : Card
{
    public override int Number => 311;

    private const int Picture = 178;
    private const int TiltingPicture = 188;
    private const int FallPicture = 164;

    /// <summary>The fact (tag 1) set by the stag horns.</summary>
    private const int TrapDisarmedFact = 0x1B;

    private const int DungeonCell = 0x131;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        // C: the early way out runs before the table set-up (R121+0x2302), so card_items / card_saints and the
        //   option handlers stay as the previous card left them; nothing reads them before the next card sets its own.
        if (c.Events.FactAnywhere(1, TrapDisarmedFact))
        {
            // the original passes frame mode 2, drawn like mode 0 (card_text R043+0x0E40 treats 0 and 2 alike).
            c.Screen.Text(0);
            c.BattleMode = 1;
            return c.Next = -2;
        }
        ShowPicture(c, TiltingPicture);
        c.Next = Number;
        // The original leaves option flags 5..9 as the previous card left them; here they are hidden.
        var menu = Menu(c, 1);
        menu.Enable(0, 1, 2, 3, 4);
        c.SetCardItems(0x68, 0x66, 0x63, 0x67);
        c.SetCardSaints(0x59, 0x61, -1, 0x15);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            // orig: R121+0x1E9E / R121+0x1F00 matched — leap: the least agile member's effective agility.
            // C: party_min_eff_attr2 / party_max_skill set best_member, which the hints read (as unsigned char).
            new MenuLine(chance => Leap(c, chance), () => OptionHint.ChanceOf(c.PartyMinEffectiveAgility(), c.BestMember)),
            // orig: R121+0x1F1C / R121+0x1F7E matched — disarm: half the best Artifice (signed /2, never negative).
            new MenuLine(chance => Disarm(c, chance), () => OptionHint.ChanceOf(c.PartyMaxSkill((int)Skill.Artifice) / 2, c.BestMember)),
            new MenuLine(_ => JamItWithAPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.Unknown)),
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    // orig: the common tail of R121+0x1E9E / _031c / _03a2 / _0432 matched
    private static void Across(CardContext c, int battleMode)
    {
        c.BattleMode = battleMode;
        c.Next = -2;
    }

    // orig: R121+0x1E9E matched
    private static void Leap(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.Screen.Text(2);
            Across(c, 2);
            return;
        }
        ShowPicture(c, FallPicture);
        c.Screen.Text(3);
        c.Next = DungeonCell;
    }

    // orig: R121+0x1F1C matched
    private static void Disarm(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(4);
            Across(c, 1);
            return;
        }
        c.Screen.Text(5);
        c.Next = DungeonCell;
    }

    // orig: R121+0x1FA2 matched
    private static void JamItWithAPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        ShowPicture(c, Picture);
        c.Screen.Text(6); // card_text(k, 2): frame mode 2 drawn like mode 0 (card_text R043+0x0E40 treats 0 and 2 alike)
        Across(c, 1);
    }

    // orig: R121+0x2032 matched
    // C: an int function: `return 0` after a cancelled prayer, `return;` without a value after text 7, and it falls
    //   off its end after the slide; the menu ignores the value.
    private static void Pray(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            c.Screen.Text(7);
            Across(c, 1);
            return;
        }
        ShowPicture(c, FallPicture);
        c.Screen.Text(8);
        c.Next = DungeonCell;
    }

    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    // orig: R121+0x20E6 / _0540 / _059a / _05f4 / _064e / _06a8 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
