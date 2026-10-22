using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Urban00": the city's inn by day. The party sits in the common room to hear the news,
/// takes rooms for the night (full rest), stays at the inn for longer (the lodging screen),
/// visits the stables, leaves or takes up belongings kept here, looks for companions to hire,
/// or goes back to the streets. An inn whose city despises the party only serves it in the
/// common room, at the risk of a fight.
/// </summary>
// orig: R051+0x091E (18C9:000E) matched; tests: CardSmokeTests
// DORMANT: R051+0x1280 (18C9:0970), the init, closes all 20 lines with the "screwed up."
//   handler R051+0x12B8: lines 10..19 stay closed.
public sealed class InnByDay : Card
{
    public const int CardNumber = 0x1D;

    private const int Picture = 337, BackRoomPicture = 338, RoomPicture = 339;

    private const int Challenged = 0x01, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        InnAtNight = 0x1E, News = 0x66, Companions = 0xAA, InnClosed = 0xB5;

    /// <summary>A closed place in the city (kind 0x51).</summary>
    private const int Closed = 0x51;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        // C: the music piece is drawn: rand_mod(2) ? 9 : 10.
        c.PlayMusic(c.Random(2) != 0 ? 9 : 0x0A);
        var picture = Picture;
        Show(c, picture);
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Hidden, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Hidden };
        // C: loc_query 39: something is stored here.
        if (c.HereQuery(0x27) != 0)
        {
            states[5] = OptionState.Enabled;
        }
        c.StandingSeen = c.Reputation(c.Here);
        var size = c.HereQuery(2);
        if (c.StandingSeen <= -40)
        {
            states[1] = OptionState.Hidden;
            states[2] = OptionState.Hidden;
            states[4] = OptionState.Shown;
            // C: the card's own picture buffer changes: the back-room picture stays for the visit.
            picture = BackRoomPicture;
            ShowPicture(c, picture);
        }
        c.Variables.Money[0] = Money.FromPfennigs(c.ServicePrice(c.StandingSeen, size));
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            states[1] = OptionState.Shown;
        }
        var block = states[2] == OptionState.Hidden ? 3 : 0;

        var menu = Menu(c, block);
        void Set(int line, OptionState state) => menu[line] = state;
        void CloseRoomsIfPoor()
        {
            if (c.State.Party.Purse < c.Variables.Money[0])
            {
                Set(1, OptionState.Shown);
            }
        }
        void UpdateStorage() => Set(5, c.HereQuery(0x27) != 0 ? OptionState.Enabled : OptionState.Hidden);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k <= 9; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R051+0x0CE6 (18C9:03D6) + R051+0x0C00 (18C9:02F0) R051 seg 18C9 matched
            new MenuLine(chance => SitInTheCommonRoom(c, chance), () => CommonRoomHint(c)),
            // orig: R051+0x0E22 (18C9:0512) + R051+0x0D30 (18C9:0420) R051 seg 18C9 matched
            new MenuLine(() => { TakeRooms(c, picture); CloseRoomsIfPoor(); c.PassHours(9); c.Next = c.IsDaylight ? CardNumber : InnAtNight; },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x0EA8 (18C9:0598) + R051+0x0E5A (18C9:054A) R051 seg 18C9 matched
            new MenuLine(() =>
                {
                    c.Camp(0);
                    c.Next = c.EventAtHere(Closed) ? InnClosed : c.IsDaylight ? CardNumber : InnAtNight;
                    CloseRoomsIfPoor();
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x0F94 (18C9:0684) + R051+0x0EE0 (18C9:05D0) R051 seg 18C9 matched
            new MenuLine(() => VisitTheStables(c, picture), OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1040 (18C9:0730) + R051+0x0FCC (18C9:06BC) R051 seg 18C9 matched
            new MenuLine(() => { UseTheStore(c, BackRoomPicture, 5, picture); UpdateStorage(); c.PassHours(1); c.Next = CardNumber; },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x10EC (18C9:07DC) + R051+0x1078 (18C9:0768) R051 seg 18C9 matched
            // C: the hint sets r.a = leader and then overwrites it with -1.
            new MenuLine(() => { UseTheStore(c, RoomPicture, 6, picture); UpdateStorage(); c.PassHours(1); c.Next = CardNumber; },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1146 (18C9:0836) + R051+0x1128 (18C9:0818) R051 seg 18C9 matched
            new MenuLine(() =>
                {
                    c.PassHours(2);
                    c.State.SavedCard = CardNumber;
                    c.State.SavedCard2 = InnAtNight;
                    c.Next = Companions;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x119C (18C9:088C) + R051+0x117E (18C9:086E) R051 seg 18C9 matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight; }, OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x11F2 (18C9:08E2) + R051+0x11D4 (18C9:08C4) R051 seg 18C9 matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight; }, OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1248 (18C9:0938) + R051+0x122A (18C9:091A) R051 seg 18C9 matched
            // DORMANT: opt_flag_9 is 0: a second way to the side streets is never offered.
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>"Success is assured" (−1); in bad standing the chance not to be thrown out, −10 − standing.</summary>
    private static OptionHint CommonRoomHint(CardContext c) =>
        c.StandingSeen <= -40 ? OptionHint.ChanceOf(-10 - c.StandingSeen, OptionHint.WholeParty) : OptionHint.Say(SuccessAssured);

    /// <summary>
    /// The common room: everyone present regains an eighth of their Endurance (at most to the
    /// maximum). In bad standing a failed roll gets the party thrown out to the watch; else
    /// two hours pass over the news.
    /// </summary>
    private static void SitInTheCommonRoom(CardContext c, int chance)
    {
        // C: all five slots are walked (the party record's end), not only party_size.
        for (var i = 0; i < 5; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                var maximum = c.AttributeMaximum(i, 0);
                var endurance = c.Attribute(i, 0) + maximum / 8;
                c.SetAttribute(i, 0, Math.Min(endurance, maximum));
            }
        }
        // C: root 0000:1716(0x100) redraws the party panel (presentation).
        // C: the && skips the draw in good standing.
        if (c.StandingSeen <= -40 && c.Random(100) > chance)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(4);
            c.Next = Challenged;
            return;
        }
        c.PassHours(2);
        c.State.SavedCard = c.IsDaylight ? CardNumber : InnAtNight;
        c.Next = News;
    }

    /// <summary>A night in the rooms: Endurance back to the maximum and a point of Strength for the wounded; the price is paid.</summary>
    private void TakeRooms(CardContext c, int picture)
    {
        ShowPicture(c, RoomPicture);
        c.Screen.Text(2);
        Show(c, picture);
        for (var i = 0; i < 5; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.SetAttribute(i, 0, c.AttributeMaximum(i, 0));
                if (c.Attribute(i, 1) < c.AttributeMaximum(i, 1))
                {
                    c.AddAttribute(i, 1, 1);
                }
            }
        }
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
    }

    /// <summary>The stables (a text for a party already all mounted, another otherwise), then the stablemaster's shop.</summary>
    private void VisitTheStables(CardContext c, int picture)
    {
        ShowPicture(c, BackRoomPicture);
        // orig: R051+0x0EE0
        // BUG: the two stable texts look swapped: a party all of whose present members have a mount gets text 1, and
        // any other party text 7, the only one in which the stablemaster asks whether the party's mounts are for sale
        // (R051+0x0EE0).
        c.Screen.Text(c.PartyGet(5) != 0 ? 1 : 7);
        Show(c, picture);
        c.PassHours(1);
        c.Barter(-1, 0x2000, "Stablemaster");
        c.Next = c.IsDaylight ? CardNumber : InnAtNight;
    }

    private void UseTheStore(CardContext c, int storePicture, int text, int picture)
    {
        ShowPicture(c, storePicture);
        c.Screen.Text(text);
        Show(c, picture);
        c.OpenItemStore(c.Here);
    }
}
