using Darklands.Content;
using Darklands.Content.Formats.Graphics;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Simulation.Overland;

namespace Darklands.Presentation;

/// <summary>
/// The travel screen: a window onto the world map right of the party panel, with the season's
/// colour, the place icons and the party, moved by keys or by clicking where to go.
/// </summary>
// orig: world-map-start R030+0x00A8 (1462:00A8) + world-map-loop R030+0x02C0 (1462:02C0, matched) + the key handler R030+0x0C44 (unmatched) + the drawing R030+0x16C6 (party), R030+0x20D2 (map), R030+0x1F30 (pictures, matched), all; tests: OverlandTests, OverlandTests, OverlandTests
// DIVERGENCE: the original keeps the party as view-relative registers over a cell buffer of the view and scrolls the view in jumps of 128 pixels across and 92 down when the marker comes within 20 pixels of an edge (Scroll below follows those jumps and thresholds). Here the page (OverlandPage) is drawn from absolute map positions (WorldMap) with the same view.
// DIVERGENCE: not ported from the loop: the joystick (DS:E3FA/DS:E3FB, taken as centred: no stick; the held keys DS:0880..DS:0882 are, see HeldKey), the "DS:9C46" reload after the menu bar (the bar itself, right button and F10, comes with GameUi.Poll). The copy-protection check of world-map-entry is DarklandsGame.Travel's (the question left out, its random draws made).
public sealed class OverlandScreen(GameUi ui, PartyPanel panel, WorldMap map)
{
    private const int AreaX = 60;
    private const int AreaY = 16;
    private const int AreaWidth = 260;
    private const int AreaHeight = 184;

    /// <summary>The map buffer's corner left out of the view: 20d2 draws the cells from (0, 0) of its 260x184 buffer and copies it from (8, 8) to (60, 16).</summary>
    // orig: gfx-rect-copy(DS:284E, 8, 8, 0x104, 0xB8, screen, 0x3C, 0x10) at R030+0x2599..0x25AD; the view's first cell (column DS:286A, row DS:286E + DS:286C) is drawn at (0 or 8, 0) of the buffer
    private const int BufferCrop = 8;

    /// <summary>The pause after every pass of the loop: R030+0x3096(delayc) with delayc = 30, a busy wait on clock() (GameUi.DelayClock).</summary>
    // orig: delayc = 30; R030+0x3096(delayc) at the bottom of world-map-loop (R030+0x0C2F..0x0C35); the body is wait-ticks R030 seg 1767 in matched (spins until clock() has advanced by the argument)
    // C: clock() moves only at a BIOS tick, by 50 or 60 (DosClock), so the wait ends at the next tick and a pass lasts one tick (54.9 ms: 18.2
    //   passes a second) while its own work takes less (the emulated original's passes: 50 or 66.7 ms, the game's 60 Hz timer interrupts
    //   passing the BIOS its ticks, about 12,000 bytes of code standing, 44,000 a step, 270,000 when an hour passes)
    private const int StepClock = 30;

    /// <summary>wait-ticks(75) of the wait-in-place loop (key A or F7), twice per pass.</summary>
    private const int RestMilliseconds = 75;

    /// <summary>The marker's sprite row for "standing", used while waiting in place (direction 9).</summary>
    private const int WaitingDirection = 9;

    private OverlandTiles? _pictures;
    private readonly OverlandPage _page = new();

    /// <summary>Whether 20d2 is due: the view moved, the background changed or the screen is drawn again after another one.</summary>
    // orig: `if (occ != DS:286C || DS:286A != oad) 20d2(...)` after the scroll block of R030+0x0C44, `if (m != DS:2860) 20d2(...)` after an hour in
    //   R030+0x1300, and the redraws of world-map-start, the `redraw` label of 0c44, the portrait click and the menu bar's party-icons-stale
    private bool _mapDirty = true;
    // orig: the view of R030: DS:286A the first column shown, DS:286E the first row of the row buffer, DS:286C the first row shown in it; the
    //   marker's position in the view is DS:2866 / DS:2868 (here _x - _originX, _y - _originY)
    /// <summary>A debugging aid: each step of the marker, as tools/oracle/lockstep.py prints the original's (DARKLANDS_TRACE_MAP).</summary>
    public static Action<string>? Trace { get; set; } = Environment.GetEnvironmentVariable("DARKLANDS_TRACE_MAP") is null ? null : Console.WriteLine;

