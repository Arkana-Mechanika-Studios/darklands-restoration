using Darklands.Core.Events;
using Darklands.Core.World;

namespace Darklands.Core.Rules;

/// <summary>
/// Locations: standing, the <c>loc_query</c>/<c>loc_set</c> values, hostility, local prices,
/// and finding places on the map. Codes 2..11, 15..35, 40 and 41 read the <em>current city
/// record</em> whatever location is asked about, as in the original.
/// </summary>
public static class LocationRules
{
    // ---- standing -------------------------------------------------------------------

    /// <summary>The party's standing at a location, −99..99 (<c>loc_reputation</c>).</summary>
    // orig: loc_reputation R029+0x199C (0E76:199C) matched
    public static int Reputation(GameState s, int loc) => s.Locations[loc].Reputation;

    // orig: R029+0x19B4 (0E76:19B4) matched
    public static void SetReputation(GameState s, int loc, int value) => s.Locations[loc].Reputation = (short)value;

    /// <summary>
    /// A random change of standing (<c>loc_reputation_adj</c>): with chance (100 − |standing|) %
    /// it moves by |a| + rnd(|b| − |a|), up if <paramref name="b"/> &gt; 0, else down, kept in
    /// −99..99. Returns the (possibly unchanged) standing.
    /// </summary>
    // orig: loc_reputation_adj R029+0x19D0 (0E76:19D0) matched; tests: Oracle:rules.json loc_reputation_adj; WorldRulesTests
    // C: the rise adds a (not |a|): r += rand_mod(|b| - |a|) + a; the fall subtracts |a| + rand_mod(|b| - |a|); the chance draw comes first.
    // C: loc -1 (the open country) reads locations[-1], the last four bytes of enemy_items (2E38:10BA, 0 in play: a null far pointer),
    // C: so rep is the word at 0000:0012 (NullPage.Int4Segment): |rep| > 100, the test fails, nothing is written, but the chance is drawn.
    public static int AdjustReputation(GameState s, int loc, int a, int b)
    {
        if (loc == GameState.Wilderness)
        {
            // DORMANT: a segment word of 100 or less at 0000:0012 would pass the test and move that null record's rep (a write into the vector table).
            _ = s.Random.Below(100);
            return NullPage.Int4Segment;
        }
        var place = s.Locations[loc];
        var r = place.Reputation;
        if (100 - Math.Abs(place.Reputation) >= s.Random.Below(100))
        {
            var low = Math.Abs(a);
            var high = Math.Abs(b);
            r = b > 0 ? r + s.Random.Below(high - low) + a : r - (low + s.Random.Below(high - low));
            r = Math.Clamp(r, -99, 99);
            place.Reputation = r;
        }
        return r;
    }

    /// <summary>The standing band 0 (≥ 51) .. 5 (≤ −75), <c>loc_query</c> 1.</summary>
    // orig: loc_query R029+0x1A8E (0E76:1A8E) draft
    // C: case 1 has no return: the band is left in AX (checked against the asm).
    public static int ReputationBand(int reputation) => reputation switch
    {
        <= -75 => 5,
        <= -40 => 4,
        <= -10 => 3,
        <= 10 => 2,
        <= 50 => 1,
        _ => 0,
    };

    // ---- loc_query / loc_set --------------------------------------------------------

