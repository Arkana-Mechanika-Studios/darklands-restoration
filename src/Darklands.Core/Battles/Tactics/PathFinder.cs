namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The path search (<c>route-search</c>): up to ten "trackers" walk from the combatant toward its
/// destination, going straight while they can and following walls (one on each side) when
/// blocked; the first to arrive wins, then straight shortcuts between its waypoints shorten the
/// path. A path is a list of step bytes (direction 0..9; after packing the high nibble is the
/// facing), 0xFF terminated.
/// </summary>
// orig: R014 12F7 draft (the draft keeps raw names: "gamenames: off"); spec D §4.5..4.8, the tracker
// rules and tables asm-verified there
public sealed class PathFinder
{
    public const int MaxSteps = 250;
    public const int MaxWaypoints = 40;
    private const int Trackers = 10;

    private static readonly int[,] Heading = { { 2, 8, 8, 1 }, { 2, 3, 4, 5 }, { 6, 9, 9, 5 }, { 6, 7, 0, 1 } };
    private static readonly int[,] TurnAway = { { 99, 99 }, { 2, 6 }, { 5, 1 }, { 99, 99 }, { 99, 99 }, { 6, 2 }, { 1, 5 } };
    private static readonly int[,] TurnTo = { { 99, 99 }, { 6, 2 }, { 1, 5 }, { 99, 99 }, { 99, 99 }, { 2, 6 }, { 5, 1 } };
    private static readonly int[] Straight = [99, 1, 2, 99, 99, 5, 6, 0];
    private static readonly int[] Reverse = [99, 5, 6, 99, 99, 1, 2, 0];

    private static readonly sbyte[,] WallFollow =
    {
        { 1, 2, 1, 5, 1, 2, 1, 6, 1, 2, 1, 5, 1, 2, 1, -1 }, { -1, 2, -1, 5, -1, 2, -1, 6, -1, 2, -1, 5, -1, 2, -1, -1 },
        { -1, -1, 5, 5, -1, -1, 6, 6, -1, -1, 5, 5, -1, -1, 1, -1 }, { 5, 5, 5, 5, 6, 6, 6, 6, 5, 5, 5, 5, 1, 2, 1, -1 },
        { 5, 5, 5, 5, 6, 6, 6, 6, 5, 5, 5, 5, 1, 2, 1, -1 }, { -1, -1, -1, -1, 6, 6, 6, 6, -1, -1, -1, -1, 1, 2, 1, -1 },
        { -1, -1, -1, -1, -1, -1, -1, -1, 1, 2, 1, 5, 1, 2, 1, -1 }, { 1, 2, 1, 5, 1, 2, 1, 6, 1, 2, 1, 5, 1, 2, 1, -1 },
        { 2, 2, 5, 5, 2, 2, 6, 6, 2, 2, 5, 5, 2, 2, 1, -1 }, { 6, 6, 6, 6, 6, 6, 6, 6, 1, 2, 1, 5, 1, 2, 1, -1 },
    };

    private static readonly sbyte[,] WallFollowOther =
    {
        { 6, 6, 6, 6, 6, 6, 6, -1, 5, 5, 5, 5, 2, 2, 1, -1 }, { -1, 6, -1, 6, -1, 6, -1, -1, -1, 5, -1, 5, -1, 2, -1, -1 },
        { -1, -1, 1, 6, -1, -1, 1, -1, -1, -1, 1, 5, -1, -1, 1, -1 }, { 2, 2, 1, 6, 2, 2, 1, -1, 2, 2, 1, 5, 2, 2, 1, -1 },
        { 2, 2, 1, 6, 2, 2, 1, -1, 2, 2, 1, 5, 2, 2, 1, -1 }, { -1, -1, -1, -1, 2, 2, 1, -1, -1, -1, -1, -1, 2, 2, 1, -1 },
        { -1, -1, -1, -1, -1, -1, -1, -1, 5, 5, 5, 5, 2, 2, 1, -1 }, { 6, 6, 6, 6, 6, 6, 6, -1, 5, 5, 5, 5, 2, 2, 1, -1 },
        { 1, 6, 1, 6, 1, 6, 1, -1, 1, 5, 1, 5, 1, 2, 1, -1 }, { 5, 5, 5, 5, 2, 2, 1, -1, 5, 5, 5, 5, 2, 2, 1, -1 },
    };

