using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Rules;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Simulation.Overland;

/// <summary>What happened after a step on the map.</summary>
public enum StepOutcome
{
    /// <summary>The party walked on.</summary>
    Walked,

    /// <summary>Deep water: the step was not taken.</summary>
    Blocked,

    /// <summary>The party reached a place; the result carries its card.</summary>
    Arrived,

    /// <summary>Something happened on the road; the result carries the encounter card (or 0 for none).</summary>
    Encounter,
}

public readonly record struct StepResult(StepOutcome Outcome, int Card = 0);

/// <summary>
/// Travel on the world map: each step costs time by terrain, may reach a place, and may bring
/// an encounter, as the travel screen of the game decides.
/// </summary>
// orig: Step = R030+0x1300 (1462:1300) draft (the draft of 1300 does not match the bytes: shape right, stack slots differ; checked against its disassembly - the forced arrival with party_x/party_y, the blocked test, the minutes, the arrival loop, the notice loop with its message and cue, the danger roll and the weight all agree with the C), read with the key handler R030+0x0C44 (draft, bytes differ; its clamps, the DS:290E toggle and the scroll block were checked against the disassembly too) and the sel == 1000 / 2000 branches of overland_loop R030+0x02C0 (draft, bytes match); tests: OverlandTests
// DIVERGENCE: the original keeps the party as three view-relative registers (DS:2866/DS:2868 the marker inside the 260x184 view, DS:286A/DS:286C/DS:286E the view's column and row offsets into the scrolling G[][] terrain buffer); this class works on absolute map pixels and cells (WorldMap.CellAt). The cell under the marker is the same one (the formulas of 1300: row = (y + 11) / 4, column from x + 16 or x + 24 by the row's parity, are those of CellAt); the terrain read through G is one cell off at the view's column 0 / row 0, as in the original (WorldMap.BufferOffset, View).
public sealed class OverlandTravel(GameState state, WorldMap map, Func<int, bool>? eventStarted = null)
{
    /// <summary>Event kinds the travel rules look at.</summary>
    // orig: event_started(0x5d) at the top of 1300 (the party is carried to the nearest city), event_started(0x4b) in its water test (a saint's travel blessing), event_started(0x5f) in overland_loop (ambushed in camp)
    private const int ForcedArrival = 0x5D;
    private const int MayCrossWater = 0x4B;
    private const int AmbushedInCamp = 0x5F;

    /// <summary>An event of this kind at a place sets the encounter weight there (event_at / event_value_at 0x2A); one of kind 0x2B holds a message shown when the place is first noticed.</summary>
    // orig: event_at(0x2a, loc) / event_value_at(0x2a, loc) in R030+0x2D44; event_value_at(0x2b, i) / event_remove(0x2b, -1, i, -1, -1) / map_message in 1300
    // C: what scripts create the 0x2A and 0x2B events was not searched; their meaning (an encounter weight, a message number) is read from the use here.
    private const int WeightEvent = 0x2A;
    private const int NoticeMessageEvent = 0x2B;

    /// <summary>The map icon of a place whose notice plays a music cue (icon 15).</summary>
    private const int CueIcon = 15;

    /// <summary>Music cue played while such a place is the nearest one noticed: thunk 09C0:18F1(0x18).</summary>
    public const int NoticeCue = 0x18;

    private readonly Func<int, bool> _eventStarted = eventStarted ?? state.Events.Started;
    private int _minutes;
    private int _danger;
    private int _terrain;

    /// <summary>Called for every game hour that passes while travelling.</summary>
    // orig: advance_hours(1) in 1300 (when 60 minutes are full, and for a forced arrival), followed there by the clock picture and the season palette (R030+0x2DD6)
    public Action? HourPassed { get; set; }

    /// <summary>Shows the map message of a place just noticed (<c>map_message</c>) and waits for a key; the screen supplies it.</summary>
    // orig: map_message R030+0x2BB0 (1462:2BB0) draft (bytes match): record `message` (400 bytes each) of darkland.msg in a framed box
    public Action<int>? MapMessage { get; set; }

