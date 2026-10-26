using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetH01": armed Hussite rebels from (or fleeing) the nearest city demand to know whether
/// the party is friend or foe. The party can talk, pray to a Bohemian saint, attack, run or
/// throw a potion and run. The nearest city is marked as touched by the rebellion.
/// </summary>
// orig: R114+0x09B4 (18D3:0004) matched; tests: CardSmokeTests
// orig: R114+0x1260 (18D3:08B0) matched (registration: saint pop-ups only, they do not name the member)
// C: the registration does not set item_fn (DS:EC00), so a potion chosen in line 4's pop-up box calls (DS:EB0A[menu_slot] = item_fn[slot],
//   R043's card loop) the far pointer an earlier card registered, into whatever code is loaded there now, and line 4 gets its AX as the chance.
//   Direct calls j_hussite.py (local/batch/evid1o), card_menu stubbed to that choice: from the Tyrnau map state the gate card's pointers
//   213D:0D7E (slot 0) run R114 code that returns 0x223C (the potion always works), 213D:0DE8 (slot 1) runs into an invalid instruction,
//   213D:0E50 (slot 2) returns 0, slot 3 and every slot after a load or a card without item handlers (banks-markt: all 0000:0000) jump to 0000:0000.
// DIVERGENCE: that call is not reproduced (an earlier card's code pointer into another overlay's bytes: undefined); the pop-up's choice
//   takes the menu's pop-up hint ("Success is assured.", the member named) and its chance -1: the potion always fizzles.
// DORMANT: text 12 (an unknown saint angers them) is never shown; lines 5..9 have empty handlers (R114+0x124C..08ae).
public sealed class HussiteBand : Card
{
    private const int HussitesPicture = 140;

    public override int Number => 281;

    public override int Run(CardContext c)
    {
        Show(c, HussitesPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        c.Variables.Chosen[0] = c.Leader;
        var city = c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);
        c.Variables.Location = city;
        // C: locations[var_loc] + 0x14 is the place's flag byte; 0x80 marks a city in revolt.
        var place = c.State.Locations[city];
        if ((place.Flags & 0x80) != 0)
        {
            place.Flags |= 1;
            menu.Block = 0;
        }
        else
        {
            menu.Block = 1;
            place.Flags |= 0x81;
            if (!c.Events.Started(0x5E))
            {
                c.EventHere(0x5E, 0x1068);
            }
        }
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0x4E, 0x81, 0x36, 0x3D);
        menu[4] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();
        menu.Popup = () => OptionHint.Say(SuccessAssured, c.MenuMember);

