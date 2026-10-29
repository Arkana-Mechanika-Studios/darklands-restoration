namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Chase00": the party runs through the streets with the city guard in hot pursuit. Keep
/// running, turn and fight, lay an ambush, hide around a corner, throw a potion behind you,
/// or surrender. (The original counts a roll at or above the chance as success.)
/// </summary>
// orig: R074+0x15D2 (1995:0002) matched; tests: CardSmokeTests
// orig: R074+0x1CE8 (1995:0718) matched; R074+0x1D54 (1995:0784) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
// C: every roll here succeeds when rand_mod(100) >= chance (the reverse of most cards): kept.
public sealed class ChasedByTheCityGuard : Card
{
    public const int CardNumber = 0x7A;

    private const int CaughtPicture = 10;
    private const int DungeonPicture = 13;

    private static readonly int[] Potions = [0x60, 0x65, 0x61, 0x68];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 46);
        c.Next = CardNumber;
        var standing = c.Reputation(c.Here);
        c.SetCardSaints();
        c.SetCardItems(Potions);
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 5);
        menu[4] = c.ItemsUsable();

        return menu.Run(
            new MenuLine(chance => KeepRunning(c, standing, chance), () => RunningChance(c)),
            new MenuLine(() => Fight(c, standing), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(chance => LayAnAmbush(c, standing, chance), () => AmbushChance(c)),
            new MenuLine(chance => Hide(c, standing, chance), () => HidingChance(c)),
            new MenuLine(chance => ThrowAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion),
                () => PotionChance(c, c.MenuMember, c.MenuSlot)),
            new MenuLine(() => Surrender(c), OptionHint.Saying(SuccessAssured)));
    }

    private static int Street(CardContext c) => c.IsDaylight ? 9 : 0x0A;

    // orig: R074+0x184A (1995:027A) matched; tests: CardSmokeTests
    private static OptionHint RunningChance(CardContext c)
    {
        var chance = c.PartyMinEffectiveAgility() * 3;
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    // orig: R074+0x1790 (1995:01C0) matched; tests: CardSmokeTests
    private static void KeepRunning(CardContext c, int standing, int chance)
    {
        if (c.Random(100) >= chance)
        {
            c.PassHours(c.Random(4));
            ShowPicture(c, 347);
            c.Screen.Text(0x0F);
            c.PracticeSkill(Core.Characters.Who.Everybody, 0x10, 1, 5, 0);
            c.Next = Street(c);
            return;
        }
        ShowPicture(c, 47);
        c.Screen.Text(0x0E);
        c.PassHours(1);
        Fight(c, standing);
    }

    // orig: R074+0x195C (1995:038C) matched; tests: CardSmokeTests
    private static OptionHint AmbushChance(CardContext c)
    {
        var leader = c.Leader;
        var skill = c.Skill(leader, 0x10) + c.Skill(leader, 0x0F);
        c.Variables.Chosen[0] = leader;
        return OptionHint.ChanceOf(Math.Clamp(skill, 0, 100), leader);
    }

    // orig: R074+0x18B0 (1995:02E0) matched; tests: CardSmokeTests
    private static void LayAnAmbush(CardContext c, int standing, int chance)
    {
        var leader = c.Leader;
        if (c.Random(100) >= chance)
        {
            c.PracticeSkill(leader, 0x0F, 1, 7, 0);
            c.PracticeSkill(leader, 0x10, 1, 7, 0);
            c.Screen.Text(0x0C);
        }
        else
        {
            c.PracticeSkill(leader, 0x0F, 0, 7, 0);
            c.PracticeSkill(leader, 0x10, 0, 7, 0);
            c.Variables.Chosen[0] = leader;
            ShowPicture(c, CaughtPicture);
            c.Screen.Text(0x0B);
        }
        Fight(c, standing);
    }

    // orig: R074+0x1A84 (1995:04B4) matched; tests: CardSmokeTests
    // C: the loop runs over every slot below party_size, empty or not (an empty slot's skill counts).
    /// <summary>The clumsiest member (lowest skill 0x0F over all slots) and the city's size decide; night helps.</summary>
    private static OptionHint HidingChance(CardContext c)
    {
        var lowest = 99;
        for (var i = 0; i < c.PartySize; i++)
        {
            var skill = c.Skill(i, 0x0F);
            if (skill < lowest)
            {
                c.Variables.Chosen[0] = i;
                lowest = skill;
            }
        }
        if (!c.IsDaylight)
        {
            lowest += 20;
        }
        return OptionHint.ChanceOf(Math.Clamp(c.LocationQuery(c.Here, 2) * 3 + lowest, 0, 100), OptionHint.WholeParty);
    }

    // orig: R074+0x19A4 (1995:03D4) matched; tests: CardSmokeTests
    private static void Hide(CardContext c, int standing, int chance)
    {
        if (c.Random(100) >= chance)
        {
            ShowPicture(c, 55);
            c.PracticeSkill(Core.Characters.Who.Everybody, 0x0F, 1, 10, 0);
            if (c.IsDaylight)
            {
                c.WaitUntilHour(19);
                c.Screen.Text(9);
            }
            else
            {
                c.WaitUntilHour(5);
                c.Screen.Text(0x0A);
            }
            c.Next = Street(c);
            return;
        }
        c.Variables.Chosen[0] = c.Leader;
        ShowPicture(c, CaughtPicture);
        c.Screen.Text(0x0B);
        c.PracticeSkill(Core.Characters.Who.Everybody, 0x0F, 0, 10, 0);
        Fight(c, standing);
    }

    // orig: R074+0x1D8C (1995:07BC) matched; R074+0x1DF0 (1995:0820) matched; R074+0x1E54 (1995:0884) matched; R074+0x1EB8 (1995:08E8) matched; tests: CardSmokeTests
    /// <summary>Each potion works better in a smaller city, and with an agile thrower.</summary>
    private static OptionHint PotionChance(CardContext c, int member, int slot)
    {
        var size = c.LocationQuery(c.Here, 2);
        if (slot == 3)
        {
            return OptionHint.ChanceOf(Math.Clamp(c.EffectiveAgility(member) + (12 - size) * 5, 0, 100), member);
        }
        var chance = ((slot == 0 ? 2 : slot == 1 ? 8 : 5) - size) * 5;
        if (c.EventAtHere(0x12))
        {
            chance -= 20;
        }
        chance += c.EffectiveAgility(member);
        c.Variables.Chosen[0] = member;
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), member);
    }

    // orig: R074+0x1AF2 (1995:0522) matched; R074+0x1C32 (1995:0662) matched; tests: CardSmokeTests
    private static void ThrowAPotion(CardContext c, int chance)
    {
        // C: the option line itself only asks for a member and a potion (chance -1, so the
        // ">= chance" roll always succeeds); member and slot are then whatever the pop-up left.
        // C: the chance is the handler's the loop calls (CardMenu.Run): the option line's -1, the box's for a click there, and for Enter
        // C: there opt_fn[k], the box's handler for slot k % 4.
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) >= chance)
        {
            c.AdjustReputation(c.Here, 0, -3);
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    ShowPicture(c, 189);
                    c.Screen.Text(5);
                    break;
                case 2:
                    ShowPicture(c, 19);
                    c.Screen.Text(6);
                    break;
                case 3:
                    ShowPicture(c, 20);
                    c.Screen.Text(7);
                    break;
            }
            c.PassHours(1);
            c.Next = Street(c);
            return;
        }
        if (c.MenuSlot is >= 0 and < 4)
        {
            c.NameToSlot(0, Potions[c.MenuSlot], 0);
        }
        c.Screen.Text(8);
        c.Next = CardNumber;
    }

    // orig: R074+0x1C6A (1995:069A) matched; R074+0x1CB0 (1995:06E0) matched; tests: CardSmokeTests
    private static void Surrender(CardContext c)
    {
        ShowPicture(c, DungeonPicture);
        c.Screen.Text(4);
        c.PassHours(3);
        c.Next = 0x0D;
    }

    // orig: R074+0x1F00 (1995:0930) matched; R074+0x1872 (1995:02A2) matched; R074+0x1878 (1995:02A8) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, int standing)
    {
        // C: arguments are evaluated right to left: the second group is drawn first.
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        var result = c.RunBattle(0x1A, c.State.PreviousCard + c.Here, 3, 2, first, 0, 2, second, -1, 0, 0);
        c.AdjustReputation(c.Here, -1, -5);
        var hours = 2000 / c.LocationQuery(c.Here, 2);
        switch (result)
        {
            case 0:
                ShowPicture(c, 3);
                c.Screen.Text(1);
                c.Events.Renew(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, hours, 0, 0);
                c.PassHours(0);
                c.Next = Street(c);
                break;
            case 1:
                c.ReportAway();
                c.Screen.Text(2);
                c.Events.Renew(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, hours, 0, 0);
                c.PassHours(1);
                c.Next = Street(c);
                break;
            case 2:
                c.ReportAway();
                c.Screen.Text(3);
                c.PassHours(1);
                c.Next = Street(c);
                break;
            case 3:
            case 4:
                ShowPicture(c, DungeonPicture);
                c.Screen.Text(4);
                c.PassHours(3);
                c.Next = 0x0D;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the chase on.
                break;
        }
        c.EventHere(0x11, standing <= -75 ? 240 : 120);
    }
}