    /// <summary>Plays a music cue (<c>thunk 09C0:18F1</c>); the screen supplies it.</summary>
    public Action<int>? PlayCue { get; set; }

    /// <summary>The view's first column and first shown row (DS:286A, DS:286C), which the terrain read depends on at the map's borders (<see cref="WorldMap.BufferOffset"/>); the screen supplies it.</summary>
    public Func<(int Column, int Rows)> View { get; set; } = () => (1, 1);

    /// <summary>
    /// The code of the map screen's cell buffer under the marker at map pixel (x, y), as the step reads it; the screen supplies it. Without
    /// it the step reads the map cell the buffer would hold for a view drawn afresh.
    /// </summary>
    public Func<int, int, int>? CellUnderMarker { get; set; }

    // orig: the terrain read of R030+0x1300 (R030+0x1376..0x1387: G[(y + 11) / 4][col]) and of R030+0x16C6 (DS:D843), through the cell buffer of R030+0x20D2
    // C: G is filled from column / row 1 when the view's is 0, so at the west / north border the cell read is the one east / south of the party's
    // C: G is decoded from the row buffer, which can hold other rows than the view's registers name (RowBuffer.Load's stale slots after a
    //   scroll up, the scroll block's bottom jump): then the step reads the terrain of those rows (CellUnderMarker, from the screen's buffer).
    //   The marker's column in G is at most 16 (DS:2866 <= 0xF7) and its row at most 47 (DS:2868 <= 0xB1): the step never reads G's last
    //   column (the cell past the map's east end at the east border) nor the rows after the buffer.
    private int TerrainUnderMarker(int column, int row, int x, int y)
    {
        if (CellUnderMarker is { } read)
        {
            var code = read(x, y);
            return code >= Terrain.FirstPlaceIcon ? Terrain.Castle : code;
        }
        return FreshTerrainUnderMarker(column, row);
    }

    private int FreshTerrainUnderMarker(int column, int row)
    {
        var view = View();
        var (dc, dr) = WorldMap.BufferOffset(view.Column, view.Rows);
        return map.TerrainAt(column + dc, row + dr);
    }

    /// <summary>The terrain the party stands on.</summary>
    // orig: DS:D843
    public int CurrentTerrain => _terrain;

    /// <summary>Starts a stretch of travel: minute and danger counters begin at zero.</summary>
    // orig: DS:8672 = 0 (minutes) and DS:E488 = 0 (danger) in overland_open R030+0x00A8 (1462:00A8) matched; DS:D843 (the terrain) is set by 16c6, which every idle pass of the loop calls through 0c44 before any step: the cell under the party at the start
    // C: DS:D843 is a global that keeps its value between visits to the map; the first pass of overland_loop overwrites it before a step can read it.
    public void Begin()
    {
        _minutes = 0;
        _danger = 0;
        var (column, row) = (state.MapPosition.X, state.MapPosition.Y);
        // C: at the start the row buffer was just read for the view (RowBuffer.Open): the buffer holds the map's own cells
        _terrain = FreshTerrainUnderMarker(column, row);
    }

