using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityW03": the party stands under a city wall with the soldiers closing in. It can throw a
/// potion (a Sunburst, or the gooey one) to hold them off and climb the wall (with a rope if one
/// is carried), pray to a saint, attack the soldiers and then climb the wall, run away, or (one
/// time in two) try the barred sally port. Fights are played inside the card and lead on to the
/// outcome cards (0x73 the wilderness, 0xC/0xD captured or beaten, 9/10 into the city).
/// </summary>
// orig: R073+0x1668 (199E:0008) matched; tests: CardSmokeTests
// orig: handler setup R073+0x20F4 (item pop-ups 0b56/0b9c, standard saint pop-ups 0bd8/0c62/0cec/0d76,
// unused-line handler 0b1e "screwed up.")
// C: the card sets the window globals DS:EE70 = 0 and DS:EE72 = 7; they are not modelled (nothing reconstructed reads them).
public sealed class SoldiersClosingInAtTheWall : Card
{
    private const int RopeItem = 0x3B;
    private const int Stealth = (int)Skill.Stealth;
    private const int Wilderness = 0x73;

    public override int Number => 0x76;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        // C: the init helper R073+0x20F4 sets opt_on[0..19] = 1 (with the "screwed up." handler) and the loop copies only
        // flags 0..4: lines 5..19 stay enabled. The card's texts have seven option lines, two more than the code knows, so lines 5
        // and 6 are offered and do nothing (card oracle: CardOracleTests).
        menu.Enable(Enumerable.Range(5, 15).ToArray());
        // C: cur_rep is the card's copy of the reputation, read at the start; nothing here changes it.
        var standing = c.LocationQuery(c.Here, 0);
        c.SetCardItems(0x65, 0x68);
        c.SetCardSaints(0x36, 0x3D, 0x59, 0x61);
        menu[0] = c.ItemsUsable();
        menu[2] = c.SaintsUsable();
        // Line 1 is the sally port. C: opt_on[1] is copied from opt_flag_1 before the text is drawn,
        // and only the flag is cleared by a roll over 50: the port line is hidden from the next round on.
        var sallyPort = OptionState.Enabled;
        menu.EachRound = () =>
        {
            menu[1] = sallyPort;
            if (c.Random(100) <= 0x32)
            {
                menu.Block = 1;
            }
            else
            {
                sallyPort = OptionState.Hidden;
                menu.Block = 0;
            }
        };