    /// <summary>
    /// A location value (<c>loc_query</c>; the reference's numbering). 0 standing, 1 its band, 2 city
    /// size (1 in the wilderness, 2 outside cities), 3..11 shop qualities, 12..14 the
    /// location's three local values, 15..31 city buildings, 32 the location flags, 33 the
    /// local random seed, 34 the city's exit x, 35 within 14 days of the city's festival,
    /// 37/38 the location's recorded value and mark, 39 something stored here, 40/41 the city
    /// type words (swapped after an uprising). Codes the original has no case for return 0
    /// below 42 and the code itself from 42 on (what the original leaves in its register).
    /// </summary>
    // orig: loc_query R029+0x1A8E (0E76:1A8E) draft; cur_loc_query R029+0x1A7E (0E76:1A7E) matched; tests: Oracle:rules.json loc_query; WorldRulesTests
    // C: checked against the asm (jump table 0..0x29, unsigned): codes above 41, negative ones included, return the code (AX); 40 takes +5A when flag 0x80 is set, else +58, and 41 the other way round.
    // DORMANT: codes 19, 21, 22, 25, 30, 36 jump straight to the exit after `shl ax,1; xchg bx,ax` (their entries in the jump table at R029+0x1AA8 all
    //   point to R029+0x1DFA): they return the caller's BX; here 0. No call passes them (every literal code in the scripts and rules, and the day + 3 of the
    //   market search, is another one).
    // DIVERGENCE: 39..41 read locations[cur_loc]: in the wilderness that is the far pointer before the table (locations[-1]); here an empty record.
    public static int Query(GameState s, int loc, int what)
    {
        var city = s.City;
        int Contents(CityContents bit) => city.Contents & (int)bit;
        Location Here() => s.Here ?? new Location();   // the original reads an undefined record in the wilderness
        return what switch
        {
            0 => s.Locations[loc].Reputation,
            1 => ReputationBand(s.Locations[loc].Reputation),
            2 => s.CurrentLocation == GameState.Wilderness ? 1 : s.CurrentLocation >= Location.CityCount ? 2 : city.CitySize,
            3 => city.Shop(CityShop.Merchant),
            4 => city.Shop(CityShop.Blacksmith),
            5 => city.Shop(CityShop.Swordsmith),
            6 => city.Shop(CityShop.Bowyer),
            7 => city.Shop(CityShop.Armorer),
            8 => city.Shop(CityShop.Tinker),
            9 => city.Shop(CityShop.Clothier),
            10 => city.Shop(CityShop.UnknownShop2),
            11 => city.Shop(CityShop.UnknownShop),
            12 or 13 or 14 => (sbyte)s.Locations[loc].LocalValues[what - 12],
            15 => Contents(CityContents.Fortress),
            16 => Contents(CityContents.Unknown0),
            17 => Contents(CityContents.Unknown3),
            18 => Contents(CityContents.Docks),
            20 => Contents(CityContents.Slums),
            23 => Contents(CityContents.Mint),
            24 => Contents(CityContents.TownHall),
            26 => Contents(CityContents.Barracks),
            27 => Contents(CityContents.University),
            28 => Contents(CityContents.Monastery),
            29 => Contents(CityContents.Cathedral),
            31 => Contents(CityContents.Pawnshop),
            32 => (sbyte)s.Locations[loc].Flags,
            33 => (short)(s.CurrentLocation == GameState.Wilderness ? s.Party.Seed + s.MapPosition.Y + s.MapPosition.X
                : s.CurrentLocation >= Location.CityCount ? s.CurrentLocation + s.Party.Seed
                : city.Ordinal + s.Party.Seed),
            34 => city.ExitX,
            35 => Math.Abs(city.FestivalDay - DaysBeforeThisMonth(s)) <= 14 ? 1 : 0,
            37 => s.Locations[loc].Recorded[0],
            38 => s.Locations[loc].Mark,
            39 => Here().CacheHandle != -1 ? 1 : 0,
            40 => (Here().Flags & Location.UprisingFlag) != 0 ? city.OtherCityType : city.CityType,
            41 => (Here().Flags & Location.UprisingFlag) != 0 ? city.CityType : city.OtherCityType,
            >= 0 and <= 41 => 0,
            _ => what,
        };
    }

    private static int DaysBeforeThisMonth(GameState s)
    {
        var d = 0;
        for (var m = 0; m < s.Date.Month; m++)
        {
            d += Time.GameDate.DaysInMonth(m);
        }
        return d;
    }

    /// <summary>
    /// Changes a location value (<c>loc_set</c>): 0 standing, 2 city size, 3..8 and 11 shop
    /// qualities, 12..14 local values, 32 adds flag bits, 37 records a value. Original quirks:
    /// 6 and 11 write each other's shop (6 sets the shop <c>loc_query</c> 11 reads and the
    /// other way round), and 37, while the third slot is free, overwrites every occupied slot
    /// with the value instead of shifting the list.
    /// </summary>
    // orig: loc_set R029+0x1DFE (0E76:1DFE) matched; tests: Oracle:rules.json loc_set; WorldRulesTests
    // BUG: 6 writes city_rec[0x66] and 11 city_rec[0x67] (FixShopSetSwap); 37 overwrites every occupied slot (FixRecordedListShift).
    // DORMANT: codes 1, 9, 10 and the other loc_query codes have no case: nothing is written.
    public static void Set(GameState s, int loc, int what, int value)
    {
        var place = s.Locations[loc];
        switch (what)
        {
            case 0:
                place.Reputation = (short)value;
                break;
            case 2:
                s.City.CitySize = value;
                break;
            case 3:
                s.City.SetShop(CityShop.Merchant, value);
                break;
            case 4:
                s.City.SetShop(CityShop.Blacksmith, value);
                break;
            case 5:
                s.City.SetShop(CityShop.Swordsmith, value);
                break;
            case 6:
                s.City.SetShop(s.Behaviour.FixShopSetSwap ? CityShop.Bowyer : CityShop.UnknownShop, value);
                break;
            case 7:
                s.City.SetShop(CityShop.Armorer, value);
                break;
            case 8:
                s.City.SetShop(CityShop.Tinker, value);
                break;
            case 11:
                s.City.SetShop(s.Behaviour.FixShopSetSwap ? CityShop.UnknownShop : CityShop.Bowyer, value);
                break;
            case 12 or 13 or 14:
                place.LocalValues[what - 12] = (sbyte)value;
                break;
            case 32:
                place.Flags = (byte)(place.Flags | value);
                break;
            case 37:
                var slots = place.Recorded;
                if (slots[2] == 0 && s.Behaviour.FixRecordedListShift)
                {
                    slots[2] = slots[1];
                    slots[1] = slots[0];
                    slots[0] = value;
                }
                else if (slots[2] == 0)
                {
                    if (slots[1] != 0)
                    {
                        slots[2] = value;
                    }
                    if (slots[0] != 0)
                    {
                        slots[1] = value;
                    }
                    slots[0] = value;
                }
                break;
        }
    }