    private readonly BattleMap _map;
    private readonly int[] _state = new int[Trackers];
    // orig: R014+0x5CA4 (12F7:1494) and R014+0x5D04 (12F7:14F4) matched
    // (a tracker's position, goal, facing and sprite stored from / loaded into the scratch record DS:114C, a copy of the searcher made by
    // R014+0x5EB4 matched): here the tracker's position is these arrays and the collision test takes the searcher
    // at that position (tests: Oracle:path.json).
    private readonly (int X, int Y)[] _position = new (int, int)[Trackers];
    private readonly (int X, int Y)[] _previous = new (int, int)[Trackers];
    private readonly int[] _side = new int[Trackers];
    private readonly int[] _balance = new int[Trackers];
    private readonly byte[][] _steps = [.. Enumerable.Range(0, Trackers).Select(_ => new byte[MaxSteps + 1])];
    private readonly int[][] _waypointStep = [.. Enumerable.Range(0, Trackers).Select(_ => new int[MaxWaypoints + 3])];
    private readonly int[][] _waypointSide = [.. Enumerable.Range(0, Trackers).Select(_ => new int[MaxWaypoints + 3])];
    private readonly (int X, int Y)[][] _waypoint = [.. Enumerable.Range(0, Trackers).Select(_ => new (int, int)[MaxWaypoints + 3])];
    private readonly int[] _waypoints = new int[Trackers];
    private readonly int[] _firstWaypoint = new int[Trackers];
    private (int X, int Y) _goal;
    private int _who;
    private int _target = -1;
    private sbyte[] _stepX = new sbyte[10];
    private sbyte[] _stepY = new sbyte[10];
    private int _length;
    private int _winner;
    private (int X, int Y) _end;

    public PathFinder(BattleMap map)
    {
        _map = map;
    }

    /// <summary>The last path found (step bytes, 0xFF terminated) and its length.</summary>
    public byte[] Path { get; } = new byte[MaxSteps];

    public int PathLength { get; private set; }

    /// <summary>Waypoints of the last path (step index and position).</summary>
    public int WaypointCount { get; private set; }

    private readonly int[] _pathWaypointStep = new int[MaxWaypoints + 3];
    private readonly (int X, int Y)[] _pathWaypoint = new (int, int)[MaxWaypoints + 3];

    /// <summary>
    /// Searches a path for combatant <paramref name="who"/> from its position to its destination;
    /// true when found (the path is in <see cref="Path"/>).
    /// </summary>
    /// <param name="steps">The walking step tables of the combatant's animation set.</param>
    /// <param name="openDoors">For enemies: opens the unlocked closed doors for the search and restores them afterwards (spec D §4.5).</param>
    // orig: route-search R014+0x481A (12F7:000A) draft; tests: Oracle:path.json (60 searches)
    // DIVERGENCE: the busy cursor and the stopped clock (DS:07D3) during the search are the screen's.
    /// <param name="tracked">Runs once the trackers found a path of at least one step (its straight waypoints dropped), before the shortcuts.</param>
    public bool Find(Battle battle, int who, sbyte[] stepX, sbyte[] stepY, Action<bool>? openDoors = null, Action? tracked = null)
    {
        var c = battle[who];
        _who = who;
        _stepX = stepX;
        _stepY = stepY;
        PathLength = 9999;
        _target = c.Target;
        if (_target != -1 && battle[_target].Floor != c.Floor)
        {
            return false;
        }
        _map.StrictWalls = true;
        _map.Searching = true;
        _map.SearchTarget = _target;
        try
        {
            // DS:D697 is set only for the destination test: during the trackers moving combatants block again.
            var atGoal = _map.Collides(who, c.Destination.X, c.Destination.Y);
            _map.Searching = false;
            var enemy = who >= Combatant.PartySlots;
            if (!atGoal.IsFree && !HitTarget())
            {
                // BUG: R014+0x48CA leaves through the common exit (R014+0x4B36): R014+0x4B5A (for an enemy) puts every door that is no twin
                // back to the open flags DS:E0C0 kept at the last enemy search that opened them, though this search opened none, and copies
                // those flags back (DS:E0C0 to the level's +0x33D3): a door opened or shut since then is set back.
                if (enemy)
                {
                    openDoors?.Invoke(false);
                }
                return false;
            }
            if (enemy)
            {
                openDoors?.Invoke(true);
            }
            try
            {
                var found = Track((c.Position.X, c.Position.Y), c.Destination, c.Facing);
                if (_length == 0)
                {
                    PathLength = 1;
                    Path[0] = 0xFF;
                    return found;
                }
                if (!found)
                {
                    return false;
                }
                Array.Copy(_steps[_winner], Path, _length + 1);
                PathLength = _length;
                WaypointCount = _waypoints[_winner];
                for (var k = 0; k < WaypointCount; k++)
                {
                    _pathWaypointStep[k] = _waypointStep[_winner][k];
                    _pathWaypoint[k] = _waypoint[_winner][k];
                }
                DropStraightWaypoints();
                // R014+0x4AE4: the AI's path hook (1DB7:0158) runs here, on the trackers' waypoints, whatever the shortcuts give after it.
                tracked?.Invoke();
                // C: route-search returns the shortcut pass's result: a failed re-plan makes the whole search fail.
                if (!Shortcuts((c.Position.X, c.Position.Y)))
                {
                    return false;
                }
                var end = Array.IndexOf(Path, (byte)0xFF, 0, MaxSteps - 1);
                PathLength = end < 0 ? MaxSteps - 1 : end;
                Path[MaxSteps - 1] = 0xFF;
                return true;
            }
            finally
            {
                if (enemy)
                {
                    openDoors?.Invoke(false);
                }
            }
        }
        finally
        {
            _map.StrictWalls = false;
            _map.Searching = false;
        }
    }

