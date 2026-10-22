using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Night00": the night watch stops the party for breaking the curfew. Pay the fine, run,
/// throw a potion, pray, or draw steel (which may scare them off or end in a fight).
/// </summary>
// orig: R059+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R059+0x0B22 (opt_on[*]=1, screwed-up handler 0bc4, item handlers 0bfc/0c38/0c74/0cb0,
// saint handlers 0cec/0d76/0e00/0e8a: standard pop-ups)
public sealed class NightWatch : Card
{
    private const int StreetPicture = 44;
    private const int ChasePicture = 46;
    private const int SunlightPicture = 45;
    private const int FleeingWatchPicture = 8;
    private const int Curfew = 0x40;

    public override int Number => 0x3C;

    public override int Run(CardContext c)
    {
        Show(c, StreetPicture);
        // C: DS:EE70 = 0xf and DS:EE72 = 7 (text window state) are set here; not modelled.
        c.Next = Number;
        // C: opt_flag_0..4 = 1 and opt_flag_5..9 = 0 (the five dormant lines below are never offered).
        var menu = Menu(c, OpeningText(c));
        menu.Enable(0, 1, 2, 3, 4);
        c.EventHere(Curfew, 7);

        // C: money_set(var_money1, 0, 0, (rep / -50 + size + party_money[0] + 1) * present_count): signed division truncates
        // toward zero; the product is a 16-bit int in the original.
        var fine = Money.From(0, 0, (c.HereQuery(0) / -50 + c.HereQuery(2) + c.State.Party.Purse.Florins + 1) * c.PresentCount());
        c.Variables.Money[0] = fine;
        // C: money_cmp(party_money, var_money1) >= 0 enables line 0, else it is shown greyed.
        menu[0] = c.State.Party.Purse >= fine ? OptionState.Enabled : OptionState.Shown;

        c.SetCardItems(0x5F, 0x65, 0x61, 0x66);
        c.SetCardSaints(0x6F, 0x31, 0x57, 0x64);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        return menu.Run(
            // orig: R059+0x0510 (line 0); handler R059+0x0530 (success assured, 100%, leader)
            new MenuLine(_ => PayTheFine(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            // orig: R059+0x056A (line 1); handler R059+0x0642 (chance = (best Streetwise + weakest effective Agility) / 2, whole party)
            new MenuLine(chance => RunAway(c, menu, chance),
                () => new OptionHint((c.PartyMinEffectiveAgility() + c.PartyMaxSkill((int)Core.Characters.Skill.Streetwise)) / 2, "", OptionHint.WholeParty)),
            // orig: R059+0x066A (line 2); handler R059+0x0736 (success assured); pop-up handlers 0bfc..0cb0
            new MenuLine(chance => ThrowAPotion(c, menu, chance), OptionHint.Saying(SuccessAssured), () => c.MemberPopupHint()),
            // orig: R059+0x076E (line 3); handler R059+0x082C (select saint); pop-up handlers 0cec..0e8a
            new MenuLine(_ => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R059+0x0864 (line 4); handler R059+0x093A (enter combat)
            new MenuLine(_ => DrawWeapons(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // DORMANT: lines 5..9 (opt_flag_5..9 = 0): the card text lists no such lines; each shows the first text and leaves
            // for the city entry card. Line 5 passes an hour, lines 6..9 none. Handlers: assured (5), assured (6),
            // assured at 100% for the leader (7), assured (8, 9).
            // orig: handler R059+0x0990, action 0972 R059+0x0972
            new MenuLine(_ => ShowFirstTextAndLeave(c, 1), OptionHint.Saying(SuccessAssured)),
            // orig: handler R059+0x09E6, action 09c8 R059+0x09C8
            new MenuLine(_ => ShowFirstTextAndLeave(c, 0), OptionHint.Saying(SuccessAssured)),
            // orig: handler R059+0x0A3C, action 0a1e R059+0x0A1E
            new MenuLine(_ => ShowFirstTextAndLeave(c, 0), () => new OptionHint(100, SuccessAssured, c.Leader)),
            // orig: handler R059+0x0A94, action 0a76 R059+0x0A76
            new MenuLine(_ => ShowFirstTextAndLeave(c, 0), OptionHint.Saying(SuccessAssured)),
            // orig: handler R059+0x0AEA, action 0acc R059+0x0ACC
            new MenuLine(_ => ShowFirstTextAndLeave(c, 0), OptionHint.Saying(SuccessAssured)));
    }

    private static void ShowFirstTextAndLeave(CardContext c, int hours)
    {
        c.Screen.Text(0);
        c.PassHours(hours);
        c.Next = 4;
    }

    /// <summary>A first meeting, a second one tonight, or the watch catching up after a chase from cards 0x16/0x22.</summary>
    // orig: main function R059+0x0000, text selection before event_here(0x40, 7)
    private static int OpeningText(CardContext c)
    {
        if (c.PreviousCard is 0x16 or 0x22)
        {
            return 1;
        }
        return c.EventAtHere(Curfew) ? 2 : 0;
    }

    private static int StreetAfterEscape(CardContext c) => c.IsDaylight ? 9 : 10;

    // orig: R059+0x0510 (1838:0510)
    private static void PayTheFine(CardContext c)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.PassHours(1);
        c.Next = c.SavedCard;
    }

    // orig: R059+0x056A (1838:056A)
    // C: rand_mod(100) <= chance succeeds (Succeeds); the failure path calls party_min_eff_attr2() only for its side
    // effect on best_member, and Chosen[0] is (unsigned char)best_member.
    private void RunAway(CardContext c, CardMenu menu, int chance)
    {
        var streetwise = (int)Core.Characters.Skill.Streetwise;
        if (c.Succeeds(chance))
        {
            c.AdjustReputation(c.Here, -1, -2);
            c.PracticeSkill(Who.Everybody, streetwise, 7, 5, -1);
            c.PassHours(1);
            ShowPicture(c, ChasePicture);
            c.Screen.Text(3);
            c.Next = StreetAfterEscape(c);
            return;
        }
        c.PracticeSkill(Who.Everybody, streetwise, 0, 5, -1);
        ShowPicture(c, ChasePicture);
        menu.Block = 4;
        c.PartyMinEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        menu[1] = OptionState.Shown;
        c.Next = Number;
    }

    // orig: R059+0x066A (1838:066A): the potion is taken from the chooser before the roll
    private void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Succeeds(chance))
        {
            c.AdjustReputation(c.Here, 1, 3);
            ShowPicture(c, ChasePicture);
            c.Screen.Text(5);
            c.PassHours(1);
            c.Next = StreetAfterEscape(c);
            return;
        }
        ShowPicture(c, ChasePicture);
        menu.Block = 12;
        c.PartyMinEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        menu[2] = OptionState.Shown;
        c.Next = Number;
    }

    // orig: R059+0x076E (1838:076E); -1 from the saint pop-up (cancelled) leaves everything as it was
    private void Pray(CardContext c, CardMenu menu)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            c.PassHours(1);
            c.AdjustReputation(c.Here, 5, 10);
            ShowPicture(c, SunlightPicture);
            c.Screen.Text(6);
            c.Next = c.SavedCard;
            return;
        }
        ShowPicture(c, StreetPicture);
        menu.Block = 13;
        c.Variables.Chosen[0] = c.MenuMember;
        menu[3] = OptionState.Shown;
        c.Next = Number;
    }

