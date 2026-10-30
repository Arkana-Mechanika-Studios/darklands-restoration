using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$fugge00": the office of the Fuggers. Redeem or buy letters of credit, ask the master
/// banker for a commission, or report one done. A party in bad standing with the city or the
/// bank meets guarded clerks, and one in very bad standing is thrown out.
/// </summary>
// orig: R060+0x1FEC (1A36:000C) matched; tests: CardSmokeTests
// orig: handler setup R060+0x2C10 (card-option-enabled[*]=1, screwed-up handler 0cd2, item handlers 0d0a/0d46/0d82/0dbe,
// saint handlers 0dfa/0e84/0f0e/0f98: standard pop-ups, no items or saints listed)
public sealed class FuggerBank : Card
{
    private const int House = 8;
    private const int WelcomePicture = 356;
    private const int GuardedPicture = 353;
    private const int PursePicture = 194;
    private const int ChestPicture = 195;

    public override int Number => 0x43;

    public override int Run(CardContext c)
    {
        var memory = c.Memory;
        // C: reputation-now = place-query(location-now, 0) is read once here; it is never written back and the later tests use this copy.
        var standing = c.LocationQuery(c.Here, 0);
        // C: the picture is chosen with DS:4BB6 (the Fugger favour) before the "++" below.
        var picture = standing >= 0 && c.State.FuggerStanding >= 0 ? WelcomePicture : GuardedPicture;
        Show(c, picture);
        c.Next = Number;
        // C: DS:EE72 = 7 and DS:EE70 = 0 (text window state) are set here; not modelled.
        // C: card-option-flag-0,1,2,6,7 = 1; card-option-flag-3,4,5,8,9 = 0 (lines 4, 5, 8, 9 stay hidden: dormant below).
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 6, 7);
        c.Variables.Chosen[0] = c.Leader;
        c.RandomName(c.City.Ordinal + House, 0, 0);
        // C: DS:9072 (the florins word of Party.BankNotes) holds the florins deposited as letters of credit; signed test.
        if (c.State.Party.BankNotes.Florins > 0)
        {
            c.Variables.Money[0] = Money.From(c.State.Party.BankNotes.Florins, 0, 0);
        }
        else
        {
            menu[0] = OptionState.Hidden;
        }
        if (c.State.FuggerStanding <= 0)
        {
            c.State.FuggerStanding++;
        }
        // C: open-quest-owner-value(10, 8) || open-quest-owner-value(3, 8) || event-kind-at(7, 8) || location-now-query(0) < 0 || DS:4BB6 < 0.
        if (BankingHouses.CommissionPending(c, House) || c.HereQuery(0) < 0 || c.State.FuggerStanding < 0)
        {
            menu[2] = OptionState.Hidden;
        }
        // C: resolved-quest-at-owner(10, 8) || resolved-quest-at-owner(3, 8): a finished commission lets the party report it.
        if (BankingHouses.CommissionDone(c, House))
        {
            menu[2] = OptionState.Hidden;
            menu[3] = OptionState.Enabled;
        }
        if (c.State.Party.Purse.Florins <= 1)
        {
            menu[1] = OptionState.Shown;
        }