    /// <summary>The steps of the last winning tracker (0xFF terminated).</summary>
    public ReadOnlySpan<byte> LastTrack => _steps[_winner];

    /// <summary>Waypoint k of the last path found.</summary>
    public MapPoint Waypoint(int k) => new(_pathWaypoint[k].X, _pathWaypoint[k].Y);

    /// <summary>
    /// The trackers alone (route-tracker-table called directly by the AI's path costs): the number of
    /// steps from <paramref name="from"/> to <paramref name="goal"/>, or −1 when no tracker arrives.
    /// The wall and search rules are whatever the map has at the time.
    /// </summary>
    // orig: route-tracker-table R014+0x4BFA (12F7:03EA) draft (as called by 1DB7:0028)
    public int TrackLength(int who, int target, MapPoint from, MapPoint goal, int facing, sbyte[] stepX, sbyte[] stepY)
    {
        _who = who;
        _target = target;
        _stepX = stepX;
        _stepY = stepY;
        return Track((from.X, from.Y), goal, facing) ? _length : -1;
    }

    /// <summary>Waypoints where the direction does not change are dropped (the shifted-in one is not re-tested).</summary>
    private void DropStraightWaypoints()
    {
        var k = 0;
        while (k < WaypointCount - 1)
        {
            var at = _pathWaypointStep[k];
            if (at + 1 < MaxSteps && at >= 0 && Path[at + 1] == Path[at])
            {
                for (var m = k; m < WaypointCount - 1; m++)
                {
                    _pathWaypointStep[m] = _pathWaypointStep[m + 1];
                    _pathWaypoint[m] = _pathWaypoint[m + 1];
                }
                WaypointCount--;
            }
            k++;
        }
    }

    // orig: R014+0x5C8C (12F7:147C) matched: DS:E11C[k] of the last collision test, 0 for −1
    private bool HitTarget() => _target is >= 0 and < Combatant.Count && _map.LastParts[_target] != 0;

    private bool Collides((int X, int Y) p) => !_map.Collides(_who, p.X, p.Y).IsFree;

    private (int, int) StepFrom((int X, int Y) p, int direction) => (p.X + _stepX[direction], p.Y + _stepY[direction]);