    /// <summary>A tester's hook (the headless host, tools/oracle/lockstep.py's mapclick): true when a pass of the map's loop has read the left
    /// button (before it acts on it: a portrait's sheet opens after), false when the loop is left. Not set in play.</summary>
    public static Action<bool>? PassHook { get; set; }

    private int _viewColumn;
    private int _viewTop;
    private int _viewRows;

    private int _originX => _viewColumn * WorldMap.TileWidth;

    private int _originY => (_viewTop + _viewRows) * WorldMap.RowHeight;
    private int _x;
    private int _y;
    private int _direction;
    private int _frame;
    private bool _verticalStep;
    private int _background = 0x68;
    private string _coordinates = "";

    /// <summary>Travels until a place is reached or something happens; returns the card to show.</summary>
    // orig: world-map-start R030+0x00A8 (1462:00A8) (music, the first draw) and world-map-loop R030+0x02C0 (1462:02C0) (the passes);
    // the return value is the `sel` branch of the loop: a location's arrival card, its own encounter card (2000), a road encounter
    // (1000), or the camp ambush
    public int Travel(GameState state, Func<int, bool>? eventStarted = null)
    {
        ui.Redraw = () => UseMapPalette(state);   // orig: the `redraw` label of 0c44 after a character sheet or the party information: pictures reloaded, season palette, panel
        try
        {
            return TravelLoop(state, eventStarted);
        }
        finally
        {
            ui.Redraw = null;
            PassHook?.Invoke(false);
        }
    }

