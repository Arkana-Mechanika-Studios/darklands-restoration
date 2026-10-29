using Darklands.Core.Events;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Core.Rules;

/// <summary>
/// Quests: kind-8 events (tag = quest number) and their resolution into kind-0x24 events.
/// Quest 1 is the witches' sabbat, 2 a city's politics, 4 the dragon, 12 the mine; 3, 7 and
/// 10 are started by card scripts.
/// </summary>
public static class Quests
{
    public const int Sabbat = 1;
    public const int Politics = 2;
    public const int Dragon = 4;
    public const int Mine = 12;

    private const int Fact = EventKinds.LocationFact;

    /// <summary>
    /// Resolves the quests with <paramref name="tag"/> at <paramref name="loc"/> (−1 = any;
    /// <c>ev8_resolve</c>): each becomes a resolved quest (for a year) at its city, the
    /// city's standing changes by the quest's <see cref="TimedEvent.Data1"/> (unclamped), the
    /// party gets the reward of size <see cref="TimedEvent.Data2"/>; then the quests' facts
    /// (and the quests themselves) are cleared.
    /// </summary>
    // orig: ev8_resolve R029+0x3E06 (0E76:3E06) matched; tests: EventLogTests
    // C: event_new(f1a, f1e, f20, 0x24, f1a, type0, 0, 8760 (the address DS:2238), 0, f28); rep += f2a unclamped; give_reward(f2c); then f2e and f2c of the new event.
    // DIVERGENCE: when the log is full the original writes f2e/f2c through events[-1]; here nothing is written.
    public static void Resolve(GameState s, int tag, int loc)
    {
        var log = s.Events;
        var found = false;
        for (var i = log.Next(EventKinds.Quest, 0); i != -1; i = log.Next(EventKinds.Quest, i + 1))
        {
            var q = log[i]!;
            if ((tag != -1 && q.Tag != tag) || (loc != -1 && q.Location != loc))
            {
                continue;
            }
            found = true;
            var j = log.Create(q.Owner, q.Value, q.Extra, EventKinds.ResolvedQuest, q.Owner, q.Type, 0, EventLog.OneYear, 0, q.Tag);
            s.Locations[q.Value].Reputation = (short)(s.Locations[q.Value].Reputation + q.Data1);
            Practice.GiveReward(s, q.Data2);
            if (log[j] is { } resolved)
            {
                resolved.Data3 = q.Data3;
                resolved.Data2 = q.Location;
            }
        }
        if (found)
        {
            log.ClearFacts(tag, loc);
        }
    }

    /// <summary>
    /// The quests at an expiring event's location are resolved (expiry handler 3): resolved
    /// quest events (forever) and a standing change. The original then writes into an
    /// undefined slot; that write is left out.
    /// </summary>
    // orig: R029+0x4378 (0E76:4378) matched
    // DIVERGENCE: `events[k]->f2e = events[i]->f2e` writes through the uninitialised local k; left out.
    public static void ResolveAt(GameState s, TimedEvent expiring)
    {
        var log = s.Events;
        for (var i = log.Next(EventKinds.Quest, 0); i != -1; i = log.Next(EventKinds.Quest, i + 1))
        {
            var q = log[i]!;
            if (q.Location == expiring.Location)
            {
                log.Create(q.Owner, q.Value, q.Extra, EventKinds.ResolvedQuest, q.Owner, q.Type, 0, EventLog.Forever, 0, q.Tag);
                LocationRules.AdjustReputation(s, q.Value, q.Data1, q.Data1);
            }
        }
    }

    // ---- the monthly quests ---------------------------------------------------------

