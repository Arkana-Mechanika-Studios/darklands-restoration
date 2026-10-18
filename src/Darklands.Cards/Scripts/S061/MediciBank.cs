using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Medic00": the office of the Medici. Letters of credit, commissions and their rewards,
/// as at the Fuggers (see <see cref="BankingHouses"/>), with the Medici's own manners.
/// </summary>
// orig: R061+0x0978 (18CF:0008) matched; tests: CardSmokeTests
// orig: handler setup R061+0x150E (opt_on[*]=1, screwed-up handler 0c40, item handlers 0c78/0cb4/0cf0/0d2c,
// saint handlers 0d68/0df2/0e7c/0f06: standard pop-ups, no items or saints listed)
public sealed class MediciBank : Card
{
    private const int House = 6;
    private const int OfficePicture = 356;
    private const int PursePicture = 194;

    public override int Number => 0x45;

    public override int Run(CardContext c)
    {
        var memory = c.Memory;
        Show(c, OfficePicture);
        // C: cur_rep = loc_query(cur_loc, 0) is read once; options_block = 0 or 2 is chosen once, from cur_rep and DS:4BB8
        // (the Medici favour) as they stand now, and not refreshed after a failed report lowers the favour.
        var standing = c.LocationQuery(c.Here, 0);
        // C: opt_flag_0,1,2,6,7 = 1; opt_flag_3,4,5,8,9 = 0 (lines 4, 5, 8, 9 stay hidden: dormant below).
        var menu = Menu(c, standing >= 0 && c.State.MediciStanding >= 0 ? 0 : 2);
        menu.Enable(0, 1, 2, 6, 7);
        c.Next = Number;
        // C: DS:EE72 = 7 and DS:EE70 = 0 (text window state) are set here; not modelled.
        c.Variables.Chosen[0] = c.Leader;
        c.RandomName(c.City.Ordinal + House, 0, 0);
        if (c.State.Party.BankNotes.Florins > 0)
        {
            c.Variables.Money[0] = Money.From(c.State.Party.BankNotes.Florins, 0, 0);
        }
        else
        {
            menu[0] = OptionState.Hidden;
        }
        if (c.State.Party.Purse.Florins <= 1)
        {
            menu[1] = OptionState.Shown;
        }
        // C: ev8_owner_value(10, 6) || ev8_owner_value(3, 6) || event_type_at(7, 6) || cur_loc_query(0) < 0 || DS:4BB8 < 0.
        // Unlike the Fuggers the Medici office does not raise a favour of 0 first.
        if (BankingHouses.CommissionPending(c, House) || c.HereQuery(0) < 0 || c.State.MediciStanding < 0)
        {
            menu[2] = OptionState.Hidden;
        }
        // C: ev24_at_owner(10, 6) || ev24_at_owner(3, 6): a finished commission lets the party report it.
        if (BankingHouses.CommissionDone(c, House))
        {
            menu[2] = OptionState.Hidden;
            menu[3] = OptionState.Enabled;
        }

        menu.EachRound = () =>
        {
            // C: tested at the top of every round, with the card's copy of the reputation and the current favour.
            if (standing + c.State.MediciStanding <= -100)
            {
                // Thrown out by the guards.
                ShowPicture(c, 236);
                c.Screen.Text(5);
                c.Next = 0x15;
            }
        };
        // C: var_chosen1 = leader after every choice, before the handler runs.
        menu.OnChoice = () => c.Variables.Chosen[0] = c.Leader;
        return menu.Run(
            // orig: handler R061+0x0D6C (assured, 100%, leader)
            new MenuLine(_ => RedeemLetters(c, menu), () => new OptionHint(100, SuccessAssured, c.Leader)),
            // orig: handler R061+0x0E76 (assured)
            new MenuLine(_ => BuyALetter(c, menu), OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x1024: clamp(10, 50, party_get(4) + favour + cur_rep)
            new MenuLine(chance => AskForACommission(c, menu, chance),
                () => new OptionHint(Math.Clamp(c.PartyGet(4) + c.State.MediciStanding + standing, 10, 50), "", c.Leader)),
            // orig: handler R061+0x12D2 (assured)
            new MenuLine(_ => ReportTheCommission(c, menu), OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 4 and 5 (opt_flag_4 = opt_flag_5 = 0, set nowhere): show the first text and leave for the city entry card.
            // orig: handler R061+0x1328, action 099a R061+0x130A
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x137E, action 09f0 R061+0x1360
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x13D4, action 0a46 R061+0x13B6
            new MenuLine(_ => { c.PassHours(0); c.Next = c.IsDaylight ? 0x15 : 0x16; }, OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x142A, action 0a9c R061+0x140C
            new MenuLine(_ => { c.PassHours(0); c.Next = c.IsDaylight ? 9 : 10; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 8 and 9 (opt_flag_8 = opt_flag_9 = 0, set nowhere): the same "leave" as lines 4 and 5.
            // orig: handler R061+0x1480, action 0af2 R061+0x1462
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x14D6, action 0b48 R061+0x14B8
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)));
    }

    // orig: R061+0x0CE8 (18CF:0378); card_text(6, 2), and the main picture is not restored afterwards
    private void RedeemLetters(CardContext c, CardMenu menu)
    {
        BankingHouses.RedeemLetters(c);
        menu[0] = OptionState.Hidden;
        ShowPicture(c, PursePicture);
        c.Screen.Text(6);
        c.Next = Number;
    }

    // orig: R061+0x0DA6 (18CF:0436); card_text(3, 2)
    private void BuyALetter(CardContext c, CardMenu menu)
    {
        ShowPicture(c, 190);
        c.Screen.Text(3);
        ShowPicture(c, OfficePicture);
        if (BankingHouses.DepositFlorins(c))
        {
            menu[0] = OptionState.Enabled;
        }
        c.Next = Number;
    }

    // orig: R061+0x0EAE (18CF:053E)
    private void AskForACommission(CardContext c, CardMenu menu, int chance)
    {
        // C: opt_flag_2 = 2 (shown, greyed) is written before the roll; the first roll is rand_mod(100) <= chance, the
        // second (inside GiveACommission) rand_mod(100) < 50. A refusal shows text 10 (card_text(10, 0)) and leaves the
        // picture and next_card alone.
        menu[2] = OptionState.Shown;
        if (c.Succeeds(chance))
        {
            BankingHouses.GiveACommission(c, House, Number, 50, 195, 1, TextFrame.OnPicture);
            return;
        }
        c.Screen.Text(10);
        BankingHouses.BusyForThreeDays(c, House);
    }

    // orig: R061+0x1056 (18CF:06E6)
    private void ReportTheCommission(CardContext c, CardMenu menu)
    {
        menu[3] = OptionState.Hidden;
        var size = c.LocationQuery(c.Here, 2);
        if (c.Events.ResolvedQuestAt(BankingHouses.DeliveryQuest, House, c.Here))
        {
            // C: l = events[last_event]+0x2E: the reward item of the finished commission (0 = money). No favour is added here.
            var reward = c.EventDetail(c.LastEvent);
            if (reward == 0)
            {
                c.State.Party.Purse = c.State.Party.Purse.Add(size << 1, 0, 0);
                c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2) << 1, 0, 0);
                ShowPicture(c, PursePicture);
                c.Screen.Text(7);
                c.Variables.Location = BankingHouses.NearestCastle(c);
                c.SavedCard = Number;
                c.Next = BankingHouses.ErrandRewardCard;
                c.State.SavedCard2 = House;
                BankingHouses.BusyForThreeDays(c, House);
            }
            else
            {
                c.Variables.Named[1] = c.World.ItemName(reward);
                ShowPicture(c, OfficePicture);
                c.Screen.Text(8);
            }
        }
        else if (c.Events.ResolvedQuestAt(BankingHouses.FarQuest, House, c.Here))
        {
            c.State.Party.Purse = c.State.Party.Purse.Add(size, 0, 0);
            c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2), 0, 0);
            ShowPicture(c, PursePicture);
            c.Screen.Text(7);
            BankingHouses.BusyForThreeDays(c, House);
            c.SavedCard = c.Events.Find(Core.Events.EventKinds.ResolvedQuest, -1, c.Here, House, BankingHouses.FarQuest, -1);
            c.State.SavedCard2 = Number;
            c.Next = BankingHouses.FarCommissionRewardCard;
        }
        else
        {
            // C: DS:4BB8-- (the Fuggers lower theirs by 3).
            c.State.MediciStanding--;
            ShowPicture(c, 40);
            c.Screen.Text(9);
            c.Next = c.IsDaylight ? 0x15 : 0x16;
        }
    }

    // orig: R061+0x130A (line 4 action; lines 5, 8 and 9 are the same: 09f0, 0af2, 0b48)
    private static void Leave(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 4;
    }
}