        // C: a choice in a pop-up box calls the box's handler table (item_fn / saint_fn, the same for every line), and the
        // texts' boxes are not on the code's lines: text line 1 (alchemy, the horses left behind) opens the item box over
        // handler 1 (the sally port), text line 3 (pray) the saint box over handler 3 (the attack). Their box handlers are
        // the item and saint ones (the item slot 1 and every saint handler make the member $ChosenOneName).
        return menu.Run(
            new MenuLine(chance => ThrowAPotion(c, standing, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            // orig: R073+0x1B62 (199E:0502) matched: advance_hours(0), next_card = 0x2F
            new MenuLine(_ => { c.PassHours(0); c.Next = 0x2F; }, OptionHint.Saying(CardTexts.Unknown), () => PotionHint(c)),
            new MenuLine(_ => Pray(c, standing), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(_ => Attack(c, standing), OptionHint.Saying(CardTexts.EnterCombat), c.SaintPopupHint),
            new MenuLine(_ => { c.PassHours(0); c.Next = Wilderness; }, OptionHint.Saying(SuccessAssured)));
    }

    private static int Inside(CardContext c) => c.IsDaylight ? 9 : 0x0A;

    /// <summary>The Sunburst (slot 0) works better the smaller the city; the other potion has no chance given.</summary>
    // orig: R073+0x21B6 (item 0), R073+0x21FC (item 1)
    // C: the first potion's pop-up does not set $ChosenOneName; the second one's does.
    private static OptionHint PotionHint(CardContext c)
    {
        if (c.MenuSlot == 0)
        {
            var size = Math.Min(c.LocationQuery(c.Here, 2), 9);
            return new OptionHint(Math.Clamp((0x0F - size) * 6, 0, 100), "", c.MenuMember);
        }
        c.Variables.Chosen[0] = c.MenuMember;
        return new OptionHint(-1, CardTexts.Unknown, c.MenuMember);
    }

    /// <summary>Does any member carry a rope? (The scan stops at the first one.)</summary>
    private static bool AnyRope(CardContext c)
    {
        var rope = false;
        for (var who = 0; who < c.PartySize; who++)
        {
            rope = c.CountItems(who, RopeItem) >= 1;
            if (rope)
            {
                break;
            }
        }
        return rope;
    }

    /// <summary>
    /// The chance that the party gets up the wall: without a rope the weakest able member counts
    /// ((stealth + agility) / 2); with a rope the climb is rated from 0 up (an able member's score is
    /// never below the start value 0, so the chance stays 0 and $ChosenOneName stays as it was).
    /// </summary>
    // orig: the loops of R073+0x1830 and R073+0x1BAC
    // C: with no able member the original uses an uninitialised chance (and, in the prayer, an uninitialised
    // member); 0 and the whole party are used here.
    private static (int Chance, int Who) ClimbChance(CardContext c, bool rope)
    {
        var lowest = 0x63;
        var best = 0;
        var chance = 0;
        var who = Who.Everybody;
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) != CharacterStatus.Able)
            {
                continue;
            }
            if (!rope)
            {
                who = Who.Everybody;
                chance = (c.Skill(member, Stealth) + c.EffectiveAgility(member)) / 2;
                if (chance < lowest)
                {
                    lowest = chance;
                }
                chance = lowest;
            }
            else
            {
                who = c.Variables.Chosen[0];
                chance = (c.EffectiveAgility(member) * 2 + c.Skill(member, Stealth)) / 2;
                if (chance < best)
                {
                    c.Variables.Chosen[0] = member;
                    best = chance;
                }
                chance = best;
            }
        }
        return (chance, who);
    }

    /// <summary>
    /// The potion: if the pop-up's roll fails the guards are not hindered (chance 0) and a fight
    /// follows; otherwise a second roll against the climbing chance decides whether the party is over the wall.
    /// </summary>
    // orig: R073+0x1830 (hint R073+0x1B2A "Select a person and a potion.")
    // C: the option line itself (not the pop-up) gives chance −1, which is always below the roll: a fight.
    // C: the first roll is drawn first and the second one always follows.
    private static void ThrowAPotion(CardContext c, int standing, int popupChance)
    {
        var rope = AnyRope(c);
        var (chance, _) = ClimbChance(c, rope);
        if (c.Random(100) > popupChance)
        {
            chance = 0;
        }
        if (c.Random(100) <= chance)
        {
            int text;
            if (c.MenuSlot == 0)
            {
                text = rope ? 0x0A : 3;
            }
            else
            {
                text = rope ? 0x0C : 0x0B;
            }
            c.Screen.Text(text, TextFrame.BlankPage);
            c.PassHours(1);
            c.PracticeSkill(rope ? c.Variables.Chosen[0] : Who.Everybody, Stealth, 7, 10, 0);
            c.Next = Inside(c);
            return;
        }
        c.Screen.Text(chance == 0 ? 2 : 4, TextFrame.BlankPage);
        var result = FightTheGuards(c, 0x1E, -1);
        AfterTheFight(c, standing, result, afterThePotion: true, roll: 0, best: 0);
    }

    /// <summary>
    /// The guards' fight: their groups depend on the party's lore (arguments are evaluated right to left,
    /// so the second group's random number is drawn before the first's).
    /// </summary>
    private static int FightTheGuards(CardContext c, int side, int seed)
    {
        var lore = c.PartyLoreLevel();
        var third = lore % 3 + 1;
        var second = c.Random(4) + lore / 3 + 1;
        var first = c.Random(2) + lore / 4 + 1;
        return c.RunBattle(side, seed, 8, first, second, 0x21, third, 1, -1, 0, 0);
    }

    /// <summary>The result of the fight: unrest, the guards' attitude, the cards that follow.</summary>
    // orig: the second half of R073+0x1830 (afterThePotion) and of R073+0x1E0E
    // C: 0x7D0 / city size is a signed division; a city of size 0 would stop the original with a divide error.
    private static void AfterTheFight(CardContext c, int standing, int result, bool afterThePotion, int roll, int best)
    {
        c.Events.Renew(-2, c.Here, 0, EventKinds.Unrest, 1, 0x63, 0, 0x18, 0, 0);
        var hours = 0x7D0 / c.LocationQuery(c.Here, 2);
        switch (result)
        {
            case 0:
                if (afterThePotion)
                {
                    c.Screen.Text(5, TextFrame.BlankPage);
                    c.PassHours(1);
                    c.Events.Renew(-2, c.Here, 0x1E, EventKinds.Expensive, 1, 0x63, 0, hours, 0, 0);
                    c.Next = Wilderness;
                    break;
                }
                c.Events.Renew(-2, c.Here, 0x1E, EventKinds.Expensive, 1, 0x63, 0, hours, 0, 0);
                c.PassHours(1);
                if (roll <= best)
                {
                    c.Screen.Text(0x0D, TextFrame.BlankPage);
                    c.PracticeSkill(Who.Everybody, Stealth, 7, 10, 0);
                    c.Next = Inside(c);
                }
                else
                {
                    c.Screen.Text(0x0E, TextFrame.BlankPage);
                    c.Next = Wilderness;
                }
                break;
            case 1:
                if (afterThePotion)
                {
                    c.ReportAway();
                    c.Screen.Text(6, TextFrame.BlankPage);
                    c.PassHours(1);
                    c.Events.Renew(-2, c.Here, 0x1E, EventKinds.Expensive, 1, 0x63, 0, hours, 0, 0);
                    c.Next = Wilderness;
                    break;
                }
                c.Events.Renew(-2, c.Here, 0x1E, EventKinds.Expensive, 1, 0x63, 0, hours, 0, 0);
                c.PassHours(1);
                if (best * 2 / 3 >= roll)
                {
                    c.Screen.Text(0x0F, TextFrame.BlankPage);
                    c.Next = Inside(c);
                }
                else
                {
                    c.Screen.Text(0x10, TextFrame.BlankPage);
                    c.Next = Wilderness;
                }
                break;
            case 2:
                if (afterThePotion)
                {
                    c.ReportAway();
                }
                c.Screen.Text(7, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = Wilderness;
                break;
            case 3:
                c.Screen.Text(8, TextFrame.BlankPage);
                c.PassHours(0);
                c.Next = 0x0C;
                break;
            case 4:
                c.Screen.Text(9, TextFrame.BlankPage);
                c.PassHours(3);
                c.Next = 0x0D;
                break;
        }
        c.Events.Renew(-2, c.Here, 0x14, EventKinds.Hostile, 1, 0x63, 0, standing <= -0x4B ? 0xF0 : 0x78, 0, 0);
    }

    /// <summary>A saint carries the party over the wall (the first two) or makes the soldiers lay down their weapons.</summary>
    // orig: R073+0x1BAC (hint R073+0x1DD6 "Select a person and a saint.")
    // C: any answer but 0 and −1 counts as the saint answering (not only 1).
    private void Pray(CardContext c, int standing)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 0)
        {
            c.Screen.Text(0x12, TextFrame.BlankPage);
            c.PassHours(0);
            c.Next = Number;
            return;
        }
        var slot = c.MenuSlot;
        if (slot != 0 && slot != 1)
        {
            c.NameToSlot(1, slot == 2 ? 0x59 : 0x61, 0);
            c.Screen.Text(0x11, TextFrame.BlankPage);
            if (standing > -0x0A)
            {
                c.AdjustReputation(c.Here, 3, 9);
            }
            else
            {
                c.AdjustReputation(c.Here, -3, -9);
            }
            c.PassHours(0);
            c.Next = c.IsDaylight ? 6 : 8;
            return;
        }
        c.NameToSlot(1, slot == 0 ? 0x36 : 0x3D, 0);
        c.AdjustReputation(c.Here, 1, 5);
        var (chance, who) = ClimbChance(c, AnyRope(c));
        c.PassHours(1);
        var up = 0;
        if (c.Random(100) <= chance)
        {
            up = 1;
            c.Screen.Text(0x14, TextFrame.BlankPage);
            c.Next = Inside(c);
        }
        else
        {
            c.Screen.Text(0x13, TextFrame.BlankPage);
            c.Next = Wilderness;
        }
        c.PracticeSkill(who, Stealth, up, 1, 0);
    }

    /// <summary>Fight first, then the climb: the weakest able member's skill decides the climb.</summary>
    // orig: R073+0x1E0E (hint R073+0x2072 "Enter Combat")
    private static void Attack(CardContext c, int standing)
    {
        var best = 0x63;
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) == CharacterStatus.Able)
            {
                var score = (c.Skill(member, Stealth) + c.EffectiveAgility(member)) / 2;
                if (score < best)
                {
                    best = score;
                }
            }
        }
        // C: the climb roll is drawn before the fight's random numbers.
        var roll = c.Random(100);
        var result = FightTheGuards(c, 0x54, c.Here + 0x6F);
        AfterTheFight(c, standing, result, afterThePotion: false, roll, best);
    }
}
