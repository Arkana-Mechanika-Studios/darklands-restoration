using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol09": siding with the rebels, the party joins their leader as he addresses the crowd.
/// When the city's side is far the stronger, the city guard charges at once (blocks 2 and 3);
/// otherwise the party may try to inspire the crowd or wait for it to rally (blocks 0 and 1).
/// Blocks 1 and 3 are for cities where the rebels have hired mercenaries (location value 0x29 = 2).
/// </summary>
// orig: R090+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RebelsRallyTheCrowd : Card
{
    private const int Picture = 0;
    private const int MarchOnTheTown = 0xB9;

    public override int Number => 0xB8;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var withMercenaries = c.HereQuery(0x29) == 2;
        var menu = Menu(c);
        menu.Enable(0, 1, 3, 6);
        // the card also sets its global DS:6223 (1 here, 0 below), which no other code reads.
        if (c.SidePercent(1) >= 150)
        {
            menu.Block = withMercenaries ? 3 : 2;
            menu[0] = OptionState.Hidden;
        }
        else
        {
            menu.Block = withMercenaries ? 1 : 0;
            menu[3] = OptionState.Hidden;
        }
        // orig: R090+0x07C0 (1838:07C0) matched (handlers and card-saint-lines only)
        c.SetCardSaints(0x0C, 0x7E, 0x36, 0x3D);
        // C: the prayer flag is set after the branch above, so it overrides its 0 (lines 2 and 3).
        menu[2] = c.SaintsUsable();

        return menu.Run(
            // orig: R090+0x036C (1838:036C); hint R090+0x046C matched
            new MenuLine(chance => Speak(c, chance, menu),
                () => OptionHint.ChanceOf(InspireChance(c), c.Variables.Chosen[0])),
            // orig: R090+0x04E8 (1838:04E8); hint R090+0x056A matched
            new MenuLine(chance => WaitForTheCrowd(c, chance), () => OptionHint.ChanceOf(RallyChance(c), Who.Everybody)),
            // orig: R090+0x05A8 (1838:05A8); hint R090+0x0666; saint hints R090+0x085A..09f8 matched
            new MenuLine(() => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupMemberHint),
            // orig: R090+0x069E (1838:069E); hint R090+0x06A4 matched
            new MenuLine(() => FightTheGuard(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R090+0x06DC; R090+0x0720 matched
            // DORMANT: lines 4 and 5 are hidden (flags 0); they would only let no time pass.
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            // orig: R090+0x0764 (1838:0764); hint R090+0x0788 matched
            new MenuLine(() => LeaveTheRebels(c)));
    }

    /// <summary>The most charming member speaks (the choice of speaker is part of the chance).</summary>
    // C: the hint sets $ChosenOneName as a side effect; its member field is never set (garbage), shown here as the speaker.
    private static int InspireChance(CardContext c)
    {
        c.PartyMaxAttribute((int)Attribute.Charisma);
        var speaker = c.Variables.Chosen[0] = (byte)c.BestMember;
        var chance = (c.Attribute(speaker, (int)Attribute.Charisma) + c.ReputationHere + c.SidePercent(-1)
            + c.Skill(speaker, (int)Skill.SpeakCommon)) / 5;
        if (c.HereQuery(0x29) != 2)
        {
            chance += 15;
        }
        return Math.Clamp(chance, 0, 99);
    }

    private static int RallyChance(CardContext c)
    {
        var chance = c.SidePercent(-1) / 2 - 40;
        if (c.HereQuery(0x29) != 2)
        {
            chance += 15;
        }
        return Math.Clamp(chance, 0, 99);
    }

    private static void Speak(CardContext c, int chance, CardMenu menu)
    {
        var speaker = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 10, 10);
            c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 1, 10, -1);
            c.PassHours(1);
            c.Screen.Text(4);
            c.Next = MarchOnTheTown;
            return;
        }
        c.AdjustReputation(c.Here, -5, -5);
        menu[0] = OptionState.Shown;
        c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 0, 10, -1);
        c.PassHours(1);
        c.Screen.Text(5, TextFrame.BlankPage);
        if (c.HereQuery(0x29) != 2)
        {
            c.Screen.Text(6);
            c.Next = MarchOnTheTown;
            return;
        }
        var roll = c.Random(100);
        if (c.SidePercent(-1) / 2 - 20 >= roll)
        {
            c.Screen.Text(7, TextFrame.BlankPage);
            c.Next = MarchOnTheTown;
            return;
        }
        CrowdDriftsAway(c);
    }

    private static void WaitForTheCrowd(CardContext c, int chance)
    {
        // (The original also lowers its own copy of the reputation here, which nothing reads.)
        if (c.Random(100) <= chance)
        {
            if (c.HereQuery(0x29) != 2)
            {
                c.Screen.Text(6);
            }
            else
            {
                c.Screen.Text(7, TextFrame.BlankPage);
            }
            c.PassHours(1);
            c.Next = MarchOnTheTown;
            return;
        }
        c.ConflictOutcome(-1, -1, 1);
        c.Screen.Text(8);
        c.PassHours(1);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    private static void CrowdDriftsAway(CardContext c)
    {
        c.ConflictOutcome(-1, -1, 1);
        c.Screen.Text(8);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>Saints 0x0C and 0x7E move the crowd to go home in peace; 0x36 and 0x3D turn the guard away.</summary>
    private static void Pray(CardContext c)
    {
        // C: the saint pop-up's hint, called on the choice, names the one who prays as $ChosenOneName.
        if (c.MenuPopup)
        {
            c.Variables.Chosen[0] = c.MenuMember;
        }
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 1)
        {
            c.Screen.Text(10);
            FightTheGuard(c);
            return;
        }
        c.AdjustReputation(c.Here, 5, 5);
        if (c.MenuSlot is 0 or 1)
        {
            c.Screen.Text(9);
            c.PassHours(1);
            c.Next = c.IsDaylight ? 9 : 10;
        }
        // C: slots beyond 3 do nothing and the party stays (the switch has no default).
        else if (c.MenuSlot is 2 or 3)
        {
            c.Screen.Text(17);
            c.PassHours(1);
            c.Next = MarchOnTheTown;
        }
    }

    // orig: R090+0x0222 (1838:0222) matched
    private static void FightTheGuard(CardContext c)
    {
        // Arguments are evaluated right to left: the second group's number is drawn first.
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        var result = c.BattleResult = c.RunBattle(0x25, c.Here + 0x70, 3, 2, first, 0, 2, second, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.Screen.Text(11);
                c.PassHours(1);
                c.Next = MarchOnTheTown;
                break;
            case 1 or 2:
                c.RejoinAway();
                c.AdjustReputation(c.Here, -3, -5);
                c.ConflictOutcome(-1, -1, 1);
                c.Screen.Text(c.Random(2) != 0 ? 12 : 13);
                c.PassHours(1);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 3:
                c.ConflictOutcome(-1, -1, 1);
                c.Screen.Text(15);
                c.PassHours(3);
                c.LoseItems(Who.Everybody);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 4:
                c.AdjustReputation(c.Here, -3, -4);
                c.ConflictOutcome(-1, -1, 1);
                c.Screen.Text(14);
                c.PassHours(2);
                c.LoseItems(Who.Everybody);
                c.Next = 0x0D;
                break;
        }
    }

    private static void LeaveTheRebels(CardContext c)
    {
        c.AdjustReputation(c.Here, -5, -5);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