    // ---- hostility and prices -------------------------------------------------------

    /// <summary>
    /// The current location is hostile (<c>loc_hostile</c>): an <see cref="EventKinds.Hostile"/>
    /// event, or standing −75 or lower, or −10 or lower during unrest. Original bug: the unrest
    /// test asks for an event of kind "current location" at location 0x13, so it practically
    /// never holds.
    /// </summary>
    // orig: loc_hostile_impl R039+0x00BA (1462:00BA) matched; tests: WorldRulesTests
    // BUG: event_at(cur_loc, 0x13) has its arguments swapped (FixHostileUnrestTest); the three tests short-circuit in this order.
    public static bool Hostile(GameState s)
    {
        var here = s.CurrentLocation;
        var rep = Reputation(s, here);
        bool Unrest() => s.Behaviour.FixHostileUnrestTest ? s.Events.At(EventKinds.Unrest, here) : s.Events.At(here, EventKinds.Unrest);
        return s.Events.At(EventKinds.Hostile, here) || rep <= -75 || (Unrest() && rep <= -10);
    }

    /// <summary>2 for <see cref="EventKinds.Expensive"/> here, +1 for <see cref="EventKinds.Unrest"/>.</summary>
    // orig: R039+0x0102 (1462:0102) matched
    public static int PriceEvents(GameState s) =>
        (s.Events.At(EventKinds.Expensive, s.CurrentLocation) ? 2 : 0) + (s.Events.At(EventKinds.Unrest, s.CurrentLocation) ? 1 : 0);

    /// <summary>
    /// A local price between <paramref name="low"/> and <paramref name="value"/>: the value
    /// +25 % when the location flags (<c>loc_query</c> 32) are 1, +50 % when they are 2, +20 %
    /// each for the price events, then rnd(value − low) kept in low..value.
    /// </summary>
    // orig: R039+0x0000 (1462:0000) matched
    // C: loc_query(cur_loc, 0x20) is the location flags; unrest (0x13) before expensive (0x12); clamp_range(lo, v, rand_mod(v - lo)).
    public static int LocalPrice(GameState s, int value, int low)
    {
        var kind = Query(s, s.CurrentLocation, 32);
        if (kind == 1)
        {
            value = value * 5 / 4;
        }
        if (kind == 2)
        {
            value = value * 6 / 4;
        }
        if (s.Events.At(EventKinds.Unrest, s.CurrentLocation))
        {
            value = value * 6 / 5;
        }
        if (s.Events.At(EventKinds.Expensive, s.CurrentLocation))
        {
            value = value * 6 / 5;
        }
        return Math.Clamp(s.Random.Below(value - low), low, value);
    }

    /// <summary>
    /// The party's price for a service of base <paramref name="value"/>: (value + 1) × 4/3 at
    /// standing ≤ −10, ÷ 2 at ≥ 50, × 7/10 at ≥ 10; at least 1; times the present members;
    /// × 8/3 when the location flags are 2 and × 5/3 when they are 8; kept in 1..1000.
    /// </summary>
    // orig: R039+0x1D2C (1462:1D2C) matched
    // C: the second test is `(cur_loc != -1 || cur_loc >= 0x5C)`, i.e. not in the wilderness.
    public static int ServicePrice(GameState s, int reputation, int value)
    {
        value++;
        if (reputation <= -10)
        {
            value = value * 4 / 3;
        }
        else if (reputation >= 50)
        {
            value /= 2;
        }
        else if (reputation >= 10)
        {
            value = value * 7 / 10;
        }
        value = Math.Max(value, 1) * PartyQueries.PresentCount(s);
        if (s.CurrentLocation != GameState.Wilderness && Query(s, s.CurrentLocation, 32) == 2)
        {
            value = value * 8 / 3;
        }
        if (s.CurrentLocation != GameState.Wilderness && Query(s, s.CurrentLocation, 32) == 8)
        {
            value = value * 5 / 3;
        }
        return Math.Clamp(value, 1, 1000);
    }

