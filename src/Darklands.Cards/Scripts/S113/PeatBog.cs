using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetP02": the least nimble member falls into a peat bog and sinks. The party can throw a
/// rope, pull by hand, strip the member of all gear, pray, use a potion, or leave the member
/// to die (an untried abandonment brings a curse). The member loses some possessions in the muck.
/// </summary>
// orig: R113+0x0B8C (18F0:000C) matched; tests: CardSmokeTests
// orig: R113+0x1428 (18F0:08A8) matched (registration: item pop-ups R113+0x1502/09b8/09ee/0a22 and saint pop-ups do not name the member)
public sealed class PeatBog : Card
{
    public override int Number => 278;

    public override int Run(CardContext c)
    {
        Show(c, 331);
        c.Next = Number;
        var menu = Menu(c, c.PresentCount() == 1 ? 5 : 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5);
        c.PartyMinEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        // C: count_items_flags(-2, 0x64, 0): the long mask 0x64 passed as two words.
        if (c.CountItemsWithFlags(Who.Everybody, 0x64) == 0)
        {
            menu[0] = OptionState.Shown;
        }
        c.SetCardItems(0x6A, 0x6D, 0x70, 0x6E);
        c.SetCardSaints(0x12, 0x32, 0x33, 0x3C);
        menu[4] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R113+0x0EA0 (18F0:0320) matched (hint)
            new MenuLine(p => ThrowARope(c, menu, p), () => HaulHint(c, 3, true)),
            // orig: R113+0x0F64 (18F0:03E4) matched (hint)
            new MenuLine(p => PullByHand(c, menu, p), () => HaulHint(c, 2, true)),
            // orig: R113+0x100E (18F0:048E) matched (hint)
            new MenuLine(p => StripAndPull(c, menu, p), () => HaulHint(c, 3, false)),
            // orig: R113+0x10DA (18F0:055A) matched (hint)
            new MenuLine(_ => CallASaint(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R113+0x1204 (18F0:0684) matched (hint)
            new MenuLine(p => UseAPotion(c, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R113+0x12E0 (18F0:0760) matched (hint)
            new MenuLine(_ => LeaveThemToTheirFate(c, menu), () => OptionHint.Say(SuccessAssured)),
            // DORMANT: lines 6..9 are never offered; each lets no time pass (R113+0x1318, 07dc, 0820, 0864).
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)));
    }

    /// <summary>
    /// Hauling: the party's average Agility plus its average Strength (times <paramref name="strength"/>),
    /// less what the sinking member carries when <paramref name="burdened"/>.
    /// </summary>
    // C: party[var_chosen1][0x35] is the member's carried weight as a signed char.
    private static OptionHint HaulHint(CardContext c, int strength, bool burdened)
    {
        var chance = c.PartyAverageAttribute((int)Attribute.Agility) + c.PartyAverageAttribute((int)Attribute.Strength) * strength;
        if (burdened)
        {
            chance -= (sbyte)c.Encumbrance(c.Variables.Chosen[0]);
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), OptionHint.WholeParty);
    }