    /// <summary>The trackers from a start to a goal; true when one arrives.</summary>
    // orig: route-tracker-table R014+0x4BFA (12F7:03EA) draft
    private bool Track((int X, int Y) start, MapPoint goal, int facing)
    {
        _ = facing;
        _goal = (goal.X, goal.Y);
        _length = 0;
        Array.Clear(_waypoints);
        Array.Clear(_state);
        _state[0] = 1;
        _position[0] = start;
        _previous[0] = start;
        _firstWaypoint[0] = 0;
        var result = false;
        for (var step = 0; step < MaxSteps; step++)
        {
            for (var k = 0; k < Trackers; k++)
            {
                if (_state[k] == 0)
                {
                    continue;
                }
                if ((_state[k] & 0x80) != 0)
                {
                    _state[k] &= 0x7F;
                    continue;
                }
                var (gx, gy) = _goal;
                int dx = gx - _position[k].X, dy = gy - _position[k].Y;
                var ratio = dx == 0 ? 20 : Math.Abs(dy) * 4 / Math.Abs(dx);
                var slope = ratio == 0 ? 0 : ratio <= 2 ? 1 : ratio <= 6 ? 2 : 3;
                var quad = dx >= 0 && dy < 0 ? 0 : dx >= 0 ? 1 : dy >= 0 ? 2 : 3;
                var dir = Heading[quad, slope];
                if (_state[k] == 1)
                {
                    var r = StraightStep(k, dir, step);
                    if (r == 1)
                    {
                        return true;
                    }
                    continue;
                }
                if (AtGoal(k))
                {
                    return Finish(k, step);
                }
                var last = step > 0 ? _steps[k][step - 1] : 0;
                var side = _side[k];
                (int X, int Y) t;
                int nd;
                if (_balance[k] >= 1)
                {
                    t = StepFrom(_position[k], dir);
                    if (!Collides(t))
                    {
                        _state[k] = 1;
                        nd = dir;
                        Moved(k, step, nd, last, t);
                        continue;
                    }
                    if (HitTarget())
                    {
                        return Finish(k, step);
                    }
                }
                if (_balance[k] < 1)
                {
                    nd = Table(TurnTo, last, side);
                    t = StepFrom(_position[k], nd);
                    if (!Collides(t))
                    {
                        _balance[k]++;
                        Moved(k, step, nd, last, t);
                        continue;
                    }
                    if (HitTarget())
                    {
                        return Finish(k, step);
                    }
                }
                nd = TableAt(Straight, last);
                t = StepFrom(_position[k], nd);
                if (Collides(t))
                {
                    if (HitTarget())
                    {
                        return Finish(k, step);
                    }
                    nd = Table(TurnAway, last, side);
                    t = StepFrom(_position[k], nd);
                    if (!Collides(t))
                    {
                        _balance[k]--;
                    }
                    else
                    {
                        if (HitTarget())
                        {
                            return Finish(k, step);
                        }
                        nd = TableAt(Reverse, last);
                        t = StepFrom(_position[k], nd);
                        if (!Collides(t))
                        {
                            _balance[k] -= 2;
                        }
                        else if (HitTarget())
                        {
                            return Finish(k, step);
                        }
                        // C: otherwise the tracker moves into the blocked reverse position (a quirk of the original).
                    }
                }
                Moved(k, step, nd, last, t);
            }
        }
        return result;
    }

    // C: the tables are indexed with directions 7..9 too, reading the bytes that follow; 99 entries are never used.
    private static int Table(int[,] table, int direction, int side) =>
        direction < table.GetLength(0) ? table[direction, side] : 99;

    private static int TableAt(int[] table, int direction) => direction < table.Length ? table[direction] : 99;

    private void Moved(int k, int step, int nd, int last, (int X, int Y) t)
    {
        if (nd is < 0 or > 9)
        {
            // DIVERGENCE: a 99 table entry (never used in the shipped tables per spec D) would index the step tables out of range.
            _state[k] = 0;
            return;
        }
        _previous[k] = _position[k];
        if (nd != last)
        {
            var w = _waypoints[k];
            _waypointStep[k][w] = step - 1;
            _waypointSide[k][w] = _side[k];
            _waypoint[k][w] = _position[k];
            _waypoints[k]++;
            LoopCheck(k, _waypoints[k] - 1);
            if (_waypoints[k] >= MaxWaypoints)
            {
                _state[k] = 0;
                return;
            }
        }
        _steps[k][step] = (byte)nd;
        _position[k] = t;
        if (Math.Abs(_balance[k]) >= 7)
        {
            _state[k] = 0;
        }
    }