    // ---- the map --------------------------------------------------------------------

    /// <summary>Map distance: the larger axis plus half the smaller, with y counted a third.</summary>
    // orig: the distance (dx = |x-X|, dy = |y-Y|/3, the larger plus half the smaller),
    // repeated inline in nearest_city_impl R030+0x271A (1462:271A) and R034+0x1A86
    public static int Distance(int x1, int y1, int x2, int y2)
    {
        var dx = Math.Abs(x2 - x1);
        var dy = Math.Abs(y2 - y1) / 3;
        return dx >= dy ? dy / 2 + dx : dx / 2 + dy;
    }

    /// <summary>The nearest city to a map position, not counting a city standing exactly there (<c>nearest_city</c>).</summary>
    // orig: nearest_city_impl R030+0x271A (1462:271A) matched; tests: WorldRulesTests
    public static int NearestCity(GameState s, int x, int y)
    {
        var best = -1;
        var min = 9999;
        for (var i = 0; i < Location.CityCount && i < s.Locations.Count; i++)
        {
            var p = s.Locations[i];
            if (p.X == x && p.Y == y && p.Icon == 0)
            {
                continue;
            }
            var d = Distance(p.X, p.Y, x, y);
            if (d < min)
            {
                min = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// The nearest location with map icon <paramref name="icon"/> that is not at the position
    /// and not destroyed (flag 4) (<c>nearest_loc_type</c>); if there is none, the current
    /// location (−1 in the wilderness).
    /// </summary>
    // orig: nearest_loc_type_impl R030+0x2842 (1462:2842) matched; tests: WorldRulesTests
    public static int NearestOfType(GameState s, int x, int y, int icon)
    {
        var best = -1;
        var min = 9999;
        for (var i = 0; i < s.Locations.Count; i++)
        {
            var p = s.Locations[i];
            if (p.Icon != icon || (p.X == x && p.Y == y) || (p.Flags & 4) != 0)
            {
                continue;
            }
            var d = Distance(p.X, p.Y, x, y);
            if (d < min)
            {
                min = d;
                best = i;
            }
        }
        return min == 9999 && s.CurrentLocation != GameState.Wilderness ? s.CurrentLocation : best;
    }

    /// <summary>
    /// A far location with map icon <paramref name="icon"/>: the farthest one closer than
    /// 100 + rnd 100, else the nearest (<see cref="NearestOfType"/>).
    /// </summary>
    // orig: R030+0x28F2 (1462:28F2) matched; tests: WorldRulesTests
    // C: rand_mod(100) is drawn before the scan; destroyed places (flag 4) are not skipped here, unlike nearest_loc_type.
    public static int FarOfType(GameState s, int x, int y, int icon)
    {
        var best = -1;
        var reach = s.Random.Below(100);
        var max = 0;
        for (var i = 0; i < s.Locations.Count; i++)
        {
            var p = s.Locations[i];
            // BUG: only the icon is tested: a pagan altar the party has purged (its cards closed, 0x62: PaganAltarOnTheHill) is
            // still chosen as an errand's site, which can then never be done; NearestOfType's flag-4 test does not catch it either.
            if (p.Icon != icon || (p.X == x && p.Y == y))
            {
                continue;
            }
            var d = Distance(p.X, p.Y, x, y);
            if (d > max && d < reach + 100)
            {
                max = d;
                best = i;
            }
        }
        return best != -1 ? best : NearestOfType(s, x, y, icon);
    }

    /// <summary>A random non-city location with map icon <paramref name="icon"/> (the search starts at a random place).</summary>
    // orig: R039+0x0504 (1462:0504) matched
    // DIVERGENCE: the original loops forever when no place has the icon; here the scan stops after one round.
    public static int RandomOfType(GameState s, int icon)
    {
        var i = s.Random.Below(0x141) + Location.CityCount;
        for (var tries = 0; tries < s.Locations.Count && s.Locations[i].Icon != icon; tries++)
        {
            if (++i >= s.Locations.Count)
            {
                i = Location.CityCount;
            }
        }
        return i;
    }
}
