using Darklands.Core.Characters;
using Darklands.Core.World;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Sacri00": bound on the cultists' hilltop at midnight, awaiting sacrifice. Bear your fate,
/// beg for mercy, bribe a cultist, break the ropes, cut them on a sharp rock, slip out a
/// potion, or call on a saint.
/// </summary>
// orig: R079+0x2360 (1A6E:0000) matched; tests: CardSmokeTests
// orig: R079+0x2AFE (1A6E:079E) matched; R079+0x2B88 (1A6E:0828) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 7..19 (never offered).
public sealed class CultistSacrifice : Card
{
    public const int CardNumber = 0x8F;

    /// <summary>The cult's altar on the hill.</summary>
    private const int TheAltar = 0x8E;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu[2] = c.State.Party.Purse.TotalPfennigs <= 0 ? OptionState.Shown : OptionState.Enabled;
        menu.Enable(0, 1, 3, 4);
        c.SetCardItems(0x63, 0x74);
        c.SetCardSaints(0x0F, 0x50, 0x54, 0x6F);
        menu[5] = c.ItemsUsable();
        menu[6] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(() => BearYourFate(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => BegForMercy(c, chance), hint: () => MercyChance(c)),
            new MenuLine(() =>
            {
                if (!BribeACultist(c, BribeChance(c).Chance))
                {
                    menu[2] = OptionState.Shown;
                }
            }, () => BribeChance(c)),
            new MenuLine(() =>
            {
                if (!BreakTheRopes(c, StrengthChance(c).Chance))
                {
                    menu[3] = OptionState.Shown;
                }
            }, () => StrengthChance(c)),
            new MenuLine(() =>
            {
                if (!CutTheRopes(c, NimblenessChance(c).Chance))
                {
                    menu[4] = OptionState.Shown;
                }
            }, () => NimblenessChance(c)),
            new MenuLine(chance => SlipOutAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion),
                () => PotionHint(c, c.MenuMember)),
            // orig: R079+0x2A08 (line 6); R079+0x2C08 (1A6E:08A8) matched; R079+0x2C92 (1A6E:0932) matched; R079+0x2D1C (1A6E:09BC) matched;
            // R079+0x2DA6 (1A6E:0A46) matched; tests: CardSmokeTests
            new MenuLine(() => CallOnASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint));
    }

    /// <summary>Breaking free: the cultists come after the party.</summary>
    // orig: R079+0x2560 (1A6E:0200) matched; tests: CardSmokeTests
    private static void FightTheCultists(CardContext c)
    {
        var second = c.Random(5) + 3;
        switch (c.RunBattle(0x39, -1, 7, 1, second, 0, 2, 1, -1, 0, 0))
        {
            case 0:
                c.Screen.Text(6, TextFrame.BlankPage);
                c.Next = TheAltar;
                break;
            case 1:
            case 2:
                c.DropAway();
                c.Screen.Text(7, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                c.Screen.Text(8, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = CardNumber;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party bound.
                break;
        }
    }

    /// <summary>The party fights while being sacrificed; beaten, everybody dies.</summary>
    // orig: R079+0x25F0 (1A6E:0290) matched; R079+0x26A0 (1A6E:0340) matched; tests: CardSmokeTests
    private static void BearYourFate(CardContext c)
    {
        c.Screen.Text(0x0B, TextFrame.BlankPage);
        c.LoseItems(Who.Everybody);
        var second = c.Random(5) + 3;
        switch (c.RunBattle(0x39, -1, 7, 1, second, 0, 2, 1, -1, 0, 0))
        {
            case 0:
                c.Screen.Text(6, TextFrame.BlankPage);
                c.Next = TheAltar;
                break;
            case 1:
            case 2:
                c.Screen.Text(7, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                c.Screen.Text(0x0C, TextFrame.BlankPage);
                // C: all five slots are emptied; the card stays (the game ends when it is left).
                for (var i = 0; i < 5; i++)
                {
                    if (i < c.PartySize)
                    {
                        c.SetStatus(i, CharacterStatus.Empty);
                    }
                }
                c.PassHours(0);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results change nothing.
                break;
        }
    }

    // orig: R079+0x277C (1A6E:041C) matched; tests: CardSmokeTests
    private static OptionHint MercyChance(CardContext c) =>
        OptionHint.ChanceOf((1000 - c.State.Party.Fame) / 10, OptionHint.WholeParty);

    // orig: R079+0x26D8 (1A6E:0378) matched; tests: CardSmokeTests
    private static void BegForMercy(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(9, TextFrame.BlankPage);
            c.Injure(Who.Everybody, 1, 1, 10);
            c.AddAttribute(Who.Everybody, 6, -100);
            c.AddSkill(Who.Everybody, 9, -(c.Random(6) + 1));
            c.PassHours(3);
            c.LoseItems(Who.Everybody);
            c.Next = 0x0C;
            return;
        }
        c.Screen.Text(0x0A, TextFrame.BlankPage);
        c.AddAttribute(Who.Everybody, 6, -50);
        c.AddSkill(Who.Everybody, 9, -(c.Random(3) + 1));
        c.Next = CardNumber;
    }

    // orig: R079+0x27EC (1A6E:048C) matched; tests: CardSmokeTests
    private static OptionHint BribeChance(CardContext c)
    {
        c.Variables.Money[0] = c.State.Party.Purse;
        // C: the pfennig total is a long taken unsigned (low 16 bits) before the division.
        return OptionHint.ChanceOf(Math.Clamp((ushort)c.State.Party.Purse.TotalPfennigs / 120, 1, 99), OptionHint.WholeParty);
    }

    // orig: R079+0x2798 (1A6E:0438) matched; tests: CardSmokeTests
    private static bool BribeACultist(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1, TextFrame.BlankPage);
            c.PassHours(4);
            c.State.Party.Purse = Core.Economy.Money.Zero;
            FightTheCultists(c);
            return true;
        }
        c.Screen.Text(2, TextFrame.BlankPage);
        c.Next = CardNumber;
        return false;
    }

    // orig: R079+0x2864 (1A6E:0504) matched; tests: CardSmokeTests
    private static OptionHint StrengthChance(CardContext c)
    {
        var chance = c.PartyMaxAttribute(1);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    // orig: R079+0x282A (1A6E:04CA) matched; tests: CardSmokeTests
    private static bool BreakTheRopes(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(0x0D, TextFrame.BlankPage);
            FightTheCultists(c);
            return true;
        }
        c.Screen.Text(0x0E, TextFrame.BlankPage);
        c.Next = CardNumber;
        return false;
    }

    // orig: R079+0x2906 (1A6E:05A6) matched; tests: CardSmokeTests
    // C: party-stat-top(0x0F) reads past the seven attributes: record +0x49 + 0x0F = +0x58,
    // which is skill 1; the result is kept in the attribute query's globals (DS:9C1D, DS:9A44).
    private static OptionHint NimblenessChance(CardContext c)
    {
        var chance = c.PartyMaxAttributeAtSkillOne();
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    // orig: R079+0x2886 (1A6E:0526) matched; tests: CardSmokeTests
    private static bool CutTheRopes(CardContext c, int chance)
    {
        var who = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(0x0F, TextFrame.BlankPage);
            c.PracticeSkill(who, 0x0F, 1, 10, 0);
            FightTheCultists(c);
            return true;
        }
        c.Screen.Text(0x10, TextFrame.BlankPage);
        c.PracticeSkill(who, 0x0F, 0, 10, 0);
        c.Injure(Who.Everybody, 1, 1, 10);
        c.Injure(who, 1, 1, 10);
        c.Next = CardNumber;
        return false;
    }

    // orig: R079+0x2BC0 (1A6E:0860) matched; R079+0x2BE4 (1A6E:0884) matched; tests: CardSmokeTests
    private static OptionHint PotionHint(CardContext c, int member)
    {
        c.Variables.Chosen[0] = member;
        return OptionHint.ChanceOf(c.Skill(member, 0x0F), member);
    }

    // orig: R079+0x2928 (1A6E:05C8) matched; R079+0x29D0 (1A6E:0670) matched; tests: CardSmokeTests
    private static void SlipOutAPotion(CardContext c, int chance)
    {
        // C: the option line itself gives chance -1: the roll then always fails.
        // C: the chance is the handler's the loop calls (CardMenu.Run): the option line's -1, the box's for a click there, and for Enter
        // C: there card-option-handler[k], the box's handler for slot k % 4.
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Variables.Chosen[0] = c.MenuMember;
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(c.Variables.Chosen[0], 0x0F, 1, 10, 0);
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(5, TextFrame.BlankPage);
                    FightTheCultists(c);
                    break;
                case 1:
                    c.Screen.Text(4, TextFrame.BlankPage);
                    c.PassHours(1);
                    c.Next = TheAltar;
                    break;
                default:
                    // DORMANT: slots 2, 3 hold no potion: nothing happens.
                    break;
            }
            return;
        }
        c.PracticeSkill(c.Variables.Chosen[0], 0x0F, 0, 10, 0);
        c.Screen.Text(3, TextFrame.BlankPage);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    // orig: R079+0x2A08 (1A6E:06A8) matched; R079+0x2AC6 (1A6E:0766) matched; tests: CardSmokeTests
    private static void CallOnASaint(CardContext c)
    {
        if (c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]) == -1)
        {
            return;
        }
        // C: the script tests the hint handler's chance (always -1) instead of the prayer's
        // answer, so every prayer that is said succeeds.
        const int handlersChance = -1;
        if (handlersChance != 0)
        {
            if (c.MenuSlot == 0 || c.MenuSlot == 3)
            {
                c.Screen.Text(0x11, TextFrame.BlankPage);
                c.PassHours(1);
                c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
                var village = c.State.Locations[c.Here];
                village.ArrivalCard = 0x88;
                // C: the flags are set to 4 (=), not or-ed as the altar card does.
                village.Flags = Location.DestroyedFlag;
                village.Icon = 9;
                c.Next = 0x0C;
            }
            if (c.MenuSlot != 1 && c.MenuSlot != 2)
            {
                return;
            }
            c.Screen.Text(0x12, TextFrame.BlankPage);
            c.PassHours(3);
            c.Next = 0x0C;
            return;
        }
#pragma warning disable CS0162 // DORMANT: the unanswered prayer (text 19) is unreachable.
        c.Screen.Text(0x13, TextFrame.BlankPage);
        c.Next = CardNumber;
#pragma warning restore CS0162
    }
}
