using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol15": the crowd at the council hall; the city lord's guards fire on it from the
/// balcony. The party may talk or bribe the guards at the doors away, inspire the crowd to
/// storm the hall, pray, break in at night (never offered here), use a potion on the door,
/// or abandon the rebels. Every way in leads to the fight inside.
/// </summary>
// orig: R090+0x22EC (1A66:000C) matched; tests: CardSmokeTests
public sealed class RebelsBeforeTheCouncilHall : Card
{
    private const int Picture = 0;
    private const int FirePotion = 0x63;
    private const int AcidPotion = 0x66;
    private const int SergeantRefused = 0x26;
    private const int RebelsTag = 2;

    public override int Number => 0xBB;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        // C: the item box's handlers give the potion 100 % beside the member ($ChosenOneName unchanged); a choice there by a click
        // C: or by Enter is rated by them (CardMenu.ItemBox), a choice of the line itself by its hint (-1).
        menu.ItemBox = () => OptionHint.ChanceOf(100, c.MenuMember);
        menu.Enable(0, 1, 2, 4, 7);
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        c.PartyMaxSkill((int)Skill.Stealth);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        var price = Money.From(c.HereQuery(2) + c.SidePercent(1) / 18, 0, 0);
        c.Variables.Money[0] = price;
        if (c.State.Party.Purse < price)
        {
            menu[1] = OptionState.Shown;
        }
        if (c.Events.Fact(RebelsTag, SergeantRefused, c.Here))
        {
            menu[0] = OptionState.Shown;
            menu[1] = OptionState.Shown;
        }
        // C: the card writes earlier-screen = 0xBC (nothing reads it before the dispatcher sets it again),
        // and greys out both breaking in and waiting.
        c.State.PreviousCard = 0xBC;
        menu[4] = OptionState.Shown;
        menu[6] = OptionState.Shown;
        // orig: R090+0x2D44 (1A66:0A64) matched
        c.SetCardItems(FirePotion, AcidPotion);
        c.SetCardSaints(0x0C, 0x36, 0x3D);
        menu[5] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        void GuardsRefuse()
        {
            c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, SergeantRefused, 0, 0, 0x30, 0, RebelsTag);
            menu[0] = OptionState.Shown;
            menu[1] = OptionState.Shown;
            c.Screen.Text(2);
            c.PassHours(1);
        }

        return menu.Run(
            // orig: R090+0x26CA (1A66:03EA); hint R090+0x273A matched
            new MenuLine(() =>
                {
                    if (Succeeds(c, TalkChance(c)))
                    {
                        c.Screen.Text(1);
                        c.PassHours(1);
                        StormTheHall(c);
                    }
                    else
                    {
                        GuardsRefuse();
                    }
                },
                () => OptionHint.ChanceOf(TalkChance(c), c.Leader)),
            // orig: R090+0x2790 (1A66:04B0); hint R090+0x280C matched
            new MenuLine(() =>
                {
                    if (Succeeds(c, BribeChance(c)))
                    {
                        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
                        c.Screen.Text(3);
                        c.PassHours(1);
                        StormTheHall(c);
                    }
                    else
                    {
                        GuardsRefuse();
                    }
                },
                () => OptionHint.ChanceOf(BribeChance(c), c.Leader)),
            // orig: R090+0x2858 (1A66:0578); hint R090+0x28C6 matched
            new MenuLine(chance => InspireTheCrowd(c, chance, menu),
                () => OptionHint.ChanceOf(InspireChance(c), c.Variables.Chosen[0])),
            // orig: R090+0x2956 (1A66:0676); hint R090+0x29CE; saint hints R090+0x2E2A..0c5e matched
            new MenuLine(() => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupMemberHint),
            // orig: R090+0x2A06 (1A66:0726); hint R090+0x2B10 matched
            // DORMANT: line 4 is always greyed out here, so the night break-in never runs.
            new MenuLine(chance => BreakInAtNight(c, chance, menu),
                () => OptionHint.ChanceOf(BreakInChance(c), Who.Everybody)),
            // orig: R090+0x2B3A (1A66:085A); hint R090+0x2BDA; item hints R090+0x2DFA, R090+0x2E12 matched
            new MenuLine(chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion)),
            // orig: R090+0x2C12 (1A66:0932); hint R090+0x2C24 matched
            // DORMANT: line 6 is always greyed out here.
            new MenuLine(() => { c.PassHours(22); c.Next = 0xBC; }, OptionHint.Saying(NoRisk)),
            // orig: R090+0x2C5C (1A66:097C); hint R090+0x2C94 matched
            new MenuLine(() => Desert(c), OptionHint.Saying("Coward!")),
            // orig: R090+0x2CBC; R090+0x2D00 matched
            // DORMANT: lines 8 and 9 are hidden (flags 0); they would only let no time pass.
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)));
    }

    private static bool Succeeds(CardContext c, int chance) => c.Random(100) <= chance;

    private static int TalkChance(CardContext c) =>
        Math.Clamp((c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.SidePercent(-1) + c.Skill(Who.Leader, (int)Skill.SpeakCommon) - 0x46) / 3, 0, 99);

    private static int BribeChance(CardContext c) =>
        Math.Clamp(c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2 + (c.Attribute(Who.Leader, (int)Attribute.Charisma) + 20) * 2, 0, 99);

    /// <summary>The most charming member speaks (the choice of speaker is part of the chance).</summary>
    private static int InspireChance(CardContext c)
    {
        c.PartyMaxAttribute((int)Attribute.Charisma);
        var speaker = c.Variables.Chosen[0] = (byte)c.BestMember;
        var chance = (c.SideStrength(-1) + c.ReputationHere + c.Attribute(speaker, (int)Attribute.Charisma) * 2
            + c.Skill(speaker, (int)Skill.SpeakCommon)) / 4;
        if (c.HereQuery(0x29) != 2)
        {
            chance += 10;
        }
        return Math.Clamp(chance, 0, 99);
    }

    /// <summary>The party's average Stealth or its best Artifice, whichever is lower.</summary>
    // C: smaller-of(avg, max): the arguments are evaluated right to left, so the Artifice query runs first.
    private static int BreakInChance(CardContext c)
    {
        var artifice = c.PartyMaxSkill((int)Skill.Artifice);
        return Math.Min(c.PartyAverageSkill((int)Skill.Stealth), artifice);
    }

    private static void InspireTheCrowd(CardContext c, int chance, CardMenu menu)
    {
        if (Succeeds(c, chance))
        {
            c.AdjustReputation(c.Here, 15, 15);
            c.PassHours(1);
            c.Screen.Text(4);
            StormTheHall(c);
            return;
        }
        menu[2] = OptionState.Shown;
        c.AdjustReputation(c.Here, -5, -5);
        c.Screen.Text(5);
        c.PassHours(1);
        c.Next = 0xBB;
    }

    private static void Pray(CardContext c)
    {
        // C: the saint pop-up's hint, called on the choice, names the one who prays as $ChosenOneName.
        if (c.MenuPopup)
        {
            c.Variables.Chosen[0] = c.MenuMember;
        }
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.PassHours(1);
            c.Screen.Text(6);
            StormTheHall(c);
            return;
        }
        c.AdjustReputation(c.Here, -2, -1);
        c.PassHours(1);
        c.Screen.Text(7);
        c.Next = 0xBB;
    }

    /// <summary>Waiting for the night and prying open a shutter at the back of the hall.</summary>
    private static void BreakInAtNight(CardContext c, int chance, CardMenu menu)
    {
        var artifice = c.PartyMaxSkill((int)Skill.Artifice);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        c.PassHours(c.IsDaylight ? 22 : 1);
        if (Succeeds(c, chance))
        {
            c.PassHours(2);
            c.Screen.Text(8);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 6, 0);
            c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 1, 6, 0);
            StormTheHall(c);
        }
        else
        {
            menu[4] = OptionState.Shown;
            c.AdjustReputation(c.Here, -5, -5);
            if (artifice > stealth)
            {
                c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, 0);
                c.Screen.Text(10);
            }
            else
            {
                c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 0, 10, 0);
                c.Screen.Text(9);
            }
            c.PassHours(1);
            c.Next = 0xBB;
        }
        c.PartyMaxSkill((int)Skill.Stealth);
        c.Variables.Chosen[0] = (byte)c.BestMember;
    }

    /// <summary>The potion chosen in the pop-up always works; chosen any other way it fails and burns its carrier.</summary>
    private static void UseAPotion(CardContext c, int chance)
    {
        var potion = c.CardItems[c.MenuSlot];
        c.TakeItem(c.MenuMember, potion);
        if (Succeeds(c, chance))
        {
            c.Screen.Text(potion == AcidPotion ? 11 : 12);
            c.PassHours(1);
            StormTheHall(c);
            return;
        }
        c.Injure(c.MenuMember, 1, 2, 7);
        c.Screen.Text(13);
        c.PassHours(1);
        c.Next = 0xBB;
    }

    private static void Desert(CardContext c)
    {
        c.ConflictOutcome(-1, -1, 1);
        c.Screen.Text(16);
        c.PassHours(0);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>The fight inside the hall; a win leads to the city lord (0x60).</summary>
    // orig: R090+0x25E6 (1A66:0306) matched
    private static void StormTheHall(CardContext c)
    {
        // Arguments are evaluated right to left: the second group's number is drawn first.
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        var result = c.BattleResult = c.RunBattle(0x25, c.Here + 0x70, 3, 2, first, 0, 2, second, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.PassHours(0);
                c.Next = 0x60;
                break;
            case 1 or 2:
                c.PassHours(1);
                c.ReportAway();
                c.ConflictOutcome(-1, -1, 1);
                c.Next = 0x82;
                break;
            case 3:
                c.ConflictOutcome(-1, -1, 1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(14);
                c.PassHours(1);
                c.Next = 0x0D;
                break;
            case 4:
                c.ConflictOutcome(-1, -1, 1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(15);
                c.PassHours(2);
                c.Next = 0x0D;
                break;
        }
    }
}