    /// <summary>
    /// Starts the dragon quest unless one is under way (<c>thunk 09C0:1F09</c>): the dragon's lair
    /// nearest to here (anywhere when in the wilderness) gets one of four dragon cards, a
    /// quest for a year with the nearest city, its facts, and the villages within 40/80 map
    /// units are ravaged (the first one shows the dragon when the quest is of the first sort).
    /// </summary>
    // orig: R039+0x11FC (1462:11FC) matched; tests: WorldRulesTests
    // C: nearest_loc_type(rand_mod(300), rand_mod(900), 15): arguments right to left, y is drawn first.
    public static void StartDragon(GameState s)
    {
        if (s.Events.Find(EventKinds.Quest, -1, -1, -1, Dragon, -1) != -1)
        {
            return;
        }
        var lair = NearHere(s, 300, 900, 0, Icons.DragonLair);
        var place = s.Locations[lair];
        var sort = s.Random.Below(4);
        place.ArrivalCard = sort switch { 0 => 0x9D, 1 => 0x9B, 2 => 0x9E, _ => 0x9C };
        var log = s.Events;
        var ev = log.Create(-2, lair, 0x5F, EventKinds.Quest, sort, LocationRules.NearestCity(s, place.X, place.Y), 0, EventLog.OneYear, 0, Dragon);
        var quest = log[ev];
        if (quest is not null)
        {
            quest.Data1 = s.Random.Below(20) + 30;
            quest.Data2 = 4;
            quest.Data3 = s.Random.Below(16);
        }
        log.Create(-2, lair, 0, 0x2A, 0, 10, 0, EventLog.OneYear, 0, 0);
        log.Create(-2, lair, 0, 0x2B, 0, 2, 0, EventLog.Forever, 0, 0);
        foreach (var type in new[] { 1, 3, 4, 7, 6 })
        {
            log.Create(-2, lair, 0, Fact, type, 0, 0, EventLog.OneYear, 0, Dragon);
        }
        var n = 0;
        for (var i = Location.CityCount; i < s.Locations.Count; i++)
        {
            var v = s.Locations[i];
            if (place.X - 40 <= v.X && place.X + 40 > v.X && place.Y - 80 <= v.Y && place.Y + 80 > v.Y && v.Icon == Icons.Village)
            {
                if (n++ == 0 && sort == 0)
                {
                    v.ArrivalCard = 0x9D;
                }
                else
                {
                    v.ArrivalCard = 0x88;
                    v.Flags |= 0x20;
                    v.Icon = Icons.RuinedVillage;
                }
            }
        }
        if (n == 0 && sort == 0 && quest is not null)
        {
            quest.Type = s.Random.Below(3) + 1;
        }
    }

    /// <summary>
    /// Starts the mine quest unless one is under way (<c>thunk 09C0:1F27</c>): the mine nearest to
    /// here (anywhere in the east half when in the wilderness) gets the mine card and a quest
    /// for a year.
    /// </summary>
    // orig: R039+0x15C2 (1462:15C2) matched
    // C: nearest_loc_type(rand_mod(300), rand_mod(600) + 300, 6): y first.
    public static void StartMine(GameState s)
    {
        if (s.Events.Find(EventKinds.Quest, -1, -1, -1, Mine, -1) != -1)
        {
            return;
        }
        var mine = NearHere(s, 300, 600, 300, Icons.Mines);
        var sort = s.Random.Below(3);
        s.Locations[mine].ArrivalCard = 0x2D;
        var ev = s.Events.Create(-2, mine, 0x5F, EventKinds.Quest, sort, 0x63, 0, EventLog.OneYear, 0, Mine);
        if (s.Events[ev] is { } quest)
        {
            quest.Data1 = s.Random.Below(10) + 40;
            quest.Data2 = 3;
            quest.Data3 = s.Random.Below(0x26);
        }
    }

    /// <summary>
    /// Starts the witches' sabbat quest unless one is under way (<c>thunk 09C0:1F1D</c>): the quest
    /// (forever), a fact that lasts until a feast day near this month (with a sabbat site as
    /// <see cref="TimedEvent.Data1"/>; its end starts the sabbat), and fact 2.
    /// </summary>
    // orig: R039+0x1B0C (1462:1B0C) matched; tests: PracticeAndItemsTests
    // DIVERGENCE: when the 300-slot log is full the original writes the quest's three data words through events[-1] (R039+0x1B61..0x1B81: the far pointer
    //   2E38:1B28, null in play, so into the interrupt vectors at 0000:002A..002E); here nothing is written.
    public static void StartSabbat(GameState s)
    {
        if (s.Events.Find(EventKinds.Quest, -1, -1, -1, Sabbat, -1) != -1)
        {
            return;
        }
        var log = s.Events;
        var ev = log.Create(-2, 0, 0x5F, EventKinds.Quest, 0, 0x63, 0, EventLog.Forever, 0, Sabbat);
        if (log[ev] is { } quest)
        {
            quest.Data1 = s.Random.Below(10) + 60;
            quest.Data2 = 6;
            quest.Data3 = 0;
        }
        var day = FeastDayNearThisMonth(s);
        var until = GameDate.Create(s.Date.Year, 0, day, 0);
        PlanSabbat(s, day, s.Date.DaysUntil(until));
        log.Create(-2, 0, 0x5F, Fact, 2, 0, 0, EventLog.Forever, 0, Sabbat);
    }

