namespace Darklands.Core.Battles.Generation;

/// <summary>
/// Plans a castle or keep (overlays R005 and R006): a stack of up to five levels, each a set of
/// rectangular rooms (the keep with its corner towers, gate houses, buildings, an outer wall, a gate
/// path, corridors) kept in the <see cref="CastleWork"/> area, with stairs between the levels. The
/// plan is then turned into floor images by the same class's second half (overlay R007).
/// </summary>
/// <remarks>
/// Levels: level 1 is the ground floor with the keep standing in its yard, levels 2.. are its upper
/// floors up to the top level (<see cref="_topLevel"/>), and level 0 is the cellar below. Room kinds
/// (the value written in a room's cells): 2 keep hall, 3 tower, 4 gate house, 5 building, 6 gate path,
/// 7 corridor, 8 wall segment.
/// </remarks>
// orig: R005 1432 layout-battlefield-entry R005+0x0000 and R006 15af..1abf,  (drafts "COMPLETE", mostly unmatched)
internal sealed partial class CastleGenerator
{
    private const int X0 = 0;
    private const int X1 = 1;
    private const int Y0 = 2;
    private const int Y1 = 3;

    /// <summary>The ground floor: the keep stands in its yard, the gate path leads in, and every other level is planned from it.</summary>
    private const int GroundLevel = 1;

    private readonly GenerationState _g;
    private readonly CastleWork _w = new();

    // The generator's globals (DS words of the original).
    private readonly int[] _roomCount = new int[CastleWork.Levels];       // layout-room-list
    private readonly int[] _lastKeepRoom = new int[CastleWork.Levels];    // DS:DB4F: last room of the keep and its towers
    private readonly int[] _lastCastleRoom = new int[CastleWork.Levels];  // DS:DB59: last room before the corridors and the outer wall
    private readonly int[] _stripCount = new int[CastleWork.Levels];      // DS:DB67
    private readonly int[] _stairCount = new int[CastleWork.Levels];      // layout-exit-total
    private readonly int[] _bounds = new int[4];                          // room-bounds-left, DS:DB3F, DS:DB41, DS:DB43: bounding box of the rooms (not the gate path)
    private int _level;            // layout-depth
    private int _lowestLevel;      // layout-depth-min
    private int _highestLevel;     // layout-depth-max
    private int _topLevel;         // DS:DB7F
    private int _lastLevel;        // DS:DB83
    private int _topLevelBuilt;    // DS:DB87
    private int _cellarBuilt;      // DS:DB88
    private int _gateRoom;         // DS:DB63: the gate path room of level 1
    private int _gateSide;         // gate-wall-side

    public CastleGenerator(GenerationState g)
    {
        _g = g;
    }

    /// <summary>One die roll, dice-roll(1, sides): 0 without a draw below 1 side, 1 without a draw for 1 side.</summary>
    // orig: dice-roll R004+0x20F6 (0E76:20F6) draft (byte-matched per the draft notes); see BattlefieldForces.RollDice
    private int Roll(int sides) => _g.Roll(sides);

    /// <summary>dice-roll(count, sides).</summary>
    // orig: dice-roll R004+0x20F6 (0E76:20F6) draft
    private int Dice(int count, int sides) => _g.Forces.RollDice(count, sides);

    // orig: the C macros min and max of R006 seg 16E1 and R006 seg 1637 (plain int operands)
    private static int Min(int a, int b) => a < b ? a : b;

    // orig: see Min
    private static int Max(int a, int b) => a < b ? b : a;

    /// <summary>Rectangle word <paramref name="field"/> (X0, X1, Y0, Y1) of a room: DS:DB3B[(level * 30 + room) * 4 + field].</summary>
    // orig: DS:DB3B, the room table of the work area (see CastleWork)
    private int Room(int level, int room, int field) => _w.Room(level, room, field);

    /// <summary>The kind of a room: DS:DB91[level * 30 + room].</summary>
    // orig: DS:DB91, the room kind table of the work area
    private int RoomKind(int level, int room) => _w.RoomKind(level, room);

