using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityG04": the gate of a city preparing for war. Guards turn away casual visitors. Walk in
/// as if you belong, befriend the guards, pay a farmer to hide you in his hay, use a potion or
/// a saint, attack the guards, or step back.
/// </summary>
// orig: R074+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R074+0x09B4 (1838:09B4) matched; R074+0x0A32 (1838:0A32) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 7..19 (never offered).
// C: the toll is a module static money (DS:5EEC), set afresh on each visit; the start-up also calls idle-action().
public sealed class CityGateInWartime : Card
{
    public const int CardNumber = 0x78;

    /// <summary>The card's picture is not one of the numbered card pictures.</summary>
    private const string Picture = "gate4";

    // Potions that distract the guards (by pop-up slot): a stench, a blinding flash, smoke.
    private static readonly int[] Distractions = [0x5F, 0x65, 0x61];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(Name, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        var toll = Money.From(c.LocationQuery(c.Here, 2) / 6 + 1, 0, 0);
        c.Variables.Money[0] = toll;
        if (c.EventAtHere(0x1F))
        {
            menu[0] = OptionState.Shown;
        }
        if (c.EventAtHere(0x0A))
        {
            menu[1] = OptionState.Shown;
        }
        if (c.State.Party.Purse < toll)
        {
            menu[2] = OptionState.Shown;
        }
        var standing = c.Reputation(c.Here);
        c.SetCardSaints(0x59, 0x61);
        c.SetCardItems(Distractions);
        menu[3] = c.ItemsUsable();
        menu[4] = c.SaintsUsable();
        menu.EachRound = () => menu.Block = c.IsDaylight ? 0 : 0x12;

        return menu.Run(
            new MenuLine(() => WalkIn(c, menu, standing, WalkInChance(c, standing).Chance), () => WalkInChance(c, standing)),
            new MenuLine(() => Befriend(c, menu, standing), () => BefriendChance(c, standing)),
            new MenuLine(() => HideInTheHay(c, toll), OptionHint.Saying(NoRisk)),
            new MenuLine(chance => UsePotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion),
                () => OptionHint.ChanceOf(DistractionChance(c, c.MenuSlot), c.MenuMember)),
            new MenuLine(() => CallASaint(c, standing), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() => AttackTheGuards(c, standing), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R074+0x096A (1838:096A) matched; R074+0x097C (1838:097C) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x73; }, OptionHint.Saying(NoRisk)));
    }

    private static int CityStreet(CardContext c) => c.IsDaylight ? 6 : 8;

    // orig: R074+0x0328 (1838:0328) matched; tests: CardSmokeTests
    private static OptionHint WalkInChance(CardContext c, int standing)
    {
        var chance = standing + 30;
        if (c.EventAtHere(0x13))
        {
            chance -= 40;
        }
        if (c.Hostile())
        {
            chance = 0;
        }
        if (standing >= 50)
        {
            chance = 100;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), OptionHint.WholeParty);
    }

    // orig: R074+0x0262 (1838:0262) matched; tests: CardSmokeTests
    private static void WalkIn(CardContext c, CardMenu menu, int standing, int chance)
    {
        var roll = c.Random(100);
        if (standing >= 50)
        {
            c.Screen.Text(1, TextFrame.BlankPage);
            c.PassHours(1);
        }
        else
        {
            if (roll > chance && c.Hostile())
            {
                c.PassHours(0);
                c.Next = 1;
                return;
            }
            if (roll > chance)
            {
                c.PassHours(1);
                c.Screen.Text(2, TextFrame.BlankPage);
                c.Events.Renew(-2, c.Here, 0, 0x1F, 1, 0x63, 0, 24, 0, 0);
                menu[0] = OptionState.Shown;
                return;
            }
            c.PassHours(1);
            c.Screen.Text(3, TextFrame.BlankPage);
        }
        c.Next = CityStreet(c);
    }

    // orig: R074+0x043C (1838:043C) matched; tests: CardSmokeTests
    private static OptionHint BefriendChance(CardContext c, int standing)
    {
        var charisma = c.Attribute(c.Leader, 5);
        var speaking = c.Skill(c.Leader, 0x0A);
        var chance = 0;
        if (standing > -10 && !c.Hostile() && !c.EventAtHere(0x11))
        {
            chance = Math.Max((speaking + charisma) / 2, standing);
            if (c.EventAtHere(0x13))
            {
                chance -= 25;
            }
            chance = Math.Clamp(chance, 0, 100);
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R074+0x0376 (1838:0376) matched; tests: CardSmokeTests
    private static void Befriend(CardContext c, CardMenu menu, int standing)
    {
        var chance = BefriendChance(c, standing).Chance;
        var roll = c.Random(100);
        // C: in a hostile city the original leaves the outcome flag (register DI) unset.
        // UNCERTAIN: it is taken as 0 (failure), which then overrides following-card = 1 and keeps the
        // party at the gate; a non-zero leftover would instead let the party in (text 5).
        var success = false;
        if (c.Hostile())
        {
            c.PassHours(0);
            c.Next = 1;
        }
        else
        {
            success = chance >= roll;
        }
        c.PracticeSkill(c.Leader, 0x0A, success ? 1 : 0, 10, 0);
        if (success)
        {
            c.Screen.Text(5, TextFrame.BlankPage);
            c.PassHours(3);
            c.Next = CityStreet(c);
            return;
        }
        c.Events.Renew(-2, c.Here, 0, 0x0A, 1, 0x63, 0, 12, 0, 0);
        c.Screen.Text(0x11, TextFrame.BlankPage);
        menu[1] = OptionState.Shown;
        c.PassHours(1);
        c.Next = CardNumber;
    }

    // orig: R074+0x04DC (1838:04DC) matched; R074+0x0514 (1838:0514) matched; tests: CardSmokeTests
    private static void HideInTheHay(CardContext c, Money toll)
    {
        c.Screen.Text(4, TextFrame.BlankPage);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(toll);
        c.PassHours(2);
        c.Next = CityStreet(c);
    }

    // orig: R074+0x0A6A (1838:0A6A) matched; R074+0x0A8E (1838:0A8E) matched; R074+0x0AB2 (1838:0AB2) matched; tests: CardSmokeTests
    // DORMANT: pop-up slot 3 has no handler of its own (a stale one) and no item (-1).
    /// <summary>How well each distraction works: the party's average agility plus 10, 30 or 20.</summary>
    private static int DistractionChance(CardContext c, int slot) =>
        Math.Clamp(c.PartyAverageEffectiveAgility() + slot switch { 0 => 10, 1 => 30, _ => 20 }, 0, 100);

    // orig: R074+0x054C (1838:054C) matched; R074+0x061A (1838:061A) matched; tests: CardSmokeTests
    private static void UsePotion(CardContext c, int chance)
    {
        // The option line itself only asks for a member and a potion (chance −1).
        // C: the chance is the handler's the loop calls (CardMenu.Run): the option line's -1, the box's for a click there, and for Enter
        // C: there card-option-handler[k], the box's handler for slot k % 4.
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Events.Renew(-2, c.Here, 0, 0x13, 1, 0x63, 0, 24, 0, 0);
        if (c.Random(100) > chance)
        {
            c.Screen.Text(9, TextFrame.BlankPage);
            c.PassHours(0);
            c.Next = CardNumber;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(6, TextFrame.BlankPage);
                c.AdjustReputation(c.Here, -1, -4);
                break;
            case 1:
                c.Screen.Text(7, TextFrame.BlankPage);
                c.AdjustReputation(c.Here, 0, -3);
                break;
            case 2:
                c.Screen.Text(8, TextFrame.BlankPage);
                c.AdjustReputation(c.Here, 0, -2);
                break;
            default:
                // DORMANT: slot 3 (no item there): no text, no reputation change, but the party is in.
                break;
        }
        c.PassHours(0);
        c.Next = CityStreet(c);
    }

    // orig: R074+0x0652 (1838:0652) matched; R074+0x06FC (1838:06FC) matched; R074+0x0AD6 (1838:0AD6) matched; R074+0x0B60 (1838:0B60) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c, int standing)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            // Any slot but the first names St. 0x61 (slots 2, 3 hold no saint: dormant).
            c.NameToSlot(1, c.MenuSlot == 0 ? 0x59 : 0x61, 0);
            c.Screen.Text(0x0A, TextFrame.BlankPage);
            if (standing > -10)
            {
                c.AdjustReputation(c.Here, 3, 9);
            }
            else
            {
                c.AdjustReputation(c.Here, -3, -9);
            }
            c.PassHours(0);
            c.Next = CityStreet(c);
            return;
        }
        c.Screen.Text(0x0B, TextFrame.BlankPage);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    // orig: R074+0x0734 (1838:0734) matched; R074+0x0932 (1838:0932) matched; tests: CardSmokeTests
    private static void AttackTheGuards(CardContext c, int standing)
    {
        // C: arguments are evaluated right to left (party-lore-rating draws nothing).
        var third = c.Random(3) + 1;
        var second = c.Random(5) + 4;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        var result = c.RunBattle(0x53, c.Here + 0x6F, 3, first, second, 0, third, 1, -1, 3, 0);
        var reputation = standing;
        if (reputation <= -40)
        {
            // The original rolls for a further loss and then ignores the result.
            // C: improvement-roll may draw a random number; its result is discarded.
            c.ImproveRoll(reputation, -2, -7);
        }
        else
        {
            reputation -= 40;
        }
        c.SetReputation(c.Here, reputation);
        c.Events.Renew(-2, c.Here, 0, 0x13, 1, 0x63, 0, 24, 0, 0);
        var hours = 2000 / c.LocationQuery(c.Here, 2);
        switch (result)
        {
            case 0:
                c.Screen.Text(0x0C, TextFrame.BlankPage);
                c.PassHours(0);
                c.Events.Renew(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, hours, 0, 0);
                c.Next = 6;
                break;
            case 1:
                c.ReportAway();
                c.Screen.Text(0x0E, TextFrame.BlankPage);
                c.PassHours(1);
                c.Events.Renew(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, hours, 0, 0);
                c.Next = c.IsDaylight ? 9 : 0x0A;
                break;
            case 2:
                c.ReportAway();
                c.Screen.Text(0x0F, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = 0x73;
                break;
            case 3:
                c.Screen.Text(0x0D, TextFrame.BlankPage);
                c.PassHours(0);
                // Left outside: the party is in the wilderness (the event below then too).
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            case 4:
                c.Screen.Text(0x10, TextFrame.BlankPage);
                c.PassHours(3);
                c.Next = 0x0D;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party at the gate.
                break;
        }
        c.Events.Renew(-2, c.Here, 0x14, 0x11, 1, 0x63, 0, standing <= -75 ? 240 : 120, 0, 0);
    }
}
