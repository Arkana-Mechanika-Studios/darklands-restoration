using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Cards;

/// <summary>Locations, city politics and quests for card scripts.</summary>
public sealed partial class CardContext
{
    /// <summary>The current location (<c>location-now</c>), −1 in the wilderness.</summary>
    public int Here => State.CurrentLocation;

    /// <summary>The current city's working record (<c>city-record</c>).</summary>
    public CityRecord City => State.City;

    /// <summary>Loads a city's record afresh (<c>city-read</c>).</summary>
    // orig: city-read R029+0x4E68 (0E76:4E68) matched
    // C: an unknown city number reads the file's start (negative) or nothing (past the last city: the working copy stays): CityFile.
    public void LoadCity(int city) => State.City = CityFile.Read(State.Data, city, State.City) ?? State.City;

    /// <summary><c>place-query</c> (see <see cref="LocationRules.Query"/> for the codes).</summary>
    // orig: place-query R029+0x1A8E (0E76:1A8E) draft
    public int LocationQuery(int loc, int what) => LocationRules.Query(State, loc, what);

    /// <summary><c>location-now-query</c>.</summary>
    // orig: location-now-query R029+0x1A7E (0E76:1A7E) matched
    // C: declared without a return; place-query's AX comes back.
    public int HereQuery(int what) => LocationRules.Query(State, Here, what);

    /// <summary><c>place-assign</c> (6 and 11 swapped, 37 overwrite: original quirks).</summary>
    // orig: place-assign R029+0x1DFE (0E76:1DFE) matched
    public void LocationSet(int loc, int what, int value) => LocationRules.Set(State, loc, what, value);

    /// <summary><c>place-standing</c>.</summary>
    // orig: place-standing R029+0x199C (0E76:199C) matched
    public int Reputation(int loc) => LocationRules.Reputation(State, loc);

    /// <summary>The party's standing here (<c>reputation-now</c> = <c>place-query(location-now, 0)</c>).</summary>
    // orig: reputation-now DS:EE74
    // C: cards copy place-query(location-now, 0) into reputation-now (DS:EE74) at their start; here it is read live.
    public int ReputationHere => LocationRules.Reputation(State, Here);

    /// <summary><c>place-standing-adjust</c>.</summary>
    // orig: place-standing-adjust R029+0x19D0 (0E76:19D0) matched
    public int AdjustReputation(int loc, int a, int b) => LocationRules.AdjustReputation(State, loc, a, b);

    /// <summary><c>place-hostile</c>.</summary>
    // orig: place-hostile-inner R039+0x00BA (1462:00BA) matched
    public bool Hostile() => LocationRules.Hostile(State);

    /// <summary>R039 0102: 2 for event 0x12 here, +1 for 0x13.</summary>
    // orig: R039+0x0102 (1462:0102) matched
    public int PriceEvents() => LocationRules.PriceEvents(State);

    /// <summary>R039 0000: a local price between low and value.</summary>
    // orig: R039+0x0000 (1462:0000) matched
    public int LocalPrice(int value, int low) => LocationRules.LocalPrice(State, value, low);

    /// <summary>R039 1d2c: a service's price for the party.</summary>
    // orig: R039+0x1D2C (1462:1D2C) matched
    public int ServicePrice(int reputation, int value) => LocationRules.ServicePrice(State, reputation, value);

    /// <summary><c>closest-city</c>.</summary>
    // orig: closest-city-inner R030+0x271A (1462:271A) matched
    public int NearestCity(int x, int y) => LocationRules.NearestCity(State, x, y);

    /// <summary><c>closest-place-of-kind</c>.</summary>
    // orig: closest-place-of-kind-inner R030+0x2842 (1462:2842) matched
    public int NearestLocationOfType(int x, int y, int icon) => LocationRules.NearestOfType(State, x, y, icon);

    /// <summary>R030 28f2 (thunk 09C0:1F13): a far location of a type.</summary>
    // orig: R030+0x28F2 (1462:28F2) matched
    public int FarLocationOfType(int x, int y, int icon) => LocationRules.FarOfType(State, x, y, icon);

    /// <summary>The city nearest to the party: here, or the one nearest to the map position.</summary>
    // orig: closest-city-inner R030+0x271A (1462:271A) matched
    // C: the saints' `location-now == -1 ? closest-city(party-map-column, party-map-row) : location-now` (R039 165c 024c and others).
    public int NearestCityToParty() => Here != -1 ? Here : NearestCity(State.MapPosition.X, State.MapPosition.Y);

    // ---- city politics --------------------------------------------------------------

    /// <summary><c>place-side-read</c>.</summary>
    // orig: place-side-read-inner R039+0x0A34 (1462:0A34) matched
    public int SideOf(int bit) => CityPolitics.SideOf(State, bit);

    /// <summary><c>place-side-write</c>.</summary>
    // orig: place-side-write-inner R039+0x0A7E (1462:0A7E) matched
    public void SetSide(int bit, int side) => CityPolitics.SetSide(State, bit, side);

