using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetG00": gargoyles dive at the party on a barren stretch of land. Fight them, outrun
/// them (on horseback if everybody rides), or call on a saint.
/// </summary>
// orig: R059+0x0F14 (1929:0004) matched; tests: CardSmokeTests
// orig: handler setup R059+0x14A4 (opt_on[*]=0, screwed-up handler 0636 for all 20 lines, item handlers
// 066e/06aa/06e6/0722, saint handlers 075e/07e8/0872/08fc: standard pop-ups)
public sealed class GargoylesAttack : Card
{
    private const int OverlandMap = 0x0C;

    public override int Number => 0x3D;

    public override int Run(CardContext c)
    {
        Show(c, 241);
        // C: DS:EE70 = 0 and DS:EE72 = 7 (text window state) are set here; not modelled.
        c.Next = Number;
        // C: opt_flag_0..2 = 1; opt_flag_3..9 are never set by this card and are copied to opt_on[3..9] as they stood
        // (stale globals of the previous card); the lines past the text's third are not offered here.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.SetCardSaints(9, 0x17, 0x1D, 0x6C);
        menu[2] = c.SaintsUsable();

        return menu.Run(
            // orig: handler R059+0x1256 (enter combat); action 0340 R059+0x1250 (calls the fight, 0172)
            new MenuLine(_ => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R059+0x128E (line 1); handler R059+0x1384
            new MenuLine(chance => RunForIt(c, chance), () => EscapeChance(c)),
            // orig: R059+0x13CA (line 2); handler R059+0x146C (select saint); pop-up handlers 075e..08fc
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // DORMANT: lines 3..9 have the "screwed up." handler from the setup function (0636) and no case in the action
            // switch; opt_on[3..9] come from opt_flag_3..9, which this card never sets (stale globals).
            // orig: R059+0x1546
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)));
    }

    /// <summary>The slowest rider when everybody is mounted, else the least agile member.</summary>
    // orig: R059+0x1384 (1929:0474)
    private static OptionHint EscapeChance(CardContext c)
    {
        // C: party_min_skill / party_min_attr run first (they set best_member), then var_chosen1 = (unsigned char)best_member.
        var value = c.PartyGet(5) != 0 ? c.PartyMinSkill((int)Skill.Riding) : c.PartyMinAttribute((int)Attribute.Agility);
        c.Variables.Chosen[0] = c.BestMember;
        return new OptionHint(Math.Clamp(value, 1, 99), "", c.BestMember);
    }

    // orig: R059+0x128E (1929:037E); a roll of at most the chance succeeds
    private static void RunForIt(CardContext c, int chance)
    {
        if (c.Succeeds(chance))
        {
            if (c.PartyGet(5) != 0)
            {
                c.PassHours(1);
                ShowPicture(c, 240);
                c.Screen.Text(1);
                c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 7, 10, 0);
            }
            else
            {
                c.PassHours(3);
                ShowPicture(c, 242);
                c.Screen.Text(2);
                c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 0, 10, 0);
            }
            BackToTheMap(c);
            return;
        }
        ShowPicture(c, 239);
        c.Screen.Text(3);
        Fight(c);
    }

    // orig: R059+0x13CA (1929:04BA); -1 from the saint pop-up (cancelled) changes nothing
    private static void Pray(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, 234);
            c.Screen.Text(4);
            BackToTheMap(c);
            return;
        }
        ShowPicture(c, 240);
        c.Screen.Text(5);
        Fight(c);
    }

    // orig: R059+0x1082 (1929:0172)
    private static void Fight(CardContext c)
    {
        var gargoyles = c.Random(3) + c.PartyLoreLevel() / 6 + 2;
        switch (c.RunBattle(0x33, -1, 0x2F, 1, gargoyles, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, 0);
                ShowPicture(c, 242);
                c.Screen.Text(6);
                break;
            case 1:
            case 2:
                c.DropAway();
                break;
            case 3:
                CarriedOffToHell(c);
                break;
            case 4:
                SurrenderToThem(c);
                break;
        }
        BackToTheMap(c);
    }

    /// <summary>Defeated: the gargoyles take all the gear and carry one member off for ever.</summary>
    // orig: R059+0x1082 case 3
    private static void CarriedOffToHell(CardContext c)
    {
        // C: var_chosen1 = rand_mod(5), then it steps on (mod 5) to the next used slot; with no member left it would loop for ever.
        var victim = c.Random(5);
        while (c.Status(victim) == CharacterStatus.Empty)
        {
            victim = (victim + 1) % 5;
        }
        c.Variables.Chosen[0] = victim;
        ShowPicture(c, 242);
        c.Screen.Text(7);
        c.LoseItems(Who.Everybody);
        // C: thunk 09C0:18B5(victim), party[victim].used = 0, root 0000:1588(-2), root 0000:1716(-2) are what DeleteMember reconstructs.
        c.DeleteMember(victim);
        c.PassHours(0);
    }

    /// <summary>Surrendering to hell's creatures costs every member some Virtue, and the gear.</summary>
    // orig: R059+0x1082 case 4
    private static void SurrenderToThem(CardContext c)
    {
        ShowPicture(c, 240);
        c.Screen.Text(8);
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                // C: party[i]+0x60 (Virtue) -= rand_mod(6), then raised to 0 if it is <= 0.
                var skills = c.State.Party.Members[i].Skills;
                skills[(int)Skill.Virtue] = Math.Max(0, skills[(int)Skill.Virtue] - c.Random(6));
            }
        }
        c.LoseItems(Who.Everybody);
    }

    // C: every path of the fight, and the escapes, end with cur_loc = -1 and next_card = 0xC (the overland map).
    private static void BackToTheMap(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = OverlandMap;
    }
}
