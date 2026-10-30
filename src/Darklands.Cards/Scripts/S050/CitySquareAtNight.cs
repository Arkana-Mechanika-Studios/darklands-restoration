namespace Darklands.Cards.Scripts;

/// <summary>
/// "$citys01": the city square at night. Past the night watch the party goes to the town
/// hall's back ways (card 0x6D), the council's locked hall, a prisoner's cell window (when
/// a fellow prisoner is held), the university, or back to the main street or
/// the side streets. Anyone seen meets the night encounter. Afterwards, the city's politics
/// may call the party aside.
/// </summary>
// orig: R050+0x0C48 (18FC:0008) matched; tests: CardSmokeTests
// DORMANT: R050+0x149E (18FC:085E) is an empty init. The option table is not reset
//   here: lines 7..19 keep what the card before left.
public sealed class CitySquareAtNight : Card
{
    public const int CardNumber = 0x19;

    private const int Picture = 107;

    private const int MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A, CitySquare = 0x12,
        LockedCouncilHall = 0x2C, University = 0x31, PoliticsCalls = 0x37, NightEncounter = 0x3C, TownHallAtNight = 0x6D,
        Hall117 = 0x75, PrisonWindow = 0x11E;

    /// <summary>Events: the square's watch alerted (0x14), a fellow prisoner (0x4D), politics settled (0x3E).</summary>
    private const int SquareAlert = 0x14, FellowPrisoner = 0x4D, PoliticsSettled = 0x3E, Politics = 2;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(6);
        Show(c, Picture);
        var council = c.HereQuery(0x18) != 0 ? OptionState.Enabled : OptionState.Hidden;
        var university = c.HereQuery(0x1B) != 0 ? OptionState.Enabled : OptionState.Hidden;
        var prison = c.EventAtHere(FellowPrisoner) ? OptionState.Enabled : OptionState.Hidden;
        c.StandingSeen = c.Reputation(c.Here);
        // C: the result (the original's DS:58EC) is never used, but its random draw is made.
        _ = c.LocalPrice(0x14, 3);

        var menu = Menu(c, c.EventAtHere(SquareAlert) ? 1 : 0);
        menu.Enable(0, 1, 2, 4, 5, 6);
        menu[1] = council;
        menu[2] = prison;
        menu[4] = university;
        c.Next = Number;
        var next = menu.Run(
            // orig: R050+0x1028 (18FC:03E8) + R050+0x0FDE (18FC:039E) R050 seg 18FC matched
            Sneak(c, 25, 20, () => { c.State.SavedCard2 = CardNumber; c.Next = TownHallAtNight; }),
            // orig: R050+0x10D2 (18FC:0492) + R050+0x1086 (18FC:0446) R050 seg 18FC matched
            Sneak(c, 28, 20, () => { c.PassHours(0); c.Next = LockedCouncilHall; }),
            // orig: R050+0x1174 (18FC:0534) + R050+0x1130 (18FC:04F0) R050 seg 18FC matched
            Sneak(c, 20, 30, () => c.Next = PrisonWindow),
            // orig: R050+0x121E (18FC:05DE) + R050+0x11D2 (18FC:0592) R050 seg 18FC matched
            // DORMANT: card-option-flag-3 is set to 0 and never to 1: this line (card 0x75) is never offered.
            Sneak(c, 30, 35, () => { c.PassHours(0); c.Next = Hall117; }),
            // orig: R050+0x12D0 (18FC:0690) + R050+0x127C (18FC:063C) R050 seg 18FC matched
            // C: "daylight-test() ? 0x31 : 0x31": the university either way.
            Sneak(c, 50, 20, () => { c.PassHours(1); c.Next = University; }),
            // orig: R050+0x1388 (18FC:0748) + R050+0x132E (18FC:06EE) R050 seg 18FC matched
            Sneak(c, 45, 20, () => { c.PassHours(1); c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight; }),
            // orig: R050+0x1440 (18FC:0800) + R050+0x13E6 (18FC:07A6) R050 seg 18FC matched
            Sneak(c, 65, 20, () => { c.PassHours(1); c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight; }));
        return ThePoliticsMayIntervene(c, next);
    }

    /// <summary>
    /// A running value from 100: each member lowers it to his Stealth when lower, and it grows
    /// by <paramref name="step"/> after each member; less (by <paramref name="alertPenalty"/>)
    /// when the price events are exactly the first one, or the square's watch is alerted.
    /// </summary>
    // C: the step is added inside the loop; no member's status is checked.
    private static OptionHint StealthChance(CardContext c, int step, int alertPenalty)
    {
        var value = 100;
        for (var i = 0; i < c.PartySize; i++)
        {
            var stealth = c.Skill(i, 0x0F);
            if (stealth < value)
            {
                value = stealth;
            }
            value += step;
        }
        if (c.PriceEvents() == 1 || c.EventAtHere(SquareAlert))
        {
            value -= alertPenalty;
        }
        return OptionHint.ChanceOf(Math.Clamp(value, 0, 99), OptionHint.WholeParty);
    }

    private static MenuLine Sneak(CardContext c, int step, int alertPenalty, Action arrive) =>
        new(() =>
            {
                // (the line's chance is asked here, not rated by the menu: the draw comes first and the line has no pop-up.)
                if (c.Random(100) <= StealthChance(c, step, alertPenalty).Chance)
                {
                    arrive();
                    return;
                }
                c.Events.Renew(-2, c.Here, 0, SquareAlert, 1, 0x63, 0, 32, 0, 0);
                c.State.SavedCard = CardNumber;
                c.Next = NightEncounter;
            },
            () => StealthChance(c, step, alertPenalty));

    /// <summary>
    /// Leaving: an agent of the city's politics may take the party aside (likelier for a weak
    /// party, by night, and when the faction's side holds the city), then back to the square;
    /// an uprising under way or decided takes over.
    /// </summary>
    private static int ThePoliticsMayIntervene(CardContext c, int next)
    {
        var here = c.Here;
        var events = c.Events;
        if (events.QuestAt(Politics, here))
        {
            int chance;
            if (events.FactValue(Politics, 0, here) == 1)
            {
                chance = c.IsDaylight ? 15 : 25;
                chance += -(c.PartyMaxAttribute(3) - 33);
            }
            else
            {
                chance = 0x3A - c.PartyMaxAttribute(3);
            }
            if (c.Random(100) <= chance)
            {
                c.State.SavedCard = CitySquare;
                next = PoliticsCalls;
            }
        }
        if (events.Fact(Politics, 0x15, here) && events.FactValue(Politics, 0, here) == 1 && !c.EventAtHere(PoliticsSettled))
        {
            next = 0xBD;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) == -1 && !c.EventAtHere(PoliticsSettled))
        {
            next = 0xBE;
        }
        if (events.Fact(Politics, 0x15, here) && events.FactValue(Politics, 0, here) == 1)
        {
            next = 0x80;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) == -1)
        {
            next = 0x7B;
        }
        c.Next = next;
        return next;
    }
}