    /// <summary>A tracker going straight: when blocked it becomes a wall follower and may spawn one for the other side.</summary>
    // orig: R014+0x52C4 (12F7:0AB4) draft
    private int StraightStep(int k, int dir, int step)
    {
        if (AtGoal(k))
        {
            Finish(k, step);
            return 1;
        }
        var t = StepFrom(_position[k], dir);
        if (!Collides(t))
        {
            _previous[k] = _position[k];
            _steps[k][step] = (byte)dir;
            _position[k] = t;
            return 2;
        }
        if (HitTarget())
        {
            Finish(k, step);
            return 1;
        }
        var mask = 0;
        foreach (var (d, bit) in new[] { (1, 1), (2, 2), (5, 4), (6, 8) })
        {
            var probe = StepFrom(_position[k], d);
            if (Collides(probe))
            {
                if (HitTarget())
                {
                    Finish(k, step);
                    return 1;
                }
                mask |= bit;
            }
        }
        int nd = WallFollow[dir, mask];
        var save = _position[k];
        var s = step;
        if (nd == -1)
        {
            _state[k] = 0;
        }
        else
        {
            _side[k] = 0;
            _balance[k] = 0;
            _state[k] = 2;
            if (s != 0)
            {
                var w = _waypoints[k];
                _waypointStep[k][w] = s - 1;
                _waypointSide[k][w] = 0;
                _waypoint[k][w] = _position[k];
                _waypoints[k]++;
                LoopCheck(k, _waypoints[k] - 1);
                if (_waypoints[k] > MaxWaypoints - 1)
                {
                    _state[k] = 0;
                    return -1;
                }
            }
            save = _position[k];
            _position[k] = StepFrom(_position[k], nd);
            _steps[k][s] = (byte)nd;
            _previous[k] = _position[k];
        }
        int nd2 = WallFollowOther[dir, mask];
        if (nd2 == -1)
        {
            return 2;
        }
        var n = Array.IndexOf(_state, 0);
        if (n < 0)
        {
            return -1;
        }
        _state[n] = k < n ? 0x82 : 2;
        _side[n] = 1;
        Array.Copy(_steps[k], _steps[n], s);
        _waypoints[n] = _waypoints[k];
        _firstWaypoint[n] = _waypoints[n];
        for (var w = 0; w < _waypoints[k]; w++)
        {
            _waypointStep[n][w] = _waypointStep[k][w];
            _waypointSide[n][w] = _waypointSide[k][w];
            _waypoint[n][w] = _waypoint[k][w];
        }
        _balance[n] = 0;
        _position[n] = StepFrom(save, nd2);
        _steps[n][s] = (byte)nd2;
        _previous[n] = _position[n];
        return 2;
    }

    /// <summary>A tracker that comes back to a waypoint another live tracker (or itself) already passed on the same side dies.</summary>
    // orig: R014+0x519E (12F7:098E) draft (byte-matched per the draft notes)
    private void LoopCheck(int k, int w)
    {
        var (x, y) = _waypoint[k][w];
        for (var m = 0; m < Trackers; m++)
        {
            if (_state[m] == 0)
            {
                continue;
            }
            var end = m == k ? w - 1 : _waypoints[m];
            for (var j = _firstWaypoint[m]; j < end; j++)
            {
                if (_waypointSide[m][j] == _side[k] && Math.Abs(_waypoint[m][j].X - x) <= 1 && Math.Abs(_waypoint[m][j].Y - y) <= 1)
                {
                    _waypoints[k] = 42;
                    return;
                }
            }
        }
    }