    private int TravelLoop(GameState state, Func<int, bool>? eventStarted)
    {
        // orig: world-map-start R030+0x00A8: DS:9C3D = thunk 09C0:18F1(5) (the piece playing, kept: nothing on the map plays it back), then
        //   `if (roll-below(2)) thunk 09C0:18F1(0x14) else thunk 09C0:18F1(0x13)`: one draw of the generator and a travel tune; tests: MusicTests
        ui.SaveMusic();
        ui.PlayMusic(state.Random.Below(2) != 0 ? 0x14 : 0x13);
        // orig: `DS:A891 = 3;` in world-map-start R030+0x00A8 (the map-leave mode, GameState.MapLeaveMode)
        state.MapLeaveMode = 3;
        PlaceParty(state);
        // orig: world-map-start R030+0x022B: R030+0x1C76 allocates the row buffer and reads the rows from DS:286E (R030+0x1DD4(file, DS:286E, -1)),
        //   then R030+0x1F30 the pictures
        map.Rows.Open(_viewTop);
        LoadPictures();
        UseMapPalette(state);
        var travel = new OverlandTravel(state, map, eventStarted);
        travel.HourPassed = () =>
        {
            // orig: R030+0x1300 after pass-hours(1): the clock picture onto the screen, then 2dd6 and 20d2 when the background changed
            TimePassage.Advance(state, 1);
            _page.DrawClock(Clock(state));
            var background = _background;
            ApplySeason(state);
            _mapDirty |= _background != background;
        };
        travel.PlayCue = tune => ui.PlayMusic(tune);
        travel.MapMessage = ShowMapMessage;
        travel.View = () => (_viewColumn, _viewRows);
        // C: the step reads 20d2's cell buffer under the marker (R030+0x137B..0x1382), drawn for the current view before every step (Draw)
        travel.CellUnderMarker = (x, y) =>
        {
            var (row, column) = CellBuffer.MarkerCell(x - _originX, y - _originY, _viewTop, _viewRows);
            return map.Buffer[row, column];
        };
        travel.Begin();
        (int X, int Y)? target = null;
        while (true)
        {
            Draw(state);
            // orig: R030+0x0C44's F1..F5 / Ctrl-F1..F5 cases (HotKeys, with GameUi.SheetKeys)
            var action = ui.PollWithSheetKeys();
            var key = action?.Key ?? Keys.None;
            // orig: R030+0x0540..0x066A: `if (key-waiting-test() || DS:0880 || DS:0881 || DS:0882) { key = read-key(); if (key-waiting-test()) key =
            //   read-key(); else key = the held key's (HeldKey, or the key read); DS:0880 = DS:0881 = DS:0882 = 0; }`: a second waiting key is
            //   taken in the same pass and replaces the first, so the two presses a held key leaves in the buffer (BiosKeyBuffer) make one step,
            //   and the walk stops within a pass of the key's release
            // C: with a key held (the keyboard interrupt's bytes) and none waiting, read-key waits for the next key (root 047C:0744, INT 16h
            //   AH=0); the held key then replaces it: the ` and \ keys and the keypad's - and + (DS:088A) walk as Up, Left, Down and Right
            //   do, and any key read while Enter is held is Enter
            var held = ui.Directions;
            if (key != Keys.None || held.X != 0 || held.Y != 0 || held.Enter)
            {
                if (key == Keys.None)
                {
                    key = ui.ReadKey();
                }
                key = ui.TryReadKey(out var second) ? second : HeldKey(held.X, held.Y, held.Enter) ?? key;
                held.ClearForTheMap();
            }

            // C: `if (mouse-button-state & 1)`: while the left button is HELD, a point right of x = 0x3c steers the marker, a point at or left of it clears the target and opens the sheet of the member in that row.
            // DIVERGENCE: the portrait click opens the sheet once per press (the original would reopen it every pass while the button stays down); the steering follows the held button as in the original.
            var mouse = ui.Mouse;
            PassHook?.Invoke(true);
            if (mouse.Left)
            {
                if (mouse.X > AreaX)
                {
                    // C: mxe = pointer-column - 0x41; mye = pointer-row - 0x15: a point of the view (world-map-loop's locals), not of the map: when the view
                    //   jumps (0c44 moves DS:2866 / DS:2868 by 128 / 92 pixels) the target stays where it was in the view
                    target = (mouse.X - AreaX - 5, mouse.Y - AreaY - 5);
                }
                else if (action is { IsLeftClick: true })
                {
                    target = null;
                    OpenSheet(state, mouse.Y / PartyPanel.RowHeight);
                }
            }
            // C: the right button with a Shift key held shows "character X:%02d Y:%02d" at (0x58, 0x1f) in white, until the next redraw.
            _coordinates = ui.Mouse.Right && ui.ShiftHeld ? CoordinateText() : "";

            // orig: the start of R030+0x0C44 (+0x54/+0x63), every pass: party-map-column / party-map-row = the cell under the marker (after the last pass's
            //   scroll). The menu bar (Poll above) comes before it, as menu-bar-loop does in world-map-loop: a save there holds the cell of the
            //   previous pass, the one the last walk stepped from.
            var (cellColumn, cellRow) = WorldMap.CellAt(_x, _y);
            state.MapPosition = new Core.World.MapPosition(cellColumn, cellRow);
            var (dx, dy) = key != Keys.None ? DirectionOf(key) : TowardTarget(target);
            if (key != Keys.None)
            {
                // C: a key (or the joystick) overrides the mouse: mxe = 0
                target = null;
                if (IsRestKey(key))
                {
                    // orig: case 0x1e41 / 0x1e61 / 0x4100 of 0c44
                    if (Rest(state, travel) is { } restCard)
                    {
                        return restCard;
                    }
                }
                else if (IsCampKey(key))
                {
                    // orig: case 0x2e43 / 0x2e63 / 0x4200 of 0c44
                    if (CampOnTheMap(state, travel) is { } campCard)
                    {
                        return campCard;
                    }
                }
            }
            if (dx == 0 && dy == 0)
            {
                // C: key 0x4c35 (idle) runs 0c44 with DS:8676 = 0: no step, the standing marker; the frame counter still counts (`if (key) DS:2910++`)
                _direction = 0;
                _frame = (_frame + 1) % 8;
                ui.DelayClock(StepClock);
                continue;
            }
            var result = MoveOnce(state, travel, dx, dy);
            if (result is { Outcome: StepOutcome.Blocked })
            {
                // C: 9999 -> `mxe = 0` in the loop (the target is dropped)
                target = null;
            }
            else if (result is { Outcome: StepOutcome.Arrived or StepOutcome.Encounter, Card: > 0 } r)
            {
                return r.Card;
            }
            ui.DelayClock(StepClock);
        }
    }

    /// <summary>Opens a member's character sheet (a click on the portrait), if the slot holds someone.</summary>
    // orig: the `pointer-column <= 0x3c` arm at the top of world-map-loop: `sel = pointer-row / 40; if (party[sel].present) { free the map icons; member-sheet-overlay-entry(sel); redraw ... }`
    public Action<int>? CharacterSheet { get; init; }

    /// <summary>Camps on the map (C or F8). Receives the party's watch value (<see cref="WorldMap.CampWatch"/>); the screen it opens advances the clock itself.</summary>
    // orig: thunk 09C0:1FF9(R030+0x25BE()) in 0c44
    // C: R036+0x0000 takes the value as the day's poaching risk (CampStay.DailyRisk: the risk grows by it each day).
    public Action<int>? Camp { get; init; }

    private void OpenSheet(GameState state, int member)
    {
        if (member < state.Party.Members.Count && state.Party.Members[member].IsPresent)
        {
            CharacterSheet?.Invoke(member);
            UseMapPalette(state);
        }
    }

