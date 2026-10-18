using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Docks00": the city's docks. Ships sail to up to four (five) other ports named in the
/// city record, on some days only; the passage costs a fare per member and more per horse. The
/// party books a passage, or goes back to the main or side street. In a hostile city the watch
/// may catch the party at the quay.
/// </summary>
// orig: R051+0x1BA6 (19F2:0006) unmatched; checked against asm; tests: CardSmokeTests
// DORMANT: R051+0x2E4E (19F2:12AE), the init, enables lines 10..19 with the "screwed up."
//   handler R051+0x2EF0 and installs pop-up handlers the card never offers (no items or
//   saints): R051+0x2F28, R051+0x2F64, R051+0x2FA0,
//   R051+0x2FDC (items) and R051+0x3018, R051+0x30A2,
//   R051+0x312C, R051+0x31B6 (saints).
public sealed class Docks : Card
{
    public const int CardNumber = 0x1F;

    private const int DayPicture = 9, NightPicture = 68, ArrestPicture = 236;

    private const int Challenged = 0x01, CityOutskirts = 0x04, MainStreet = 0x06, MainStreetAtNight = 0x08,
        SideStreet = 0x09, SideStreetAtNight = 0x0A, Arrival = 0x5A;

    /// <summary>The city record's words of the ports ships sail to (−1 none).</summary>
    private static readonly int[] PortWords = [0x4A, 0x4C, 0x4E, 0x50, 0x52];

    /// <summary>A captain who will take the party privately (kind 0x39, 4 hours).</summary>
    private const int PrivatePassage = 0x39;

    /// <summary>Horses and other mounts (item flag 0x2000) pay their own fare.</summary>
    private const uint Mounts = 0x2000;

    /// <summary>The menu ends to be offered again (an hour passes and its text follows the day).</summary>
    private const int Redraw = int.MinValue;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        var picture = c.IsDaylight ? DayPicture : NightPicture;
        Show(c, picture);
        var states = new[] { OptionState.Hidden, OptionState.Hidden, OptionState.Hidden, OptionState.Hidden, OptionState.Hidden,
            OptionState.Hidden, OptionState.Enabled, OptionState.Enabled, OptionState.Hidden, OptionState.Hidden };
        var city = c.City;
        for (var k = 0; k < 4; k++)
        {
            var port = city.Word(PortWords[k]);
            if (port != -1)
            {
                c.Variables.Named[k] = c.State.Locations[port].Name;
            }
        }
        // C: 16-bit int arithmetic; the size is asked twice (no side effect).
        var fare = (short)((c.HereQuery(2) + 4) * c.PartySize + (c.HereQuery(2) + 5) * c.CountItemsWithFlags(Who.Everybody, Mounts));
        for (var k = 0; k < 5; k++)
        {
            c.Variables.Money[k] = Money.FromPfennigs(fare);
        }
        var sails = new bool[5];
        if ((c.HereQuery(0x20) & 1) != 0)
        {
            for (var k = 0; k < 5; k++)
            {
                sails[k] = (c.HereQuery(0x21) + c.State.Date.Day + k) % 4 == 0;
            }
            for (var k = 0; k < 5; k++)
            {
                c.Variables.Money[k] = c.Variables.Money[k].Times(2);
            }
        }
        // DORMANT: the original tests the same bit (loc_query 32 & 1) a second time here, so this branch
        //   (one port drawn at random, fares × 4) can never run:
        //   r = rand_mod(4); sails[k] = r == k; fares × 4.
        else
        {
            for (var k = 0; k < 5; k++)
            {
                sails[k] = (c.HereQuery(0x21) + c.State.Date.Day + k) % 3 == 0;
            }
        }
        for (var k = 0; k < 4; k++)
        {
            states[k] = city.Word(PortWords[k]) == -1 ? OptionState.Hidden : sails[k] ? OptionState.Enabled : OptionState.Shown;
        }

