using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Snuck00": the party has crept into the guild's living quarters at night (the guild in
/// party_x, its secret's fact in party_y). Search the leader's papers for evidence, wait, or
/// loom over his sleeping family to change the guild's mind; a stumble raises the house.
/// </summary>
// orig: R071+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R071+0x0990 (standard pop-ups, none offered; unused-line handler 0a32)
public sealed class NightInTheGuildLeadersHouse : Card
{
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;

    /// <summary>The main loop stops at card 0x62.</summary>
    private const int EndOfTheGameLoop = 0x62;

    public override int Number => 0x6C;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 9);
        if ((c.GuildCode & Fact(c, 9)) != 0)
        {
            menu[0] = OptionState.Shown;
        }
        var standing = c.LocationQuery(c.Here, 0);
        // Listed, but no pop-up line uses them.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        return menu.Run(
            new MenuLine(chance => SearchForEvidence(c, ref standing, chance), () => SneakChance(c)),
            new MenuLine(chance => WaitInHiding(c, ref standing, chance), () => SneakChance(c)),
            new MenuLine(chance => FrightenTheFamily(c, ref standing, chance), () => SneakChance(c)),
            // C: line 3 is offered but its action (07ca) is empty. Hint R071+0x07CC.
            new MenuLine(_ => { }, () => SneakChance(c)),
            // DORMANT: line 4 (opt_flag_4 = 0): back to the card before. orig: R071+0x07DE (hint 07f0)
            new MenuLine(_ => { c.PassHours(0); c.Next = c.PreviousCard; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 5..8 (opt_flag = 0): card 0x62. orig: R071+0x0828/0872/08bc/0906 (hints 083a/0884/08ce/0918)
            new MenuLine(_ => LeaveTheGame(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveTheGame(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveTheGame(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveTheGame(c), OptionHint.Saying(SuccessAssured)),
            // C: line 9 is offered and leads to card 0x62, which ends the original's main loop; its hint
            // (0962) leaves the chance and member unset (−1 here). orig: R071+0x0950
            new MenuLine(_ => LeaveTheGame(c), OptionHint.Saying(SuccessAssured)));
    }

    private static void LeaveTheGame(CardContext c)
    {
        c.PassHours(0);
        c.Next = EndOfTheGameLoop;
    }

    private static int Fact(CardContext c, int type) => c.Events.FactValue(PoliticsQuest, type, c.Here);

    /// <summary>The party's average Stealth, better before dawn and worse by day.</summary>
    // orig: R071+0x026E (hints 068e, 06c2, 07b8, 07cc)
    // C: a void function used as an int: its value is AX after the last call, clamp99(v).
    private static OptionHint SneakChance(CardContext c)
    {
        var value = c.PartyAverageSkill((int)Skill.Stealth);
        var hour = c.State.Date.Hour;
        if (hour is >= 0 and <= 5)
        {
            value += 10;
        }
        if (hour is >= 5 and <= 18)
        {
            value -= 10;
        }
        return new OptionHint(Math.Clamp(value, 0, 99), "", OptionHint.WholeParty);
    }

    /// <summary>Somebody blunders into the household: the party's name in the city sinks.</summary>
    // orig: R071+0x02A8
    private static void RaiseTheHouse(CardContext c, ref int standing)
    {
        if (c.Random(2) == 1)
        {
            // BUG: the original makes $ChosenOneName the least agile member's Agility value
            // (party_min_attr's result), not the member; a value past the party names nobody.
            c.Variables.Chosen[0] = c.PartyMinAttribute((int)Attribute.Agility);
            c.Screen.Text(1);
        }
        else
        {
            c.Variables.Chosen[0] = c.Leader;
            c.Screen.Text(2);
        }
        standing = standing >= -10 ? -30 : standing - 20;
        c.LocationSet(c.Here, 0, standing);
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, 0);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    private static void FoundTheEvidence(CardContext c)
    {
        c.Events.Update(-2, c.Here, FactKind, 9, c.GuildFact, PoliticsQuest, EventUpdate.Or);
        c.Events.Update(-2, c.Here, FactKind, 0x0A, c.GuildFact, PoliticsQuest, EventUpdate.Or);
    }

    /// <summary>
    /// Search the leader's chambers. What can be found is the city's: letters (Perception),
    /// a locked chest (Artifice), a woman in the monastery, a hidden compartment.
    /// </summary>
    // orig: R071+0x0328 (hint 068e)
    // C: the function ends with `return next_card = saved_card` on every path, so a blunder's
    // street card is replaced by the saved card as well.
    private static void SearchForEvidence(CardContext c, ref int standing, int chance)
    {
        if (c.Random(100) < chance)
        {
            if ((c.GuildCode & Fact(c, 8)) != 0)
            {
                c.PartyMaxSkill((int)Skill.ReadWrite);
                c.Variables.Chosen[0] = c.BestMember;
                // C: a negative remainder matches no case; case 3 does not exist and case 5 cannot happen.
                switch ((c.City.Ordinal + c.GuildFact) % 5)
                {
                    case 0:
                        if (c.Random(50) > (byte)c.BestValue)
                        {
                            c.Screen.Text(3);
                        }
                        else if (c.PartyMaxAttribute((int)Attribute.Perception) >= c.Random(100))
                        {
                            FoundTheEvidence(c);
                            c.Screen.Text(0x0A);
                        }
                        else
                        {
                            c.Screen.Text(4);
                        }
                        break;
                    case 1:
                        if (c.Random(50) > (byte)c.BestValue)
                        {
                            c.Screen.Text(3);
                        }
                        else
                        {
                            c.PartyMaxSkill((int)Skill.Artifice);
                            var picker = c.BestMember;
                            c.Variables.Chosen[0] = picker;
                            if (c.Random(100) <= (byte)c.BestValue)
                            {
                                c.PracticeSkill(picker, (int)Skill.Artifice, 7, 10, 0);
                                FoundTheEvidence(c);
                                c.Screen.Text(5);
                            }
                            else
                            {
                                c.PracticeSkill(picker, (int)Skill.Artifice, 0, 10, 0);
                                c.Screen.Text(6);
                            }
                        }
                        break;
                    case 2:
                        if (c.Random(50) > (byte)c.BestValue)
                        {
                            c.Screen.Text(3);
                        }
                        else if (c.PartyMaxAttribute((int)Attribute.Perception) << 1 >= c.Random(100))
                        {
                            FoundTheEvidence(c);
                            // C: slot 0x27 lies past var_named (see RandomName); $NamedOneName stays as it was.
                            c.RandomName(c.City.Ordinal + 0x1B, 1, 0x27);
                            c.Screen.Text(7);
                        }
                        else
                        {
                            c.Screen.Text(4);
                        }
                        break;
                    case 4:
                        c.Screen.Text(8);
                        // C: && stops at the first false test: later rolls are not made.
                        if (c.Random(50) > c.PartyMaxSkill((int)Skill.ReadWrite)
                            && c.PartyMaxAttribute((int)Attribute.Perception) << 1 >= c.Random(100)
                            && c.Random(100) <= c.PartyMaxSkill((int)Skill.Artifice))
                        {
                            FoundTheEvidence(c);
                            c.Screen.Text(9);
                        }
                        break;
                    case 5:
                        // DORMANT: x % 5 is never 5. The original compares with best_member here.
                        if (c.Random(50) > (byte)c.BestMember)
                        {
                            c.Screen.Text(3);
                        }
                        else if (c.PartyMaxAttribute((int)Attribute.Perception) >= c.Random(100))
                        {
                            FoundTheEvidence(c);
                            c.Screen.Text(0x0B);
                        }
                        else
                        {
                            c.Screen.Text(4);
                        }
                        break;
                }
            }
            else
            {
                c.Screen.Text(9, TextFrame.BlankPage);
            }
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 7, 5, 1);
            c.PassHours(1);
        }
        else
        {
            RaiseTheHouse(c, ref standing);
        }
        c.Next = c.SavedCard;
    }

    // orig: R071+0x06A0 (hint 06c2)
    private static void WaitInHiding(CardContext c, ref int standing, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.PassHours(2);
            return;
        }
        RaiseTheHouse(c, ref standing);
    }

    /// <summary>Loom over the leader's wife and children: they may sway him (from his side to neutral, or to the cause).</summary>
    // orig: R071+0x06D4 (hint 07b8)
    private static void FrightenTheFamily(CardContext c, ref int standing, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.PassHours(1);
            if (c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.PartyGet(4) / 20 - c.HereQuery(0) / 2 >= c.Random(100))
            {
                var guild = c.GuildCode;
                c.Support[0] = c.SideOf(guild);
                c.SetSide(guild, c.Support[0] != 0 ? 0 : Fact(c, 0));
                c.Support[0] = c.SideOf(guild);
                c.Screen.Text(0x0C);
            }
            else
            {
                c.Screen.Text(0x0D);
            }
            c.Screen.Text(0x0E);
            c.Next = 0x14;
            return;
        }
        RaiseTheHouse(c, ref standing);
    }
}
