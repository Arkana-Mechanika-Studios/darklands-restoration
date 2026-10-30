using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityW02": the guards on a city wall are out of action and the party gets over the wall:
/// the most agile member climbs up with a rope and helps the others, or everybody climbs
/// alone (the weakest climber may fall and be hurt). Success leads into the city, a fall
/// back out to the wilderness cards.
/// </summary>
// orig: R068+0x1C14 (19F9:0004) matched; tests: CardSmokeTests
// orig: handler setup R068+0x2422 (empty: nothing installed for pop-ups, so the item and
// saint lists and pop-up handlers of the card before stay as they were); unused-line handler R068+0x2424 ("screwed up.")
// C: the card sets the window globals DS:EE72 = 7 and DS:EE70 = 0; they are not modelled (nothing reconstructed reads them).
// C: the card does not clear card-item-lines/card-saint-lines or install pop-up handlers (stale from the previous card); its text has no item or
// saint line and it never asks for usable items or saints, so those stale tables are never consulted: nothing to reconstruct.
public sealed class ClimbingTheWallPastTheGuards : Card
{
    private const int MainPicture = 280;
    private const int WallPicture = 65;
    private const int FallPicture = 66;
    private const int RopeItem = 0x3B;
    private const int Stealth = (int)Skill.Stealth;

    /// <summary>DS:5DF8: the best climber so far; a module global that keeps its value between visits (0 at the start).</summary>
    private const int BestClimberWord = 0x5DF8;

    /// <summary>DS:5DFA: that climber's score (0 at the start).</summary>
    private const int BestScoreWord = 0x5DFA;

    /// <summary>The wilderness cards after a fall: by day, at night.</summary>
    private static int Outside(CardContext c) => c.IsDaylight ? 0x0E : 0x0F;

    private static int Inside(CardContext c) => c.IsDaylight ? 9 : 0x0A;

    public override int Number => 0x63;

    public override int Run(CardContext c)
    {
        Show(c, MainPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        // C: local-in-di is overwritten for every member, so only the last member's rope counts (1 without members).
        // BUG: the rope line is greyed unless the last member carries a rope, whoever else has one.
        var rope = true;
        for (var who = 0; who < c.PartySize; who++)
        {
            rope = c.CountItems(who, RopeItem) >= 1;
        }
        if (!rope)
        {
            menu[0] = OptionState.Shown;
        }
        if (!c.PartyAllWell())
        {
            menu[1] = OptionState.Shown;
        }
        // The best climber: the highest score ever seen (the words are not reset between visits, so a
        // later visit only replaces it by a strictly higher score).
        for (var who = 0; who < c.PartySize; who++)
        {
            if (c.Status(who) == CharacterStatus.Able)
            {
                var score = ClimbingScore(c, who);
                if (c.DataWord(BestScoreWord) < score)
                {
                    c.Variables.Chosen[0] = who;
                    c.SetDataWord(BestClimberWord, who);
                    c.SetDataWord(BestScoreWord, score);
                }
            }
        }

        return menu.Run(
            new MenuLine(chance => ClimbWithARope(c, chance), () => BestClimberHint(c)),
            new MenuLine(chance => ClimbAlone(c, chance), () => WeakestClimberHint(c)),
            // The potion line: "using that potion was a waste". orig: R068+0x21DA (hint), 05ac (action) R068+0x21BC
            new MenuLine(_ => { c.PassHours(0); c.Next = Outside(c); }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 3..9 (the text has only three option lines): time passes and the card moves to the
            // wilderness card 0x62. orig: actions R068+0x2212/064c/0698/06e2/072e/077a/07c6, hints 0614/065e/06aa/06f4/0740/078c/07d8
            new MenuLine(_ => LeaveForTheWilderness(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveForTheWilderness(c), () => new OptionHint(-1, SuccessAssured, c.Leader)),
            new MenuLine(_ => LeaveForTheWilderness(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => LeaveForTheWilderness(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => LeaveForTheWilderness(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => LeaveForTheWilderness(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => LeaveForTheWilderness(c), () => new OptionHint(100, SuccessAssured, c.Leader)));
    }

    /// <summary>(agility scaled by fatigue × 2 + stealth) / 2.</summary>
    // orig: the expression in R068+0x1C14, 03c2 and 054a
    private static int ClimbingScore(CardContext c, int who) => (c.EffectiveAgility(who) * 2 + c.Skill(who, Stealth)) / 2;

    private static void LeaveForTheWilderness(CardContext c)
    {
        c.PassHours(0);
        c.Next = 0x62;
    }

    /// <summary>The best climber (of this and earlier visits) goes up the wall with the rope.</summary>
    // orig: R068+0x1FBC
    private static OptionHint BestClimberHint(CardContext c)
    {
        var climber = c.DataWord(BestClimberWord);
        c.Variables.Chosen[0] = climber;
        return new OptionHint(c.DataWord(BestScoreWord), "", climber);
    }

    // orig: R068+0x1EC6
    private static void ClimbWithARope(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            ShowPicture(c, WallPicture);
            c.Screen.Text(1);
            c.PracticeSkill(c.Variables.Chosen[0], Stealth, 7, 10, 0);
            c.PassHours(1);
            c.Next = Inside(c);
            return;
        }
        ShowPicture(c, FallPicture);
        c.Screen.Text(2);
        ShowPicture(c, MainPicture);
        c.Injure(c.Variables.Chosen[0], 1, 1, 10);
        c.PracticeSkill(c.Variables.Chosen[0], Stealth, 0, 10, 0);
        c.PassHours(1);
        c.Next = Outside(c);
    }

    /// <summary>The weakest able climber sets the chance that everybody gets up.</summary>
    // orig: R068+0x215A
    private static OptionHint WeakestClimberHint(CardContext c)
    {
        var lowest = 999;
        for (var who = 0; who < c.PartySize; who++)
        {
            if (c.Status(who) == CharacterStatus.Able)
            {
                var score = ClimbingScore(c, who);
                if (score < lowest)
                {
                    c.Variables.Chosen[0] = who;
                    lowest = score;
                }
            }
        }
        return new OptionHint(lowest, "", OptionHint.WholeParty);
    }

    /// <summary>Everybody climbs alone: one roll; every able member weaker than the roll falls, the others watch.</summary>
    // orig: R068+0x1FD2
    private static void ClimbAlone(CardContext c, int chance)
    {
        var roll = c.Random(100);
        if (roll < chance)
        {
            c.Screen.Text(3);
            c.PracticeSkill(Who.Everybody, Stealth, 7, 10, 0);
            c.PassHours(1);
            c.Next = Inside(c);
            return;
        }
        var chosen = c.Variables.Chosen[0];
        for (var who = 0; who < c.PartySize; who++)
        {
            if (c.Status(who) != CharacterStatus.Able)
            {
                continue;
            }
            c.Variables.Chosen[0] = who;
            if (ClimbingScore(c, who) <= roll)
            {
                ShowPicture(c, WallPicture);
                c.Screen.Text(4);
                ShowPicture(c, MainPicture);
                c.Injure(who, 1, 1, 10);
                c.PracticeSkill(who, Stealth, 0, 5, 0);
            }
            else
            {
                ShowPicture(c, WallPicture);
                c.Screen.Text(5);
                ShowPicture(c, MainPicture);
                c.PracticeSkill(who, Stealth, 7, 3, 0);
            }
        }
        c.Variables.Chosen[0] = chosen;
        c.PassHours(1);
        c.Next = Outside(c);
    }
}