    /// <summary>Cell index (level * 40 + x) * 40 + y, the subscript the original computes by hand everywhere.</summary>
    // orig: the index arithmetic of layout-cell-walls, layout-cell-kind and layout-cell-door
    private static int CellIndex(int level, int x, int y) => (level * CastleWork.Cells + x) * CastleWork.Cells + y;

    /// <summary>Is a cell kind a room floor (2..8)?</summary>
    // orig: the test kind > 1 && kind < 9 repeated through R005 and R006
    private static bool IsFloor(int kind) => kind > 1 && kind < 9;

    /// <summary>The number of levels the driver planned.</summary>
    public int LevelCount => _highestLevel - _lowestLevel + 1;

    /// <summary>Plans the levels for a battlefield type; returns the number of levels.</summary>
    // orig: layout-battlefield-entry R005+0x0000 (1432:0000) draft; checked against asm (calls and order)
    // C: n is counted before castle-builder-entry runs and the start level it reports is relative to the lowest level.
    public int Plan(int battlefield)
    {
        ClearWork();
        PlanLevels(battlefield);
        if (battlefield != 0)
        {
            _g.MarkPersistent();
        }
        return LevelCount;
    }

    /// <summary>Clears the work area and the bounding box.</summary>
    // orig: layout-reset R005+0x005C (1432:005C) draft; checked against asm
    private void ClearWork()
    {
        _w.Clear();
        Array.Clear(_stairCount);
        for (var level = 0; level < CastleWork.Levels; level++)
        {
            _roomCount[level] = 0;
            _lastKeepRoom[level] = -1;
            _lastCastleRoom[level] = -1;
            _stripCount[level] = 0;
        }
        _bounds[Y0] = _bounds[X0] = 0x29;
        _bounds[X1] = _bounds[Y1] = 0;
    }

    /// <summary>Plans each level in turn: 1, 2 ... up to the last, then the cellar 0.</summary>
    // orig: R005+0x022A (1432:022A) draft; checked against asm (order of the calls)
    // C: the last level number is re-read by the loop test and may be lowered by the stair planner when no way up was found.
    // C: layout-depth stays at the last value the loop set, even when that level was skipped; the final widen uses it.
    private void PlanLevels(int battlefield)
    {
        _lowestLevel = 6;
        _highestLevel = -1;
        _topLevel = _lastLevel = Roll(2) + 2;
        _topLevelBuilt = _cellarBuilt = 1;
        if (battlefield == 0)
        {
            _lastLevel = 1;
            _topLevel = 2;
            _topLevelBuilt = _cellarBuilt = 0;
        }
        _gateRoom = -1;
        for (var i = 0; i <= _lastLevel; i++)
        {
            _level = i + 1;
            if (_level > _lastLevel)
            {
                _level = 0;
            }
            // DORMANT: the top level is switched off together with the loop bound (the stair planner lowers _lastLevel to its own
            // level, below the top level), so the loop never reaches a top level that is not built.
            if (_level == _topLevel && _topLevelBuilt == 0)
            {
                continue;
            }
            if (_level == 0 && _cellarBuilt == 0)
            {
                continue;
            }
            _lowestLevel = _level > _lowestLevel ? _lowestLevel : _level;
            _highestLevel = _level < _highestLevel ? _highestLevel : _level;
            MarkReachable(30, 30);
            if (_level < _topLevel)
            {
                BuildKeep();
            }
            PlaceTowers(battlefield);
            _lastKeepRoom[_level] = _roomCount[_level] - 1;
            if (_level == 1)
            {
                PlanGate(battlefield);
                _gateRoom = _roomCount[_level] - 1;
            }
            else
            {
                ContinueStairs();
            }
            if (_level == _topLevel)
            {
                _lastCastleRoom[_level] = _roomCount[_level] - 1;
                BuildOuterWall();
            }
            else
            {
                PlaceBuildings();
                if (_level >= 1)
                {
                    PlaceGateHouses();
                }
                if (_level == 1)
                {
                    WidenGate();
                }
                var level = _level;
                _lastCastleRoom[level] = _roomCount[level] - 1;
                if (level >= 1 && level < _lastLevel)
                {
                    PlaceStairs(battlefield);
                }
                BuildCorridors(battlefield);
                if (_level == 0)
                {
                    _cellarBuilt = 0;
                }
            }
        }
        WidenGate();
    }
}
