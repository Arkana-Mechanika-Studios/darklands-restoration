using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Cathe01": a cathedral at night. The party can attend the next Mass (waiting for it), talk
/// to a priest about the cathedral's saints and relics, donate one of its relics, ask for
/// sanctuary, leave, or, after finishing the bishop's task, ask for the promised relic.
/// </summary>
// orig: R056+0x2838 (1ABB:0008) matched; tests: CardSmokeTests
// orig: R056+0x3034 (1ABB:0804) matched (init helper: enables all 20 option lines and empties card_items and card_saints; the card uses neither)
// DORMANT: R056+0x306C (1ABB:083C) the init helper points opt_fn[0..19] at this "screwed up." default; lines 0..5 get their own handlers, lines 6..9 are cleared by the entry and lines 10..19 stay enabled but the MSG has only 6 lines, so it never runs.
public sealed class CathedralAtNight_Cathe01 : Card
{
    public const int CardNumber = 0x33;

    private const int Picture = 17;
    private const int MassPicture = 272, PriestPicture = 155, NoRelicTalkPicture = 299, PrelatePicture = 271, BigCathedralTalkPicture = 269,
        BishopPicture = 243;

    /// <summary>The cathedral by day (Cathe00); the night card is this one.</summary>
    private const int CathedralByDay = 0x32, Church = 0x13, Sanctuary = 0x81;

    /// <summary>The item flag of a relic.</summary>
    private const uint RelicFlag = 0x1000;

    /// <summary>The quest tag of the bishop's task and the owner it is resolved for.</summary>
    private const int BishopsTask = 0x0A, BishopsTaskOwner = 3;

    private const int RelicOption = 2;

    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    // orig: R056+0x2838 (1ABB:0008) matched (entry)
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var relic = OptionState.Enabled;
        var sanctuary = OptionState.Enabled;
        var reward = OptionState.Hidden;
        c.StandingSeen = c.Reputation(c.Here);
        // Without a relic there is nothing to donate.
        if (c.CountItemsWithFlags(-2, RelicFlag) == 0)
        {
            relic = OptionState.Shown;
        }
        // In good standing nobody needs the cathedral's protection.
        if (!c.Hostile() && c.StandingSeen > -10)
        {
            sanctuary = OptionState.Shown;
        }
        if (c.Events.ResolvedQuestAt(BishopsTask, BishopsTaskOwner, c.Here))
        {
            reward = OptionState.Enabled;
        }