    // orig: case 0x1e41 / 0x1e61 / 0x4100 ('A', 'a', F7) of R030+0x0C44, written from the C
    // C: the marker stands (sprite row 9) and every pass lets one step's time go by at the same place, with wait-ticks(75) before and after; the loop ends on a key press (checked after both delays) or when a mouse button is down (checked at the top). Any result of 1300 other than "walked on" leaves the loop: an arrival, an encounter, or a blocked step (impossible in place). DS:A891 is 1 until the first step and 3 after it.
    private int? Rest(GameState state, OverlandTravel travel)
    {
        _frame = 0;
        state.MapLeaveMode = 1;
        try
        {
            return RestLoop(state, travel);
        }
        finally
        {
            // C: every way out of the do/while sets DS:A891 = 3 (after the step, and after the loop).
            state.MapLeaveMode = 3;
        }
    }

    // orig: R030+0x0C44 case 0x1E41: `goto test; do { if (mouse-button-state & 3) break; wait-ticks(75); kf = 1300(...); DS:A891 = 3;
    //   if (kf != -1) return kf; 16c6(..., 9); wait-ticks(75); test: } while (!key-waiting-test());` (wait-ticks R030+0x3096 spins on the
    //   clock without an input test): a key pressed during the first delay still lets that step's time pass, and the key stays in the
    //   buffer for the map's next pass (key-waiting-test only looks); tests: OverlandKeyTests.AKeyInTheRestsFirstDelayStillLetsTheStepHappen; lockstep sessions with the rest key agree (replay)
    private int? RestLoop(GameState state, OverlandTravel travel)
    {
        while (!ui.KeyWaiting)
        {
            if (ui.Mouse.Buttons != MouseButtons.None)
            {
                return null;
            }
            ui.Delay(RestMilliseconds);
            var result = travel.Step(_x, _y);
            Trace?.Invoke($"rest step -> {result.Outcome}");
            state.MapLeaveMode = 3;
            if (result.Outcome != StepOutcome.Walked)
            {
                return result.Card > 0 ? result.Card : null;
            }
            _direction = WaitingDirection;
            Draw(state);
            _frame = (_frame + 1) % 8;
            ui.Delay(RestMilliseconds);
        }
        return null;
    }

    // orig: case 0x2e43 / 0x2e63 / 0x4200 ('C', 'c', F8) of R030+0x0C44: free the icon buffer, thunk 09C0:1FF9(25be()) (thunk 09C0:1FF9 = the camp, R036+0x0000), `if (event-begun-flag(0x5f)) return 1000`, then reload the pictures and redraw
    // C: 1000 is the road-encounter branch, which for an ambush in camp (event 0x5f) is roll-below(2) ? 0x16a : 0x169.
    private int? CampOnTheMap(GameState state, OverlandTravel travel)
    {
        // C: 25be reads the cell buffer 20d2 filled for the view (Draw), under the marker as the view's registers place it: at the west / north
        //   border the block starts one cell east / south (WorldMap.BufferOffset), and it is read flat (CellBuffer.CampWatch)
        var (row, column) = CellBuffer.MarkerCell(_x - _originX, _y - _originY, _viewTop, _viewRows);
        Camp?.Invoke(map.Buffer.CampWatch(state.Party.Members.Count, row, column));
        UseMapPalette(state);
        return travel.AmbushInCamp() is { } card and > 0 ? card : null;
    }

    private static bool IsRestKey(int key) => key == Keys.F7 || Keys.IsLetter(key, 'a') && (key >> 8) == 0x1E;

    private static bool IsCampKey(int key) => key == Keys.F8 || Keys.IsLetter(key, 'c') && (key >> 8) == 0x2E;

    // orig: sprintf(bufk, "character X:%02d Y:%02d", colh, rowo) in world-map-loop
    // C: the numbers are NOT the party's cell: the row is (DS:2868 + 11) / 4 + DS:286E + DS:286C (one more than party-map-row, no - 1) and the column uses +9 (odd rows) or +17 (even rows) where the party's cell uses +16 / +24 and -1; the display is a debugging aid and keeps its own arithmetic.
    private string CoordinateText()
    {
        var row = (_y + 11) / WorldMap.RowHeight;
        var column = (row - 1) % 2 != 0 ? (_x + 9) / WorldMap.TileWidth : (_x + 17) / WorldMap.TileWidth;
        return $"character X:{column:00} Y:{row:00}";
    }