    /// <summary>Whether a tracker has reached the goal (or passed it).</summary>
    // orig: R014+0x58A4 (12F7:1094) draft (byte-matched per the draft notes)
    private bool AtGoal(int k)
    {
        int dx = _goal.X - _position[k].X, dy = _goal.Y - _position[k].Y;
        int ex = _goal.X - _previous[k].X, ey = _goal.Y - _previous[k].Y;
        if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2)
        {
            return true;
        }
        // C: root 0410:0038 gives −1 for a negative value and 1 otherwise (0 counts as positive; asm-checked).
        static int Sign(int v) => v < 0 ? -1 : 1;
        if (Sign(dx) == Sign(ex) && Sign(dy) == Sign(ey))
        {
            return false;
        }
        // C: the half sizes are read for combatant number k (the tracker number), not the searcher: a quirk of the original.
        return Math.Abs(dx) < _halfWidth(k) && Math.Abs(dy) < _halfHeight(k);
    }

    private Func<int, int> _halfWidth = _ => 5;
    private Func<int, int> _halfHeight = _ => 2;

    /// <summary>The combatants' half sizes the goal test reads (by tracker number).</summary>
    public void UseHalfSizes(Func<int, int> width, Func<int, int> height)
    {
        _halfWidth = width;
        _halfHeight = height;
    }

    // orig: R014+0x580E (12F7:0FFE) draft (byte-matched per the draft notes)
    private bool Finish(int k, int step)
    {
        var w = _waypoints[k];
        if (w < _waypointStep[k].Length)
        {
            _waypointStep[k][w] = step;
            _waypoint[k][w] = _position[k];
        }
        _end = _position[k];
        _steps[k][step] = 0xFF;
        _waypoints[k]++;
        _winner = k;
        _length = step;
        return true;
    }

    /// <summary>Straight re-plans between waypoints to shorten the path.</summary>
    // orig: R014+0x5976 (12F7:1166) draft (asm-verified per spec D §4.7)
    private bool Shortcuts((int X, int Y) start)
    {
        var n = WaypointCount;
        var wx = new (int X, int Y)[n];
        var wstep = new int[n];
        Array.Copy(_pathWaypoint, wx, n);
        Array.Copy(_pathWaypointStep, wstep, n);
        var result = new byte[MaxSteps + 1];
        byte[]? best = null;
        var bestLength = 0;
        (int X, int Y) bestEnd = default;
        int based = 0, length = 0, a0 = 1, a = 1;
        var done = false;
        var position = start;
        var goal = _goal;
        do
        {
            if (a0 >= n)
            {
                break;
            }
            for (a = a0; a < n; a++)
            {
                goal = wx[a];
                if (!Track(position, new MapPoint(goal.X, goal.Y), 0))
                {
                    return false;
                }
                if (_length + based < wstep[a])
                {
                    best = (byte[])_steps[_winner].Clone();
                    bestLength = _length;
                    bestEnd = _end;
                    var d = wstep[a] - _length - based;
                    for (var m = a; m < n; m++)
                    {
                        wstep[m] -= d;
                    }
                }
                else if (best is not null)
                {
                    Array.Copy(best, 0, result, length, Math.Min(bestLength, MaxSteps - length));
                    length += bestLength;
                    position = bestEnd;
                    based = wstep[a - 1];
                    a0 = a + 1;
                    best = null;
                    if (n == a)
                    {
                        done = true;
                    }
                    break;
                }
            }
        }
        while (a < n);
        if (!done)
        {
            if (a0 < 2)
            {
                if (!Track(position, new MapPoint(goal.X, goal.Y), 0) || _length + length + 1 > MaxSteps - 1)
                {
                    return false;
                }
                Array.Copy(_steps[_winner], 0, result, length, _length);
                length += _length;
            }
            else
            {
                for (a = a0 - 1; a < n; a++)
                {
                    if (!Track(position, new MapPoint(wx[a].X, wx[a].Y), 0) || _length + length + 1 > MaxSteps - 1)
                    {
                        return false;
                    }
                    Array.Copy(_steps[_winner], 0, result, length, _length);
                    length += _length;
                    position = _end;
                }
            }
        }
        if (length != 0)
        {
            result[length] = 0xFF;
            Array.Copy(result, Path, Math.Min(length + 1, MaxSteps));
        }
        return true;
    }

    /// <summary>Gives the path to a combatant: each step byte gets its facing in the high nibble, with one-step facing jitter removed.</summary>
    // orig: packing R014+0x5E2A (12F7:161A) draft (asm-verified per spec D §4.8) after the copy (memcpy) repeated inline in
    // route-hand-over R014+0x5D64 (12F7:1554) draft and route-hand-over-restore R014+0x5DE0 (12F7:15D0) draft
    // C: length + 1 bytes are copied over the combatant's path (the bytes after them keep an earlier path's), byte 249 is set to the end byte.
    public static void GiveTo(byte[] p, byte[] path, int length)
    {
        Array.Copy(path, p, Math.Clamp(length + 1, 0, MaxSteps));
        p[MaxSteps - 1] = 0xFF;
        for (var k = 0; k < MaxSteps && p[k] != 0xFF; k++)
        {
            p[k] = (byte)(p[k] | p[k] << 4);
        }
        if (length > 1)
        {
            for (var k = 0; k < MaxSteps - 2 && p[k + 2] != 0xFF; k++)
            {
                if ((p[k + 2] & 0xF0) == (p[k] & 0xF0) && (p[k + 1] & 0xF0) != (p[k] & 0xF0))
                {
                    p[k + 1] = (byte)((p[k + 1] & 0x0F) | (p[k] & 0xF0));
                }
            }
        }
    }
}