    /// <summary>The party moves onto the cell under map pixel (x, y).</summary>
    // orig: R030+0x1300 (1462:1300): returns -1 walked, 9999 blocked, a location index arrived, 1000 a road encounter, 2000 the place's own encounter - the four StepOutcomes
    // C: whenever 1300 returns 0 or more, 0c44 puts the marker back (DS:2866 = oxo, DS:2868 = oyo, DS:8676 = 0); the cell (MapPosition) is the old one for Walked, Blocked and Arrived, the new one for Encounter (see OverlandScreen.MoveOnce).
    public StepResult Step(int x, int y)
    {
        var (column, row) = WorldMap.CellAt(x, y);
        if (_eventStarted(ForcedArrival))
        {
            // C: advance_hours(1); return cur_loc = nearest_city_impl(party_x, party_y). party_x and party_y were set at the start of 0c44 from the cell the party stood on before this step, so the city nearest to the old position is chosen, not to the one stepped towards (the new MapPosition is only set below, and not for an arrival).
            HourPassed?.Invoke();
            return new StepResult(StepOutcome.Arrived, ArriveAt(LocationRules.NearestCity(state, state.MapPosition.X, state.MapPosition.Y)));
        }

        // C: t = c >= 50 ? 28 : c — a place icon counts as a castle (16c6 uses c > 50 for the same test, so for the single code 50 it leaves DS:D843 at 50; place icon codes start at 0x1C and 0x33, so this is not reachable).
        var terrain = TerrainUnderMarker(column, row, x, y);
        // C: (t == 1 || t == 2) && !event_started(0x4b) && DS:D843 != 1 && DS:D843 != 2 -> 9999: deep water stops the party unless it already stands on deep water; a minor river (3) never stops it.
        if (Terrain.IsDeepWater(terrain) && !_eventStarted(MayCrossWater) && !Terrain.IsDeepWater(_terrain))
        {
            return new StepResult(StepOutcome.Blocked);
        }
        _terrain = terrain;
        // orig: DS:8672 += R030+0x2AC8(t) = Terrain.MinutesPerStep (checked: all 18 terrain cases and the default 9 of 2ac8 agree with the table)
        PassMinutes(Terrain.MinutesPerStep(terrain));

        // C: t >= 25 && t != 27 && t != 25 (castle 28, city 29 and the other place icons); the reach is 4 around a city (29), 1 around anything else.
        // C: an arrival returns with cur_loc set only (1462:158E): party_x / party_y keep the cell 0c44 took at the start of the pass, the one
        //   stepped from (0c44 then puts the marker back); a walk is read from the marker by the next pass, an encounter sets them (1462:1672).
        if (Terrain.CanEnterPlace(terrain) && PlaceAt(column, row, terrain == Terrain.City ? 4 : 1) is { } place)
        {
            return new StepResult(StepOutcome.Arrived, ArriveAt(place));
        }
        // C: a walk (-1) does not write party_x / party_y either: they keep the cell 0c44 took at the start of this pass until the next pass
        //   of 0c44 reads them from the marker (1462:0C44 +0x54/+0x63, OverlandScreen); a save from the menu bar in between (menu_bar_run comes
        //   before 0c44 in overland_loop) holds the cell stepped from. Only an encounter sets them here (below).

        var near = NearestNoticedPlace(column, row);
        // C: after the notice scan (which may show a map message even over water) a step onto deep water ends here.
        if (Terrain.IsDeepWater(terrain))
        {
            return new StepResult(StepOutcome.Walked);
        }
        // C: the danger counter DS:E488 rises with probability 10/500 per step while it is below 10: rand_mod(500) <= 9 is drawn only then.
        if (_danger < 10 && state.Random.Below(500) <= 9)
        {
            _danger++;
        }
        // C: w = 2d44(t, found) is computed (no draw) before rand_mod(1000) is drawn; "(DS:E488 + 1) * w < rand_mod(1000)" is int arithmetic.
        var weight = EncounterWeight(terrain, near);
        if ((_danger + 1) * weight < state.Random.Below(1000))
        {
            return new StepResult(StepOutcome.Walked);
        }
        // C: party_x/party_y = the cell stepped to; cur_loc = found (-1 when no place is near).
        state.MapPosition = new MapPosition(column, row);
        state.CurrentLocation = near;
        // C: found != -1 && unk0e != 0x62 && rand_mod(100) <= 60 -> 2000: the place's own encounter card (short-circuit: no draw unless the place has one that is not 0x62).
        if (near >= 0 && state.Locations[near].EncounterCard != 0x62 && state.Random.Below(100) <= 60)
        {
            return new StepResult(StepOutcome.Encounter, state.Locations[near].EncounterCard);
        }
        return new StepResult(StepOutcome.Encounter, RoadEncounter());
    }

    /// <summary>After a camp on the map: when the party was ambushed in its camp (event 0x5F) the road encounter that follows, else null.</summary>
    // orig: `if (event_started(0x5f)) return 1000;` after thunk 09C0:1FF9(...) in the camp case of R030+0x0C44, i.e. the sel == 1000 branch with cur_loc = -1 (set when the loop started)
    public int? AmbushInCamp() => _eventStarted(AmbushedInCamp) ? RoadEncounter() : null;