    /// <summary>The fact that ends on the sabbat's day and starts it at a random sabbat site.</summary>
    // orig: R039+0x1B0C (1462:1B0C) matched; R029+0x4534 (0E76:4534) matched
    // C: days_until(&d) * 24 is a 16-bit product; the site is drawn after the event exists.
    // DIVERGENCE: when the log is full the original writes the site through events[-1] (R039+0x1BFD, R029+0x4592: into 0000:002A, the INT 0Ah vector's
    //   segment); here nothing is written. No sabbat is planned from then on (the quest still exists, so nothing starts it again): SabbatSite gives -1.
    private static void PlanSabbat(GameState s, int day, int days)
    {
        var ev = s.Events.Create(-2, 0, 0x63, Fact, 4, day, (int)ExpiryHandler.SabbatBegins, days * 24,
            (int)EventFlags.CallExpiryHandler, Sabbat);
        // Arguments right to left: the y position is drawn first.
        var y = s.Random.Below(900);
        var x = s.Random.Below(300);
        var site = LocationRules.NearestOfType(s, x, y, Icons.WitchSabbat);
        if (s.Events[ev] is { } fact)
        {
            fact.Data1 = site;
        }
    }

    /// <summary>
    /// A sabbat begins (expiry handler 8): its site shows the sabbat card, a two-day fact ends
    /// it again, and the next sabbat is planned for a feast day (this year or next).
    /// </summary>
    // orig: R029+0x4534 (0E76:4534) matched
    public static void SabbatBegins(GameState s, TimedEvent e)
    {
        var site = e.Data1;
        s.Locations[site].ArrivalCard = 0x13E;
        var log = s.Events;
        var ending = log.Create(-2, 0, 0x63, Fact, 5, 0, (int)ExpiryHandler.SabbatEnds, 0x30, (int)EventFlags.CallExpiryHandler, Sabbat);
        if (log[ending] is { } end)
        {
            end.Data1 = site;
        }
        var day = FeastDayNearThisMonth(s);
        var year = s.Date.DayOfYear < day ? s.Date.Year : s.Date.Year + 1;
        PlanSabbat(s, day, s.Date.DaysUntil(GameDate.Create(year, 0, day, 0)));
    }

    /// <summary>The sabbat is over (expiry handler 7): the site shows its after-sabbat card.</summary>
    // orig: R029+0x450A (0E76:450A) matched
    public static void SabbatEnds(GameState s, TimedEvent e) => s.Locations[e.Data1].ArrivalCard = 0x13D;

    /// <summary>
    /// A feast day (day of the year) near the current month (<c>thunk 09C0:1F81</c>): one of 80, 103,
    /// 145, 162, 172, 265, 304 and 360 by the month, chosen at random.
    /// </summary>
    // orig: R039+0x1C22 (1462:1C22) matched
    // DORMANT: case 0 (cur_month + 1 == 0) is the default's draw; it would need a month of -1.
    public static int FeastDayNearThisMonth(GameState s)
    {
        var r = s.Random;
        return (s.Date.Month + 1) switch
        {
            3 => r.Below(2) == 0 ? 0x91 : 0x67,
            4 => r.Below(2) == 0 ? 0xA2 : 0x91,
            5 => r.Below(3) != 0 ? 0xAC : 0xA2,
            6 or 7 or 8 => r.Below(2) != 0 ? 0x109 : 0x130,
            9 => r.Below(2) != 0 ? 0x130 : 0x168,
            10 or 11 => r.Below(2) != 0 ? 0x168 : 0x50,
            _ => r.Below(2) != 0 ? 0x50 : 0x67,
        };
    }