    /// <summary>One step: horizontal moves every time, vertical moves every other time, as in the game.</summary>
    // orig: the direction cases of R030+0x0C44 (DS:2866 ± 1 for the horizontal part, DS:2868 ± 1 only when DS:290E++ was non-zero, DS:8676 = direction)
    // C: `if (DS:290E++)` counts every pass with a vertical key, moved or not, and `if (DS:290E > 1) DS:290E = 0` resets it after the second: a vertical pixel needs two passes, and each pass pays for a step (1300) even when the marker did not move or was held by the border (`if (DS:8676 != 0) 1300(...)`).
    private StepResult MoveOnce(GameState state, OverlandTravel travel, int dx, int dy)
    {
        var x = _x + dx;
        var y = _y;
        if (dy != 0)
        {
            _verticalStep = !_verticalStep;
            if (!_verticalStep)
            {
                y += dy;
            }
        }
        // orig: DS:2866 kept in [0xB, 0xF7] and DS:2868 in [0xD, 0xB1]: in the view
        x = _originX + Math.Clamp(x - _originX, 0xB, 0xF7);
        y = _originY + Math.Clamp(y - _originY, 0xD, 0xB1);
        _direction = DirectionNumber(dx, dy);
        _frame = (_frame + 1) % 8;
        var result = travel.Step(x, y);
        Trace?.Invoke($"step d={dx},{dy} view {x - _originX},{y - _originY} col {_viewColumn} top {_viewTop} rows {_viewRows} cell {state.MapPosition.X},{state.MapPosition.Y} -> {result.Outcome}");
        // orig: for a result of 0 or more (blocked, arrived, an encounter) 0c44 puts the marker back and returns before the scroll block; only a
        //   plain walk (-1) keeps the new position (the cell the party stands on is OverlandTravel.Step's)
        if (result.Outcome == StepOutcome.Walked)
        {
            _x = x;
            _y = y;
            Scroll();
        }
        return result;
    }

    // orig: the start of world-map-start: x = party-map-column = city exit (cities) or the location's own position, view centred with DS:2866 = 0x82 and DS:2868 = 0x40 (hence the 130 and 64), then location-now = -1 in world-map-loop
    // C: for a city (location-now < 92) the party leaves from the exit point of the city record (exit-column, exit-row); for another place from the place's own x, y; in the wilderness from party-map-column, party-map-row.
    // orig: world-map-start R030+0x00A8 (1462:015A..0222) places the view and the marker for party (x, y) in rows of 4 and columns of 16 pixels:
    //   top = max(y / 100 - 1, 0) * 100 (DS:286E); r = y - top - 16 (DS:286C); while r > 0x53 the top moves down 24 rows; marker y DS:2868 = 0x40;
    //   near the map's bottom (height - top - 0x30 < r) DS:2868 = (top - height + r + 0x40) * 4, DS:286E = height - 0x83, DS:286C = 0x53;
    //   DS:286C < 0 becomes 0; marker x DS:2866 = 0x82 with the view column DS:286A = x - 8, near the right edge (x - 8 > width - 0x11)
    //   DS:2866 = (x - 8 - width) * 16 + 0x192 with DS:286A = width - 0x11, at the left (x - 8 <= 0) DS:2866 = 0x82 - (x - 8) with DS:286A = 0.
    // C: in one absolute pixel (WorldMap.CellAt) the marker stands at (16 x + 2, 4 y) away from the edges. Every pass of 0c44, the idle one
    //   included, sets party-map-column/party-map-row from the cell under the marker (R030+0x0C44 +0x54/+0x63): (16 x + 2, 4 y) is cell (x, y + 1), so the
    //   party stands one row south of the exit point as soon as the map runs (the first pass comes before any step or save).
    // C: x - 8 <= 0 puts the marker at 0x82 - (x - 8) pixels, not 16 x + 2 (a column near the left edge reads another cell); kept.
    private void PlaceParty(GameState state)
    {
        var (column, row) = (state.MapPosition.X, state.MapPosition.Y);
        state.CurrentLocation = GameState.Wilderness;
        var top = Math.Max(row / 100 - 1, 0) * 100;
        var rows = row - top - 0x10;
        var markerY = 0x40;
        while (rows > 0x53)
        {
            top += 0x18;
            rows = row - top - 0x10;
        }
        if (map.Height - top - 0x30 < rows)
        {
            markerY = (top - map.Height + rows + 0x40) * 4;
            top = map.Height - 0x83;
            rows = 0x53;
        }
        rows = Math.Max(rows, 0);
        var markerX = 0x82;
        var viewColumn = column - 8;
        if (viewColumn > map.Width - 0x11)
        {
            markerX = (viewColumn - map.Width) * 16 + 0x192;
            viewColumn = map.Width - 0x11;
        }
        else if (viewColumn <= 0)
        {
            markerX = 0x82 - viewColumn;
            viewColumn = 0;
        }
        _viewColumn = viewColumn;
        _viewTop = top;
        _viewRows = rows;
        _x = _originX + markerX;
        _y = _originY + markerY;
        var (cellColumn, cellRow) = WorldMap.CellAt(_x, _y);
        state.MapPosition = new Core.World.MapPosition(cellColumn, cellRow);
        _verticalStep = false;
        _frame = 0;
        _direction = 0;
    }