    /// <summary>Only one hour is taken off the minute count per step; the rest carries over.</summary>
    // C: if (DS:8672 >= 60) { advance_hours(1); ...; DS:8672 -= 60 } — at most one hour per step; the largest step is 197 minutes, the surplus stays in DS:8672 and is paid off, one hour per later step.
    // DORMANT: 2ac8 itself advances an hour for each full 0x5A0 (1440) minutes of a single step (while (v >= 0x5a0)); no terrain costs that much, so the loop never runs and is not written.
    private void PassMinutes(int minutes)
    {
        _minutes += minutes;
        if (_minutes >= 60)
        {
            HourPassed?.Invoke();
            _minutes -= 60;
        }
    }

    // orig: the end of overland_loop for a location index below 92 loads that city's record (darkland.cty) and description (darkland.dsc); every index except the special 1000, 2000 and 9999 then returns locations[cur_loc]->menu, the arrival card
    // DIVERGENCE: the city record and description are not read here: GameState.City loads the working copy for the current location on first use, and TextVariables takes the description from WorldData.
    private int ArriveAt(int place)
    {
        state.CurrentLocation = place;
        return place >= 0 ? state.Locations[place].ArrivalCard : 0;
    }

    /// <summary>The first place (in table order) whose position is within reach of the cell.</summary>
    // orig: the first loop of 1300 over locations 0..413: x in [col - r, col + r + 1], y in [row - r, row + r + 1] and menu != 0x62; returns cur_loc = i
    private int? PlaceAt(int column, int row, int reach)
    {
        for (var i = 0; i < state.Locations.Count; i++)
        {
            var p = state.Locations[i];
            if (p.X >= column - reach && p.X <= column + reach + 1 && p.Y >= row - reach && p.Y <= row + reach + 1 &&
                p.ArrivalCard != 0x62)
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>The closest place whose notice range covers the party, or -1.</summary>
    // orig: the second loop of 1300 (range test, distance dy <= dx ? dy / 2 + dx : dx / 2 + dy, a place counts only when strictly closer than the best so far, which starts at 100)
    // C: "(-1 - unk08) / 2 + a > x" and "(unk08 + 1) / 2 + a <= x", with a = DS:286A + col = column + 1, are the bounds below; int division truncates towards zero, the same for the non-negative ranges of the data.
    // C: the map message fires inside the loop, every time a place closer than the best so far turns up that is not the one already found: a message can be shown for a place that a later, closer one then replaces.
    private int NearestNoticedPlace(int column, int row)
    {
        var best = -1;
        var bestDistance = 100;
        for (var i = 0; i < state.Locations.Count; i++)
        {
            var p = state.Locations[i];
            var u = p.NoticeRange;
            if (p.X < column + 1 - (u + 1) / 2 || p.X > column + (u + 1) / 2 || p.Y < row - u || p.Y > row + u + 1)
            {
                continue;
            }
            var dx = Math.Abs(p.X - column - 1);
            var dy = Math.Abs(p.Y - row - 1);
            var distance = dy <= dx ? dx + dy / 2 : dy + dx / 2;
            if (distance < bestDistance)
            {
                if (best != i && state.Events.ValueAt(NoticeMessageEvent, i) is var message and not 0)
                {
                    state.Events.Remove(NoticeMessageEvent, -1, i, -1, -1);
                    MapMessage?.Invoke(message);
                }
                bestDistance = distance;
                best = i;
            }
        }
        // C: found != -1 && locations[found]->icon == 15 -> thunk 09C0:18F1(0x18), on every step while such a place is the nearest one noticed.
        if (best != -1 && state.Locations[best].Icon == CueIcon)
        {
            PlayCue?.Invoke(NoticeCue);
        }
        return best;
    }

    /// <summary>The encounter weight of the terrain, plus the nearest noticed place's own.</summary>
    // orig: R030+0x2D44 (1462:2D44) draft (bytes match)
    // C: "loc >= 0 && loc <= 0x19e" (0x19E = 414 is one past the table and never a found index); an event of kind 0x2A at the place replaces the place's own danger (unk0a) by the event's value; the terrain part, 1 to 3, is always kept.
    internal int EncounterWeight(int terrain, int near)
    {
        var weight = terrain switch
        {
            4 or 5 or 16 or 17 or 20 or 21 => 3,
            6 or 7 or 14 or 15 or 18 or 19 or 22 or 23 => 2,
            _ => 1,
        };
        if (near >= 0 && near <= 0x19E)
        {
            weight += state.Events.At(WeightEvent, near) ? state.Events.ValueAt(WeightEvent, near) : state.Locations[near].Danger;
        }
        return weight;
    }

    /// <summary>The road encounter for the terrain, or 0 when nothing happens.</summary>
    // orig: the sel == 1000 branch of overland_loop R030+0x02C0 (matched). The order of the checks is the order of the C if / else-if chain; a branch that finds nothing leaves the chain (no later check is tried) and the loop simply goes on: result 0.
    internal int RoadEncounter()
    {
        if (_eventStarted(AmbushedInCamp))
        {
            // C: return rand_mod(2) ? 0x16a : 0x169
            return state.Random.Below(2) != 0 ? 0x16A : 0x169;
        }
        var events = state.Events;
        var here = state.CurrentLocation;
        // C: cur_loc != -1 && ev8_at(4, cur_loc): near the dragon's lair
        if (here != GameState.Wilderness && events.QuestAt(DragonQuest, here))
        {
            return DragonLairEncounter();
        }
        // C: !is_daylight() && !rand_mod(3) && ev8_exists(7): the draw happens only at night, the quest check only if it came up 0
        if (!state.Date.IsDaylight && state.Random.Below(3) == 0 && events.QuestExists(NightHuntQuest))
        {
            return NightHuntEncounter();
        }
        // C: !is_daylight() && party_lore_level() > 4 && loc_fact_any(1, 2)
        if (!state.Date.IsDaylight && PartyQueries.LoreLevel(state) > 4 && events.FactAnywhere(1, 2))
        {
            return 0x126;
        }
        return TerrainEncounter();
    }

    private const int DragonQuest = 4;
    private const int NightHuntQuest = 7;

    /// <summary>Near the dragon's lair: what the party meets depends on the dragon quest's sort.</summary>
    // orig: the cur_loc != -1 && ev8_at(4, cur_loc) arm of the sel == 1000 branch of R030+0x02C0; the dragon sort 1fe5 (Quests.DragonSort)
    private int DragonLairEncounter()
    {
        var events = state.Events;
        // C: loc_fact_value_any(4, 1) % 2 == 0 -> 0x97
        if (events.FactValueAnywhere(DragonQuest, 1) % 2 == 0)
        {
            return 0x97;
        }
        // C: switch (thunk 09C0:1FE5()) has no default: a sort other than 0..3 falls out of the if chain, no terrain encounter is tried and the map goes on (restored; an earlier version fell back to the terrain table).
        return Quests.DragonSort(state) switch
        {
            0 => events.FactValueAnywhere(DragonQuest, 5) == 0 ? 0x97 : 0xA1,
            1 => 0x9E,
            2 => 0x9B,
            3 => 0x9C,
            _ => 0,
        };
    }

    /// <summary>At night, quest 7 counts a night out (fact 5 += 1) and its stage picks the encounter.</summary>
    // orig: event_update(-2, -1, 0x1c, 5, 1, 7, 1) and the switch on loc_fact_value_any(7, 5)
    private int NightHuntEncounter()
    {
        var events = state.Events;
        events.Update(Who.Everybody, -1, EventKinds.LocationFact, 5, 1, NightHuntQuest, EventUpdate.Add);
        var nights = events.FactValueAnywhere(NightHuntQuest, 5);
        return nights switch
        {
            2 => 0xA8,
            3 => events.FactAnywhere(NightHuntQuest, 1) ? 0xA9 : 0xA8,
            6 or 9 => events.FactAnywhere(NightHuntQuest, 2) ? 0x41 : 0xA9,
            1 => 0x41,
            _ => nights % 3 == 0 && !events.FactAnywhere(NightHuntQuest, 2) ? 0xA9 : 0x41,
        };
    }

    /// <summary>The encounter tables by terrain; 0 when the roll finds nothing.</summary>
    // orig: the switch (DS:D843) arm of the sel == 1000 branch: one rand_mod(n) per table, the cases of the C in order (each range below was compared with its C case list); terrain 1, 2, 3 and anything not listed (25 to 28 and above) draw nothing and find nothing.
    // C: "Winter()" is the C's `if (cur_month < 3 || cur_month > 9) return 0x117; break;`: the winter encounter 0x117 is possible in months 0..2 and 10..11 (January to March, November and December); the highlands table (22, 23) uses months below 4 or above 8 instead.
    private int TerrainEncounter()
    {
        var winter = state.Date.Month < 3 || state.Date.Month > 9;
        int Winter() => winter ? 0x117 : 0;
        var r = state.Random;
        return _terrain switch
        {
            4 => r.Below(9) switch { <= 1 => 0x112, <= 4 => Winter(), <= 6 => 0x116, _ => 0x11B },
            5 => r.Below(17) switch
            {
                <= 1 => 0x112, <= 4 => Winter(), <= 6 => 0x116, <= 8 => 0xA0, <= 10 => 0x102, <= 12 => 0x103,
                13 => 0x111, _ => 0x122,
            },
            6 or 7 => r.Below(17) switch
            {
                <= 1 => 0x112, <= 3 => 0xA5, <= 5 => 0x107, <= 7 => 0xA0, <= 9 => 0x102, <= 11 => 0x103,
                12 => 0x111, 13 => 0x118, _ => 0x122,
            },
            8 or 9 => r.Below(26) switch
            {
                <= 1 => 0xA0, <= 3 => 0x102, <= 5 => 0x103, <= 8 => 0x106, <= 10 => 0x107, <= 13 => 0x7F,
                <= 17 => 0x104, <= 19 => 0x105, <= 22 => 0x108, <= 24 => Winter(), _ => 0x24,
            },
            10 or 11 => r.Below(17) switch
            {
                <= 1 => 0xA0, <= 3 => 0xA5, <= 5 => 0x102, <= 7 => 0x103, <= 10 => 0x106, <= 12 => 0x107,
                13 => 0x112, <= 15 => Winter(), _ => 0x24,
            },
            >= 12 and <= 15 => r.Below(16) switch
            {
                <= 1 => 0xA0, <= 3 => 0xA5, <= 5 => 0x102, <= 7 => 0x103, 8 => 0x113, 9 => 0x112, 10 => 0x122,
                <= 12 => Winter(), 13 => 0x118, _ => 0x24,
            },
            16 or 17 => r.Below(16) switch
            {
                <= 1 => 0xA0, <= 3 => 0xA5, 4 => 0x111, 5 => 0x113, <= 7 => 0x112, <= 9 => 0x122,
                <= 12 => Winter(), 13 => 0x118, _ => 0x24,
            },
            >= 18 and <= 21 => r.Below(15) switch
            {
                <= 1 => 0xA0, <= 3 => 0x102, <= 5 => 0x103, 6 => 0x111, <= 8 => 0x112, <= 11 => Winter(),
                12 => 0x118, _ => 0x24,
            },
            22 or 23 => r.Below(9) switch
            {
                <= 2 => 0xF0, 3 => 0x111,
                _ => state.Date.Month < 4 || state.Date.Month > 8 ? 0x117 : 0,
            },
            Terrain.Road => r.Below(29) switch
            {
                <= 1 => 0xA0, <= 3 => 0xA6, <= 5 => 0xA7, 6 => 0x3D, 7 => 0xC3, <= 9 => 0x102, <= 11 => 0x103,
                <= 14 => 0x106, <= 16 => 0x107, <= 18 => 0x7F, <= 22 => 0x104, <= 24 => 0x105, 25 => 0x163,
                26 => 0x108, _ => 0x24,
            },
            _ => 0,
        };
    }
}