    /// <summary>
    /// The location of the given icon nearest to the current location, or — in the wilderness
    /// — to a random point (y drawn first: rnd(<paramref name="rangeY"/>) + <paramref name="offsetY"/>, then x).
    /// </summary>
    // orig: R039+0x11FC (1462:11FC) matched; R039+0x15C2 (1462:15C2) matched
    private static int NearHere(GameState s, int rangeX, int rangeY, int offsetY, int icon)
    {
        if (s.Here is { } here)
        {
            return LocationRules.NearestOfType(s, here.X, here.Y, icon);
        }
        var y = s.Random.Below(rangeY) + offsetY;
        var x = s.Random.Below(rangeX);
        return LocationRules.NearestOfType(s, x, y, icon);
    }

    // ---- the quests' details --------------------------------------------------------

    // orig: R039+0x1510 (1462:1510) matched
    // C: without a quest the function falls through with AX = event_find's -1 (checked against the asm): -1, as here.
    private static int Field(GameState s, int tag, Func<TimedEvent, int> field) =>
        s.Events[s.Events.Find(EventKinds.Quest, -1, -1, -1, tag, -1)] is { } q ? field(q) : -1;

    /// <summary>The dragon quest's detail (<see cref="TimedEvent.Data3"/>; <c>thunk 09C0:212F</c>), −1 without one (the original: undefined).</summary>
    // orig: R039+0x1510 (1462:1510) matched
    public static int DragonDetail(GameState s) => Field(s, Dragon, q => q.Data3);

    /// <summary>The dragon quest's sort (<c>thunk 09C0:1FE5</c>).</summary>
    // orig: R039+0x153E (1462:153E) matched
    public static int DragonSort(GameState s) => Field(s, Dragon, q => q.Type);

    /// <summary>The dragon's lair (<c>thunk 09C0:2157</c>).</summary>
    // orig: R039+0x156C (1462:156C) matched
    public static int DragonLair(GameState s) => Field(s, Dragon, q => q.Location);

    /// <summary>The dragon is slain (<c>thunk 09C0:216B</c>): its lair's card becomes 0x62 (closed), the dragon quest here is resolved.</summary>
    // orig: R039+0x159A (1462:159A) matched
    public static void DragonSlain(GameState s)
    {
        s.Locations[DragonLair(s)].ArrivalCard = 0x62;
        Resolve(s, Dragon, s.CurrentLocation);
    }

    /// <summary>The mine quest's sort (<c>thunk 09C0:2193</c>).</summary>
    // orig: R039+0x16B4 (1462:16B4) matched
    public static int MineSort(GameState s) => Field(s, Mine, q => q.Type);

    /// <summary>The mine quest's detail (<c>thunk 09C0:219D</c>).</summary>
    // orig: R039+0x16E2 (1462:16E2) matched
    public static int MineDetail(GameState s) => Field(s, Mine, q => q.Data3);

    /// <summary>The planned sabbat's site (<c>thunk 09C0:20FD</c>), −1 if none.</summary>
    // orig: R039+0x1CFE (1462:1CFE) matched
    // C: no planned sabbat (AX stays -1, R039+0x1D15) only when the log was full at a planning (PlanSabbat) or after 31.12.1499 (the forever-end bug at EventLog.Create:
    //   the quest's end clears its facts); the start card and every sabbat's begin plan the next one. The card oracle's start states have no sabbat
    //   quest at all, so its $SabbatLocation reads locations[-1] (D-09b57b).
    public static int SabbatSite(GameState s) =>
        s.Events[s.Events.Find(Fact, 4, -1, -1, Sabbat, -1)] is { } f ? f.Data1 : -1;

    /// <summary>The planned sabbat's day of the month (<c>thunk 09C0:21A7</c>).</summary>
    // orig: R039+0x1CD2 (1462:1CD2) matched; mday_of_yday R029+0x5892 (1367:0982) matched
    // C: declared void: the day comes back in AX from mday_of_yday.
    public static int SabbatDayOfMonth(GameState s) => DayOfMonth(s.Events.FactValueAnywhere(Sabbat, 4));

    /// <summary>The planned sabbat's month (<c>thunk 09C0:20DF</c>).</summary>
    // orig: R039+0x1CE8 (1462:1CE8) matched; month_of_yday R029+0x5848 (1367:0938) matched
    public static int SabbatMonth(GameState s) => MonthOf(s.Events.FactValueAnywhere(Sabbat, 4));

