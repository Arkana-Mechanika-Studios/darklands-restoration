using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityF00": the fortress of the city lord by day. The party asks for an audience, or
/// seeks service at the gate, or calls upon a saint to be admitted; the lord may give the
/// party a commission (quest 3 of tag 10). Or it goes back to the main or side street.
/// </summary>
// orig: R050+0x1CEE (1A06:000E) matched; tests: CardSmokeTests
// DORMANT: R050+0x2934 (1A06:0C54), the init, gives lines 10..19 the "screwed up." handler
//   R050+0x29D6 and installs item pop-up handlers that cannot run (no card-item-lines):
//   R050+0x2A0E, R050+0x2A4A, R050+0x2A86,
//   R050+0x2AC2 ("Success is assured.", naming the member).
public sealed class FortressByDay : Card
{
    public const int CardNumber = 0x1B;

    private const int Picture = 236, GuardPicture = 10, AudiencePicture = 293, WaitPicture = 40, TurnedAwayPicture = 277,
        BlessingPicture = 240, RefusedPicture = 263;

    private const int Challenged = 0x01, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        CitySquare = 0x12, CitySquareAtNight = 0x19, Commission = 0x90;

    /// <summary>Events at the city: the fortress shut to the party (0x3A), recently refused (0x3C), welcome (0x3B).</summary>
    private const int Shut = 0x3A, Refused = 0x3C, Welcome = 0x3B;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x11);
        Show(c, Picture);
        var audience = OptionState.Shown;
        var service = OptionState.Shown;
        var saint = OptionState.Shown;
        c.SetCardSaints(5, 0x70, 0x86, 0x81);
        if (c.HereQuery(0x18) == 0)
        {
            audience = OptionState.Enabled;
            service = OptionState.Enabled;
            saint = c.SaintsUsable();
        }
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 8, 9);
        menu[0] = audience;
        menu[1] = service;
        menu[2] = saint;
        c.Next = Number;
        return menu.Run(
            // orig: R050+0x21F8 (1A06:0518) + R050+0x1F4C (1A06:026C) R050 seg 1A06 matched
            new MenuLine(chance => AskForAnAudience(c, chance), () => AdmissionChance(c)),
            // orig: R050+0x24E0 (1A06:0800) + R050+0x2298 (1A06:05B8) R050 seg 1A06 matched
            new MenuLine(chance => SeekService(c, chance), () => AdmissionChance(c)),
            // orig: R050+0x270E (1A06:0A2E) + R050+0x2580 (1A06:08A0) R050 seg 1A06 matched
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R050+0x2752 (1A06:0A72) + R050+0x2746 (1A06:0A66) R050 seg 1A06 matched
            // DORMANT: options 3..7 have flag 0; their actions only let no time pass and stay.
            Idle(c, () => OptionHint.Say(SuccessAssured)),
            // orig: R050+0x2796 (1A06:0AB6) + R050+0x278A (1A06:0AAA) R050 seg 1A06 matched
            Idle(c, () => OptionHint.Say(SuccessAssured)),
            // orig: R050+0x27DA (1A06:0AFA) + R050+0x27CE (1A06:0AEE) R050 seg 1A06 matched
            Idle(c, () => OptionHint.Say(SuccessAssured)),
            // orig: R050+0x281E (1A06:0B3E) + R050+0x2812 (1A06:0B32) R050 seg 1A06 matched
            Idle(c, () => OptionHint.Say(SuccessAssured)),
            // orig: R050+0x2862 (1A06:0B82) + R050+0x2856 (1A06:0B76) R050 seg 1A06 matched
            Idle(c, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R050+0x28B0 (1A06:0BD0) + R050+0x289C (1A06:0BBC) R050 seg 1A06 matched
            new MenuLine(() => c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight, OptionHint.Saying(SuccessAssured)),
            // orig: R050+0x28FC (1A06:0C1C) + R050+0x28E8 (1A06:0C08) R050 seg 1A06 matched
            new MenuLine(() => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight, OptionHint.Saying(SuccessAssured)));
    }

    private static MenuLine Idle(CardContext c, Func<OptionHint> hint) =>
        new(() => c.PassHours(0), hint);

    /// <summary>Half the leader's Speak Common plus the standing and his Charisma, halved; 1 after a refusal, 99 when welcome; 0 at standing −40 or worse.</summary>
    private static OptionHint AdmissionChance(CardContext c)
    {
        int chance;
        if (c.HereQuery(0) <= -40)
        {
            chance = 0;
        }
        else
        {
            var value = (c.Skill(Core.Characters.Who.Leader, 0x0A) / 2 + c.HereQuery(0) + c.Attribute(Core.Characters.Who.Leader, 5)) / 2;
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

    /// <summary>The lord's commission: quest 3 of tag 10 to a nearby site; the party then leaves to <paramref name="returnTo"/>.</summary>
    private static void ReceiveACommission(CardContext c, Func<int> returnTo)
    {
        var here = c.State.Locations[c.Here];
        c.Variables.Location = c.NearestLocationOfType(here.X, here.Y, 2);
        // orig: R050+0x21F8; R050+0x24E0
        // BUG: a fortress commission is never paid in money: it is recorded for owner 0x0A, which only the town hall
        // pays (CouncilHall, R054+0x1D08), and the fortress gives commissions only where the city has no town hall
        // (location query 0x18 = 0) (R050+0x21F8, R050+0x24E0); this card has no resolved-commission test of its own.
        c.StartQuest3(0x0A, c.Variables.Location, c.City.Ordinal + 0x62, 0x0F, 2, 0);
        c.PassHours(1);
        // C: the street is chosen after the hour has passed.
        c.State.SavedCard = returnTo();
        c.State.SavedCard2 = c.Variables.Location;
        c.Next = Commission;
    }

    /// <summary>
    /// An audience: when the roll is within the chance, the lord receives the party at once if
    /// its standing and fame (a tenth) reach the roll, else after a wait of 1..3 hours (no later
    /// than 18:00); both end in a commission. Otherwise the party is refused for a week: badly
    /// (−10 standing) on a roll over twice the chance, else after waiting until 19:00 (−1).
    /// </summary>
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
            ShowPicture(c, GuardPicture);
            c.Screen.Text(1);
        }
        else
        {
            var roll = c.Random(100);
            if (roll <= chance)
            {
                var regard = c.HereQuery(0) + c.PartyGet(4) / 10;
                if (regard >= roll)
                {
                    c.PassHours(1);
                    ShowPicture(c, AudiencePicture);
                    c.Screen.Text(4);
                }
                else
                {
                    // C: smaller-of(roll-below(3) + 1, hours-remaining-until(18)): hours-remaining-until is evaluated first; it draws nothing.
                    var until = TimeOfDay.HoursUntil(c.State.Date.Hour, 18);
                    c.Variables.Numbers[0] = Math.Min(c.Random(3) + 1, until);
                    c.PassHours(c.Variables.Numbers[0] + 1);
                    ShowPicture(c, WaitPicture);
                    c.Screen.Text(2);
                }
                Show(c, Picture);
                c.EventHere(Refused, 168);
                ReceiveACommission(c, () => c.IsDaylight ? CitySquare : CitySquareAtNight);
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
            ShowPicture(c, TurnedAwayPicture);
            c.Screen.Text(3);
        }
        c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
    }

    /// <summary>
    /// Seeks service at the gate: within the chance, a commission if a third of the standing
    /// and a twentieth of the fame reach the roll, else the party is told to come back (the
    /// fortress shut until five); otherwise refused for a week (−10 standing on a bad roll while
    /// the standing is under 10).
    /// </summary>
    private void SeekService(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (chance == 0)
        {
            c.Next = Challenged;
            return;
        }
        if (c.EventAtHere(Shut))
        {
            ShowPicture(c, GuardPicture);
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
                    ShowPicture(c, AudiencePicture);
                    c.Screen.Text(4);
                    Show(c, Picture);
                    ReceiveACommission(c, () => c.IsDaylight ? MainStreet : MainStreetAtNight);
                    return;
                }
                c.PassHours(2);
                // orig: R050+0x2298
                // BUG: the "come back" appointment is never kept: the event names expiry handler 4 (a one-day welcome,
                // event 0x3B) but its flags are 0, so the handler never runs (R029+0x3F30, R050+0x2298).
                c.Events.Create(-2, c.Here, 0x5A, Shut, 0, 0x63, 4, TimeOfDay.HoursUntil(c.State.Date.Hour, 5), 0, 0);
                c.Screen.Text(7);
                // The party stays at the fortress gate.
                return;
            }
            c.EventHere(Refused, 168);
            if (chance * 2 < roll && c.HereQuery(0) < 10)
            {
                c.LocationSet(c.Here, 0, c.HereQuery(0) - 10);
                ShowPicture(c, TurnedAwayPicture);
                c.Screen.Text(5);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                return;
            }
            c.Screen.Text(6);
        }
        c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight;
    }

    // orig: R050+0x2AFE (1A06:0E1E), R050+0x2B88 (1A06:0EA8),
    //   R050+0x2C12 (1A06:0F32), R050+0x2C9C (1A06:0FBC) R050 seg 1A06 matched
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    /// <summary>A saint opens the lord's door (only an answer of exactly 1): a commission follows.</summary>
    private void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
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
                    ShowPicture(c, AudiencePicture);
                    c.Screen.Text(9);
                    c.Screen.Text(8, TextFrame.BlankPage);
                    break;
                case 3:
                    ShowPicture(c, AudiencePicture);
                    c.Screen.Text(0x0A);
                    ShowPicture(c, BlessingPicture);
                    c.Screen.Text(8);
                    break;
            }
            ReceiveACommission(c, () => c.IsDaylight ? CitySquare : CitySquareAtNight);
            return;
        }
        ShowPicture(c, RefusedPicture);
        c.Screen.Text(0x0B);
        c.Next = CardNumber;
    }
}