    /// <summary><c>side-one-power</c> (−1) / <c>side-two-power</c> (1).</summary>
    // orig: side-one-power-inner R039+0x0D46 (1462:0D46) matched; side-two-power-inner R039+0x0BF6 (1462:0BF6) matched
    public int SideStrength(int side) => CityPolitics.Strength(State, side);

    /// <summary><c>side-one-share</c> (−1) / <c>side-two-share</c> (1).</summary>
    // orig: side-one-share-inner R039+0x0E96 (1462:0E96) matched; side-two-share-inner R039+0x0EAA (1462:0EAA) matched
    public int SidePercent(int side) => CityPolitics.Percent(State, side);

    /// <summary><c>fight-resolution</c>(party's side, winner, side that started it).</summary>
    // orig: fight-resolution-inner R039+0x0EBE (1462:0EBE) matched
    public void ConflictOutcome(int partySide, int winner, int leadingSide) =>
        CityPolitics.ConflictOutcome(State, partySide, winner, leadingSide);

    /// <summary>Sets up a city's politics (R039 0558).</summary>
    // orig: R039+0x0558 (1462:0558) matched
    public void SetUpCityPolitics(int loc) => CityPolitics.SetUpCity(State, loc);

    // ---- quests ---------------------------------------------------------------------

    /// <summary><c>open-quest-resolve</c>.</summary>
    // orig: open-quest-resolve R029+0x3E06 (0E76:3E06) matched
    public void ResolveQuest(int tag, int loc) => Quests.Resolve(State, tag, loc);

    /// <summary>thunk 09C0:2175: a quest of tag 3 (uses the card's $LocName location).</summary>
    // orig: R039+0x10B6 (1462:10B6) matched
    public int StartQuest3(int owner, int target, int type, int value, int level, int detail) =>
        Quests.StartQuest3(State, owner, target, type, value, level, detail, Variables.Location);

    /// <summary>thunk 09C0:20D5: quest 7 here.</summary>
    // orig: R039+0x1F00 (1462:1F00) matched
    public void StartQuest7() => Quests.StartQuest7(State);

    /// <summary>thunk 09C0:21B1: quest 10 for a region kind; sets $LocName to its site; returns the event slot.</summary>
    // orig: R039+0x1712 (1462:1712) matched
    public int StartQuest10(int region)
    {
        var slot = Quests.StartQuest10(State, region, out var site);
        Variables.Location = site;
        return slot;
    }

    /// <summary>thunk 09C0:214D: quest 10 about <paramref name="target"/> is finished here.</summary>
    // orig: R039+0x1AB0 (1462:1AB0) matched
    public void FinishQuest10(int target) => Quests.FinishQuest10(State, target);

    /// <summary>thunk 09C0:1F09 / thunk 09C0:1F27 / thunk 09C0:1F1D: the monthly quests.</summary>
    // orig: R039+0x11FC (1462:11FC) matched
    public void StartDragonQuest() => Quests.StartDragon(State);

    // orig: R039+0x15C2 (1462:15C2) matched
    public void StartMineQuest() => Quests.StartMine(State);

    // orig: R039+0x1B0C (1462:1B0C) matched
    public void StartSabbatQuest() => Quests.StartSabbat(State);

    /// <summary>thunk 09C0:212F / thunk 09C0:1FE5 / thunk 09C0:2157 / thunk 09C0:216B: the dragon quest.</summary>
    // orig: R039+0x1510 (1462:1510) matched
    public int DragonDetail() => Quests.DragonDetail(State);

    // orig: R039+0x153E (1462:153E) matched
    public int DragonSort() => Quests.DragonSort(State);

    // orig: R039+0x156C (1462:156C) matched
    public int DragonLair() => Quests.DragonLair(State);

    // orig: R039+0x159A (1462:159A) matched
    public void DragonSlain() => Quests.DragonSlain(State);

    /// <summary>thunk 09C0:2193 / thunk 09C0:219D: the mine quest.</summary>
    // orig: R039+0x16B4 (1462:16B4) matched
    public int MineSort() => Quests.MineSort(State);

    // orig: R039+0x16E2 (1462:16E2) matched
    public int MineDetail() => Quests.MineDetail(State);

    /// <summary>thunk 09C0:20FD / thunk 09C0:21A7 / thunk 09C0:20DF: the next sabbat's site, day and month.</summary>
    // orig: R039+0x1CFE (1462:1CFE) matched
    public int SabbatSite() => Quests.SabbatSite(State);

    // orig: R039+0x1CD2 (1462:1CD2) matched
    public int SabbatDayOfMonth() => Quests.SabbatDayOfMonth(State);

    // orig: R039+0x1CE8 (1462:1CE8) matched
    public int SabbatMonth() => Quests.SabbatMonth(State);

    /// <summary>thunk 09C0:1F81: a feast day near this month.</summary>
    // orig: R039+0x1C22 (1462:1C22) matched
    public int FeastDayNearThisMonth() => Quests.FeastDayNearThisMonth(State);
}
