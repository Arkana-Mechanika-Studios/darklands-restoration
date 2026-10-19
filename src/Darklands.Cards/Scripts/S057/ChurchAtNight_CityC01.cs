using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityC01": the city church at night, with only an altar boy about. The party can attend the
/// next Mass (waiting for it), ask the boy when it will be, seek sanctuary, or leave.
/// </summary>
// orig: R057+0x09FE (18D7:000E) matched; tests: CardSmokeTests
// orig: R057+0x0F02 (18D7:0512) matched (init helper: enables all 20 option lines and empties card_items and card_saints; the card uses neither)
// DORMANT: R057+0x0F3A (18D7:054A) the init helper points opt_fn[0..19] at this "screwed up." default; lines 0..3 get their own handlers and lines 4..19 stay enabled but the MSG has only 4 lines, so it never runs.
public sealed class ChurchAtNight_CityC01 : Card
{
    public const int CardNumber = 0x35;

    private const int Picture = 60;
    private const int MassPicture = 272, AltarBoyPicture = 233, ChapelPicture = 155;

    /// <summary>The church by day (CityC00); the night card is this one.</summary>
    private const int ChurchByDay = 0x34, Church = 0x13, Sanctuary = 0x81;

    /// <summary>The hour named when no Mass is held.</summary>
    private const int NoMassHour = 6;

    /// <summary>The word the line's hint leaves where the boy's answer reads its hour in the bands it does not cover.</summary>
    private const int StaleHintWord = -1;

    public override int Number => CardNumber;

    // orig: R057+0x09FE (18D7:000E) matched (entry)
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.StandingSeen = c.Reputation(c.Here);
        // In good standing nobody needs sanctuary.
        var sanctuary = !c.Hostile() && c.StandingSeen > -10 ? OptionState.Shown : OptionState.Enabled;

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        menu[2] = sanctuary;
        c.Next = Number;
        return menu.Run(
            // orig: R057+0x0B6A (18D7:017A) matched (option 0 action, see AttendMass; read from the disassembly); hint R057+0x0CFA "no risk"
            new MenuLine(() => AttendMass(c), OptionHint.Saying(NoRisk)),
            // orig: R057+0x0D32 (18D7:0342) matched (option 1 action, see AskAboutMass; read with the asm); hint R057+0x0E04 (the leader is named)
            new MenuLine(() => AskAboutMass(c), () => LeaderHint(c)),
            // orig: R057+0x0E40 (18D7:0450) matched (option 2: the boy shows the way to the chapel and sanctuary); hint R057+0x0E7C "success is assured"
            new MenuLine(() =>
                {
                    ShowPicture(c, ChapelPicture);
                    c.Screen.Text(4);
                    c.Next = Sanctuary;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R057+0x0EB4 (18D7:04C4) matched (option 3: back to the church, no time passes); hint R057+0x0EC6 (the leader is named)
            new MenuLine(() =>
                {
                    c.PassHours(0);
                    c.Next = Church;
                },
                () => LeaderHint(c)));
    }

    /// <summary>"Success is assured" for the leader, who is also the one named in the text.</summary>
    // orig: R057+0x0E04 (18D7:0414) matched
    // orig: R057+0x0EC6 (18D7:04D6) matched (same body)
    private static OptionHint LeaderHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.Say(SuccessAssured, c.Leader);
    }

    private int ThisCardForTheHour(CardContext c) => c.IsDaylight ? ChurchByDay : CardNumber;

    /// <summary>
    /// Attends the Mass if the city is big enough for one at this time of day (every member then
    /// gains Divine Favor and the party waits for the hour after the next); else the boy names the
    /// hour of the next Mass.
    /// </summary>
    // orig: R057+0x0B6A (18D7:017A) matched (hand-built from the asm)
    // C: the original switches on the time-of-day band numbered from one (0 for 21-23).
    private void AttendMass(CardContext c)
    {
        var size = c.HereQuery(2);
        var sitsThroughMass = false;
        var nextMassHour = NoMassHour;
        switch (TimeOfDay.BandFromOne(c.State.Date.Hour))
        {
            case 0 or 1:
                if (size >= 7)
                {
                    sitsThroughMass = true;
                }
                else
                {
                    nextMassHour = NoMassHour;
                }
                break;
            case 2:
                sitsThroughMass = true;
                break;
            case 3:
                if (size >= 5)
                {
                    sitsThroughMass = true;
                }
                else
                {
                    nextMassHour = size > 3 ? 0x12 : NoMassHour;
                }
                break;
            case 6:
                if (size >= 4)
                {
                    sitsThroughMass = true;
                }
                else
                {
                    nextMassHour = NoMassHour;
                }
                break;
            case 7:
                if (size >= 6)
                {
                    sitsThroughMass = true;
                }
                else
                {
                    nextMassHour = NoMassHour;
                }
                break;
            default:
                nextMassHour = NoMassHour;
                break;
        }
        if (sitsThroughMass)
        {
            for (var member = 0; member < c.PartySize; member++)
            {
                c.AddAttribute(member, 6, c.Skill(member, 8) / 60 + c.Skill(member, 0x0B) / 40 + 1);
            }
            c.WaitUntilHour((TimeOfDay.BandFromOne(c.State.Date.Hour) + 1) * 3);
            ShowPicture(c, MassPicture);
            c.Screen.Text(2);
            ShowPicture(c, Picture);
            c.Next = ThisCardForTheHour(c);
            return;
        }
        c.PassHours(0);
        c.NameToSlot(3, nextMassHour, 0);
        ShowPicture(c, AltarBoyPicture);
        c.Screen.Text(1);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    /// <summary>The altar boy names the hour of the next Mass, which depends on the size of the city and the time of day.</summary>
    // orig: R057+0x0D32 (18D7:0342) matched (hand-built from the asm)
    private void AskAboutMass(CardContext c)
    {
        var size = c.HereQuery(2);
        // BUG: bands 4 and 5 (R057+0x0D6A) assign nothing and name the uninitialised local [bp-2] as the hour. The card's
        // loop (R057+0x0B0A) runs the line's hint R057+0x0E04 just before this action at the same stack depth, and the
        // hint's local [bp-4] = -1 lies there: hour -1, whose bell is Matins (fix flag FixChurchAltarBoyStaleHour: the no-Mass hour).
        var hour = c.State.Behaviour.FixChurchAltarBoyStaleHour ? NoMassHour : StaleHintWord;
        switch (TimeOfDay.BandFromOne(c.State.Date.Hour))
        {
            case 0:
                hour = size >= 7 ? 0 : NoMassHour;
                break;
            case 1:
                hour = size >= 7 ? 0 : NoMassHour;
                break;
            case 2:
                hour = NoMassHour;
                break;
            case 3:
                hour = size >= 5 ? 9 : size == 4 ? 0x12 : NoMassHour;
                break;
            case 6:
                hour = size >= 4 ? 0x12 : NoMassHour;
                break;
            case 7:
                hour = size >= 6 ? 0x15 : NoMassHour;
                break;
        }
        c.NameToSlot(3, hour, 0);
        ShowPicture(c, AltarBoyPicture);
        c.Screen.Text(3);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }
}
