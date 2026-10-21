using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Hanse00": the offices of the Hanseatic League. Seek a commission (humbly or boldly),
/// report one done, or chat with the clerks about the city's politics. The League will not
/// receive a party the city hates, and keeps a famous party waiting less.
/// </summary>
// orig: R061+0x2278 (1A5F:0008) matched; tests: CardSmokeTests
// orig: handler setup R061+0x326E (opt_on[*]=1, screwed-up handler 10a0, item handlers 10d8/1114/1150/118c,
// saint handlers 11c8/1252/12dc/1366: standard pop-ups, no items or saints listed)
public sealed class HanseaticLeague : Card
{
    private const int House = 7;
    private const int PursePicture = 194;
    private const int ClerksTalk = 0x2E;

    public override int Number => 0x47;

    public override int Run(CardContext c)
    {
        var memory = c.Memory;
        Show(c, 356);
        // C: DS:EE72 = 7 and DS:EE70 = 0 (text window state) are set here; not modelled.
        c.Next = Number;
        // C: opt_flag_0,1,7,8,9 = 1; opt_flag_2..6 = 0 (lines 2 and 3 are switched on when a commission can be reported;
        // lines 4, 5 and 6 are never switched on: dormant below).
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 7, 8, 9);
        c.Variables.Chosen[0] = c.Leader;
        c.RandomName(c.City.Ordinal + House, 0, 0);
        // C: cur_rep = loc_query(cur_loc, 0), read once; it is not written back.
        var standing = c.LocationQuery(c.Here, 0);
        // C: ev8_owner_value(10, 7) || ev8_owner_value(3, 7) || event_type_at(7, 7): a commission is running or the house is busy.
        if (BankingHouses.CommissionPending(c, House))
        {
            menu[0] = OptionState.Hidden;
            menu[1] = OptionState.Hidden;
        }
        // C: ev24_at_owner(10, 7) || ev24_at_owner(3, 7): a finished commission lets the party report it.
        if (BankingHouses.CommissionDone(c, House))
        {
            menu[0] = OptionState.Hidden;
            menu[1] = OptionState.Hidden;
            menu[2] = OptionState.Enabled;
            menu[3] = OptionState.Enabled;
        }

        // C: these three early exits come before the menu loop; cur_rep is compared with -0x4b (-75) and -0x32 (-50).
        if (standing <= -75 && c.PreviousCard == 1)
        {
            return c.Next = 0x15;
        }
        if (standing <= -75)
        {
            ShowPicture(c, 277);
            c.Screen.Text(2);
            return c.Next = 1;
        }
        if (c.State.HanseStanding + standing <= -50)
        {
            ShowPicture(c, 347);
            c.Screen.Text(3);
            return c.Next = 0x15;
        }
        if (c.State.HanseStanding <= 0)
        {
            c.State.HanseStanding++;
        }

        var waits = false;
        menu.EachRound = () =>
        {
            // C: DS:9082 is the party's fame; the test is made at the top of every round and the hour passes after
            // card_options(1) (text block 1) was shown and continued, before card_menu (AfterOptions).
            // orig: R061+0x2278 (1A5F:0008): `if (fame / 10 + favour + cur_rep <= 20) { card_options(1); advance_hours(1); } else card_options(0);`
            // A long wait before a snotty clerk sees the party.
            waits = c.State.Party.Fame / 10 + c.State.HanseStanding + standing <= 20;
            menu.Block = waits ? 1 : 0;
        };
        menu.AfterOptions = () =>
        {
            if (waits)
            {
                c.PassHours(1);
            }
        };
        var charm = () => new OptionHint(Math.Clamp((c.Attribute(c.Leader, (int)Attribute.Charisma) << 1) + c.State.HanseStanding + standing, 0, 99), "", c.Leader);
        return menu.Run(
            // orig: R061+0x260A (line 0); handler R061+0x27DA: clamp(10, 50, cur_rep / 2 + favour / 2 + fame)
            new MenuLine(chance => SeekACommission(c, menu, chance, waitFirst: true),
                () => new OptionHint(Math.Clamp(standing / 2 + c.State.HanseStanding / 2 + c.State.Party.Fame, 10, 50), "", c.Leader)),
            // orig: R061+0x2812 (line 1); handler R061+0x29D8: clamp(10, 50, cur_rep + fame)
            new MenuLine(chance => SeekACommission(c, menu, chance, waitFirst: false),
                () => new OptionHint(Math.Clamp(standing + c.State.Party.Fame, 10, 50), "", c.Leader)),
            // orig: R061+0x2A00 (line 2); handler R061+0x2CB2: clamp(0, 99, (Charisma << 1) + favour + cur_rep)
            new MenuLine(_ => ReportTheCommission(c, favourChanges: true), charm),
            // orig: R061+0x2CEC (line 3); handler R061+0x2F94 (assured)
            new MenuLine(_ => ReportTheCommission(c, favourChanges: false), OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 4, 5 and 6 (opt_flag_4..6 = 0, set nowhere): lines 4 and 5 show the first text and leave for the
            // city entry card; line 6 only calls advance_hours(0) and stays in the menu.
            // orig: handler R061+0x2FEA, action 0d5c R061+0x2FCC
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x3040, action 0db2 R061+0x3022
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x3084, action 0e08 R061+0x3078
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R061+0x30BC (line 7); handler R061+0x31A4: clamp(0, 99, Charisma << 1)
            new MenuLine(chance => ChatWithTheClerks(c, chance),
                () => new OptionHint(Math.Clamp(c.Attribute(c.Leader, (int)Attribute.Charisma) << 1, 0, 99), "", c.Leader)),
            // orig: handler R061+0x31EA, action 0f66 R061+0x31D6
            new MenuLine(_ => c.Next = c.IsDaylight ? 9 : 10, OptionHint.Saying(SuccessAssured)),
            // orig: handler R061+0x3236, action 0fb2 R061+0x3222
            new MenuLine(_ => c.Next = c.IsDaylight ? 0x15 : 0x16, OptionHint.Saying(SuccessAssured)));
    }

