using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Urban01": the city's inn at night (the same scene as the inn by day). The party sits in
/// the common room, takes rooms for the night (full rest), stays at the inn for longer (the
/// lodging screen), visits the stables or the belongings kept here, looks for companions to
/// hire, or goes back to the streets. An inn whose city despises the party only serves it in
/// the common room, at the risk of being thrown out.
/// </summary>
// orig: R051+0x12F0 (1967:0000) matched; tests: CardSmokeTests
// orig: R051+0x1B6C (1967:087C) matched (the empty init call)
// DORMANT: R051+0x1B6E (1967:087E), the "screwed up." default handler, is installed by nothing.
public sealed class InnAtNight : Card
{
    public const int CardNumber = 0x1E;

    private const int Picture = 340, StablesPicture = 341, RoomPicture = 342, DespisedPicture = 343;

    private const int Challenged = 0x01, CityOutskirts = 0x04, MainStreet = 0x06, MainStreetAtNight = 0x08,
        SideStreet = 0x09, SideStreetAtNight = 0x0A, InnByDay = 0x1D, News = 0x66, Companions = 0xAA, InnClosed = 0xB5;

    /// <summary>A closed place in the city (kind 0x51).</summary>
    private const int Closed = 0x51;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(9);
        var picture = Picture;
        Show(c, picture);
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Hidden, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Hidden };
        if (c.HereQuery(0) <= -40)
        {
            states[1] = OptionState.Hidden;
            states[2] = OptionState.Hidden;
            states[4] = OptionState.Shown;
            // C: the card's own picture buffer changes too: the despised picture is what every later reload shows.
            picture = DespisedPicture;
            ShowPicture(c, picture);
        }
        if (c.HereQuery(0x27) != 0)
        {
            states[5] = OptionState.Enabled;
        }
        c.StandingSeen = c.ReputationHere;
        var size = c.HereQuery(2);
        c.Variables.Money[0] = Money.From(0, 0, c.ServicePrice(c.StandingSeen, size));
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            states[1] = OptionState.Shown;
        }
        // The text block is chosen once per round in the original, but line 2 is only closed by the
        // despised standing, fixed on arrival, so the block cannot change while the card stays.
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
            // orig: R051+0x15C4 (1967:02D4) + R051+0x1682 (1967:0392) R051 seg 1967 matched
            new MenuLine(chance => SitInTheCommonRoom(c, chance), () => CommonRoomHint(c)),
            // orig: R051+0x16CC (1967:03DC) + R051+0x17B0 (1967:04C0) R051 seg 1967 matched
            new MenuLine(() => { TakeRooms(c, picture); CloseRoomsIfPoor(); c.PassHours(9); c.Next = c.IsDaylight ? InnByDay : CardNumber; },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x17E8 (1967:04F8) + R051+0x1836 (1967:0546) R051 seg 1967 matched
            new MenuLine(() =>
                {
                    c.Camp(0);
                    c.Next = c.EventAtHere(Closed) ? InnClosed : c.IsDaylight ? InnByDay : CardNumber;
                    CloseRoomsIfPoor();
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x186E (1967:057E) + R051+0x18B8 (1967:05C8) R051 seg 1967 matched
            new MenuLine(() =>
                {
                    ShowPicture(c, StablesPicture);
                    c.Screen.Text(1);
                    Show(c, picture);
                    c.Next = CardNumber;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x18F0 (1967:0600) + R051+0x1964 (1967:0674) R051 seg 1967 matched
            new MenuLine(() =>
                {
                    ShowPicture(c, StablesPicture);
                    c.Screen.Text(5);
                    Show(c, picture);
                    c.OpenItemStore(c.Here);
                    UpdateStorage();
                    c.PassHours(1);
                    c.Next = CardNumber;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x199C (1967:06AC) + R051+0x19D8 (1967:06E8) R051 seg 1967 matched
            new MenuLine(() =>
                {
                    c.Screen.Text(6, TextFrame.BlankPage);
                    c.OpenItemStore(c.Here);
                    UpdateStorage();
                    c.PassHours(1);
                    c.Next = CardNumber;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1A10 (1967:0720) + R051+0x1A2E (1967:073E) R051 seg 1967 matched
            new MenuLine(() =>
                {
                    c.PassHours(2);
                    c.State.SavedCard = InnByDay;
                    c.State.SavedCard2 = CardNumber;
                    c.Next = Companions;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1A66 (1967:0776) + R051+0x1A84 (1967:0794) R051 seg 1967 matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight; }, OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1ABC (1967:07CC) + R051+0x1ADA (1967:07EA) R051 seg 1967 matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight; }, OptionHint.Saying(SuccessAssured)),
            // orig: R051+0x1B12 (1967:0822) + R051+0x1B30 (1967:0840) R051 seg 1967 matched
            // DORMANT: card-option-flag-9 is 0 and nothing sets it: this way out to the outskirts (text 0) is never offered.
            // C: the hint sets r.a = leader and then overwrites it with -1.
            new MenuLine(() => { c.Screen.Text(0); c.PassHours(0); c.Next = CityOutskirts; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>"Success is assured" (-1); in bad standing the chance not to be thrown out, -10 - standing.</summary>
    // orig: R051+0x1682 (1967:0392) matched
    private static OptionHint CommonRoomHint(CardContext c) =>
        c.StandingSeen <= -40 ? OptionHint.ChanceOf(-10 - c.StandingSeen, OptionHint.WholeParty) : OptionHint.Say(SuccessAssured);

    /// <summary>
    /// The common room: everyone present regains a twelfth of their Endurance. In bad standing
    /// a failed roll gets the party thrown out; else two hours pass over the news.
    /// </summary>
    // orig: R051+0x15C4 (1967:02D4) matched
    private static void SitInTheCommonRoom(CardContext c, int chance)
    {
        // C: all five slots are walked (the party record's end), not only party-headcount.
        for (var i = 0; i < 5; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddAttribute(i, 0, c.AttributeMaximum(i, 0) / 12);
            }
        }
        // C: root 0000:1716(0x100) redraws the party panel (presentation).
        // C: the && skips the draw in good standing.
        if (c.StandingSeen <= -40 && c.Random(100) > chance)
        {
            ShowPicture(c, RoomPicture);
            c.Screen.Text(4);
            c.Next = Challenged;
            return;
        }
        c.PassHours(2);
        c.State.SavedCard = c.IsDaylight ? InnByDay : CardNumber;
        c.Next = News;
    }

    /// <summary>A night in the rooms: Endurance back to the maximum and a point of Strength for the wounded; the price is paid.</summary>
    // orig: R051+0x16CC (1967:03DC) matched
    private void TakeRooms(CardContext c, int picture)
    {
        ShowPicture(c, RoomPicture);
        c.Screen.Text(2);
        Show(c, picture);
        // C: party-headcount slots, with no check for an empty one.
        for (var i = 0; i < c.PartySize; i++)
        {
            c.SetAttribute(i, 0, c.AttributeMaximum(i, 0));
            if (c.AttributeMaximum(i, 1) > c.Attribute(i, 1))
            {
                c.AddAttribute(i, 1, 1);
            }
        }
        // C: root 0000:1716(0x100) redraws the party panel (presentation).
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
    }
}
