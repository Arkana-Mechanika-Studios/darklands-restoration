using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityE00" (card 0x73): before the walls of a city the party may not simply enter. Wait
/// for dawn or for night and try the main gate, examine the walls, or climb them (which may
/// bring the guard down on the party); or go back to the map.
/// </summary>
// orig: R072+0x1494 (1981:0004) matched; tests: CardSmokeTests
// orig: handler setup R072+0x1A0E is empty (unused-line handler 0580 "screwed up." is never installed)
public sealed class BeforeTheCityWalls : Card
{
    private const int MainGate = 0x78;

    public override int Number => 0x73;

    public override int Run(CardContext c)
    {
        c.PlaySound(6);
        // C: % keeps the sign; a negative remainder chooses no picture and pic_buf keeps the last one.
        var picture = (c.HereQuery(0x21) % 3) switch
        {
            0 => 4,
            1 => 6,
            2 => 7,
            _ => -1,
        };
        // DIVERGENCE: the original only fills the name and picture buffers (no card_load): the screen
        // keeps the previous card ($CityE00, card 4, when the party comes from the city entry) until
        // the engine redraws it from these buffers. It is shown here at once.
        // C: a negative remainder leaves pic_buf as the last card left it, and the engine draws the card's texts with it.
        if (picture >= 0)
        {
            Show(c, picture);
        }
        else
        {
            c.Screen.ShowWithTheLastPicture(Name);
        }
        c.Next = Number;
        // C: only opt_on[0..4] are copied each round; lines 5..9 keep the previous card's opt_on and
        // opt_fn (and the switch has no case for them), so they are offered hidden here.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        // C: cur_rep is read (loc_reputation) but not used by this card.
        _ = c.Reputation(c.Here);
        if (c.EventAtHere(0x1E))
        {
            menu[3] = OptionState.Shown;
        }

        return menu.Run(
            new MenuLine(_ => GateByDay(c), () => OptionHint.Say(c.IsDaylight ? NoRisk : CardTexts.SleepUntilDawn)),
            new MenuLine(_ => GateByNight(c), () => OptionHint.Say(c.IsDaylight ? CardTexts.WaitForNight : NoRisk)),
            new MenuLine(_ => ExamineTheWalls(c), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(chance => ClimbTheWall(c, menu, chance), () => ClimbChance(c)),
            new MenuLine(_ => { c.PassHours(0); c.Next = 0x0C; }, OptionHint.Saying(NoRisk)));
    }

    /// <summary>Wait for dawn (at night) and go to the main gate.</summary>
    // orig: R072+0x165E (hint R072+0x16B0)
    private static void GateByDay(CardContext c)
    {
        if (!c.IsDaylight)
        {
            ShowPicture(c, 6);
            c.Screen.Text(1);
            c.WaitUntilHour(5);
        }
        c.Next = MainGate;
    }

    /// <summary>Wait for night (by day) and go to the main gate.</summary>
    // orig: R072+0x16F6 (hint R072+0x1726)
    private static void GateByNight(CardContext c)
    {
        if (c.IsDaylight)
        {
            c.Screen.Text(2, TextFrame.BlankPage);
            c.WaitUntilHour(19);
        }
        else
        {
            c.PassHours(0);
        }
        c.Next = MainGate;
    }

    // orig: R072+0x176C (hint R072+0x179E)
    private static void ExamineTheWalls(CardContext c)
    {
        if (!c.IsDaylight)
        {
            // DIVERGENCE: $CityE00.msg has no block 9; the original prints what follows the last block (bytes 07 02 0A under the oracle), the C# nothing.
            c.Screen.Text(9, TextFrame.BlankPage);
            c.WaitUntilHour(5);
        }
        c.Screen.Text(3, TextFrame.BlankPage);
        c.Next = 0x76;
    }

    // orig: R072+0x19A2
    private static OptionHint ClimbChance(CardContext c)
    {
        var chance = c.PartyMinSkill((int)Skill.Stealth);
        c.Variables.Chosen[0] = c.BestMember;
        return new OptionHint(chance, "", c.BestMember);
    }

    /// <summary>Climb the wall in the dark (waiting for 3 o'clock by day); seen, the guard attacks.</summary>
    // orig: R072+0x17D6
    private void ClimbTheWall(CardContext c, CardMenu menu, int chance)
    {
        if (c.IsDaylight)
        {
            c.Screen.Text(4, TextFrame.BlankPage);
            c.WaitUntilHour(3);
        }
        if (c.Succeeds(chance))
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            c.Next = 0x77;
            return;
        }
        c.Screen.Text(5, TextFrame.BlankPage);
        c.EventHere(0x1E, 12);
        c.ExtendEventHere(Core.Events.EventKinds.Unrest, 24);
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, 0);
        menu[3] = OptionState.Shown;
        // C: loc_query(cur_loc, 2) is called and its result dropped.
        _ = c.LocationQuery(c.Here, 2);
        // C: arguments right to left: the officers' quality (rand_mod(3) + 1) is drawn first, then the
        // guards' number (rand_mod(5)), then their quality (rand_mod(3)).
        var officerQuality = c.Random(3) + 1;
        var guards = c.Random(5) + 4;
        var guardQuality = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x53, c.Here + 0x6F, 3, guardQuality, guards, 0, officerQuality, 1, -1, 3, 0))
        {
            case 0:
            case 1:
            case 2:
                c.ReportAway();
                c.Screen.Text(6, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = Number;
                return;
            case 3:
                // DIVERGENCE: $CityE00.msg has 7 blocks, so blocks 7 (result 3) and 8 (result 4) do not exist: msg_load (R043+0x020E) sets
                //   msg_index.ofs[0..6] only and card_text prints msg_file + the stale ofs[7] / ofs[8] a message file with more blocks left
                //   there earlier in the session (0 after start-up: the file's header bytes, 07 02 0A under the oracle); not game state,
                //   the C# prints nothing.
                c.Screen.Text(7, TextFrame.BlankPage);
                Disgrace(c);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                return;
            case 4:
                c.Screen.Text(8, TextFrame.BlankPage);
                Disgrace(c);
                c.PassHours(3);
                c.Next = 0x0D;
                return;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results (the original lists −64..−1 and a default) change nothing.
                return;
        }
    }

    /// <summary>The city's opinion falls by 40, unless it is already at −40 or below.</summary>
    // BUG: at −40 or below the original draws improve_roll(v, −3, −9) and drops the result, so the
    // standing stays as it is (the draw is still made).
    private static void Disgrace(CardContext c)
    {
        var standing = c.LocationQuery(c.Here, 0);
        if (standing <= -40)
        {
            _ = c.ImproveRoll(standing, -3, -9);
        }
        else
        {
            standing -= 40;
        }
        Core.Rules.LocationRules.SetReputation(c.State, c.Here, standing);
    }
}
