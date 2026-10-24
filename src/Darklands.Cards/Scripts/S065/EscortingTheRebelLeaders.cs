using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol10": the party and the city guard lead captured rebel leaders to the dungeon while
/// angry crowds block the way. Each crowd is calmed by a speech, a saint, a potion or a fight;
/// the stronger the rebels, the more crowds before the fortress (card 0xB6). Fleeing or losing
/// a fight frees the leaders.
/// </summary>
// orig: R065+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R065+0x089A; unused-line handler R065+0x093C ("screwed up.")
public sealed class EscortingTheRebelLeaders : Card
{
    private const int AtTheFortress = 0xB6;

    public override int Number => 0x54;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        // C: DS:8E06 is a module global, reset here: the number of crowds dispersed so far.
        var crowdsPassed = 0;
        c.PartyMaxAttribute((int)Attribute.Charisma);
        c.Variables.Chosen[0] = c.BestMember;
        c.SetCardItems(0x5F, 0x65, 0x62, 0x66);
        c.SetCardSaints(0x23, 0x0C, 0x3D, 0x36);
        menu[2] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();
        if (c.SidePercent(1) >= 50)
        {
            c.Screen.Text(1, TextFrame.BlankPage);
        }

        void Dispersed()
        {
            crowdsPassed++;
            menu[0] = OptionState.Enabled;
        }

        // The rebels' strength (side 1 in percent of side −1) sets how many crowds stand in the way:
        // three from 200%, two from 50%, else one. Block 0 is the first crowd, 2 a later one, 3/4 the
        // last before the fortress or the square.
        // C: side2_percent() is asked afresh in each test, so the band can change between rounds; a
        // count past the band's number (after the band fell) keeps showing the last crowd's block.
        menu.EachRound = () =>
        {
            int LastCrowd() => c.HereQuery(0x18) != 0 ? 4 : 3;
            if (c.SidePercent(1) >= 200)
            {
                if (crowdsPassed == 3)
                {
                    c.Next = AtTheFortress;
                    return;
                }
                menu.Block = crowdsPassed == 2 ? LastCrowd() : crowdsPassed == 1 ? 2 : 0;
            }
            else if (c.SidePercent(1) >= 50)
            {
                if (crowdsPassed == 2)
                {
                    c.Next = AtTheFortress;
                    return;
                }
                menu.Block = crowdsPassed == 1 ? LastCrowd() : 0;
            }
            else
            {
                if (crowdsPassed == 1)
                {
                    c.Next = AtTheFortress;
                    return;
                }
                menu.Block = LastCrowd();
            }
        };