    /// <summary>A famous, forceful party may scare the watch away; otherwise it comes to a fight.</summary>
    // orig: R059+0x0864 (1838:0864)
    private static void DrawWeapons(CardContext c)
    {
        // C: cur_loc_query(0) / 10 then abs; member_best_skill(-1) is the leader's best weapon skill; below 75 the sum is 0.
        var awe = Math.Abs(c.HereQuery(0) / 10)
            + c.PartyAverageAttribute((int)Core.Characters.Attribute.Charisma) / 10
            + (c.BestWeaponSkill(Who.Leader) / 2 + c.PartyGet(4) / 20);
        if (awe < 75)
        {
            awe = 0;
        }
        awe = Math.Clamp(awe, 0, 90);
        if (c.Succeeds(awe))
        {
            ShowPicture(c, FleeingWatchPicture);
            c.Screen.Text(7);
            c.Next = c.SavedCard;
        }
        else
        {
            FightTheWatch(c);
        }
        c.PassHours(1);
    }

    // orig: R059+0x02F2 (1838:02F2)
    private static void FightTheWatch(CardContext c)
    {
        var battlefield = c.PreviousCard switch
        {
            6 or 8 => 0x13,
            9 or 10 => 0x11,
            0x21 or 0x22 => 0x15,
            _ => 0x56,
        };
        // C: the only draw among run_battle's arguments is rand_mod(5); prev_screen + cur_loc is the battle seed.
        var watchmen = c.Random(5) + 3;
        var result = c.RunBattle(battlefield, c.PreviousCard + c.Here, 3, 1, watchmen, 0, 2, 1, -1, 0, 0);
        c.AdjustReputation(c.Here, -15, -25);
        // C: 0x7d0 / loc_query(cur_loc, 2) is computed after the battle for every outcome. The city size is 0 outside
        // cities and the original then raises a divide error.
        // DIVERGENCE: a size of 0 is treated as 1 (2000 hours) instead of crashing.
        var expensiveHours = 2000 / Math.Max(c.LocationQuery(c.Here, 2), 1);
        switch (result)
        {
            case 0:
                c.Variables.Chosen[0] = c.Leader;
                ShowPicture(c, 3);
                c.Screen.Text(8);
                PricesRiseAfterTheFight(c, expensiveHours);
                c.PassHours(1);
                c.Next = c.SavedCard;
                break;
            case 1:
                c.ReportAway();
                ShowPicture(c, 59);
                c.Screen.Text(9);
                PricesRiseAfterTheFight(c, expensiveHours);
                c.PassHours(1);
                c.Next = c.SavedCard;
                break;
            case 2:
                c.ReportAway();
                ShowPicture(c, 47);
                c.Screen.Text(10);
                c.PassHours(1);
                c.Next = StreetAfterEscape(c);
                break;
            case 3:
            case 4:
                ShowPicture(c, 41);
                c.Screen.Text(11);
                c.PassHours(3);
                c.Next = 0x0D;
                break;
        }
        // C: after the switch, whatever the outcome: event_here(0x11, rep <= -75 ? 0xF0 : 0x78).
        c.EventHere(Core.Events.EventKinds.Hostile, c.HereQuery(0) <= -75 ? 240 : 120);
    }

    // orig: event_new(-2, cur_loc, 0x1e, 0x12, 1, 0x63, 0, hours, 0, 0) in R059+0x02F2 cases 0 and 1
    private static void PricesRiseAfterTheFight(CardContext c, int hours) =>
        c.Events.Create(-2, c.Here, 0x1E, Core.Events.EventKinds.Expensive, 1, 0x63, 0, hours, 0, 0);
}
