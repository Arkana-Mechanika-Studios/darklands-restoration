using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubi04": sneaking through the dark corridors of the robber knight's tower. Find him and
/// make him surrender, set the tower on fire and slip out, fight through, or climb out of a
/// window. When the party stormed in (fact 3/6), his men fall on it at once.
/// </summary>
// orig: R081+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R081+0x07C4 (1838:07C4) matched; R081+0x07FC (1838:07FC) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class InsideTheRobberKnightsTower : Card
{
    public const int CardNumber = 0x94;

    private const int Picture = 29;
    private const int TheTower = 0x93;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        if (c.Events.Fact(3, 6, c.Here))
        {
            ShowPicture(c, 260);
            c.Screen.Text(0x0A);
            Fight(c, 5);
        }
        // C: the start-up also calls do_nothing(); the knight's name is drawn after that fight.
        c.RandomName(c.Here + 0x44C, 0, 0);
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3);
        return menu.Run(
            new MenuLine(() => FindTheKnight(c), hint: () => SurrenderHint(c)),
            new MenuLine(() => SetTheTowerOnFire(c, FireChance(c).Chance), hint: () => FireChance(c)),
            // orig: R081+0x0738 (1838:0738) matched; R081+0x0742 (1838:0742) matched; tests: CardSmokeTests
            new MenuLine(() => Fight(c, 1), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R081+0x077A (1838:077A) matched; R081+0x078C (1838:078C) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(2); c.Next = TheTower; }, OptionHint.Saying(NoRisk)));
    }

    /// <summary>The knight is dead: the tower becomes a ruin (resolving the quest only in a clean win).</summary>
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

    // orig: R081+0x01B8 (1838:01B8) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, int kind)
    {
        var here = c.Here;
        // C: arguments are evaluated right to left: rand_mod(2) (last) before rand_mod(3).
        var last = c.Random(2) + 2;
        var eighth = c.Random(3) + 1;
        var result = c.RunBattle(kind, -2, here % 4 != 0 ? 0x23 : 0x26, 3, 1, 0x12, here % 3 + 2, eighth, 3, here % 3 + 1, last);
        switch (result)
        {
            case 0:
                ShowPicture(c, 38);
                c.Screen.Text(7);
                c.ResolveQuest(3, c.Here);
                TheTowerFalls(c);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                ShowPicture(c, 177);
                c.Screen.Text(9);
                ShowPicture(c, Picture);
                c.PassHours(24);
                c.Injure(Who.Everybody, 0, 1, 20);
                c.LoseItems(Who.Everybody);
                c.Events.Update(-2, c.Here, 0x1C, 2, -1, 3, EventUpdate.Add);
                c.Next = 0x0C;
                break;
            default:
                // Fled (1, 2) or anything else: the knight is dead only if he fell, or in the
                // lieutenant's fight (kind 3).
                c.ReportAway();
                if (!c.BattleTargetSlain && kind != 3)
                {
                    ShowPicture(c, 57);
                    c.Screen.Text(8);
                    c.PassHours(2);
                    c.Next = TheTower;
                    return;
                }
                ShowPicture(c, 38);
                c.Screen.Text(7);
                c.PassHours(1);
                // C: here the quest is not resolved (ev8_resolve is only called after a won battle).
                TheTowerFalls(c);
                c.Next = 0x0C;
                break;
        }
    }

    // orig: R081+0x054E (1838:054E) matched; tests: CardSmokeTests
    // C: the hint multiplies the persuasion by the best stealth / 100; the action rolls them separately.
    private static OptionHint SurrenderHint(CardContext c)
    {
        var stealth = c.PartyMaxSkill(0x0F);
        var persuasion = c.Skill(Who.Leader, 0x0A) / 10 + c.Attribute(c.Leader, 5) / 5;
        if (c.Events.Fact(3, 7, c.Here))
        {
            persuasion += 50;
        }
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.ChanceOf(Math.Clamp(persuasion * stealth / 100, 1, 99), c.Leader);
    }

    /// <summary>Unseen, the party reaches him (stealth); a persuasive leader makes his lieutenant turn on him.</summary>
    // orig: R081+0x0438 (1838:0438) matched; tests: CardSmokeTests
    private static void FindTheKnight(CardContext c)
    {
        _ = SurrenderHint(c);
        if (c.Random(100) <= c.PartyMaxSkill(0x0F))
        {
            ShowPicture(c, 30);
            c.Screen.Text(4);
            Fight(c, 2);
            return;
        }
        var persuasion = c.Skill(c.Leader, 0x0A) / 10 + c.Attribute(c.Leader, 5) / 5;
        if (c.Random(100) <= persuasion)
        {
            ShowPicture(c, 356);
            c.Screen.Text(5);
            Fight(c, 3);
            return;
        }
        ShowPicture(c, 108);
        c.Screen.Text(6);
        Fight(c, 0x7A);
    }

    // orig: R081+0x0716 (1838:0716) matched; tests: CardSmokeTests
    private static OptionHint FireChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(0x0E);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    // orig: R081+0x05D0 (1838:05D0) matched; tests: CardSmokeTests
    private static void SetTheTowerOnFire(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            c.PracticeSkill(c.Leader, 0x0E, 1, 10, 0);
            c.Events.Update(-2, c.Here, 0x1C, 4, 3, 3, EventUpdate.Add);
            if (c.Random(8) <= c.Events.FactValue(3, 4, c.Here))
            {
                ShowPicture(c, 31);
                c.Screen.Text(1);
                // C: the battle's next card is overwritten below: the party is back before the tower.
                Fight(c, 0x75);
            }
            else
            {
                ShowPicture(c, 31);
                c.Screen.Text(2);
                c.Events.Update(-2, c.Here, 0x1C, 2, 1, 3, EventUpdate.Add);
            }
        }
        else
        {
            ShowPicture(c, TowerPicture);
            c.Screen.Text(3);
            c.PassHours(1);
        }
        c.Next = TheTower;
    }

    private const int TowerPicture = 21;
}
