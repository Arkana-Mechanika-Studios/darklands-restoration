using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityW04": the party stands at the wall beside an old sally port with a soldier on the
/// wall above. It can climb up with a rope or without, break open the sewer grate, use a potion
/// on the sally port, pray, bribe a guard (the toll, $Money1, depends on the city and on the
/// party's name there), or sneak back. Getting in leads to the city cards 9/10 (or 6/8 after a
/// saint's help), failing leads back to the wilderness (0x73).
/// </summary>
// orig: R073+0x2460 (1A7E:0000) matched; tests: CardSmokeTests
// orig: handler setup R073+0x2CB0 (item pop-ups 0906/0934/0966, standard saint pop-ups 09a2/0a2c,
// unused-line handler 08ce "screwed up.")
// The card name is copied from DS:5ED2 ("$CityW04", the card table's name) instead of being a string literal.
// C: the card sets the window globals DS:EE70 = 0 and DS:EE72 = 7; they are not modelled (nothing reconstructed reads them).
public sealed class SallyPortInTheCityWall : Card
{
    private const int Stealth = (int)Skill.Stealth;
    private const int RopeItem = 0x3B;
    private const int SewerAttempt = 0x10;
    private const int BribeRefused = 0x22;
    private const int Wilderness = 0x73;

    public override int Number => 0x77;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        // C: the init helper sets opt_on[0..19] = 1 (with the "screwed up." handler) and the loop copies only flags 0..6: lines
        // 7..19 stay enabled; the card's text has ten option lines, so lines 7..9 are offered and do nothing (card oracle: CardOracleTests).
        // BUG: the text's lines 1, 3 and 5 are "abandon the horses and ..." variants the script has no options for (MSGFILES): the
        // lines are numbered in order, so from the second line on each runs another option (the second line climbs without ropes,
        // the sewer-grate line prays, the alchemy line sneaks back), and the last three (pray, bribe, sneak back) do nothing.
        menu.Enable(Enumerable.Range(7, 13).ToArray());
        // C: cur_rep is the card's copy of the reputation; the bribe line's rating refreshes it.
        var standing = c.LocationQuery(c.Here, 0);
        // The scan stops at the first member with a rope; without members the flag stays 0.
        var rope = false;
        for (var who = 0; who < c.PartySize; who++)
        {
            rope = c.CountItems(who, RopeItem) >= 1;
            if (rope)
            {
                break;
            }
        }
        if (!rope)
        {
            menu[0] = OptionState.Shown;
        }
        if (c.EventAtHere(SewerAttempt))
        {
            menu[2] = OptionState.Shown;
        }
        if (c.EventAtHere(BribeRefused))
        {
            menu[5] = OptionState.Shown;
        }
        // The bribe: twice the party size times (city size / 3 + 1) pfennigs, or, if the party's name is
        // bad, (100 - standing) / 22 pfennigs.
        var amount = (short)(c.PartySize * (c.LocationQuery(c.Here, 2) / 3 + 1) << 1);
        if (standing < 0)
        {
            amount = (short)((100 - standing) / 0x16);
        }
        var bribe = Money.From(0, 0, amount);
        c.Variables.Money[0] = bribe;
        if (c.State.Party.Purse < bribe)
        {
            menu[5] = OptionState.Shown;
        }
        c.SetCardItems(0x66, 0x63, 0x67);
        c.SetCardSaints(0x59, 0x61);
        menu[3] = c.ItemsUsable();
        menu[4] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(chance => ClimbWithARope(c, chance), () => StrongestClimberHint(c)),
            new MenuLine(chance => ClimbWithoutRopes(c, chance), () => WeakestClimberHint(c)),
            new MenuLine(chance => BreakOpenTheGrate(c, standing, chance), () => StrongestMemberHint(c)),
            new MenuLine(_ => UseAPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(_ => BribeAGuard(c, standing, bribe, menu), () => BribeHint(c, ref standing)),
            // C: a choice in a pop-up box is rated by the card's box handler for the slot (DS:EB0A[menu_slot]: item_fn or saint_fn),
            // C: whatever line opened it: the text's line 6 (alchemy) opens the potion box and runs the sneak-back case, line 7
            // C: (prayer) opens the saints' box and has no case; the saint handler still makes the praying member $ChosenOneName.
            new MenuLine(_ => { c.PassHours(0); c.Next = Wilderness; }, OptionHint.Saying(SuccessAssured), () => PotionHint(c)),
            new MenuLine(() => { }, MenuLine.ScrewedUp, c.SaintPopupHint));
    }

    private static int Inside(CardContext c) => c.IsDaylight ? 9 : 0x0A;

    /// <summary>(agility scaled by fatigue × 2 + stealth) / 2.</summary>
    private static int ClimbingScore(CardContext c, int who) => (c.EffectiveAgility(who) * 2 + c.Skill(who, Stealth)) / 2;

    /// <summary>The best climber goes up first and is $ChosenOneName; the chance is his score.</summary>
    // orig: R073+0x27BE
    private static OptionHint StrongestClimberHint(CardContext c)
    {
        var best = 0;
        for (var who = 0; who < c.PartySize; who++)
        {
            if (c.Status(who) == CharacterStatus.Able)
            {
                var score = ClimbingScore(c, who);
                if (score > best)
                {
                    c.Variables.Chosen[0] = who;
                    best = score;
                }
            }
        }
        return new OptionHint(best, "", c.Variables.Chosen[0]);
    }

    /// <summary>
    /// A roll above the chance is what gets the party up (texts 1/3), a roll at or below it a fall (2/4):
    /// the practice mode is 1 for the roll above the chance.
    /// </summary>
    // orig: R073+0x2732
    // C: the test looks inverted (a high chance makes success unlikely), but the code does exactly this
    // (`cmp ax,[bp+6]; jle` in R073+0x2732; the same in R073+0x2820).
    private void ClimbWithARope(CardContext c, int chance)
    {
        var above = c.Random(100) > chance;
        c.PracticeSkill(c.Variables.Chosen[0], Stealth, above ? 1 : 0, 10, 0);
        if (above)
        {
            c.Screen.Text(1, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = Inside(c);
            return;
        }
        c.Screen.Text(2, TextFrame.BlankPage);
        c.Injure(c.Variables.Chosen[0], 0, 0, 10);
        c.PassHours(0);
        c.Next = Number;
    }

    /// <summary>The weakest able climber is $ChosenOneName; the chance is his score.</summary>
    // orig: R073+0x28AA
    private static OptionHint WeakestClimberHint(CardContext c)
    {
        var lowest = 999;
        for (var who = 0; who < c.PartySize; who++)
        {
            if (c.Status(who) == CharacterStatus.Able)
            {
                var score = ClimbingScore(c, who);
                if (score < lowest)
                {
                    c.Variables.Chosen[0] = who;
                    lowest = score;
                }
            }
        }
        return new OptionHint(lowest, "", OptionHint.WholeParty);
    }

    // orig: R073+0x2820 (the same inverted test as the climb with a rope)
    private void ClimbWithoutRopes(CardContext c, int chance)
    {
        var above = c.Random(100) > chance;
        c.PracticeSkill(Who.Everybody, Stealth, above ? 1 : 0, 10, 0);
        if (above)
        {
            c.Screen.Text(3, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = Inside(c);
            return;
        }
        c.Screen.Text(4, TextFrame.BlankPage);
        c.Injure(c.Variables.Chosen[0], 0, 0, 10);
        c.PassHours(0);
        c.Next = Number;
    }

    /// <summary>The strongest member (any status) breaks the grate: strength × 16 / 10.</summary>
    // orig: R073+0x29A8
    // C: the scan does not look at the member's status; a member needs strength above 0 to be chosen.
    private static OptionHint StrongestMemberHint(CardContext c)
    {
        var strongest = 0;
        for (var who = 0; who < c.PartySize; who++)
        {
            if (c.Attribute(who, (int)Attribute.Strength) > strongest)
            {
                strongest = c.Attribute(who, (int)Attribute.Strength);
                c.Variables.Chosen[0] = who;
            }
        }
        return new OptionHint((short)(strongest << 4) / 10, "", c.Variables.Chosen[0]);
    }

    /// <summary>Success: through the sewer (the stink costs standing); failure: the grate cannot be opened and the line is greyed out for a while.</summary>
    // orig: R073+0x290C
    private void BreakOpenTheGrate(CardContext c, int standing, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.Screen.Text(5);
            if (standing > 0)
            {
                c.AdjustReputation(c.Here, -2, -4);
            }
            c.PassHours(c.LocationQuery(c.Here, 2) / 3);
            c.Next = Inside(c);
            return;
        }
        c.Events.Create(c.Variables.Chosen[0], c.Here, 0, SewerAttempt, 3, 0x63, 0, 0x1F4, 0, 0);
        c.Screen.Text(6, TextFrame.BlankPage);
        c.PassHours(0);
        c.Next = Number;
    }

    /// <summary>
    /// The pop-up of the potions: Thunderbolt (slot 0) and acid (slot 1) give a chance by city size,
    /// the third potion none; each makes its user $ChosenOneName.
    /// </summary>
    // orig: R073+0x2D66 (item 0), 0934 R073+0x2D94 (item 1), 0966 R073+0x2DC6 (item 2)
    private static OptionHint PotionHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var size = c.LocationQuery(c.Here, 2);
        switch (c.MenuSlot)
        {
            case 0:
                return new OptionHint((short)(-(size - 0x2D) << 1), "", c.MenuMember);
            case 1:
                return new OptionHint(0x50 - size * 3, "", c.MenuMember);
            default:
                return new OptionHint(-1, CardTexts.Unknown, c.MenuMember);
        }
    }

    /// <summary>
    /// A potion on the sally port. The chance of the pop-up is ignored: the code sets the pop-up slot to 2 and
    /// the chance to 100 itself, so the third potion's text (the fire, 0xA) is always shown and everybody is singed.
    /// </summary>
    // orig: R073+0x29FE (hint R073+0x2AB4 "Select a person and a potion.")
    // C: `menu_slot = 2` is a real store in the code (mov ds:[EE6A],2); no item is taken from the pack.
    private void UseAPotion(CardContext c)
    {
        c.Events.Renew(-2, c.Here, 0, EventKinds.Unrest, 1, 0x63, 0, 0x18, 0, 0);
        c.MenuSlot = 2;
        var chance = 100;
        // DORMANT: a roll 0..99 is never above 100: text 7 (the potion fails) and the wilderness are unreachable.
        if (c.Random(100) > chance)
        {
            c.Screen.Text(7, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = Wilderness;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                // DORMANT: the slot was just set to 2.
                c.Screen.Text(8, TextFrame.BlankPage);
                break;
            case 1:
                // DORMANT: as above.
                c.Screen.Text(9, TextFrame.BlankPage);
                break;
            case 2:
                c.Screen.Text(0x0A, TextFrame.BlankPage);
                c.Injure(Who.Everybody, 0, 0, 5);
                break;
        }
        // C: both arms of `if (chance > 0)` are the same code (chance is 100 here).
        c.PassHours(1);
        c.Next = Inside(c);
    }

    /// <summary>One of the two saints carries the party over the wall; without an answer nothing changes.</summary>
    // orig: R073+0x2AEC (hint R073+0x2B6E "Select a person and a saint.")
    // C: any answer but −1 and 0 counts as the saint answering; the saint named in $NamedOneName is chosen by the pop-up slot.
    private void Pray(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.NameToSlot(1, c.MenuSlot == 0 ? 0x59 : 0x61, 0);
        if (answer != 0)
        {
            c.Screen.Text(0x0B, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = c.IsDaylight ? 6 : 8;
            return;
        }
        c.Screen.Text(0x0C, TextFrame.BlankPage);
        c.Next = Number;
    }

    /// <summary>The bribe works (100%) unless the party's name is bad (−10 or worse), which is read afresh here.</summary>
    // orig: R073+0x2C3A
    // C: cur_rep is assigned in the handler (`(cur_rep = loc_query(cur_loc, 0)) <= -10`), so it is refreshed
    // whenever the line is rated (also for a hint).
    private static OptionHint BribeHint(CardContext c, ref int standing)
    {
        standing = c.LocationQuery(c.Here, 0);
        return new OptionHint(standing <= -10 ? 0 : 100, "", OptionHint.WholeParty);
    }

    /// <summary>Pay the sergeant, unless the name is too bad or the city hostile; then the party is thrown out and may not try again for a while.</summary>
    // orig: R073+0x2BA6 (hint R073+0x2C3A)
    // C: the toll comes from the module's money variable set at the start (and shown as $Money1); the purse is not checked here.
    private void BribeAGuard(CardContext c, int standing, Money bribe, CardMenu menu)
    {
        if (standing > -0x0A && !c.EventAtHere(EventKinds.Hostile))
        {
            c.State.Party.Purse = c.State.Party.Purse.Subtract(bribe);
            c.Screen.Text(0x0D, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = Inside(c);
            return;
        }
        c.Screen.Text(0x0E, TextFrame.BlankPage);
        c.Events.Create(-2, c.Here, 0, BribeRefused, 1, 0x63, 0, 0x30, 0, 0);
        menu[5] = OptionState.Shown;
        c.PassHours(3);
        c.Next = Wilderness;
    }
}