        menu.EachRound = () =>
        {
            // C: tested at the top of every round, with the card's copy of the reputation and the current favour.
            if (standing + c.State.FuggerStanding <= -100)
            {
                // Thrown out by the guards.
                ShowPicture(c, 10);
                c.Screen.Text(5);
                c.Next = 0x15;
                return;
            }
            menu.Block = standing >= 0 && c.State.FuggerStanding >= 0 ? 0 : 2;
        };
        // C: text-slot-chosen-one = leader after every choice, before the handler runs.
        menu.OnChoice = () => c.Variables.Chosen[0] = c.Leader;
        return menu.Run(
            // orig: R060+0x2378 (line 0); handler R060+0x240A (assured, 100%, leader)
            new MenuLine(_ => RedeemLetters(c, menu, picture), () => new OptionHint(100, SuccessAssured, c.Leader)),
            // orig: R060+0x2444 (line 1); handler R060+0x2514: clamp(0, 99, (Charisma << 1) + reputation-now + favour)
            new MenuLine(_ => BuyALetter(c, menu, picture),
                () => new OptionHint(Math.Clamp((c.Attribute(c.Leader, (int)Attribute.Charisma) << 1) + standing + c.State.FuggerStanding, 0, 99), "", c.Leader)),
            // orig: R060+0x254E (line 2); handler R060+0x26FE: clamp(10, 50, place-query(location-now, 0) + (location-now-query(0xC) + party-read(4)))
            new MenuLine(chance => AskForACommission(c, menu, picture, chance),
                () => new OptionHint(Math.Clamp(c.LocationQuery(c.Here, 0) + (c.HereQuery(0xC) + c.PartyGet(4)), 10, 50), "", c.Leader)),
            // orig: R060+0x2750 (line 3); handler R060+0x29E8 (assured)
            new MenuLine(_ => ReportTheCommission(c, menu, picture), OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 4 and 5 (card-option-flag-4 = card-option-flag-5 = 0, set nowhere): show the first text and leave for the city entry card.
            // orig: handler R060+0x2A3E, action 0a40 R060+0x2A20
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R060+0x2A94, action 0a96 R060+0x2A76
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R060+0x2AE0, action 0aec R060+0x2ACC
            new MenuLine(_ => c.Next = c.IsDaylight ? 0x15 : 0x16, OptionHint.Saying(SuccessAssured)),
            // orig: handler R060+0x2B2C, action 0b38 R060+0x2B18
            new MenuLine(_ => c.Next = c.IsDaylight ? 9 : 10, OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 8 and 9 (card-option-flag-8 = card-option-flag-9 = 0, set nowhere): the same "leave" as lines 4 and 5.
            // orig: handler R060+0x2B82, action 0b84 R060+0x2B64
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R060+0x2BD8, action 0bda R060+0x2BBA
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)));
    }

    // orig: R060+0x2378 (1A36:0398)
    private void RedeemLetters(CardContext c, CardMenu menu, int picture)
    {
        BankingHouses.RedeemLetters(c);
        menu[0] = OptionState.Hidden;
        ShowPicture(c, PursePicture);
        c.Screen.Text(6);
        ShowPicture(c, picture);
        c.Next = Number;
    }

    // orig: R060+0x2444 (1A36:0464)
    private void BuyALetter(CardContext c, CardMenu menu, int picture)
    {
        ShowPicture(c, PursePicture);
        c.Screen.Text(3);
        ShowPicture(c, picture);
        if (BankingHouses.DepositFlorins(c))
        {
            menu[0] = OptionState.Enabled;
        }
        c.Next = Number;
    }

    // orig: R060+0x254E (1A36:056E)
    private void AskForACommission(CardContext c, CardMenu menu, int picture, int chance)
    {
        // C: card-option-flag-2 = 2 (shown, greyed) is written before the roll; the first roll is roll-below(100) <= chance, the
        // second (inside GiveACommission) roll-below(100) < 50.
        menu[2] = OptionState.Shown;
        if (c.Succeeds(chance))
        {
            BankingHouses.GiveACommission(c, House, Number, 50, 225, 1, TextFrame.OnPicture);
            return;
        }
        ShowPicture(c, GuardedPicture);
        c.Screen.Text(10);
        ShowPicture(c, picture);
        BankingHouses.BusyForThreeDays(c, House);
        c.Next = Number;
    }

    // orig: R060+0x2750 (1A36:0770)
    private void ReportTheCommission(CardContext c, CardMenu menu, int picture)
    {
        var memory = c.Memory;
        var size = c.LocationQuery(c.Here, 2);
        if (c.Events.ResolvedQuestAt(BankingHouses.DeliveryQuest, House, c.Here))
        {
            c.State.FuggerStanding += 5;
            // C: l = *(int *)(events[latest-event] + 0x2e): the reward item of the finished commission (0 = money).
            var reward = c.EventDetail(c.LastEvent);
            if (reward == 0)
            {
                c.State.Party.Purse = c.State.Party.Purse.Add(size << 1, 0, 0);
                c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2) << 1, 0, 0);
                ShowPicture(c, ChestPicture);
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
                ShowPicture(c, ChestPicture);
                c.Screen.Text(8);
                ShowPicture(c, picture);
            }
        }
        else if (c.Events.ResolvedQuestAt(BankingHouses.FarQuest, House, c.Here))
        {
            c.State.FuggerStanding += 5;
            c.State.Party.Purse = c.State.Party.Purse.Add(size, 0, 0);
            c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2), 0, 0);
            ShowPicture(c, ChestPicture);
            c.Screen.Text(7);
            BankingHouses.BusyForThreeDays(c, House);
            c.SavedCard = c.Events.Find(Core.Events.EventKinds.ResolvedQuest, -1, c.Here, House, BankingHouses.FarQuest, -1);
            c.State.SavedCard2 = Number;
            c.Next = BankingHouses.FarCommissionRewardCard;
        }
        else
        {
            c.State.FuggerStanding -= 3;
            ShowPicture(c, 50);
            c.Screen.Text(9);
            c.Next = c.IsDaylight ? 0x15 : 0x16;
        }
        // C: card-option-flag-3 = 0 at the end of every branch.
        menu[3] = OptionState.Hidden;
    }

    // orig: R060+0x2A20 (line 4 action; lines 5, 8 and 9 are the same: 0a96, 0b84, 0bda)
    private static void Leave(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 4;
    }
}
