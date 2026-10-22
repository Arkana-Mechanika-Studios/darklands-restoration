using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Counc00": the council hall of the city by day (the seat of the City Lord). The party asks
/// for an audience, or for an errand from a clerk, or calls upon a saint to be admitted; the
/// City Lord may then give the party a commission (quest 3 or 10 of tag 10). When the dungeon
/// has a prisoner the party seeks (event 0x4D), it may inquire after him or ask to visit him.
/// Or it goes back to the square or a side street. Dormant: the guard's weapons training.
/// </summary>
// orig: R054+0x1A78 (19DF:0008) matched; tests: CardSmokeTests
// (the file mixes matched and unmatched functions: R054+0x1D08, _05a8, _0888 and _0f88 are `unmatched`,
//  checked against asm, see the markers on them)
// orig: R054+0x2D92 (19DF:1322) matched (init helper: all 20 lines
//   get the "screwed up." handler, the pop-up handlers of the 5 member rows are installed, items and saints emptied)
// DORMANT: R054+0x2E34 (19DF:13C4), the "screwed up." handler of lines 10..19, which nothing selects.
// DORMANT: R054+0x2E6C (19DF:13FC), R054+0x2EA8 (19DF:1438),
//   R054+0x2EE4 (19DF:1474) and R054+0x2F20 (19DF:14B0): the item pop-up
//   handlers (slots 0..3): they name the member of the pop-up and say "Success is assured."; the card has no
//   card_items and no option asks items_usable(), so the item pop-up never opens.
public sealed class CouncilHall : Card
{
    public const int CardNumber = 0x2B;

    // Pictures (PictureNames numbers): +0x0E9B, +0x0AE7, +0x0ACF, +0x0E9F, +0x0FD7, +0x0DF7, +0x0A87, +0x0A7B.
    private const int Picture = 277, ClerkPicture = 40, TurnedAwayPicture = 34, WaitedOutPicture = 278,
        DeskPicture = 356, GuardPicture = 236, RefusedPicture = 16, PrisonerPicture = 13;

    private const int Challenged = 0x01, SideStreet = 0x09, SideStreetAtNight = 0x0A, CitySquare = 0x12, CitySquareAtNight = 0x19,
        Commission = 0x90, CommissionPaid = 0x91, Errand10Offered = 0x151, Errand10Paid = 0x152, PrisonWindow = 0x11E;

    /// <summary>Events at the city: the hall shut to the party (0x3A), recently refused (0x3C), welcome (0x3B), a prisoner sought (0x4D).</summary>
    private const int Shut = 0x3A, Refused = 0x3C, Welcome = 0x3B, SoughtPrisoner = 0x4D;

    /// <summary>The owner and tag of the City Lord's errands (quest owner 0x0A).</summary>
    private const int Lord = 0x0A;