        return menu.Run(
            new MenuLine(chance => MakeASpeech(c, menu, chance, Dispersed), () => SpeechChance(c)),
            new MenuLine(_ => Pray(c, Dispersed), OptionHint.Saying(CardTexts.SelectSaint), () => SaintHint(c)),
            new MenuLine(chance => ThrowAPotion(c, chance, Dispersed), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            new MenuLine(_ => Fight(c, Dispersed), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(_ => RunAway(c), OptionHint.Saying("Coward!")),
            // DORMANT: lines 5..9 (opt_flag_5..9 = 0): time passes, nothing else.
            // orig: R065+0x0746/078a/07ce/0812/0856, hints 0752/0796/07da/081e/0862
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)));
    }

    // orig: R065+0x04B4
    private static OptionHint SpeechChance(CardContext c)
    {
        var speaker = c.Variables.Chosen[0];
        var value = (c.Attribute(speaker, (int)Attribute.Charisma) + c.HereQuery(0) + c.SidePercent(1) + c.Skill(speaker, (int)Skill.SpeakCommon)) / 5;
        return new OptionHint(Math.Clamp(value, 0, 99), "", speaker);
    }

    /// <summary>The most charismatic member (or whoever last used the pop-up) speaks to the crowd.</summary>
    // orig: R065+0x0424
    // C: var_chosen1 is also set by the pop-up handlers, so a speech after using an item or a saint is
    // made by that member.
    private static void MakeASpeech(CardContext c, CardMenu menu, int chance, Action dispersed)
    {
        var speaker = c.Variables.Chosen[0];
        if (c.Succeeds(chance))
        {
            c.AdjustReputation(c.Here, 15, 25);
            c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 1, 10, -1);
            c.PassHours(1);
            c.Screen.Text(5);
            dispersed();
            return;
        }
        c.AdjustReputation(c.Here, -5, -5);
        menu[0] = OptionState.Shown;
        c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 0, 10, -1);
        c.PassHours(1);
        c.Screen.Text(6);
    }

    /// <summary>The saint pop-up of this card names the praying member as the one who acts.</summary>
    // orig: R065+0x0A12, 0a9c R065+0x0A9C, 0b26 R065+0x0B26, 0bb0 R065+0x0BB0
    private static OptionHint SaintHint(CardContext c)
    {
        var rating = c.SaintPopupHint();
        return rating with { Member = c.MenuMember };
    }

    // orig: R065+0x051A (hint R065+0x05A0)
    private static void Pray(CardContext c, Action dispersed)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        c.Variables.Chosen[1] = c.MenuMember;
        // C: the test is answer - 1 == 0: only an answer of exactly 1 works.
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(8);
                    c.PassHours(1);
                    break;
                case 1:
                case 2:
                case 3:
                    c.Screen.Text(c.Random(2) != 0 ? 7 : 9);
                    c.PassHours(1);
                    break;
            }
            dispersed();
            return;
        }
        c.Screen.Text(0x0A);
    }

    /// <summary>The potion pop-up: 100 plus 12, 5 by day or 20 by night, 14 or 25: a thrown potion always works.</summary>
    // orig: R065+0x0978, 099a R065+0x099A, 09ce R065+0x09CE, 09f0 R065+0x09F0 (base R065+0x0974: 100)
    private static OptionHint PotionHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var chance = 100 + (c.MenuSlot switch
        {
            0 => 0x0C,
            1 => c.IsDaylight ? 5 : 0x14,
            2 => 0x0E,
            _ => 0x19,
        });
        return new OptionHint(chance, "", c.MenuMember);
    }

    /// <summary>
    /// A potion into the crowd. Chosen from the option line (chance −1) it always fails: a
    /// burning potion (0x66) then starts a fight, the others do nothing.
    /// </summary>
    // orig: R065+0x05D8 (hint R065+0x064C "Select a person and a potion.")
    private static void ThrowAPotion(CardContext c, int chance, Action dispersed)
    {
        var roll = c.Random(100);
        c.Variables.Chosen[1] = c.MenuMember;
        if (roll <= chance)
        {
            c.Screen.Text(0x0B);
            c.PassHours(1);
            dispersed();
            return;
        }
        // C: the potion is not taken from the pack in either case.
        if (c.CardItems[c.MenuSlot] == 0x66)
        {
            c.Screen.Text(0x0C);
            Fight(c, dispersed);
            return;
        }
        c.Screen.Text(0x12);
    }

    /// <summary>A fight with the mob and its ringleaders: winning disperses the crowd; otherwise the leaders go free.</summary>
    // orig: R065+0x0300 (option R065+0x0684, hint 068e)
    private static void Fight(CardContext c, Action dispersed)
    {
        // C: arguments are evaluated right to left: the ringleaders (rand_mod(4)) are drawn before the mob (rand_mod(5)).
        var ringleaders = c.Random(4) + 3;
        var mob = c.Random(5) + 3;
        switch (c.RunBattle(0x25, c.Here + 0x70, 0x0C, 2, mob, 0, 2, ringleaders, -1, 0, 0))
        {
            case 0:
                c.AdjustReputation(c.Here, 3, 5);
                c.Screen.Text(0x0D);
                c.PassHours(1);
                dispersed();
                return;
            case 1:
            case 2:
                c.RejoinAway();
                c.PassHours(1);
                c.ConflictOutcome(1, 1, -1);
                c.Screen.Text(0x0E);
                break;
            case 3:
                c.PassHours(2);
                c.ConflictOutcome(1, 1, -1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(0x0F);
                break;
            case 4:
                c.PassHours(1);
                c.ConflictOutcome(1, 1, -1);
                c.LoseItems(Who.Everybody);
                c.Injure(Who.Everybody, 1, 1, 0x14);
                c.Screen.Text(0x10);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other results (the original lists −64..−1 and has a default) change nothing.
                return;
        }
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>"Coward!": the party slips away and the rebel leaders are freed.</summary>
    // orig: R065+0x06C6 (hint R065+0x071E "Coward!")
    private static void RunAway(CardContext c)
    {
        c.Screen.Text(0x11);
        c.PassHours(1);
        c.Events.Create(-2, c.Here, 0x5A, 0x1C, 0x24, 0, 0, 0x5A0, 0, 2);
        c.ConflictOutcome(1, 1, -1);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
