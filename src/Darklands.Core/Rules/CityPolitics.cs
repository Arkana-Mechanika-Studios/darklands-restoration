using Darklands.Core.Events;
using Darklands.Core.Time;

namespace Darklands.Core.Rules;

/// <summary>
/// A city's two sides (the facts of group 2: fact 1 holds the bits of side −1, fact 2 those
/// of side 1), their strengths, an uprising's outcome, and the daily drift of a city's
/// politics. The side functions work on the <em>current</em> location.
/// </summary>
public static class CityPolitics
{
    /// <summary>The fact group of a city's politics.</summary>
    public const int Group = 2;

    /// <summary>The 15 side bits, in the order the original sets them up.</summary>
    private static readonly int[] AllBits =
        [0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x80, 0x40, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, unchecked((short)0x8000)];

    /// <summary>The 11 bits that count for a side's strength.</summary>
    private static readonly int[] StrengthBits = [0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x80, 0x40, 0x200, 0x400, 0x1000];

    /// <summary>Which side holds <paramref name="bit"/> here: −1, 1, or 0 (<c>place-side-read</c>).</summary>
    // orig: place-side-read-inner R039+0x0A34 (1462:0A34) matched; tests: WorldRulesTests
    public static int SideOf(GameState s, int bit)
    {
        var minus = s.Events.FactValue(Group, 1, s.CurrentLocation);
        var plus = s.Events.FactValue(Group, 2, s.CurrentLocation) & bit;
        return (bit & minus) != 0 ? -1 : plus != 0 ? 1 : 0;
    }

    /// <summary>Gives <paramref name="bit"/> to side −1, side 1, or neither (<c>place-side-write</c>).</summary>
    // orig: place-side-write-inner R039+0x0A7E (1462:0A7E) matched; tests: WorldRulesTests
    public static void SetSide(GameState s, int bit, int side)
    {
        var here = s.CurrentLocation;
        switch (side)
        {
            case -1:
                s.Events.Update(-2, here, EventKinds.LocationFact, 2, ~bit, Group, EventUpdate.And);
                s.Events.Update(-2, here, EventKinds.LocationFact, 1, bit, Group, EventUpdate.Or);
                break;
            case 1:
                s.Events.Update(-2, here, EventKinds.LocationFact, 1, ~bit, Group, EventUpdate.And);
                s.Events.Update(-2, here, EventKinds.LocationFact, 2, bit, Group, EventUpdate.Or);
                break;
            default:
                s.Events.Update(-2, here, EventKinds.LocationFact, 1, ~bit, Group, EventUpdate.And);
                s.Events.Update(-2, here, EventKinds.LocationFact, 2, ~bit, Group, EventUpdate.And);
                break;
        }
    }

    /// <summary>How many of the 15 bits belong to neither side.</summary>
    // orig: R039+0x0B12 (1462:0B12) matched
    public static int NeutralCount(GameState s) => AllBits.Count(b => SideOf(s, b) == 0);

    /// <summary>
    /// A side's strength (<c>side-one-power</c> for −1, <c>side-two-power</c> for 1): 2 per bit it
    /// holds and 1 per neutral bit, of 11 bits, doubled; 1 when nothing.
    /// </summary>
    // orig: side-two-power-inner R039+0x0BF6 (1462:0BF6) matched; side-one-power-inner R039+0x0D46 (1462:0D46) matched; tests: WorldRulesTests
    // C: all eleven side tests come before the eleven neutral tests (the scans' last slot ends the same).
    public static int Strength(GameState s, int side)
    {
        var v = StrengthBits.Sum(b => SideOf(s, b) == side ? 2 : 0) + StrengthBits.Count(b => SideOf(s, b) == 0);
        return v != 0 ? v << 1 : 1;
    }

    /// <summary>A side's strength in percent of the other's (<c>side-one-share</c> for −1, <c>side-two-share</c> for 1).</summary>
    // orig: side-one-share-inner R039+0x0E96 (1462:0E96) matched; side-two-share-inner R039+0x0EAA (1462:0EAA) matched; tests: WorldRulesTests
    // C: the other side's strength is computed first (local-in-si), then this side's * 100 / it.
    public static int Percent(GameState s, int side)
    {
        var other = Strength(s, -side);
        return Strength(s, side) * 100 / other;
    }