    /// <summary>The view jumps when the party comes near its edge; the marker keeps its place on the map but at the map's borders.</summary>
    // orig: the scroll block of R030+0x0C44 (1462:10E0..12AE), after a walked step or a pass without one: with DS:2868 < 0x14 or > 0xAA the
    //   shown rows move 0x17 (23) and the marker 0x5C (92) pixels the other way; the row buffer follows 0x41 rows at a time (1dd4 reloads it) and
    //   the shown rows are kept in 1 .. 0x53; at the bottom (height - top - 0x30 <= rows) the buffer is the last 0x83 rows and the marker moves by
    //   (old - new rows) * 5 pixels (rows are 4 pixels: the marker leaves its cell); with DS:2866 < 0x14 or > 0xE2 the view moves 8 columns and the
    //   marker 0x80 pixels, the view kept in 0 .. width - 0x11 with the marker corrected by 16 per column at the borders
    // C: the marker's map pixel (_x, _y) is what the next pass reads the party's cell from (0c44 +0x54/+0x63, TravelLoop): the bottom correction moves the party.
    private void Scroll()
    {
        var vx = _x - _originX;
        var vy = _y - _originY;
        var (oldColumn, oldTop, oldRows) = (_viewColumn, _viewTop, _viewRows);
        if (vy < 0x14 || vy > 0xAA)
        {
            _viewRows += vy < 0x14 ? -0x17 : 0x17;
            if (map.Height - _viewTop - 0x30 <= _viewRows)
            {
                // BUG: R030+0x1133..0x1154 sets DS:286E to the last 0x83 rows without reading them (no call of R030+0x1DD4 on this path): when the
                //   buffer's top was not already there (a top of 0x318 from world-map-start, the party walking south), the row buffer keeps the
                //   rows from the old top and the map drawn (and the cells read) from it are (height - 0x83 - old top) rows north of the view's
                //   registers until a scroll reads the rows again. Reproduced (map.Rows is left as it is).
                _viewTop = map.Height - 0x83;
                _viewRows = map.Height - _viewTop - 0x30;
                vy += (oldRows - _viewRows) * 5;
            }
            else if (_viewRows > 0x53)
            {
                _viewTop = _viewTop + 0xC4 >= map.Height ? map.Height - 0x83 : _viewTop + 0x41;
                if (_viewTop != oldTop)
                {
                    // orig: R030+0x118A: R030+0x1DD4(file, DS:286E, old DS:286E)
                    map.Rows.Load(_viewTop, oldTop);
                    _viewRows += oldTop - _viewTop;
                    vy += vy < 0x14 ? 0x5C : -0x5C;
                }
                _viewRows = Math.Min(_viewRows, 0x53);
            }
            else if (_viewRows <= 1)
            {
                _viewTop = _viewTop <= 0x41 ? 0 : _viewTop - 0x41;
                if (_viewTop != oldTop)
                {
                    // orig: R030+0x11EE: R030+0x1DD4(file, DS:286E, old DS:286E) (RowBuffer.Load: the stale slots of a scroll up)
                    map.Rows.Load(_viewTop, oldTop);
                    _viewRows += oldTop - _viewTop;
                    vy += vy < 0x14 ? 0x5C : -0x5C;
                }
                if (_viewRows <= 1)
                {
                    _viewRows = 1;
                    vy += (oldRows - 1) * 4;
                }
            }
            else
            {
                vy += vy < 0x14 ? 0x5C : -0x5C;
            }
        }
        if (vx < 0x14 || vx > 0xE2)
        {
            _viewColumn += vx < 0x14 ? -8 : 8;
            if (map.Width - 0x11 < _viewColumn)
            {
                _viewColumn = map.Width - 0x11;
                vx += (oldColumn - _viewColumn) * 16;
            }
            else if (_viewColumn <= 0)
            {
                _viewColumn = 0;
                vx += oldColumn * 16;
            }
            else
            {
                vx += vx < 0x14 ? 0x80 : -0x80;
            }
        }
        _x = _originX + vx;
        _y = _originY + vy;
        _mapDirty |= _viewColumn != oldColumn || _viewRows != oldRows;
    }


