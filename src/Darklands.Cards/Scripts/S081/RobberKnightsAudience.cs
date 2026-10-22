using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubi05": the robber knight receives the party in his crude audience chamber. Try a
/// daring move to make him surrender, ask to stay the night (to sneak out later), call a
/// saint, attack, or ask leave to go (a famous party may not be let go).
/// </summary>
// orig: R081+0x0834 (18BB:0004) matched; tests: CardSmokeTests
// orig: R081+0x1074 (18BB:0844) matched; R081+0x10D6 (18BB:08A6) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
public sealed class RobberKnightsAudience : Card
{
    public const int CardNumber = 0x95;

    private const int Picture = 56;
    private const int TheTower = 0x93;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // C: the start-up also calls do_nothing().
        c.RandomName(c.Here + 0x44C, 0, 0);
        c.SetCardItems();
        c.SetCardSaints(0x29, 0x2E, 0x40);
        var menu = Menu(c);
        menu.Enable(0, 1, 3, 4);
        menu[2] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(chance => DaringMove(c, chance), hint: () => DaringChance(c)),
            // orig: R081+0x0D5A (18BB:052A) matched; R081+0x0DA8 (18BB:0578) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                ShowPicture(c, 29);
                c.Screen.Text(4);
                if (c.IsDaylight)
                {
                    c.WaitUntilHour(0);
                }
                c.Next = 0x94;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R081+0x0DE0 (line 2); R081+0x110E (18BB:08DE) matched; R081+0x1198 (18BB:0968) matched; R081+0x1222 (18BB:09F2) matched;
            // tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R081+0x0EFA (18BB:06CA) matched; R081+0x0F04 (18BB:06D4) matched; tests: CardSmokeTests
            new MenuLine(() => Fight(c, 7), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(() =>
            {
                if (!AskLeave(c, LeaveChance(c).Chance))
                {
                    menu[4] = OptionState.Shown;
                }
            }, () => LeaveChance(c)));
    }

    private static void TheTowerFalls(CardContext c)
    {
        c.Events.Create(-2, c.Here, 0x5B, 0x1C, 1, 0, 0, 0x960, 0, 3);
        var tower = c.State.Locations[c.Here];
        tower.Icon = 0x1A;
        tower.Flags |= Core.World.Location.DestroyedFlag;
        tower.ArrivalCard = 0x157;
        tower.EncounterCard = 0x62;
        tower.NoticeRange = 1;
    }

    // orig: R081+0x09D0 (18BB:01A0) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, int kind)
    {
        var here = c.Here;
        // C: arguments are evaluated right to left: rand_mod(2) (last) before rand_mod(3).
        var last = c.Random(2) + 2;
        var eighth = c.Random(3) + 1;
        switch (c.RunBattle(kind, -2, here % 4 != 0 ? 0x23 : 0x26, 3, 1, 0x12, here % 3 + 2, eighth, 3, here % 3 + 1, last))
        {
            case 0:
                ShowPicture(c, 25);
                c.Screen.Text(9);
                c.ResolveQuest(3, c.Here);
                TheTowerFalls(c);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 1:
            case 2:
                c.RejoinAway();
                if (c.BattleTargetSlain)
                {
                    ShowPicture(c, 25);
                    c.Screen.Text(9);
                    c.PassHours(1);
                    // BUG: the quest is not resolved on this path: the knight is dead, but no patron ever pays for the
                    // quest (card oracle: result 1 with the target slain).
                    TheTowerFalls(c);
                    c.Next = 0x0C;
                    return;
                }
                ShowPicture(c, 286);
                c.Screen.Text(0x0A);
                c.PassHours(2);
                c.Next = TheTower;
                break;
            case 3:
            case 4:
                ShowPicture(c, 57);
                c.Screen.Text(0x0B);
                c.PassHours(24);
                c.Injure(Who.Everybody, 0, 1, 20);
                c.LoseItems(Who.Everybody);
                c.Events.Update(-2, c.Here, 0x1C, 2, -1, 3, EventUpdate.Add);
                c.Next = 0x0C;
                break;
            default:
                // DORMANT: other battle results leave the party in the chamber.
                break;
        }
    }

    // orig: R081+0x0CFC (18BB:04CC) matched; tests: CardSmokeTests
    private static OptionHint DaringChance(CardContext c)
    {
        var chance = c.Attribute(c.Leader, 5) / 5 + c.Skill(c.Leader, 0x0A) / 10;
        if (c.Events.Fact(3, 7, c.Here))
        {
            chance += 40;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R081+0x0C40 (18BB:0410) matched; tests: CardSmokeTests
    private static void DaringMove(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 39);
            c.Screen.Text(7);
            c.PassHours(1);
            c.Events.Create(-2, c.Here, 0x5B, 0x1C, 1, 0, 0, 0x960, 0, 3);
            Fight(c, 3);
            return;
        }
        ShowPicture(c, 26);
        c.Screen.Text(8);
        c.PassHours(1);
        Fight(c, 7);
    }

    // orig: R081+0x0DE0 (18BB:05B0) matched; R081+0x0EC2 (18BB:0692) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, 26);
            c.Screen.Text(6);
            ShowPicture(c, Picture);
            c.PassHours(1);
            // The saint's protection, for a day (fact 3/7: the daring move becomes likelier).
            c.Events.Create(-2, c.Here, 0x5B, 0x1C, 7, 0, 0, 0x18, 0, 3);
        }
        else
        {
            ShowPicture(c, 77);
            c.Screen.Text(5);
            c.PassHours(0);
        }
        c.Next = CardNumber;
    }

    // orig: R081+0x102E (18BB:07FE) matched; tests: CardSmokeTests
    private static OptionHint LeaveChance(CardContext c)
    {
        var fame = c.State.Party.Fame;
        var chance = fame > 100 ? fame / -3 + 100 : fame / 4;
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 80), OptionHint.WholeParty);
    }

    /// <summary>Returns false when he refuses (the request is not repeated).</summary>
    // orig: R081+0x0F3C (18BB:070C) matched; tests: CardSmokeTests
    private static bool AskLeave(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 24);
            c.Screen.Text(1);
            c.PassHours(1);
            c.Next = TheTower;
            return true;
        }
        if (c.State.Party.Fame > 300)
        {
            ShowPicture(c, 26);
            c.Screen.Text(2);
            Fight(c, 2);
            return true;
        }
        ShowPicture(c, 24);
        c.Screen.Text(3);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
        return false;
    }
}