    /// <summary>The month (0..11) of a day of the year.</summary>
    public static int MonthOf(int dayOfYear) => SplitDayOfYear(dayOfYear).Month;

    /// <summary>The day of the month of a day of the year (a day equal to the month's length stays in it).</summary>
    public static int DayOfMonth(int dayOfYear) => SplitDayOfYear(dayOfYear).Day;

    // orig: month_of_yday R029+0x5848 (1367:0938) matched; mday_of_yday R029+0x5892 (1367:0982) matched
    private static (int Month, int Day) SplitDayOfYear(int d)
    {
        while (d >= GameDate.DaysPerYear)
        {
            d -= GameDate.DaysPerYear;
        }
        var m = 0;
        while (GameDate.DaysInMonth(m) < d)
        {
            d -= GameDate.DaysInMonth(m);
            if (++m > 11)
            {
                m = 0;
            }
        }
        return (m, d);
    }

    // ---- quests started by card scripts ---------------------------------------------

    /// <summary>
    /// A quest of tag 3 for <paramref name="owner"/> about location <paramref name="target"/>
    /// (<c>thunk 09C0:2175</c>): if one about the target exists, another is added and the first one's
    /// <see cref="TimedEvent.Data1"/>/<see cref="TimedEvent.Data2"/> raised to at least
    /// <paramref name="value"/>/<paramref name="level"/> (≤ 4), returning 1; otherwise a new
    /// quest with value, level and <paramref name="detail"/>, and facts 2–4 and a 0x2B event at
    /// <paramref name="factLocation"/> (the card's $LocName location); returns the 0x2B event's slot
    /// (what the original leaves in AX).
    /// </summary>
    // orig: R039+0x10B6 (1462:10B6) matched; tests: FidelityFixTests
    public static int StartQuest3(GameState s, int owner, int target, int type, int value, int level, int detail, int factLocation)
    {
        level = Math.Min(level, 4);
        var log = s.Events;
        var i = log.Find(EventKinds.Quest, -1, target, -1, 3, -1);
        if (i != -1)
        {
            log.Create(owner, target, 0x5F, EventKinds.Quest, type, s.CurrentLocation, 0, 9998, 0, 3);
            var first = log[i]!;
            first.Data1 = Math.Max(first.Data1, value);
            first.Data2 = Math.Max(first.Data2, level);
            return 1;
        }
        if (log[log.Create(owner, target, 0x5F, EventKinds.Quest, type, s.CurrentLocation, 0, 9998, 0, 3)] is { } q)
        {
            q.Data1 = value;
            q.Data2 = level;
            q.Data3 = detail;
        }
        foreach (var t in new[] { 2, 3, 4 })
        {
            log.Create(-2, factLocation, 0, Fact, t, 0, 0, 9998, 0, 3);
        }
        // C: no return statement: AX still holds the last event_new's result (checked against
        // the asm: nothing after the call touches AX), i.e. the 0x2B event's slot.
        return log.Create(-2, factLocation, 0, 0x2B, 0, 1, 0, EventLog.Forever, 0, 0);
    }

    /// <summary>
    /// Starts quest 7 here unless one exists (<c>thunk 09C0:20D5</c>): forever, with a random saint
    /// (<see cref="TimedEvent.Data1"/> 0..135) and item (<see cref="TimedEvent.Data3"/> 139..170), and its fact 5.
    /// </summary>
    // orig: R039+0x1F00 (1462:1F00) matched
    // C: the item (rand_mod(0x20) + 0x8B) is drawn before the saint (rand_mod(0x88)).
    public static void StartQuest7(GameState s)
    {
        if (s.Events.QuestExists(7))
        {
            return;
        }
        var item = s.Random.Below(0x20) + 0x8B;
        var saint = s.Random.Below(0x88);
        var log = s.Events;
        if (log[log.Create(-2, s.CurrentLocation, 0x5F, EventKinds.Quest, 0, 0, 0, EventLog.Forever, 0, 7)] is { } q)
        {
            q.Data1 = saint;
            q.Data2 = 2;
            q.Data3 = item;
        }
        log.Create(-2, s.CurrentLocation, 0x5F, Fact, 5, 0, 0, EventLog.Forever, 0, 7);
    }