        return menu.Run(
            // orig: R114+0x0DCC (line 0); hint R114+0x0E40 (18D3:0490) matched
            new MenuLine(p => ClaimFriendship(c, p), () => OptionHint.ChanceOf(
                c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.LocationQuery(c.Variables.Location, 0) + c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2, c.Leader)),
            // orig: R114+0x0E8A (line 1); hint R114+0x0F78 (18D3:05C8) matched
            new MenuLine(_ => PrayToABohemianSaint(c), () => OptionHint.Say("Select a person and a saint."), () => c.SaintPopupChanceHint()),
            // orig: R114+0x0FB0 (18D3:0600) matched; hint R114+0x0FB6
            new MenuLine(_ => FightTheHussites(c), () => OptionHint.Say("Enter Combat")),
            // orig: R114+0x0FEE (line 3); hint R114+0x10C0 (18D3:0710) matched
            new MenuLine(p => RunForIt(c, p), () => OptionHint.ChanceOf(c.PartyGet(5) != 0 ? 100 : c.PartyAverageEffectiveAgility(), c.Leader)),
            // orig: R114+0x10F0 (line 4); hint R114+0x1214 (18D3:0864) matched
            new MenuLine(p => ThrowAPotionAndRun(c, p), () => OptionHint.Say("Select a person and a potion.")),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    /// <summary>Talking the rebels round; the original only succeeds when the roll equals the chance exactly.</summary>
    // orig: R114+0x0DCC (18D3:041C) matched; tests: CardSmokeTests
    // BUG: `rand_mod(100) == p` where every other card has `<=`; kept as the original has it.
    private static void ClaimFriendship(CardContext c, int chance)
    {
        if (c.Random(100) == chance)
        {
            c.Screen.Text(2);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 1, 10, 0);
            c.AdjustReputation(c.Variables.Location, 5, 10);
            c.LeaveForTheMap();
            return;
        }
        c.Screen.Text(3);
        c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 10, 0);
        FightTheHussites(c);
    }

    /// <summary>
    /// The first two saints impress them either way; the other two, answered, let the party pass,
    /// but unanswered the rebels attack (after the text of an answered prayer, as in the original).
    /// </summary>
    // orig: R114+0x0E8A (18D3:04DA) matched; tests: CardSmokeTests
    // C: invoke_saint(var_chosen1 = menu_member, ...): the member is named before the prayer.
    private static void PrayToABohemianSaint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
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
                    c.Screen.Text(4);
                    c.LeaveForTheMap();
                    break;
                case 2:
                case 3:
                    ShowPicture(c, 287);
                    c.Screen.Text(11);
                    c.LeaveForTheMap();
                    break;
            }
            c.AdjustReputation(c.Variables.Location, 5, 10);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
            case 1:
                c.Screen.Text(4);
                // The original leaves without clearing cur_loc here.
                c.Next = CardContext.MapCard;
                c.AdjustReputation(c.Variables.Location, 5, 10);
                break;
            case 2:
            case 3:
                ShowPicture(c, 287);
                c.Screen.Text(11);
                FightTheHussites(c);
                break;
        }
    }

    // orig: R114+0x0FEE (18D3:063E) matched; tests: CardSmokeTests
    private static void RunForIt(CardContext c, int chance)
    {
        if (chance == 100)
        {
            ShowPicture(c, 286);
            c.Screen.Text(5);
            c.PassHours(3);
            c.LeaveForTheMap();
        }
        else if (c.Random(100) <= chance)
        {
            ShowPicture(c, 145);
            c.Screen.Text(6);
            c.PassHours(3);
            c.LeaveForTheMap();
        }
        else
        {
            ShowPicture(c, HussitesPicture);
            c.Screen.Text(7);
            FightTheHussites(c);
        }
    }

    /// <summary>
    /// The potion (not taken from the member: the original never takes it) lets the party slip
    /// away, or fizzles; a mounted party then rides off, but the original goes on to the foot
    /// race all the same.
    /// </summary>
    // orig: R114+0x10F0 (18D3:0740) matched; tests: CardSmokeTests
    // C: rand_mod(100) <= party_avg_eff_attr2(): the average draws nothing.
    private static void ThrowAPotionAndRun(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 189);
            c.Screen.Text(8);
            c.LeaveForTheMap();
            return;
        }
        c.Screen.Text(13);
        if (c.PartyGet(5) != 0)
        {
            ShowPicture(c, 189);
            c.Screen.Text(5);
            c.PassHours(3);
            c.LeaveForTheMap();
        }
        if (c.Random(100) <= c.PartyAverageEffectiveAgility())
        {
            ShowPicture(c, 145);
            c.Screen.Text(6);
            c.PassHours(3);
            c.LeaveForTheMap();
        }
        else
        {
            c.Screen.Text(7);
            FightTheHussites(c);
        }
    }

    /// <summary>The rebels: whatever happens, the party's name in the nearest city suffers.</summary>
    // orig: R114+0x0C70 (18D3:02C0) matched; tests: CardSmokeTests
    // C: arguments right to left: rand_mod(4) (fifth) is drawn before rand_mod(2) (fourth); then run_battle draws its seed.
    private static void FightTheHussites(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var count = c.Random(4) + lore / 3 + 1;
        var groups = c.Random(2) + lore / 4 + 1;
        switch (c.RunBattle(0x2F, -1, 5, groups, count, 5, lore % 3 + 1, 1, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 286);
                c.Screen.Text(9);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                c.OneOfTheAwayLeaves();
                ShowPicture(c, 145);
                c.Screen.Text(6);
                c.PassHours(c.HoursUntil(5));
                c.LeaveForTheMap();
                break;
            case 3:
            case 4:
                c.RejoinAway();
                ShowPicture(c, 177);
                c.Screen.Text(10);
                c.PassHours(c.HoursUntil(5));
                c.LeaveForTheMap();
                break;
        }
        c.AdjustReputation(c.Variables.Location, -5, -10);
    }
}