    // orig: R030+0x20D2 when due (see _mapDirty), then R030+0x16C6 (the party) every pass, as 0c44 ends: the screen keeps what they drew
    private void Draw(GameState state)
    {
        var canvas = ui.Canvas;
        var firstRow = _viewTop + _viewRows;
        if (_mapDirty)
        {
            _mapDirty = false;
            // C: 20d2 fills the cell buffer from the row buffer as it draws (the camp watch reads it): OverlandPage.DrawMap
            _page.DrawMap(map, _pictures!, _viewColumn, _viewTop, _viewRows, (byte)_background,
                ui.Assets.PictureExists("mapheada") ? ui.Assets.Picture("mapheada").Image : null, Clock(state));
        }
        _page.DrawParty(_pictures!, firstRow, _x - _originX, _y - _originY, _direction, _frame, (byte)_background);
        _page.CopyTo(canvas);
        panel.Draw(canvas, state.Party);
        if (_coordinates.Length > 0)
        {
            ui.Text.Draw(canvas, 0x58, 0x1F, _coordinates, Palette.White);
        }
    }

    private IndexedImage? Clock(GameState state)
    {
        var clock = $"mapbrs{state.Date.Hour / 3 * 3:00}";
        return ui.Assets.PictureExists(clock) ? ui.Assets.Picture(clock).Image : null;
    }

    /// <summary>The map pictures' colours (cards and sheets change the palette), with the season's changes.</summary>
    // orig: image-paint-palette(1, "pics\\mapicons.pic", palette-now) (reloads the palette of MAPICONS.PIC) followed by R030+0x2DD6 and palette-load(palette-now) - in 1f30, after a sheet (0c44's redraw label) and after a portrait click
    private void UseMapPalette(GameState state)
    {
        _mapDirty = true;
        ui.Canvas.UsePalette(ui.Assets.Picture("mapicons"));
        ApplySeason(state);
    }

    // orig: R030+0x2DD6, see SeasonPalette; the background is DS:2860, the colour the map buffer is cleared to before the tiles are drawn (root 047C:04EA(1, DS:2860) in 20d2)
    // C: world-map-start first sets DS:2860 from the month alone (winter 0xF, 6 in April, September and October, 0x68 otherwise); 1f30 then calls 2dd6 which recomputes it with the day of the month, so only the second values are ever drawn.
    private void ApplySeason(GameState state)
    {
        var palette = new CanvasPaletteBytes(ui.Canvas);
        var result = SeasonPalette.Apply(palette, state.Date.Month, state.Date.Day);
        _background = result.Background;
    }

    /// <summary>The palette of the canvas as 6-bit bytes: colour n is bytes 3n (red), 3n + 1, 3n + 2.</summary>
    // C: the game keeps the VGA DAC values (6 bits); the canvas holds 8-bit colours made from them as (v << 2) | (v >> 4), which v = c >> 2 undoes exactly.
    public sealed class CanvasPaletteBytes(Canvas canvas) : SeasonPalette.IPaletteBytes
    {
        public int this[int offset]
        {
            get
            {
                var color = canvas.Screen.Palette[offset / 3];
                return ((int)(color >> ShiftOf(offset)) & 0xFF) >> 2;
            }
            set
            {
                var index = offset / 3;
                var shift = ShiftOf(offset);
                var eight = (uint)((value << 2) | (value >> 4)) & 0xFF;
                canvas.Screen.Palette[index] = (canvas.Screen.Palette[index] & ~(0xFFu << shift)) | eight << shift;
            }
        }

        private static int ShiftOf(int offset) => (offset % 3) switch { 0 => 16, 1 => 8, _ => 0 };
    }