        _menu = Menu(c, 0);
        _menu.Enable(0, 1, 2, 3, 4, 5);
        _menu[2] = relic;
        _menu[3] = sanctuary;
        _menu[5] = reward;
        c.Next = Number;
        return _menu.Run(
            // orig: R056+0x2A42 (1ABB:0212) matched (option 0 action, see AttendMass; read from the disassembly); hint R056+0x2BD0 "success is assured"
            new MenuLine(() => AttendMass(c), OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2C08 (1ABB:03D8) unmatched (option 1 action, see TalkToAPriest; checked against asm); hint R056+0x2DC0 "success is assured"
            new MenuLine(() => TalkToAPriest(c), OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2DF8 (1ABB:05C8) matched (option 2 action, see DonateARelic); hint R056+0x2EC8 "success is assured"
            new MenuLine(() => DonateARelic(c),
                // the handler leaves its member word unset (r.b is stale); it is shown for the whole party.
                OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2EFA (1ABB:06CA) matched (option 3: sanctuary, no time passes); hint R056+0x2F0C "success is assured"
            new MenuLine(() => { c.PassHours(0); c.Next = Sanctuary; }, OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2F44 (1ABB:0714) matched (option 4: back to the church, no time passes); hint R056+0x2F56 "success is assured"
            new MenuLine(() => { c.PassHours(0); c.Next = Church; }, OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2F8E (1ABB:075E) matched (option 5 action, see AskForTheReward); hint R056+0x2FEE (see RewardHint)
            new MenuLine(() => AskForTheReward(c), () => RewardHint(c)));
    }

    private int ThisCardForTheHour(CardContext c) => c.IsDaylight ? CathedralByDay : CardNumber;

    /// <summary>
    /// Attends the next Mass: when the cathedral is big enough for the hour the party sits through
    /// the service (and every member gains Divine Favor), else the priest names the hour of the
    /// next Mass and the party stays.
    /// </summary>
    // orig: R056+0x2A42 (1ABB:0212) matched (the tool could not decompile it; the C is hand-built and checked against the asm)
    // C: the original switches on the time-of-day band numbered from one (0 for 21-23); bands 2, 4 and 5 and anything above 7 have no Mass.
    private void AttendMass(CardContext c)
    {
        var sitsThroughMass = false;
        var size = c.HereQuery(2);
        var nextMassHour = 6;
        var band = TimeOfDay.BandFromOne(c.State.Date.Hour);
        switch (band)
        {
            case 0 or 1:
                if (size >= 8)
                {
                    nextMassHour = 1;
                }
                break;
            case 3:
                if (size >= nextMassHour)
                {
                    nextMassHour = 1;
                }
                else if (size > 4)
                {
                    nextMassHour = 0x12;
                }
                break;
            case 6:
                if (size >= 5)
                {
                    nextMassHour = 1;
                }
                break;
            case 7:
                if (size >= 7)
                {
                    sitsThroughMass = true;
                }
                else
                {
                    nextMassHour = 6;
                }
                break;
            default:
                nextMassHour = 6;
                break;
        }
        if (sitsThroughMass)
        {
            for (var member = 0; member < c.PartySize; member++)
            {
                c.AddAttribute(member, 6, c.Skill(member, 0x0B) / 0x28 + c.Skill(member, 8) / 0x3C + 1);
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
        ShowPicture(c, PriestPicture);
        c.Screen.Text(1);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    /// <summary>
    /// A talk with a priest about the cathedral: which relic it keeps (location datum 0x25, 0 none)
    /// and which saint it is dedicated to (datum 0x26, 0 none) decide the text and picture.
    /// </summary>
    // orig: R056+0x2C08 (1ABB:03D8) unmatched (the compiler keeps cmp dx,1 where the C gives dec dx; behaviour read from the asm)
    private void TalkToAPriest(CardContext c)
    {
        var relic = c.HereQuery(0x25);
        c.Variables.Numbers[0] = relic;
        if (relic != 0)
        {
            c.NameToSlot(0, relic, 0);
        }
        var saint = c.HereQuery(0x26);
        if (saint == 0)
        {
            if (relic == 0)
            {
                ShowPicture(c, NoRelicTalkPicture);
                c.Screen.Text(3);
            }
            else if (relic == 1)
            {
                ShowPicture(c, PrelatePicture);
                c.Screen.Text(4);
            }
            else
            {
                ShowPicture(c, BigCathedralTalkPicture);
                c.Screen.Text(5);
            }
        }
        else
        {
            c.NameToSlot(1, saint, 1);
            if (relic == 0)
            {
                ShowPicture(c, NoRelicTalkPicture);
                c.Screen.Text(6);
            }
            else if (relic == 1)
            {
                ShowPicture(c, PrelatePicture);
                c.Screen.Text(7);
            }
            else
            {
                ShowPicture(c, PrelatePicture);
                c.Screen.Text(8);
            }
        }
        c.PassHours(1);
        ShowPicture(c, Picture);
        c.Next = ThisCardForTheHour(c);
    }

    /// <summary>The party gives away a relic: standing rises by 30, everybody's Divine Favor is lifted to the top, and the line closes with the last relic.</summary>
    // orig: R056+0x2DF8 (1ABB:05C8) matched
    private void DonateARelic(CardContext c)
    {
        c.AdjustReputation(c.Here, 30, 30);
        c.AddAttribute(-2, 6, 99);
        c.Variables.Chosen[0] = c.Leader;
        c.NameToSlot(0, c.FirstItemWithFlags(-2, RelicFlag), 0);
        c.TakeItemWithFlags(-2, RelicFlag);
        _menu[RelicOption] = OptionState.Shown;
        if (c.CountItemsWithFlags(-2, RelicFlag) != 0)
        {
            _menu[RelicOption] = OptionState.Enabled;
        }
        ShowPicture(c, PrelatePicture);
        c.Screen.Text(0x0A);
        c.PassHours(3);
        ShowPicture(c, Picture);
        c.Next = ThisCardForTheHour(c);
    }

    /// <summary>The bishop's reward: in good standing "success is assured", else a sure failure.</summary>
    // orig: R056+0x2FEE (1ABB:07BE) matched
    private static OptionHint RewardHint(CardContext c) =>
        c.StandingSeen > -10 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(0, OptionHint.WholeParty);

    // orig: R056+0x2F8E (1ABB:075E) matched
    // C: the action ignores the chance the hint showed and always plays the bishop's refusal.
    private void AskForTheReward(CardContext c)
    {
        ShowPicture(c, BishopPicture);
        c.Screen.Text(0x0B);
        c.PassHours(0);
        ShowPicture(c, Picture);
        c.Next = ThisCardForTheHour(c);
    }
}