        while (true)
        {
            // C: every round of the menu lets an hour pass first.
            c.PassHours(1);
            int block;
            if ((c.HereQuery(0x20) & 1) != 0)
            {
                block = 1;
            }
            else if ((c.HereQuery(0x20) & 2) != 0)
            {
                block = c.EventAtHere(PrivatePassage) ? 4 : (c.HereQuery(0x21) + c.State.Date.Day) % 3 == 0 ? 6 : 2;
            }
            else
            {
                block = 0;
            }
            var menu = Menu(c, block);
            menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
            for (var k = 0; k <= 9; k++)
            {
                menu[k] = states[k];
            }
            c.Next = Number;
            var next = menu.Run(
                // orig: R051+0x2332 (19F2:0792) + R051+0x21A4 (19F2:0604) R051 seg 19F2 unmatched; checked against asm
                Passage(c, 0),
                // orig: R051+0x2546 (19F2:09A6) + R051+0x23B8 (19F2:0818) R051 seg 19F2 unmatched
                Passage(c, 1),
                // orig: R051+0x275A (19F2:0BBA) + R051+0x25CC (19F2:0A2C) R051 seg 19F2 unmatched
                Passage(c, 2),
                // orig: R051+0x2970 (19F2:0DD0) + R051+0x27E2 (19F2:0C42) R051 seg 19F2 unmatched
                Passage(c, 3),
                // orig: R051+0x2B84 (19F2:0FE4) + R051+0x29F6 (19F2:0E56) R051 seg 19F2 unmatched
                // DORMANT: opt_flag_4 is 0: the fifth port (record word 0x52) is never offered.
                Passage(c, 4),
                // orig: R051+0x2CBA (19F2:111A) + R051+0x2C0C (19F2:106C) R051 seg 19F2 unmatched
                // DORMANT: opt_flag_5 is 0: the private passage is never offered (its event 0x39 then never set here).
                new MenuLine(chance => SeekAPrivatePassage(c, chance), () => PrivateChance(c)),
                // orig: R051+0x2D1E (19F2:117E) + R051+0x2D0A (19F2:116A) R051 seg 19F2 matched
                new MenuLine(() => c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight, OptionHint.Saying(SuccessAssured)),
                // orig: R051+0x2D6A (19F2:11CA) + R051+0x2D56 (19F2:11B6) R051 seg 19F2 matched
                new MenuLine(() => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight, OptionHint.Saying(SuccessAssured)),
                // orig: R051+0x2DC0 (19F2:1220) + R051+0x2DA2 (19F2:1202) R051 seg 19F2 matched
                // DORMANT: lines 8 and 9 have flag 0 (text 0 again, then the outskirts).
                new MenuLine(() => { c.Screen.Text(0); c.PassHours(0); c.Next = CityOutskirts; }, OptionHint.Saying(SuccessAssured)),
                // orig: R051+0x2E16 (19F2:1276) + R051+0x2DF8 (19F2:1258) R051 seg 19F2 matched
                new MenuLine(() => { c.Screen.Text(0); c.PassHours(0); c.Next = CityOutskirts; }, OptionHint.Saying(SuccessAssured)));
            if (next != Redraw)
            {
                return next;
            }
        }
    }

    /// <summary>The chance of not being caught at the quay of a hostile city: 100 − a local danger (50 at most, at least 10).</summary>
    // C: thunk 09C0:20E9(0x32, 5, 0x0A): LocalPrice's middle argument is unused; it draws.
    private static int QuaySafety(CardContext c) => -(c.LocalPrice(0x32, 0x0A) - 100);

    /// <summary>
    /// Hint of a passage: the quay safety in a hostile city; else "Success is assured", or (when
    /// the fare is more than the purse) the EXE's next message, "You need a missile weapon".
    /// </summary>
    // BUG: the hint shows R136+0x8690+0x1E5F ("You need a missile weapon") where "Not enough money" was surely meant.
    private static OptionHint PassageHint(CardContext c, int port) =>
        c.Hostile()
            ? OptionHint.ChanceOf(QuaySafety(c), OptionHint.WholeParty)
            : OptionHint.Say(c.State.Party.Purse >= c.Variables.Money[port] ? SuccessAssured : CardContext.NeedMissileWeapon);

    /// <summary>
    /// Books a passage: in a hostile city a roll within a fresh quay danger brings the watch;
    /// a party that can pay sails (1..3 hours of waiting, then 48..95 hours at sea) and arrives
    /// at the port (its city record is loaded); one that cannot is turned away (one text for a
    /// party with horses, another without) and goes back where it came from.
    /// </summary>
    private static MenuLine Passage(CardContext c, int port) =>
        new(_ =>
            {
                // C: the roll is drawn before the danger (checked against asm), and only in a hostile city.
                if (c.Hostile() && c.Random(100) <= QuaySafety(c))
                {
                    c.Next = Challenged;
                    return;
                }
                if (c.State.Party.Purse >= c.Variables.Money[port])
                {
                    c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[port]);
                    var wait = c.Random(3) + 1;
                    c.Variables.Numbers[0] = wait;
                    c.Screen.Text(9);
                    c.PassHours(wait);
                    var destination = c.City.Word(PortWords[port]);
                    c.State.CurrentLocation = destination;
                    // C: the original reads darkland.cty and darkland.dsc by hand for the port (its record and description).
                    c.LoadCity(destination);
                    c.PassHours(c.Random(0x30) + 0x30);
                    c.Next = Arrival;
                    return;
                }
                c.PassHours(1);
                c.Screen.Text(c.CountItemsWithFlags(Who.Everybody, Mounts) != 0 ? 7 : 8);
                c.Next = c.State.PreviousCard;
            },
            () => PassageHint(c, port));

    /// <summary>The party is welcome on a private ship (a lockpick-like item 0x04000040, standing 50+, or fame 250+).</summary>
    // orig: R051+0x216C (19F2:05CC) R051 seg 19F2 unmatched ("cannot print bytes")
    private static bool Welcome(CardContext c) =>
        c.CountItemsWithFlags(Who.Everybody, 0x04000040) != 0 || c.HereQuery(0) >= 0x32 || c.PartyGet(4) >= 0xFA;

    private static OptionHint PrivateChance(CardContext c)
    {
        int chance;
        if (c.Hostile())
        {
            chance = 100 - c.LocalPrice(0x64, 0x19);
            if (Welcome(c))
            {
                chance += 0x21;
            }
            if (c.PartyGet(4) >= 0xFA)
            {
                chance = 0;
            }
        }
        else
        {
            chance = 100;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    private static void SeekAPrivatePassage(CardContext c, int chance)
    {
        if (c.Hostile() && c.Random(100) <= chance)
        {
            c.Next = Challenged;
            return;
        }
        if (!Welcome(c))
        {
            c.PassHours(1);
            if (c.HereQuery(0) < 0x32 && c.PartyGet(4) < 0xFA)
            {
                c.Screen.Text(2);
            }
            else
            {
                ShowPicture(c, ArrestPicture);
                c.Screen.Text(4);
            }
            c.Next = c.State.PreviousCard;
            return;
        }
        c.EventHere(PrivatePassage, 4);
        c.PassHours(1);
        c.Next = Redraw;
    }
}
