using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CampJ00": while the party camps in a lord's forest, his master huntsman and soldiers find
/// it and demand rent (spotted first by the guard, or by surprise when nobody kept watch). The
/// party may ignore them, leave, talk, pay, pray or fight; staying on marks the camp site for a
/// week. Every outcome ends on the map (card 0x0C).
/// </summary>
// orig: R134+0x0C1C (18F9:000C) matched; tests: CardSmokeTests
//   option 0: action R134+0x0FC2; hint R134+0x102E
//   option 1: action R134+0x1066; hint R134+0x1074
//   option 2: action R134+0x10AC; hint R134+0x1104
//   option 3: action R134+0x1180; hint R134+0x11C0
//   option 4: action R134+0x11F8; hint R134+0x1270
//   option 5: action R134+0x12A8; hint R134+0x12B2
//   option 6: action R134+0x12EA; hint -
//   option 7: action -; hint -
//   option 8: action -; hint -
//   option 9: action -; hint -
//   saint pop-up: R134+0x141A, R134+0x14A2
//   other: R134+0x0EBC, R134+0x1352, R134+0x138A,
//     R134+0x152A
// DORMANT (generated): option slots 10..19 keep the default handler R134+0x1352 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// C: clamp_range(lo, hi, v) is Math.Clamp(v, lo, hi) here; every call has lo <= hi.
public sealed class HuntsmanAtTheCamp : Card
{
    public override int Number => 361;

    private const int Picture = 0;

    /// <summary>The camp may be used for another week (event 0x60, 168 hours).</summary>
    private const int LeftInPeace = 0x60;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5);
        var guard = c.State.SavedCard; // the member who kept watch, −1 none
        c.Variables.Chosen[0] = guard;
        var pfennigs = c.Random(24) + 36;
        c.Variables.Money[0] = Money.From(0, 0, pfennigs);
        c.Variables.Money[1] = Money.From(0, 0, pfennigs * c.PresentCount());
        // C: money_cmp compares the totals in pfennigs as unsigned longs; the purse is never negative here.
        if (guard == -1)
        {
            menu.Block = 1;
            if (c.State.Party.Purse < c.Variables.Money[1])
            {
                menu[3] = OptionState.Shown;
            }
        }
        else if (c.State.Party.Purse < c.Variables.Money[0])
        {
            menu[3] = OptionState.Shown;
        }
        // The original clears only the saints here; the items stay as the last card set them.
        c.SetCardSaints(0x36, 0x3D, 0x45);
        menu[4] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            new MenuLine(_ => IgnoreThem(c, menu), unknown),
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => TalkThemRound(c, menu, chance), () => TalkingChance(c)),
            new MenuLine(_ => PayTheRent(c), unknown),
            new MenuLine(_ => CallOnASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // DORMANT: lines 6..9 are never enabled. Their actions (R134+0x12EA / _06de / _06e2 / _06e6) and
            //   hints (R134+0x12EC / _06e0 / _06e4 / _06e8) are empty functions.
            // C: an empty hint leaves msg_buf and the result as they were (stale); read here as the default hint.
            new MenuLine(() => { }), new MenuLine(() => { }), new MenuLine(() => { }), new MenuLine(() => { }));
    }

    // orig: R134+0x0FC2 matched
    /// <summary>Seen coming, they walk up (text 2); otherwise one in six recognises the party, or they attack.</summary>
    private void IgnoreThem(CardContext c, CardMenu menu)
    {
        menu[0] = OptionState.Shown;
        if (c.State.SavedCard != -1 && menu.Block != 2)
        {
            menu.Block = 2;
            c.Next = Number;
            return;
        }
        if (c.Random(6) == 0)
        {
            c.EventHere(LeftInPeace, 0xA8);
            c.Screen.Text(12);
            Leave(c);
            return;
        }
        c.Screen.Text(11);
        Fight(c);
    }

    // orig: R134+0x1104 matched
    /// <summary>A third of the leader's Speak Common plus three quarters of Charisma and of Intelligence.</summary>
    private static OptionHint TalkingChance(CardContext c)
    {
        var chance = c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 3
            + c.Attribute(Who.Leader, (int)Attribute.Charisma) * 3 / 4
            + c.Attribute(Who.Leader, (int)Attribute.Intelligence) * 3 / 4;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R134+0x10AC matched
    private void TalkThemRound(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.EventHere(LeftInPeace, 0xA8);
            c.PassHours(1);
            c.Screen.Text(3);
            Leave(c);
            return;
        }
        menu[2] = OptionState.Shown;
        c.Screen.Text(4);
        c.Next = Number;
    }

    // orig: R134+0x1180 matched
    // C: the second money_sub argument is printed as -0x115E: the near address 0xEEA2, i.e. var_money2 ($Money2).
    /// <summary>$Money1 when the guard saw them coming, else $Money2 (per head).</summary>
    private static void PayTheRent(CardContext c)
    {
        var rent = c.State.SavedCard != -1 ? c.Variables.Money[0] : c.Variables.Money[1];
        c.State.Party.Purse = c.State.Party.Purse.Subtract(rent);
        c.EventHere(LeftInPeace, 0xA8);
        c.Screen.Text(5);
        Leave(c);
    }

    // orig: R134+0x11F8 matched
    // C: after a cancelled prayer (−1) the handler returns before restoring var_chosen1, which keeps the praying member.
    /// <summary>The praying member is $ChosenOneName for the texts, then the guard again (unless the prayer was cancelled).</summary>
    private void CallOnASaint(CardContext c, CardMenu menu)
    {
        var guard = c.Variables.Chosen[0];
        c.Variables.Chosen[0] = c.MenuMember;
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            c.EventHere(LeftInPeace, 0xA8);
            c.Screen.Text(6);
            Leave(c);
        }
        else
        {
            c.Screen.Text(7);
            menu.Block = 2;
            c.Next = Number;
        }
        c.Variables.Chosen[0] = guard;
    }

    /// <summary>The huntsman's men, more and better the more the party is known (party lore level).</summary>
    // orig: R134+0x0EBC matched
    // DORMANT: the C takes a flag (battle 0x30 when set, else 0x2F) but both callers pass 0, so only 0x2F is used.
    // C: arguments right to left: the count's rand_mod(4) is drawn before the level's rand_mod(2).
    private static void Fight(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var groups = lore % 3 + 1;
        var kind = lore > 5 ? 1 : 0x12;
        var count = Math.Clamp(c.Random(4) + lore / 3 + 1, c.PresentCount(), 7);
        var level = c.Random(2) + lore / 4 + 1;
        switch (c.RunBattle(0x2F, -1, 0x0F, level, count, kind, groups, 1, -1, 0, 0))
        {
            case 0:
                c.EventHere(LeftInPeace, 0xA8);
                c.PassHours(1);
                c.Screen.Text(8);
                Leave(c);
                break;
            case 1:
            case 2:
                c.RejoinAway();
                c.PassHours(1);
                c.Screen.Text(9);
                Leave(c);
                break;
            case 3:
            case 4:
                c.LoseItems(Who.Everybody);
                c.PassHours(1);
                c.Screen.Text(10);
                Leave(c);
                break;
        }
    }

    // orig: (the shared tail of the handlers: cur_loc = -1; next_card = 0x0C)
    private static void Leave(CardContext c)
    {
        c.State.CurrentLocation = GameState.Wilderness;
        c.Next = 0x0C;
    }

    // DORMANT: R134+0x138A, _077c, _077e, _0780 and _091a are empty functions nothing calls.
    // C: the table set-up R134+0x12FA fills only saint_fn and clears only card_saints (not card_items).
}