    /// <summary>The potions by slot: quality × 3/2 plus 9, 21 or −1; the last is sure (99).</summary>
    // orig: R113+0x1502 (18F0:0982) matched; slots 1..3 R113+0x1538, R113+0x156E, R113+0x15A2
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        var quality = c.ItemQuality(m, c.CardItems[c.MenuSlot]) * 3 / 2;
        var chance = c.MenuSlot switch { 0 => quality + 9, 1 => quality + 21, 2 => quality - 1, _ => 99 };
        return OptionHint.ChanceOf(chance, m);
    }

    /// <summary>The member keeps each possession with 50 plus Agility percent (at most 95).</summary>
    private static void LoseSomeThings(CardContext c)
    {
        var m = c.Variables.Chosen[0];
        c.KeepItems(m, Math.Clamp(c.Attribute(m, (int)Attribute.Agility) + 50, 1, 95));
    }

    private static void StillStuck(CardContext c, CardMenu menu, params int[] tried)
    {
        menu.Block = 3;
        menu.Set(OptionState.Shown, tried);
        c.PassHours(1);
    }

    // orig: R113+0x0E1A (18F0:029A) matched; tests: CardSmokeTests
    private static void ThrowARope(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            LoseSomeThings(c);
            c.Screen.Text(1);
            c.PassHours(1);
            c.LeaveForTheMap();
            return;
        }
        StillStuck(c, menu, 0, 1);
        c.Screen.Text(2);
        c.Next = 278;
    }

    // orig: R113+0x0EEC (18F0:036C) matched; tests: CardSmokeTests
    private static void PullByHand(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            LoseSomeThings(c);
            c.Screen.Text(4);
            c.PassHours(1);
            c.LeaveForTheMap();
            return;
        }
        StillStuck(c, menu, 1);
        c.Next = 278;
    }

    /// <summary>Stripped of everything (5 in 100 of each possession survive), the member slides out.</summary>
    // orig: R113+0x0FAC (18F0:042C) matched; tests: CardSmokeTests
    private static void StripAndPull(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.KeepItems(c.Variables.Chosen[0], 5);
            c.Screen.Text(6);
            c.PassHours(1);
            c.LeaveForTheMap();
            return;
        }
        StillStuck(c, menu, 0, 1, 2);
        c.Next = 278;
    }

    /// <summary>The praying member is $ChosenTwoName; an answered prayer saves the sinking one with most of the gear.</summary>
    // orig: R113+0x104A (18F0:04CA) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c)
    {
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                case 2:
                    c.Screen.Text(13);
                    break;
                case 3:
                    c.Screen.Text(14);
                    break;
            }
            c.PassHours(1);
            c.KeepItems(c.Variables.Chosen[0], 90);
            c.LeaveForTheMap();
            return;
        }
        c.Screen.Text(16);
        c.PassHours(1);
        c.Next = 278;
    }

    /// <summary>A potion (not used up: the original does not take it) frees the member; the last one dries the bog completely.</summary>
    // orig: R113+0x1112 (18F0:0592) matched; tests: CardSmokeTests
    private static void UseAPotion(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    LoseSomeThings(c);
                    c.Screen.Text(9);
                    break;
                case 1:
                    LoseSomeThings(c);
                    c.Screen.Text(10);
                    break;
                case 2:
                    LoseSomeThings(c);
                    c.Screen.Text(11);
                    break;
                case 3:
                    c.Screen.Text(12);
                    break;
            }
            c.PassHours(1);
            c.LeaveForTheMap();
            return;
        }
        c.Screen.Text(15);
        c.Next = 278;
    }

    /// <summary>
    /// The member sinks and is lost. If the party never even tried pulling by hand, the dying
    /// curse costs everybody Divine Favour, Charisma and Strength.
    /// </summary>
    // orig: R113+0x123C (18F0:06BC) matched; tests: CardSmokeTests
    // C: the curse test is opt_flag_1 != 2 — "pull by hand" not yet failed — not whether anything was tried.
    private static void LeaveThemToTheirFate(CardContext c, CardMenu menu)
    {
        if (menu[1] != OptionState.Shown)
        {
            c.Screen.Text(8);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -99);
            c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, -2);
            c.AddAttribute(Who.Everybody, (int)Attribute.Charisma, -5);
            c.AddAttribute(Who.Everybody, (int)Attribute.Strength, -3);
            c.AddAttributeMaximum(Who.Everybody, (int)Attribute.Strength, -1);
        }
        else
        {
            c.Screen.Text(7);
        }
        var lost = c.Variables.Chosen[0];
        c.DeleteMember(lost);
        c.SetStatus(lost, CharacterStatus.Empty);
        c.PassHours(0);
        c.LeaveForTheMap();
    }
}