    // The City Lord's name in $Named1: R136+0x8690+0x212F is entry 10 of the patron table.
    private const int CityLordName = 10;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var audience = OptionState.Shown;
        var service = OptionState.Shown;
        var saint = OptionState.Shown;
        var prisoners = OptionState.Hidden;
        c.Variables.Chosen[0] = c.Leader;
        c.SetCardSaints(5, 0x70, 0x86, 0x81);
        if (!c.Hostile())
        {
            audience = OptionState.Enabled;
            service = OptionState.Enabled;
            saint = c.SaintsUsable();
        }
        if (c.EventAtHere(Shut))
        {
            service = OptionState.Shown;
        }
        if (c.EventAtHere(SoughtPrisoner))
        {
            prisoners = OptionState.Enabled;
        }
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 8, 9);
        menu[0] = audience;
        menu[1] = service;
        menu[2] = saint;
        menu[3] = prisoners;
        menu[4] = prisoners;
        c.Next = Number;
        return menu.Run(
            // orig: R054+0x221C (19DF:07AC) hint + R054+0x2018 (19DF:05A8) action (hint matched, action unmatched)
            // (the Option lines ask their chance in the action, not rated by the menu: Option sets $ChosenOneName before the action asks the chance, and none of the lines has a pop-up except the saint line.)
            Option(c, () => AskForAnAudience(c, AudienceChance(c).Chance), () => AudienceChance(c)),
            // orig: R054+0x2526 (19DF:0AB6) hint + R054+0x22F8 (19DF:0888) action (hint matched, action unmatched)
            Option(c, () => AskForAnErrand(c, ErrandChance(c).Chance), () => ErrandChance(c)),
            // orig: R054+0x2758 (19DF:0CE8) hint + R054+0x25C6 (19DF:0B56) action matched
            Option(c, () =>
                {
                    _ = (c.MenuPopup ? SaintChance(c) : OptionHint.Say(CardTexts.SelectSaint)).Chance;
                    CallUponASaint(c);
                },
                OptionHint.Saying(CardTexts.SelectSaint)),
            // orig: R054+0x2880 (19DF:0E10) hint + R054+0x2790 (19DF:0D20) action matched
            Option(c, () => InquireAboutPrisoners(c), InquireChance(c)),
            // orig: R054+0x2996 (19DF:0F26) hint + R054+0x28CE (19DF:0E5E) action matched
            Option(c, () => VisitPrisoners(c), VisitChance(c)),
            // DORMANT: line 5 (weapons training) has flag 0 in the C and nothing ever raises it; it would cost one hour and
            //   give the party the guard's training (events at the city), or a reputation loss when refused.
            // orig: R054+0x2B8C (19DF:111C) hint + R054+0x29F8 (19DF:0F88) action (hint matched, action unmatched)
            Option(c, () => TrainWithTheGuard(c, TrainingChance(c).Chance), () => TrainingChance(c)),
            // DORMANT: line 6 has flag 0 (and an empty line in the MSG).
            // orig: R054+0x2C6A (19DF:11FA) hint + R054+0x2C4C (19DF:11DC) action matched
            Option(c, () => LetNothingHappen(c), OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 7 has flag 0 (and an empty line in the MSG).
            // orig: R054+0x2CC0 (19DF:1250) hint + R054+0x2CA2 (19DF:1232) action matched
            Option(c, () => LetNothingHappen(c), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R054+0x2D0E (19DF:129E) hint + R054+0x2CFA (19DF:128A) action matched
            Option(c, () => c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight, OptionHint.Saying(SuccessAssured)),
            // orig: R054+0x2D5A (19DF:12EA) hint + R054+0x2D46 (19DF:12D6) action matched
            Option(c, () => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>
    /// An option line. C: after each choice the menu loop sets $ChosenOneName to the leader
    /// before the line's handler and action run (a saint pop-up's handler then sets the member).
    /// </summary>
    private static MenuLine Option(CardContext c, Action choose, Func<OptionHint> hint) =>
        new(() =>
            {
                c.Variables.Chosen[0] = c.Leader;
                choose();
            },
            hint);

    // ---- chances ------------------------------------------------------------------

    /// <summary>
    /// Admission to the audience: half the leader's Speak Common plus the standing and his
    /// Charisma, halved (kept in 1..70); 1 after a refusal, 99 when welcome or when the City
    /// Lord's errand is done; 0 at standing −40 or worse.
    /// </summary>
    // orig: R054+0x221C (19DF:07AC) matched (checked against asm)
    private static OptionHint AudienceChance(CardContext c)
    {
        int chance;
        if (c.HereQuery(0) <= -40)
        {
            chance = 0;
        }
        else
        {
            var value = (c.HereQuery(0) + c.Skill(Who.Leader, 0x0A) / 2 + c.Attribute(Who.Leader, 5)) / 2;
            value = Math.Clamp(value, 1, 70);
            if (c.EventAtHere(Refused))
            {
                value = 1;
            }
            // DORMANT: nothing creates the welcome event (0x3B): the "come back" event that would start it never runs its handler, so the 99 chance is never given.
            if (c.EventAtHere(Welcome))
            {
                value = 99;
            }
            if (c.Events.ResolvedQuestAt(3, Lord, c.Here) || c.Events.ResolvedQuestAt(Lord, Lord, c.Here))
            {
                value = 99;
            }
            chance = Math.Clamp(value, 1, 99);
        }
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    /// <summary>Admission for an errand: as the audience, but not held to 70 and a finished errand does not help.</summary>
    // orig: R054+0x2526 (19DF:0AB6) matched
    private static OptionHint ErrandChance(CardContext c)
    {
        int chance;
        if (c.HereQuery(0) <= -40)
        {
            chance = 0;
        }
        else
        {
            var value = (c.HereQuery(0) + c.Skill(Who.Leader, 0x0A) / 2 + c.Attribute(Who.Leader, 5)) / 2;
            if (c.EventAtHere(Refused))
            {
                value = 1;
            }
            // DORMANT: nothing creates the welcome event (0x3B): the "come back" event that would start it never runs its handler, so the 99 chance is never given.
            if (c.EventAtHere(Welcome))
            {
                value = 99;
            }
            chance = Math.Clamp(value, 0, 99);
        }
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    /// <summary>The hint of the prisoner inquiry: half the party's average Stealth when the city is hostile, else no risk.</summary>
    // orig: R054+0x2880 (19DF:0E10) matched
    private static Func<OptionHint> InquireChance(CardContext c) => () =>
        c.Hostile() ? OptionHint.ChanceOf(c.PartyAverageSkill(0x0F) / 2, OptionHint.WholeParty) : OptionHint.Say(SuccessAssured);

    /// <summary>The hint of the prisoner visit: a quarter of the party's average Intelligence and Stealth when hostile.</summary>
    // orig: R054+0x2996 (19DF:0F26) matched
    private static Func<OptionHint> VisitChance(CardContext c) => () =>
        c.Hostile() ? OptionHint.ChanceOf((c.PartyAverageAttribute(4) + c.PartyAverageSkill(0x0F)) / 4, OptionHint.WholeParty) : OptionHint.Say(SuccessAssured);

    /// <summary>
    /// DORMANT (line 5 is closed): the training chance, for the leader: a quarter of his Speak
    /// Common plus half the standing plus half his Charisma; none at all when his best weapon
    /// skill is above a threshold (city size times 3, plus the city's trait 0x21 mod 11, plus 10);
    /// never above the standing.
    /// </summary>
    // orig: R054+0x2B8C (19DF:111C) matched (checked against asm)
    private static OptionHint TrainingChance(CardContext c)
    {
        var threshold = c.HereQuery(2) * 3 + (ushort)c.HereQuery(0x21) % 0x0B + 0x0A;
        var value = c.Skill(Who.Leader, 0x0A) / 4 + (c.HereQuery(0) / 2 + c.Attribute(Who.Leader, 5) / 2);
        if (c.BestWeaponSkill(c.Leader) > threshold)
        {
            value = 0;
        }
        value = Math.Min(c.HereQuery(0), value);
        return OptionHint.ChanceOf(Math.Clamp(value, 0, 99), c.Leader);
    }

    // orig: R054+0x2F5C (19DF:14EC), R054+0x2FE6 (19DF:1576),
    //   R054+0x3070 (19DF:1600), R054+0x30FA (19DF:168A) matched
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    // ---- actions ------------------------------------------------------------------

    /// <summary>
    /// The City Lord's commission: a paid errand (quest of the lord's tag done) is settled at
    /// once with the money ×7 or ×3 (cards 0x91 / 0x152); else with the roll below 50 he sends
    /// the party to a nearby site (quest 3, card 0x90), otherwise on a hunt for a quest 10 site
    /// (card 0x151). <paramref name="text"/> (4 audience at once, 2 after a wait, 0 none) is
    /// shown when there is a commission to hear.
    /// </summary>
    // orig: R054+0x1D08 (19DF:0298) unmatched (checked against asm: the C reads as the asm; only the register choice differs)
    private void ReceiveACommission(CardContext c, int text)
    {
        c.Variables.Named[0] = CardContext.PatronNames[CityLordName];
        // C: the roll is drawn before the event tests, whatever branch follows.
        var roll = c.Random(100);
        var here = c.State.Locations[c.Here];
        if (c.Events.ResolvedQuestAt(3, Lord, c.Here))
        {
            var pay = c.HereQuery(2) * 7;
            c.State.Party.Purse = c.State.Party.Purse.Add(pay, 0, 0);
            c.Variables.Money[0] = Money.From(c.HereQuery(2) * 7, 0, 0);
            c.RandomName(c.City.Ordinal + 0x0C, 0, 0);
            c.Variables.Location = c.NearestLocationOfType(here.X, here.Y, 2);
            c.State.SavedCard = c.IsDaylight ? CitySquare : CitySquareAtNight;
            c.Next = CommissionPaid;
            c.State.SavedCard2 = Lord;
            MarkLordBusy(c);
            return;
        }
        if (c.Events.ResolvedQuestAt(Lord, Lord, c.Here))
        {
            c.Variables.Money[0] = Money.From(c.HereQuery(2) * 3, 0, 0);
            c.State.Party.Purse = c.State.Party.Purse.Add(c.HereQuery(2) * 3, 0, 0);
            MarkLordBusy(c);
            c.State.SavedCard = c.Events.Find(0x24, -1, c.Here, Lord, Lord, -1);
            c.State.SavedCard2 = c.IsDaylight ? CitySquare : CitySquareAtNight;
            c.RandomName(c.City.Ordinal + 0x0C, 0, 0);
            c.Next = Errand10Paid;
            return;
        }
        if (roll < 0x32)
        {
            c.Variables.Money[0] = Money.From(c.HereQuery(2) * 7, 0, 0);
            c.RandomName(c.City.Ordinal + 0x0C, 0, 0);
            if (text != 0)
            {
                c.Screen.Text(text);
            }
            c.Variables.Location = c.NearestLocationOfType(here.X, here.Y, 2);
            c.StartQuest3(Lord, c.Variables.Location, c.City.Ordinal + 0x62, 0x0F, 2, 0);
            c.PassHours(1);
            // C: the street is chosen after the hour has passed.
            c.State.SavedCard = c.IsDaylight ? CitySquare : CitySquareAtNight;
            c.State.SavedCard2 = c.Variables.Location;
            c.Next = Commission;
            return;
        }
        c.PassHours(1);
        c.Variables.Money[0] = Money.From(c.HereQuery(2) * 3, 0, 0);
        if (text != 0)
        {
            c.Screen.Text(text);
        }
        c.State.SavedCard = c.StartQuest10(Lord);
        c.State.SavedCard2 = c.IsDaylight ? CitySquare : CitySquareAtNight;
        c.Next = Errand10Offered;
    }

    /// <summary>A kind-7 event of the lord (owner 0x0A) for 72 hours, as the market keeps its merchants.</summary>
    private static void MarkLordBusy(CardContext c) =>
        c.Events.Create(-2, c.Here, 0x5F, 7, Lord, 0, 0, 0x48, 0, 0);

    /// <summary>
    /// An audience: within the chance the lord receives the party at once if its standing and
    /// fame (a tenth) reach the roll, else after a wait of 2..4 hours (no later than 18:00);
    /// both end in his commission. Otherwise the party is refused for a week: badly (−10
    /// standing) on a roll over twice the chance, else after waiting until 19:00 (−1).
    /// </summary>
    // orig: R054+0x2018 (19DF:05A8) unmatched (checked against asm: conditions, order of draws and texts as in the C)
    private void AskForAnAudience(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (chance == 0)
        {
            c.Next = Challenged;
            return;
        }
        if (c.EventAtHere(Shut))
        {
            c.Screen.Text(1);
        }
        else
        {
            var roll = c.Random(100);
            if (roll <= chance)
            {
                var regard = c.HereQuery(0) + c.PartyGet(4) / 10;
                int text;
                if (regard >= roll)
                {
                    c.PassHours(1);
                    ShowPicture(c, ClerkPicture);
                    text = 4;
                }
                else
                {
                    // C: imin(rand_mod(3) + 2, hours_until(18)): hours_until is evaluated first; it draws nothing.
                    var until = TimeOfDay.HoursUntil(c.State.Date.Hour, 18);
                    c.Variables.Numbers[0] = Math.Min(c.Random(3) + 2, until);
                    c.PassHours(c.Variables.Numbers[0]);
                    ShowPicture(c, ClerkPicture);
                    text = 2;
                }
                c.EventHere(Refused, 168);
                ReceiveACommission(c, text);
                return;
            }
            c.EventHere(Refused, 168);
            if (chance * 2 < roll)
            {
                c.LocationSet(c.Here, 0, c.HereQuery(0) - 10);
                ShowPicture(c, TurnedAwayPicture);
                c.Screen.Text(5);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                return;
            }
            c.PassHours(TimeOfDay.HoursUntil(c.State.Date.Hour, 19));
            c.LocationSet(c.Here, 0, c.HereQuery(0) - 1);
            ShowPicture(c, WaitedOutPicture);
            c.Screen.Text(3);
        }
        c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
    }

    /// <summary>
    /// Asks a clerk for an errand: within the chance, the commission follows if a third of the
    /// standing and a twentieth of the fame reach the roll; else the party is told to come back
    /// (the hall shut to it until five); otherwise refused for a week (−10 standing on a bad roll
    /// while the standing is under 10).
    /// </summary>
    // orig: R054+0x22F8 (19DF:0888) unmatched (checked against asm: conditions, order of draws and texts as in the C)
    private void AskForAnErrand(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (chance == 0)
        {
            c.Next = Challenged;
            return;
        }
        if (c.EventAtHere(Shut))
        {
            c.Screen.Text(1);
        }
        else
        {
            var roll = c.Random(100);
            if (roll <= chance)
            {
                var regard = c.HereQuery(0) / 3 + c.PartyGet(4) / 20;
                if (regard >= roll)
                {
                    c.PassHours(1);
                    ShowPicture(c, ClerkPicture);
                    ReceiveACommission(c, 4);
                    return;
                }
                c.PassHours(2);
                // BUG: the "come back" appointment is never kept: the event names expiry handler 4 (a one-day welcome,
                // event 0x3B) but its flags are 0, so the handler never runs (R029+0x3F30, R054+0x22F8).
                c.Events.Create(-2, c.Here, 0x5A, Shut, 0, 0x63, 4, TimeOfDay.HoursUntil(c.State.Date.Hour, 18), 0, 0);
                ShowPicture(c, DeskPicture);
                c.Screen.Text(7);
                ShowPicture(c, Picture);
                // The party stays in the hall.
                return;
            }
            c.EventHere(Refused, 168);
            if (chance * 2 < roll && c.HereQuery(0) < 10)
            {
                int standing;
                if (c.HereQuery(0) >= 0)
                {
                    standing = Math.Clamp(c.HereQuery(0) - 10, 0, 99);
                }
                else
                {
                    standing = c.HereQuery(0) - 10;
                }
                c.LocationSet(c.Here, 0, standing);
                ShowPicture(c, TurnedAwayPicture);
                c.Screen.Text(5);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                return;
            }
            ShowPicture(c, DeskPicture);
            c.Screen.Text(6);
        }
        c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
    }

    /// <summary>A saint opens the lord's door (only an answer of exactly 1): a commission follows.</summary>
    // orig: R054+0x25C6 (19DF:0B56) matched
    private void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        c.Variables.Chosen[1] = c.MenuMember;
        if (answer - 1 == 0)
        {
            switch (c.MenuSlot)
            {
                case 0 or 1 or 2:
                    ShowPicture(c, DeskPicture);
                    c.Screen.Text(9);
                    ShowPicture(c, ClerkPicture);
                    c.Screen.Text(8);
                    break;
                case 3:
                    ShowPicture(c, DeskPicture);
                    c.Screen.Text(0x0A);
                    ShowPicture(c, ClerkPicture);
                    c.Screen.Text(8);
                    break;
            }
            // C: a slot outside 0..3 skips the texts and still gives the commission.
            // BUG: unlike the audience (ReceiveACommission), the saint's way in never asks for a finished commission
            // (ev24_at_owner(3, 0x0A, cur_loc)): a party that prays its way in to be paid gets a new commission instead.
            var here = c.State.Locations[c.Here];
            c.Variables.Location = c.NearestLocationOfType(here.X, here.Y, 2);
            c.StartQuest3(Lord, c.Variables.Location, c.City.Ordinal + 0x62, 0x0F, 2, 0);
            c.PassHours(1);
            c.State.SavedCard = c.IsDaylight ? CitySquare : CitySquareAtNight;
            c.State.SavedCard2 = c.Variables.Location;
            c.Next = Commission;
            return;
        }
        ShowPicture(c, RefusedPicture);
        c.Screen.Text(0x0B);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    /// <summary>Inquires about the prisoners: in a hostile city the party may be caught (Stealth), else it is told the prisoner's whereabouts.</summary>
    // orig: R054+0x2790 (19DF:0D20) matched
    private void InquireAboutPrisoners(CardContext c)
    {
        // C: the roll is drawn (before the average is read) only in a hostile city; && skips it otherwise.
        if (c.Hostile() && c.PartyAverageSkill(0x0F) / 2 > c.Random(100))
        {
            ShowPicture(c, ClerkPicture);
            c.Screen.Text(0x0E);
            c.Next = PrisonWindow;
            return;
        }
        if (c.Hostile())
        {
            ShowPicture(c, GuardPicture);
            c.Screen.Text(0x0F);
            c.Next = Challenged;
            return;
        }
        ShowPicture(c, ClerkPicture);
        c.Screen.Text(0x10);
        ShowPicture(c, Picture);
    }

    /// <summary>Asks to visit the prisoners: in a hostile city a failed roll gets the party caught, else the dungeon window follows.</summary>
    // orig: R054+0x28CE (19DF:0E5E) matched
    private void VisitPrisoners(CardContext c)
    {
        // C: the roll is drawn first, only when the city is hostile (the && skips it otherwise).
        if (c.Hostile() && (c.PartyAverageSkill(0x0F) + c.PartyAverageAttribute(4)) / 4 < c.Random(100))
        {
            ShowPicture(c, GuardPicture);
            c.Screen.Text(0x0F);
            c.Next = Challenged;
            return;
        }
        ShowPicture(c, PrisonerPicture);
        c.Screen.Text(0x11);
        ShowPicture(c, Picture);
        c.Next = PrisonWindow;
    }

    /// <summary>
    /// DORMANT (line 5 is closed): weapons training with the guard. An hour passes; within the
    /// chance five events of kind 0x28 (the training offers, 168 hours) are created and the price
    /// per person is named; else the standing here sinks by 1. Either way the guard is not asked
    /// again until the next day (event 0x41).
    /// </summary>
    // orig: R054+0x29F8 (19DF:0F88) unmatched (checked against asm: events, price and texts as in the C)
    private void TrainWithTheGuard(CardContext c, int chance)
    {
        c.PassHours(1);
        if (c.Random(100) <= chance)
        {
            c.Events.Create(-2, c.Here, 0x32, 0x28, 0, 0x32, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, 0x32, 0x28, 1, 0x19, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, 0x32, 0x28, 2, 0x19, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, 0x32, 0x28, 3, 0x19, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, 0x32, 0x28, 6, 0x19, 0, 0xA8, 0, 0);
            // C: (unsigned)loc_query(0x21) % 11.
            var price = c.HereQuery(2) * 3 + (ushort)c.HereQuery(0x21) % 0x0B + 0x14;
            c.Variables.Money[0] = Money.From(0, price, 0);
            ShowPicture(c, GuardPicture);
            c.Screen.Text(0x0C);
        }
        else
        {
            c.AdjustReputation(c.Here, -1, -1);
            ShowPicture(c, GuardPicture);
            c.Screen.Text(0x0D);
        }
        c.EventHere(0x41, TimeOfDay.HoursUntil(c.State.Date.Hour, 5) + 0x18);
        c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
    }

    /// <summary>DORMANT (lines 6 and 7 are closed): shows the card's first text and lets no time pass; the party goes to card 4.</summary>
    // orig: R054+0x2C4C (19DF:11DC), R054+0x2CA2 (19DF:1232) matched
    private static void LetNothingHappen(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 4;
    }
}
