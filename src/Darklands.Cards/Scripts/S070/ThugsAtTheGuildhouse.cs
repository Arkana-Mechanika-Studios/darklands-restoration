using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Polit00": at night armed thugs try to break into a guildhouse (the guild in party_x, its
/// politics fact in party_y). Scare them off, use a potion, call the watch, attack or charge;
/// beating them wins the guild over and some reward, fleeing or losing lets them do their work.
/// </summary>
// orig: R070+0x2894 (1AC1:0004) matched; tests: CardSmokeTests
// orig: handler setup R070+0x325C (standard item pop-ups 0aa6/0ae2/0b1e/0b5a, saint pop-ups unused,
// unused-line handler 0a6e)
public sealed class ThugsAtTheGuildhouse : Card
{
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;

    /// <summary>The main loop stops at card 0x62.</summary>
    private const int EndOfTheGameLoop = 0x62;

    public override int Number => 0x6B;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var standing = c.LocationQuery(c.Here, 0);
        c.SetCardItems(0x5F, 0x65, 0x62, 0x66);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        menu[1] = c.ItemsUsable();

        return menu.Run(
            new MenuLine(chance => Intimidate(c, menu, chance), () => IntimidationChance(c, standing)),
            new MenuLine(chance => FrightenWithAPotion(c, menu, chance), () => new OptionHint(-1, CardTexts.PotionWorks, c.MenuMember),
                () => c.MemberPopupHint()),
            new MenuLine(_ => CallTheWatch(c, ref standing), () => new OptionHint(-1, SuccessAssured, c.Leader)),
            // orig: R070+0x3082 (line 3); hint R070+0x30A0 (1AC1:0810) matched
            new MenuLine(chance => SurpriseAttack(c, ref standing, chance),
                () => new OptionHint(Math.Clamp(c.PartyMinSkill((int)Skill.Stealth) + 10, 0, 99), "", OptionHint.WholeParty)),
            new MenuLine(_ => Fight(c, ref standing), OptionHint.Saying(CardTexts.Unknown)),
            // C: lines 5..8 are enabled but their text lines are the placeholders "5".."8"; their hints
            // (088a, 08ca, 090a) leave the chance and member unset (stack leftovers; −1 here). They and
            // "ignore them" (line 9) go to card 0x62, which ends the original's main loop.
            // orig: R070+0x3108/08b8/08f8/0938 (lines 5..8), R070+0x3212 (line 9, hint 0994)
            new MenuLine(_ => LeaveThemBe(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveThemBe(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveThemBe(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveThemBe(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveThemBe(c), OptionHint.Saying(SuccessAssured)));
    }

    private static void LeaveThemBe(CardContext c)
    {
        c.PassHours(0);
        c.Next = EndOfTheGameLoop;
    }

    private static int Fact(CardContext c, int type) => c.Events.FactValue(PoliticsQuest, type, c.Here);

    private static int BackToTheNight(CardContext c) => c.IsDaylight ? c.SavedCard : c.State.SavedCard2;

    /// <summary>The party has helped the guild: fact 0x14 gets the guild's bit.</summary>
    private static void TheGuildIsGrateful(CardContext c) =>
        c.Events.Update(-2, c.Here, FactKind, 0x14, c.GuildCode, PoliticsQuest, EventUpdate.Or);

    // orig: R070+0x2EDE
    private static OptionHint IntimidationChance(CardContext c, int standing)
    {
        var value = c.State.Party.Fame / 20 + (Math.Abs(standing) / 2
            + (c.BestWeaponSkill(c.Leader) / 3 + c.PartyAverageSkill((int)Skill.Stealth) / 3));
        return new OptionHint(Math.Clamp(value, 0, 99), "", c.Leader);
    }

    /// <summary>A polite cough behind them: they go; or they sneer and the other approaches are off.</summary>
    // orig: R070+0x2E72
    private static void Intimidate(CardContext c, CardMenu menu, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.Screen.Text(2);
            c.PassHours(1);
            TheGuildIsGrateful(c);
            c.Next = BackToTheNight(c);
            return;
        }
        c.Screen.Text(1);
        menu[0] = menu[1] = menu[2] = OptionState.Shown;
    }

    /// <summary>
    /// A potion thrown at them. Both its option hint and its pop-up give the chance −1, so this
    /// always ends as a failed threat (the potion is used up).
    /// </summary>
    // orig: R070+0x2F56 (hint R070+0x2FF0 "Of course the potion will work!", member menu_member)
    private static void FrightenWithAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Succeeds(chance))
        {
            // DORMANT: the chance is always −1.
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                case 3:
                    c.Screen.Text(3);
                    break;
                case 2:
                    c.Screen.Text(4);
                    break;
            }
            c.PassHours(1);
            TheGuildIsGrateful(c);
            c.Next = BackToTheNight(c);
            return;
        }
        c.Screen.Text(1);
        menu[0] = menu[1] = menu[2] = OptionState.Shown;
    }

    // orig: R070+0x3028 (hint 07b8)
    private static void CallTheWatch(CardContext c, ref int standing)
    {
        c.Screen.Text(0x0A);
        Fight(c, ref standing);
        c.PassHours(0);
    }

    /// <summary>A surprise attack: the roll is made but either way it comes to the same fight.</summary>
    // orig: R070+0x3082 (hint 0810, on the menu line)
    // C: f0260(1) and f0260(0) are the same call: the fight takes no argument.
    private static void SurpriseAttack(CardContext c, ref int standing, int chance)
    {
        _ = c.Succeeds(chance);
        Fight(c, ref standing);
    }

    /// <summary>The fight with the thugs and their leader, on the street the party came from.</summary>
    // orig: R070+0x2AF0 (line 4: R070+0x30C6, hint 0840)
    private static void Fight(CardContext c, ref int standing)
    {
        var battlefield = c.PreviousCard switch
        {
            6 or 8 => 0x13,
            9 or 10 => 0x11,
            _ => 0x56,
        };
        // C: arguments right to left: the leader's quality, then the thugs' number (rand_mod(4)),
        // then their quality (rand_mod(2)).
        var leaderQuality = c.PartyLoreLevel() % 3 + 1;
        var thugs = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var thugQuality = c.Random(2) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(battlefield, -1, 0x0C, thugQuality, thugs, 0x16, leaderQuality, 1, -1, 0, 0))
        {
            case 0:
                Victory(c, ref standing);
                c.PassHours(1);
                c.Next = BackToTheNight(c);
                break;
            case 1:
                c.RejoinAway();
                TheThugsHaveTheirWay(c);
                c.Screen.Text(6);
                c.PassHours(1);
                c.Next = 0x14;
                break;
            case 2:
                c.RejoinAway();
                TheThugsHaveTheirWay(c);
                c.Screen.Text(7);
                LoseStanding(c, ref standing, c.Random(6) + 5);
                c.PassHours(1);
                c.Next = 0x14;
                break;
            case 3:
                TheThugsHaveTheirWay(c);
                c.Screen.Text(8);
                LoseStanding(c, ref standing, c.Random(6) + 9);
                c.PassHours(2);
                c.Next = 0x14;
                break;
            case 4:
                TheThugsHaveTheirWay(c);
                c.Screen.Text(9);
                LoseStanding(c, ref standing, c.Random(6) + 0x0E);
                c.PassHours(1);
                c.Next = 0x14;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results change nothing.
                break;
        }
    }

    // C: cur_rep is the card's copy of the reputation: lowered, then written back.
    private static void LoseStanding(CardContext c, ref int standing, int loss)
    {
        standing -= loss;
        c.LocationSet(c.Here, 0, standing);
    }

    /// <summary>The guild's politics move against the party's cause (−4..4 on the guild's fact).</summary>
    // orig: R070+0x2D1A
    private static void TheThugsHaveTheirWay(CardContext c)
    {
        var value = Fact(c, c.GuildFact);
        if (Fact(c, 0) == -1)
        {
            if (value <= -4)
            {
                TheGuildIsGrateful(c);
            }
            else
            {
                value += c.Random(2) + 1;
                value = Math.Max(1, value);
            }
        }
        else if (value < 4)
        {
            value -= c.Random(2) + 1;
            value = Math.Min(-1, value);
        }
        else
        {
            TheGuildIsGrateful(c);
        }
        c.Events.Update(-2, c.Here, FactKind, c.GuildFact, Math.Clamp(value, -4, 4), PoliticsQuest, EventUpdate.Set);
    }

    /// <summary>The guildsmen cheer: standing, the guild moves towards the party's cause, and ten groschen.</summary>
    // orig: R070+0x2DC2
    private static void Victory(CardContext c, ref int standing)
    {
        c.Screen.Text(5);
        standing += c.Random(6) + 0x0E;
        c.LocationSet(c.Here, 0, standing);
        int value;
        if (Fact(c, 0) == -1)
        {
            value = -1 - c.Random(2);
            value = Math.Min(-1, value);
        }
        else
        {
            value = c.Random(2) + 1;
            value = Math.Max(1, value);
        }
        c.Events.Update(-2, c.Here, FactKind, c.GuildFact, Math.Clamp(value, -4, 4), PoliticsQuest, EventUpdate.Set);
        c.State.Party.Purse = c.State.Party.Purse.Add(0, 10, 0);
    }
}
