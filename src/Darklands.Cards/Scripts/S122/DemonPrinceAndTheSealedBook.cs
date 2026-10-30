using Darklands.Core.Events;
using Darklands.Core.World;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd16": in the heart of the fortress a demon prince guards a great book sealed with red
/// silk. The party may call on a saint, throw a potion, attack or flee; each way into the fight
/// leaves a mark that the demon has been met. Once he is banished (that mark found on arrival),
/// the seal is broken, the fortress is cleansed, the party learns where the last castle lies and
/// leaves (card 0x0C).
/// </summary>
// orig: R122+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R122+0x0322; hint R122+0x0396
//   option 1: action R122+0x03CE; hint R122+0x0438
//   option 2: action R122+0x0470; hint R122+0x04AA
//   option 3: action R122+0x04E2; hint R122+0x04F0
//   option 4: action R122+0x0528; hint R122+0x0546
//   option 5: action R122+0x0582; hint R122+0x05A0
//   option 6: action R122+0x05DC; hint R122+0x05FA
//   option 7: action R122+0x0636; hint R122+0x0654
//   option 8: action R122+0x0690; hint R122+0x06AE
//   option 9: action R122+0x06EA; hint R122+0x0708
//   item pop-up: R122+0x081E, R122+0x085A, R122+0x0896,
//     R122+0x08D2
//   saint pop-up: R122+0x090E, R122+0x0996, R122+0x0A1E,
//     R122+0x0AA6
//   other: R122+0x0744, R122+0x07E6
// DORMANT (generated): option slots 10..19 keep the default handler R122+0x07E6 ("screwed up.") and are never shown.
// C (generated): card-option-flag-4/5/6/7/8/9 are never assigned or read here: the card's init zeroes card-option-enabled[0..19], so those lines stay hidden, as in the C#.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card-text-block(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card-text-block R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class DemonPrinceAndTheSealedBook : Card
{
    public override int Number => 312;

    private const int DemonPicture = 137;
    private const int BookPicture = 134;

    /// <summary>The fortress fact "the demon prince has been fought" (tag 1, type 0x1D).</summary>
    private const int FactTag = 1;
    private const int DemonFoughtFact = 0x1D;

    public override int Run(CardContext c)
    {
        if (c.Events.FactAnywhere(FactTag, DemonFoughtFact))
        {
            return BreakTheSeal(c);
        }

        Show(c, DemonPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems(0x5F, 0x67, 0x64, 0x62);
        c.SetCardSaints(9, 0x17, 0x28);
        menu[1] = c.ItemsUsable();
        menu[0] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        var showAgain = new MenuLine(_ => ShowTheDemonAgain(c), unknown);
        return menu.Run(
            new MenuLine(_ => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => ThrowAPotion(c), OptionHint.Saying(CardTexts.SelectPotion),
                () => c.MemberPopupHint(CardTexts.Unknown)),
            new MenuLine(_ => Fight(c, 99), unknown),
            new MenuLine(_ => { c.BattleMode = 1; c.Next = -2; }, unknown),
            showAgain, showAgain, showAgain, showAgain, showAgain, showAgain);
    }

    /// <summary>
    /// The demon is gone: the fortress becomes an ordinary place, the next room is opened as the
    /// way out, fame is won and the book reveals the last castle.
    /// </summary>
    // orig: R122+0x0000 (1838:0000) matched — the branch taken when fact 0x1D is known.
    private int BreakTheSeal(CardContext c)
    {
        c.Events.Create(-2, c.Here, 0, 0x1C, 1, 0, 0, EventLog.Forever, 0, FactTag);
        c.GiveReward(4);
        // DIVERGENCE: the C writes locations[location-now], [location-now + 1] and [location-now - 4] without any check; the C#
        //   skips a place outside the location list (the wilderness, or the first/last records).
        if (c.HerePlace is { } here)
        {
            here.Icon = 4;
            here.ArrivalCard = 0x62;
        }
        if (LocationAt(c, c.Here + 1) is { Icon: 4 } next)
        {
            OpenAsWayOut(next);
        }
        else if (LocationAt(c, c.Here - 4) is { Icon: 4 } before)
        {
            OpenAsWayOut(before);
        }
        c.PassHours(2);
        // C: this path never calls card-run; card-run-near(card-title, book) shows the card and loads its texts, as Show does.
        Show(c, BookPicture);
        // card-text-block(1, 2): frame mode 2 drawn like mode 0 (card-text-block R043+0x0E40 treats 0 and 2 alike).
        c.Screen.Text(1);
        return 0x0C;
    }

    private static Location? LocationAt(CardContext c, int index) =>
        index >= 0 && index < c.State.Locations.Count ? c.State.Locations[index] : null;

    private static void OpenAsWayOut(Location place)
    {
        place.Icon = 0x16;
        place.ArrivalCard = 0x12A;
        place.EncounterCard = 0x62;
    }

    // orig: R122+0x0322 (1838:0322) matched
    private static void CallASaint(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        c.Screen.Text(result != 0 ? 2 : 3);
        // C: the hours argument is DS:270F, i.e. the immediate 0x270F = 9999 (forever).
        Fight(c, EventLog.Forever);
    }

    /// <summary>The potion is used up; the sneezing powder (slot 3) at least weakens him.</summary>
    // orig: R122+0x03CE (1838:03CE) matched
    private static void ThrowAPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Screen.Text(c.MenuSlot == 3 ? 5 : 4);
        Fight(c, 99);
    }

    /// <summary>Marks the demon as fought for <paramref name="hours"/> and starts the ambush battle.</summary>
    // orig: R122+0x0470 (1838:0470) matched (the shared tail of 0322 and 03ce)
    private static void Fight(CardContext c, int hours)
    {
        c.Events.Create(-2, c.Here, 0, 0x1C, DemonFoughtFact, 0, 0, hours, 0, FactTag);
        c.PassHours(1);
        c.CombatFlags |= 8;
        c.BattleMode = 1;
        c.Next = -3;
    }

    // orig: R122+0x0528 / _0582 / _05dc / _0636 / _0690 / _06ea matched (lines 4..9, never enabled)
    private static void ShowTheDemonAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