    // orig: R030+0x2BB0 (1462:2BB0) draft (bytes match): waits for the left button to be released, reads record `message` (400 bytes) of darkland.msg, saves the area (100, 50, 180x115), fills it with colour 7 under a dark frame (colour 8), draws the text in colour 0 at (105, 55) in a column 170 wide, and waits for a key or either mouse button
    private void ShowMapMessage(int message)
    {
        var canvas = ui.Canvas;
        var saved = canvas.Save(100, 50, 180, 115);
        canvas.Fill(100, 50, 180, 115, 7);
        canvas.Fill(100, 50, 180, 1, 8);
        canvas.Fill(100, 164, 180, 1, 8);
        canvas.Fill(100, 50, 1, 115, 8);
        canvas.Fill(279, 50, 1, 115, 8);
        var text = MapMessageText(message);
        if (text.Length > 0)
        {
            new CardTextLayout(ui.Text).Draw(canvas, text, 105, 55, 170,
                new CardTextLayout.Settings { Color = 0, Leading = 0 });
        }
        while (true)
        {
            var action = ui.WaitForAction();
            if (action.IsKey || action.Click != MouseButtons.None)
            {
                break;
            }
        }
        canvas.Restore(saved);
    }

    // orig: fseek(f, msg * 400 + 1), fread(buf, 400, 1, f) on darkland.msg (the first byte is skipped by fgetc)
    // C: the record is a 400-byte block read raw, with no terminator check: the text ends at its first zero byte; a message past the end of the file draws whatever fread left in the local buffer (garbage).
    // DIVERGENCE: a message number past the end of the file shows an empty box.
    private byte[] MapMessageText(int message)
    {
        if (!ui.Assets.Files.Exists("DARKLAND.MSG"))
        {
            return [];
        }
        var bytes = ui.Assets.Files.Read("DARKLAND.MSG");
        var start = 1 + message * 400;
        if (message < 0 || start >= bytes.Length)
        {
            return [];
        }
        var length = 0;
        while (length < 400 && start + length < bytes.Length && bytes[start + length] != 0)
        {
            length++;
        }
        return bytes[start..(start + length)];
    }

    // orig: R030+0x1F30 (1462:1F30, matched), see OverlandTiles
    private void LoadPictures()
    {
        _pictures ??= new OverlandTiles(ui.Assets.Picture("mapicons").Image, ui.Assets.Picture("mapicon2").Image, ui.Assets.Picture("charicon").Image);
    }

    // orig: the key cases of 0c44: 0x4b00 left (sprite row 7), 0x4d00 right (3), 0x4800 up (1), 0x5000 down (5), 0x4700 Home (8), 0x4900 PgUp (2), 0x5100 PgDn (4), 0x4f00 End (6), 0x4c35 centre (0, standing)
    /// <summary>The key a held key gives the map (the signs of DS:0880 / DS:0881, then Enter from DS:0882), null for none held.</summary>
    // orig: R030+0x0580..0x065F (the joystick's DS:E3FA / DS:E3FB tests with a centred stick): Home, End, PgUp, PgDn for the corners, then
    // Left, Right, Up, Down, then 0x1C0D when DS:0882 is non-zero; only the signs are tested; tests: OverlandKeyTests
    public static int? HeldKey(int x, int y, bool enter) => (x, y) switch
    {
        ( < 0, < 0) => Keys.Home,
        ( < 0, > 0) => Keys.End,
        ( > 0, < 0) => Keys.PageUp,
        ( > 0, > 0) => Keys.PageDown,
        ( < 0, _) => Keys.Left,
        ( > 0, _) => Keys.Right,
        (_, < 0) => Keys.Up,
        (_, > 0) => Keys.Down,
        _ => enter ? Keys.Enter : null,
    };

    private static (int, int) DirectionOf(int key) => key switch
    {
        Keys.Left => (-1, 0),
        Keys.Right => (1, 0),
        Keys.Up => (0, -1),
        Keys.Down => (0, 1),
        Keys.Home => (-1, -1),
        Keys.PageUp => (1, -1),
        Keys.PageDown => (1, 1),
        Keys.End => (-1, 1),
        _ => (0, 0),
    };

    // orig: the "direction towards the mouse target" ladder of world-map-loop (DS:2866 / DS:2868 against mxe / mye): both axes differ -> the diagonal key, one axis -> the arrow, equal -> 0x4c35 and mxe = 0
    private (int, int) TowardTarget((int X, int Y)? target)
    {
        if (target is not { } t)
        {
            return (0, 0);
        }
        return (Math.Sign(t.X - (_x - _originX)), Math.Sign(t.Y - (_y - _originY)));
    }

    /// <summary>Sprite row: 1 north, 2 north-east … 8 north-west; 0 standing.</summary>
    private static int DirectionNumber(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => 1,
        (1, -1) => 2,
        (1, 0) => 3,
        (1, 1) => 4,
        (0, 1) => 5,
        (-1, 1) => 6,
        (-1, 0) => 7,
        (-1, -1) => 8,
        _ => 0,
    };
}
