using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol13": the rebel march meets squads of city guards on the way, then again at the
/// fortress (0xBA's way) or at the city square (0xBB's way). The party may parley, bribe the
/// lieutenant, pray or fight. Where the city's side is already twice as strong, the march goes
/// straight on.
/// </summary>
// orig: R090+0x0A82 (18E0:0002) matched; tests: CardSmokeTests
public sealed class RebelMarch : Card
{
    private const int Picture = 0;
    private const int ToTheFortress = 0xBA;
    private const int ToTheCouncilHall = 0xBB;

    public override int Number => 0xB9;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 2, 3);
        c.Variables.Money[0] = Money.From(c.SidePercent(1) / 20, 0, 0);
        c.PartyMaxAttribute((int)Attribute.Charisma);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        // C: DS:9C30[0..9] are the conditional passages of the texts (memset to 0, then [0] for no mercenaries).
        Array.Clear(c.Variables.Conditions);
        if (c.HereQuery(0x29) != 2)
        {
            c.Variables.Conditions[0] = true;
        }
        if (c.SidePercent(-1) >= 200 && c.HereQuery(0x18) != 0)
        {
            return c.Next = ToTheCouncilHall;
        }
        if (c.SidePercent(-1) >= 200)
        {
            return c.Next = ToTheFortress;
        }
        // C: the block is the script's global DS:8E20.
        menu.Block = c.SidePercent(-1) >= 50 ? 0 : c.HereQuery(0x18) != 0 ? 2 : 1;
        // orig: R090+0x1366 (18E0:08E6) matched (handlers and card-saint-lines only)
        c.SetCardSaints(0x0C, 0x3D, 0x36);
        // orig: the card's entry R090+0x0A82 (18E0:0002) matched, just after the call above
        // BUG: the bribe line's flag is set from saints-available() (meant for line 2, the prayer, which
        // stays enabled); it also overrides the 0 set just above for the far squads.
        menu[1] = c.SaintsUsable();

        // orig: R090+0x0EE0 (18E0:0460) matched
        void Onward()
        {
            if (c.SidePercent(-1) >= 50 && menu.Block == 0)
            {
                // Past the first squads: the fortress or the square is next.
                menu.Block = c.HereQuery(0x18) != 0 ? 2 : 1;
                c.Next = Number;
                menu[1] = OptionState.Hidden;
                menu[0] = OptionState.Enabled;
                return;
            }
            c.Next = c.HereQuery(0x18) != 0 ? ToTheCouncilHall : ToTheFortress;
        }

        var speaker = c.Variables.Chosen[0];
        menu.EachRound = () => speaker = c.Variables.Chosen[0];
        return menu.Run(
            // orig: R090+0x0F2E (18E0:04AE); hint R090+0x0FBE matched
            new MenuLine(chance => { if (Parley(c, menu, chance)) { Onward(); } },
                () => OptionHint.ChanceOf(ParleyChance(c, speaker), speaker)),
            // orig: R090+0x1032 (18E0:05B2); hint R090+0x105C matched
            new MenuLine(() => { BribeTheLieutenant(c); Onward(); }, () => OptionHint.Say(NoRisk, c.Leader)),
            // orig: R090+0x1096 (18E0:0616); hint R090+0x1106; saint hints R090+0x13F6..0b14 matched
            new MenuLine(() => { if (Pray(c)) { Onward(); } }, OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupMemberHint),
            // orig: R090+0x113E (18E0:06BE); hint R090+0x1144 matched
            new MenuLine(() => { if (FightTheGuard(c)) { Onward(); } }, OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R090+0x117C (18E0:06FC); hint R090+0x11FC matched
            // DORMANT: line 4 is hidden (flag 0) and nothing enables it.
            new MenuLine(chance => SlinkAway(c, chance),
                () => OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Stealth), Who.Everybody)),
            // orig: R090+0x1212; R090+0x1256; R090+0x129A; R090+0x12DE; R090+0x1322 matched
            // DORMANT: lines 5..9 are hidden (flags 0); they would only let no time pass.
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)));
    }

    private static int ParleyChance(CardContext c, int speaker)
    {
        var chance = (c.Attribute(speaker, (int)Attribute.Charisma) + c.ReputationHere + c.Skill(speaker, (int)Skill.SpeakCommon)) / 4;
        if (c.HereQuery(0x29) != 2)
        {
            chance -= 15;
        }
        return Math.Clamp(chance, 0, 99);
    }

    /// <summary>Talking the lieutenant out of the fight; true when he agrees.</summary>
    private static bool Parley(CardContext c, CardMenu menu, int chance)
    {
        var speaker = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 5, 7);
            c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 1, 10, -1);
            c.PassHours(1);
            c.Screen.Text(3);
            return true;
        }
        menu[0] = OptionState.Shown;
        c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 0, 10, -1);
        c.PassHours(1);
        c.Screen.Text(c.HereQuery(0x29) != 2 ? 4 : 5);
        return false;
    }

    // C: the bribe is paid without a check of the purse (the line's flag comes from the saints, see above).
    private static void BribeTheLieutenant(CardContext c)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.Screen.Text(13);
        c.PassHours(1);
    }

    /// <summary>Saints 0x0C, 0x3D and 0x36; true when the prayer is answered.</summary>
    private static bool Pray(CardContext c)
    {
        // C: the saint pop-up's hint, called on the choice, names the one who prays as $ChosenOneName.
        if (c.MenuPopup)
        {
            c.Variables.Chosen[0] = c.MenuMember;
        }
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return false;
        }
        c.Variables.Chosen[1] = c.MenuMember;
        if (answer == 1)
        {
            c.AdjustReputation(c.Here, 5, 7);
            c.Screen.Text(6);
            c.PassHours(1);
            return true;
        }
        c.Screen.Text(7);
        c.PassHours(1);
        return false;
    }

    /// <summary>The fight against the guards, as strong as the party's renown; true when it is won.</summary>
    // orig: R090+0x0D62 (18E0:02E2) matched
    private static bool FightTheGuard(CardContext c)
    {
        // C: MSC evaluates the arguments right to left: the leaders' number, then the guards' (its rnd(4)
        // first), then the squads' (rnd(3)); the lore level draws nothing.
        var leaders = c.PartyLoreLevel() / 4 + 1;
        var guards = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var squads = c.Random(3) + c.PartyLoreLevel() / 4 + 3;
        var result = c.BattleResult = c.RunBattle(0x25, c.Here + 0x25, 3, squads, guards, 0x16, leaders, 1, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.Screen.Text(c.HereQuery(0x29) != 2 ? 8 : 9);
                c.PassHours(1);
                return true;
            case 1 or 2:
                c.ReportAway();
                c.AdjustReputation(c.Here, -5, -5);
                c.ConflictOutcome(-1, -1, 1);
                c.Screen.Text(10);
                c.PassHours(1);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 3:
                c.ConflictOutcome(-1, -1, 1);
                c.Screen.Text(12);
                c.PassHours(3);
                c.LoseItems(Who.Everybody);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 4:
                c.AdjustReputation(c.Here, -5, -5);
                c.ConflictOutcome(-1, -1, 1);
                c.Screen.Text(11);
                c.PassHours(2);
                c.LoseItems(Who.Everybody);
                c.Next = 0x0D;
                break;
        }
        return false;
    }

    /// <summary>Leaving the march (a line the card never offers): the rebels remember it.</summary>
    private static void SlinkAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, -5, -5);
            c.Screen.Text(15);
        }
        else
        {
            c.AdjustReputation(c.Here, -0x1E, -0x23);
            c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, 0x25, 0, 0, 0x5A0, 0, 2);
            c.Screen.Text(14);
        }
        c.PassHours(1);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
