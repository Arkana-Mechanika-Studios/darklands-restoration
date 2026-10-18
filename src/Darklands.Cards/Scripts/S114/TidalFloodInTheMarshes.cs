using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetS01": a storm tide floods the coastal marshes. The party can look for high ground,
/// build a raft for its gear (needs a rope), or pray. Failing, it must abandon heavy gear. The
/// lines for praying to other saints (cards 284 and 285) are never offered.
/// </summary>
// orig: R114+0x207A (1A3F:000A) matched; tests: CardSmokeTests
// orig: R114+0x258C (1A3F:051C) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: R114+0x2626, R114+0x2628, R114+0x262A, R114+0x262C are never called.
public sealed class TidalFloodInTheMarshes : Card
{
    public override int Number => 283;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.PartyMaxSkill((int)Skill.Woodwise);
        c.Variables.Chosen[0] = c.BestMember;
        if (c.CountItems(Who.Everybody, 0x3B) == 0)
        {
            menu[1] = OptionState.Shown;
        }
        c.SetCardSaints(0x2C, 0x32, 0x33, 0x67);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R114+0x2350 (1A3F:02E0) matched (hint)
            new MenuLine(p => SeekHighGround(c, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageEffectiveAgility() + c.PartyMaxAttribute((int)Attribute.Perception), 1, 99), OptionHint.WholeParty)),
            // orig: R114+0x2430 (1A3F:03C0) matched (hint)
            new MenuLine(p => BuildARaft(c, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyMaxSkill((int)Skill.Woodwise) * 2 + c.PartyMaxSkill((int)Skill.Artifice) / 3, 1, 99), OptionHint.WholeParty)),
            // orig: R114+0x24C0 (1A3F:0450) matched (hint)
            new MenuLine(_ => Pray(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R114+0x24F8 (1A3F:0488) matched; hint R114+0x2500
            // DORMANT: lines 3 and 4 (other saints: cards 284 and 285) are never offered (opt_flag_3/4 = 0).
            new MenuLine(_ => c.Next = 284, () => OptionHint.Say(NoRisk)),
            // orig: R114+0x2538 (1A3F:04C8) matched; hint R114+0x2540
            new MenuLine(_ => c.Next = 285, () => OptionHint.Say(NoRisk)),
            // DORMANT: lines 5..9 are never offered; actions R114+0x2578.. and hints R114+0x257A.. are empty
            // (UNCERTAIN: the hints return AX/DX; neutral hints are used).
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    // orig: R114+0x22E2 (1A3F:0272) matched; tests: CardSmokeTests
    // C: everybody keeps each possession with the woodsman's ($ChosenOneName's) Agility + 10 percent.
    private static void SeekHighGround(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(2, TextFrame.BlankPage);
        }
        else
        {
            c.KeepItems(Who.Everybody, Math.Clamp(c.Attribute(c.Variables.Chosen[0], (int)Attribute.Agility) + 10, 1, 95));
            c.Screen.Text(1, TextFrame.BlankPage);
        }
        c.PassHours(c.Random(3) + 23);
        c.LeaveForTheMap();
    }

    // orig: R114+0x2380 (1A3F:0310) matched; tests: CardSmokeTests
    private static void BuildARaft(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4, TextFrame.BlankPage);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 1, 10, 0);
            c.PracticeSkill(Who.Everybody, (int)Skill.Artifice, 1, 5, -1);
        }
        else
        {
            c.KeepItems(Who.Everybody, Math.Clamp(c.Attribute(c.Variables.Chosen[0], (int)Attribute.Agility) + 20, 1, 95));
            c.Screen.Text(3, TextFrame.BlankPage);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
            c.PracticeSkill(Who.Everybody, (int)Skill.Artifice, 0, 5, -1);
        }
        c.PassHours(c.Random(3) + 23);
        c.LeaveForTheMap();
    }

    // orig: R114+0x246E (1A3F:03FE) matched; tests: CardSmokeTests
    private static void Pray(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(6, TextFrame.BlankPage);
            c.LeaveForTheMap();
            return;
        }
        menu.Block = 5;
        c.Next = 283;
    }
}