    /// <summary>
    /// Starts quest 10 for region kind <paramref name="region"/> (<c>thunk 09C0:21B1</c>): a random
    /// sort and item by region, a kind of place by sort, a far place of that kind from here
    /// (<paramref name="site"/>, the card's $LocName), and the quest (forever) owned by the
    /// region. Returns the quest's slot. Region kinds the original has no table for leave the
    /// sort undefined; here they use sort 0.
    /// </summary>
    // orig: R039+0x1712 (1462:1712) matched
    // DIVERGENCE: region kinds 1, 2, 9 and others leave k and v uninitialised in the original; here sort 0 and item 0.
    // DIVERGENCE: in the wilderness the original reads locations[-1]; here position 0, 0.
    // C: the var_loc the original sets is CardContext's $LocName (the site returned here).
    public static int StartQuest10(GameState s, int region, out int site)
    {
        var r = s.Random;
        (int Sort, int Item)[] table = region switch
        {
            3 => [(0, 0x81), (1, 0x82), (2, 0x83), (3, 0x84), (6, 0x7F)],
            7 => [(0, 0x81), (1, 0x82), (3, 0x84), (4, 0x85), (6, 0x7F), (7, 0x87), (8, 0x88), (9, 0x89), (10, 0x8A), (11, 0x78)],
            6 or 8 => [(0, 0x81), (1, 0x82), (3, 0x84), (4, 0x85), (7, 0x87), (8, 0x88), (9, 0x89), (10, 0x8A), (11, 0x78)],
            10 => [(0, 0x81), (1, 0x82), (3, 0x84), (4, 0x85), (6, 0x7F), (7, 0x87), (8, 0x88), (9, 0x89)],
            0 or 4 => [(0, 0x81), (1, 0x82), (3, 0x84), (4, 0x85), (7, 0x87), (8, 0x88), (9, 0x89)],
            5 => [(3, 0x84), (4, 0x85), (5, 0x86), (7, 0x87), (8, 0x88), (9, 0x89)],
            _ => [],
        };
        var (sort, item) = table.Length > 0 ? table[r.Below(table.Length)] : (0, 0);
        var icon = sort switch
        {
            0 or 1 => new[] { 0x10, 5, 0x11 }[r.Below(3)],
            2 => 0x12,
            3 => new[] { 0xD, 0, 0x12, 0x13 }[r.Below(4)],
            4 => r.Below(2) != 0 ? 0xD : 0x14,
            5 => 0x13,
            7 => new[] { 0x10, 0x11, 5, 0x12, 0x13, 0xD }[r.Below(6)],
            8 => new[] { 0xD, 0x14, 0x13 }[r.Below(3)],
            _ => 0,
        };
        var here = s.Here ?? new Location();
        site = LocationRules.FarOfType(s, here.X, here.Y, icon);
        var ev = s.Events.Create(region, site, 0x5F, EventKinds.Quest, sort, s.CurrentLocation, 0, EventLog.Forever, 0, 10);
        if (s.Events[ev] is { } q)
        {
            q.Data1 = r.Below(10) + 10;
            q.Data2 = 2;
            q.Data3 = item;
        }
        return ev;
    }

    /// <summary>
    /// When quest 10 is under way here (<c>thunk 09C0:214D</c>): the leader gets the item of the
    /// quest about <paramref name="target"/> and that quest is resolved.
    /// </summary>
    // orig: R039+0x1AB0 (1462:1AB0) matched
    // DIVERGENCE: without a quest about the target the original gives events[-1]->f2e (undefined); here no item.
    public static void FinishQuest10(GameState s, int target)
    {
        if (!s.Events.QuestAt(10, s.CurrentLocation))
        {
            return;
        }
        var i = s.Events.Find(EventKinds.Quest, -1, target, -1, 10, -1);
        if (s.Events[i] is { } q)
        {
            Inventory.Add(s, Characters.Who.Leader, q.Data3);
        }
        Resolve(s, 10, target);
    }
}

/// <summary>Map icons of locations the rules look for.</summary>
public static class Icons
{
    public const int City = 0x00;
    public const int RaubritterCastle = 0x02;
    public const int Mines = 0x06;
    public const int Village = 0x08;
    public const int RuinedVillage = 0x09;
    public const int DragonLair = 0x0F;
    public const int WitchSabbat = 0x15;
    public const int RuinedRaubritterCastle = 0x1A;
}