    // orig: R061+0x260A (1A5F:039A) for waitFirst; R061+0x2812 (1A5F:05A2) for the bold request
    private void SeekACommission(CardContext c, CardMenu menu, int chance, bool waitFirst)
    {
        // C: both lines are greyed (opt_flag_0 = opt_flag_1 = 2) first; rand_mod(100) <= chance, then (inside
        // GiveACommission) rand_mod(100) < 30. Only the humble request (039a) loses an hour on a refusal.
        menu[0] = OptionState.Shown;
        menu[1] = OptionState.Shown;
        if (c.Succeeds(chance))
        {
            BankingHouses.GiveACommission(c, House, Number, 30, PursePicture, 6, TextFrame.OnPicture);
            return;
        }
        if (waitFirst)
        {
            c.PassHours(1);
        }
        c.Screen.Text(5);
        BankingHouses.BusyForThreeDays(c, House);
        EscortedOutAtNight(c);
    }

    // orig: R061+0x2A00 (1A5F:0790) with favourChanges (+5 / -3); R061+0x2CEC (1A5F:0A7C) without (-1 on a miss)
    private void ReportTheCommission(CardContext c, bool favourChanges)
    {
        var memory = c.Memory;
        var size = c.LocationQuery(c.Here, 2);
        if (c.Events.ResolvedQuestAt(BankingHouses.DeliveryQuest, House, c.Here))
        {
            if (favourChanges)
            {
                c.State.HanseStanding += 5;
            }
            var reward = c.EventDetail(c.LastEvent);
            if (reward == 0)
            {
                c.State.Party.Purse = c.State.Party.Purse.Add(size * 2, 0, 0);
                c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2) * 2, 0, 0);
                ShowPicture(c, PursePicture);
                c.Screen.Text(8);
                c.Variables.Location = BankingHouses.NearestCastle(c);
                c.SavedCard = Number;
                c.Next = BankingHouses.ErrandRewardCard;
                c.State.SavedCard2 = House;
                // C: R029+0x4AA4(saved_card2 = 7) ends the hirelings' terms with value 7.
                c.EndTerms(House);
                BankingHouses.BusyForThreeDays(c, House);
                return;
            }
            c.Variables.Named[1] = c.World.ItemName(reward);
            ShowPicture(c, PursePicture);
            c.Screen.Text(8);
            return;
        }
        if (c.Events.ResolvedQuestAt(BankingHouses.FarQuest, House, c.Here))
        {
            if (favourChanges)
            {
                c.State.HanseStanding += 5;
            }
            c.State.Party.Purse = c.State.Party.Purse.Add(size, 0, 0);
            c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2), 0, 0);
            ShowPicture(c, PursePicture);
            c.Screen.Text(8);
            BankingHouses.BusyForThreeDays(c, House);
            c.SavedCard = c.Events.Find(Core.Events.EventKinds.ResolvedQuest, -1, c.Here, House, BankingHouses.FarQuest, -1);
            c.State.SavedCard2 = Number;
            c.Next = BankingHouses.FarCommissionRewardCard;
            return;
        }
        // C: DS:4BBA -= 3 (0790) or DS:4BBA-- (0a7c); the two pictures are loaded one after the other, only the second stays.
        c.State.HanseStanding -= favourChanges ? 3 : 1;
        ShowPicture(c, 40);
        ShowPicture(c, 159);
        c.Screen.Text(9);
        c.Next = c.IsDaylight ? 0x15 : 0x16;
    }

    /// <summary>Gossip with the clerks: they name a nearby city ($NamedThreeName), and may warm to the party once in ten days.</summary>
    // orig: R061+0x30BC (1A5F:0E4C)
    private void ChatWithTheClerks(CardContext c, int chance)
    {
        var here = c.State.Locations[c.Here];
        c.Variables.Named[2] = c.LocationName(c.NearestCityPassingOverOneHere(here.X, here.Y));
        // C: !event_at(0x2e, cur_loc) && rand_mod(100) <= chance: the roll is drawn only when the clerks have not talked in the last ten days.
        if (!c.EventAtHere(ClerksTalk) && c.Succeeds(chance))
        {
            // C: DS:4BBA = imin(++DS:4BBA, 99).
            c.State.HanseStanding = Math.Min(c.State.HanseStanding + 1, 99);
            c.PassHours(1);
            c.Screen.Text(7);
            c.Events.Create(-2, c.Here, 0xA, ClerksTalk, 0, 0, 0, 0xF0, 0, 0);
        }
        else
        {
            c.PassHours(1);
            c.Screen.Text(7);
        }
        if (!c.IsDaylight)
        {
            c.Screen.Text(10);
        }
        c.Next = c.IsDaylight ? Number : 0x16;
    }

    // orig: tail of R061+0x260A / R061+0x2812 (refusal): at night picture 78 and text 10, then to the street card 0x16
    private void EscortedOutAtNight(CardContext c)
    {
        if (!c.IsDaylight)
        {
            ShowPicture(c, 78);
            c.Screen.Text(10);
        }
        c.Next = c.IsDaylight ? Number : 0x16;
    }

    // orig: R061+0x2FCC (line 4 action; line 5 is the same: 0db2)
    private static void Leave(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 4;
    }
}