    /// <summary>
    /// The outcome of an uprising here (<c>fight-resolution</c>): <paramref name="partySide"/>
    /// is the side the party took, <paramref name="winner"/> the side that won, and
    /// <paramref name="leadingSide"/> the side that started it (0: none; then a chance is
    /// computed and a number drawn, both unused). Unrest for 4 weeks, the city's politics
    /// quest is resolved, the uprising flag set, and the standing changes; helping the
    /// winning side without a pact (facts 0x24 / 0x25) earns fame.
    /// </summary>
    // orig: fight-resolution-inner R039+0x0EBE (1462:0EBE) matched
    // BUG: open-quest-resolve (R029+0x3E06, called at R039+0x0F3E) clears the group's facts here, the pacts 0x24 and 0x25 included, before the
    //   branches test them (place-flag, R039+0x0F77 and after): with the politics quest under way the pact branches never run.
    // C: with c == 0 side-two-share is called up to six times and pct is set to 0 either way; only the roll-below(100) draw matters.
    // C: the reward (award-prize) comes before the standing change (its draws).
    public static void ConflictOutcome(GameState s, int partySide, int winner, int leadingSide)
    {
        if (leadingSide == 0)
        {
            _ = Percent(s, 1);   // the original works out a chance from it, then ignores it
            s.Random.Below(100);
        }
        var here = s.CurrentLocation;
        s.Events.CreateHere(EventKinds.Unrest, 0x2A0);
        Quests.Resolve(s, Group, here);
        s.Locations[here].Flags |= World.Location.UprisingFlag;
        bool Pact(int fact) => s.Events.Fact(Group, fact, here);
        void Standing(int a, int b) => LocationRules.AdjustReputation(s, here, a, b);
        void Reward(int size) => Practice.GiveReward(s, size);

        switch (leadingSide, partySide)
        {
            case (-1, -1) or (1, 1):
                var loser = -leadingSide;
                if (winner == loser)
                {
                    if (Pact(loser == 1 ? 0x24 : 0x25))
                    {
                        Standing(-10, -30);
                    }
                    else
                    {
                        Standing(-30, -60);
                    }
                }
                else if (winner == leadingSide)
                {
                    if (Pact(leadingSide == -1 ? 0x25 : 0x24))
                    {
                        Standing(-30, -70);
                    }
                    else
                    {
                        Reward(3);
                        Standing(25, 70);
                    }
                }
                break;
            case (1, -1) or (-1, 1):
                s.Events.CreateHere(leadingSide == 1 ? 0x46 : 0x45, 0xA8);
                var sideFact = partySide == -1 ? 0x24 : 0x25;
                var otherFact = partySide == -1 ? 0x25 : 0x24;
                if (winner == -partySide)
                {
                    if (Pact(sideFact))
                    {
                        Standing(-10, -30);
                    }
                    else
                    {
                        Reward(1);
                        Standing(15, 25);
                    }
                }
                else if (winner == partySide)
                {
                    if (Pact(otherFact))
                    {
                        Standing(-20, -60);
                    }
                    else
                    {
                        Standing(-15, -35);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Sets up a city's politics unless it has them (a politics quest here) or its flags are 2:
    /// the politics quest (56 days), the daily politics fact (first at 05:00), facts 6 and 7
    /// (21 and 35 days), a market weekday (bit of fact 0x17: a day with a shop), facts
    /// 0x14, 3, 5, 4, 9, 10, 0x23 (a random 0..99), 1 and 2, and each of the 15 bits to a
    /// random side.
    /// </summary>
    // orig: R039+0x0558 (1462:0558) matched; tests: WorldRulesTests
    // C: place-query(loc, 0x21) and place-query(loc, 0) are called first and their results dropped (pure reads).
    // DIVERGENCE: `while (!place-query(loc, d + 3))` loops forever in a city without any shop; here the search stops after seven days.
    // BUG: the facts made here are 0xC, 6, 7, 0x17, 0x14, 3, 5, 4, 9, 0xA, 0x23, 1 and 2 (R039+0x0558): none of type 8, the guild secret the
    //   guild cards (R062..R065) clear and read, so that fact never exists.
    public static void SetUpCity(GameState s, int loc)
    {
        if (s.Events.QuestAt(Group, loc) || LocationRules.Query(s, loc, 32) == 2)
        {
            return;
        }
        var log = s.Events;
        const int fact = EventKinds.LocationFact;
        const int flags = (int)EventFlags.CallExpiryHandler;
        log.Create(-2, loc, 0x5F, EventKinds.Quest, 0, loc, 0, 0x540, 0, Group);
        log.Create(-2, loc, 0x5A, fact, 0xC, 0x63, (int)ExpiryHandler.DailyCityPolitics, TimeOfDay.HoursUntil(s.Date.Hour, 5), flags, Group);
        log.Create(-2, loc, 0x5A, fact, 6, 0x63, 0, 0x1F8, 0, Group);
        log.Create(-2, loc, 0x5A, fact, 7, 0x63, 0, 0x348, 0, Group);
        var day = s.Random.Below(7);
        for (var tries = 0; tries < 7 && LocationRules.Query(s, loc, day + 3) == 0; tries++)
        {
            day = (day + 1) % 7;
        }
        log.Create(-2, loc, 0x5A, fact, 0x17, 1 << (sbyte)day, 0, 0x540, 0, Group);
        foreach (var type in new[] { 0x14, 3, 5, 4, 9, 0xA })
        {
            log.Create(-2, loc, 0x5A, fact, type, 0, 0, 0x540, 0, Group);
        }
        log.Create(-2, loc, 0x5A, fact, 0x23, s.Random.Below(100), 0, 0x540, 0, Group);
        log.Create(-2, loc, 0x5A, fact, 1, 0, 0, 0x540, 0, Group);
        log.Create(-2, loc, 0x5A, fact, 2, 0, 0, 0x540, 0, Group);
        foreach (var bit in AllBits)
        {
            log.Update(-2, loc, fact, s.Random.Below(2) != 0 ? 1 : 2, bit, Group, EventUpdate.Or);
        }
    }

    /// <summary>
    /// A city's day passes (expiry of its daily politics fact): facts 3–5 are cleared, fact
    /// 0x23 gets a new random value, maybe one random bit changes side, every seventh day the
    /// stronger side may stir up unrest (fact 0x15 or 0x16 for 20 days), and the next day is
    /// planned. Faithful quirks: the side change and the strengths use the <em>current</em>
    /// location, and the seventh-day test the current city record's seed.
    /// </summary>
    // orig: R029+0x4180 (0E76:4180) matched; tests: WorldRulesTests; FidelityFixTests
    // BUG: place-side-read/place-side-write and side*_percent work on location-now, not the event's city (kept).
    // C: bit = 1 << roll-below(16) is a 16-bit int (0x8000 is negative) compared with the market day fact.
    public static void DailyPolitics(GameState s, TimedEvent e)
    {
        var city = e.Location;
        var log = s.Events;
        const int fact = EventKinds.LocationFact;
        var marketDay = log.FactValue(Group, 0x17, city);
        var bit = 1 << s.Random.Below(16);
        log.Update(e.Owner, city, fact, 5, 0, Group, EventUpdate.And);
        log.Update(e.Owner, city, fact, 4, 0, Group, EventUpdate.And);
        log.Update(e.Owner, city, fact, 3, 0, Group, EventUpdate.And);
        log.Update(-2, city, fact, 0x23, s.Random.Below(100), Group, EventUpdate.Set);
        if (s.Random.Below(2) != 0 && marketDay != (short)bit)
        {
            SetSide(s, (short)bit, -SideOf(s, (short)bit));
        }
        // C: place-query(city, 0x21) + day-now is a 16-bit int: a seed near 32767 wraps before the % 7.
        if ((short)(LocationRules.Query(s, city, 33) + s.Date.Day) % 7 == 0)
        {
            int strength;
            int unrest;
            // BUG: setup makes no fact 0 (the leaning); the scan (EventLog.Next) finds the politics quest, whose value is the city's number, so
            //   the leaning is that number: 1 is read as "leaning 1" for the city numbered 1, anything else but -1 and 1 as "none".
            var leaning = log.FactValue(Group, 0, city);
            if (leaning == -1)
            {
                strength = Percent(s, 1);
                unrest = 0x15;
            }
            else if (leaning == 1)
            {
                strength = Percent(s, -1);
                unrest = 0x16;
            }
            else
            {
                var minus = Percent(s, -1);
                if (Percent(s, 1) > minus)
                {
                    strength = Percent(s, 1);
                    unrest = 0x15;
                }
                else
                {
                    strength = minus;
                    unrest = 0x16;
                }
            }
            if (!log.Fact(Group, 7, city) || (!log.Fact(Group, 6, city) && strength > 0x96) || strength > 0x10B)
            {
                log.Create(-2, city, 0, fact, unrest, 0x63, 0, 0x1E0, 0, Group);
            }
        }
        log.Create(-2, city, 0, fact, 0xC, 0x63, (int)ExpiryHandler.DailyCityPolitics, 0x18, (int)EventFlags.CallExpiryHandler, Group);
    }
}
