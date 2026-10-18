namespace Darklands.Core.Battles.Generation;

// Doors and openings between rooms (overlay R006, 1abf). Cell marks 3 and 5 are the two cells of a door across a north/south wall,
// 4 and 6 across an east/west wall, 11 the cell in front of a stair; wall bits are cleared for an opening.
internal sealed partial class CastleGenerator
{
    /// <summary>Joins a freshly added room: kind 3 (tower) to the keep, 7 (corridor) to its two ends and to anything else it touches, 8 (wall) to its two towers.</summary>
    // orig: R006+0x510A (1ABF:000A) draft
    // C: dir, lo and hi are locals the touch test fills in; the later tests read them only after a successful touch.
    private void ConnectRoom(int a, int kind, int b, int c)
    {
        var l = _level;
        var dir = 0;
        var lo = 0;
        var hi = 0;
        if (kind == KindTower)
        {
            if (l >= _topLevel)
            {
                return;
            }
            if (RoomsTouch(a, 0, ref dir, ref lo, ref hi, 0) == 0)
            {
                return;
            }
            OpenRoomSide(a, 0, dir, lo, hi, 1, 1);
            return;
        }
        if (kind == KindCorridor)
        {
            var door = RoomKind(l, b) == KindGateHouse && RoomKind(l, c) == KindGateHouse ? 0 : 1;
            if (!IsTouching(l, a, b) && RoomsTouch(a, b, ref dir, ref lo, ref hi, 0) != 0)
            {
                OpenRoomSide(a, b, dir, lo, hi, door, 1);
            }
            if (!IsTouching(l, a, c) && RoomsTouch(a, c, ref dir, ref lo, ref hi, 0) != 0)
            {
                OpenRoomSide(a, c, dir, lo, hi, door, 1);
            }
            for (var i = 0; i < _roomCount[l]; i++)
            {
                if (i != a && b != i && c != i && !IsTouching(l, i, b) && !IsTouching(l, i, c) && !IsTouching(l, i, a) &&
                    !IsLinked(l, i, b) && !IsLinked(l, i, c) && !IsLinked(l, i, a) &&
                    RoomsTouch(a, i, ref dir, ref lo, ref hi, 0) != 0)
                {
                    OpenRoomSide(a, i, dir, lo, hi, door, 1);
                }
            }
            return;
        }
        // DORMANT: the planner connects only towers (3), corridors (7) and wall segments (8).
        if (kind != KindWall)
        {
            return;
        }
        var ok = b != -1;
        if (ok)
        {
            for (var i = 0; i < _roomCount[l]; i++)
            {
                if (i != a && b != i && IsTouching(l, i, b) && IsTouching(l, i, a) && IsLinked(l, i, a))
                {
                    ok = false;
                }
                if (!ok)
                {
                    break;
                }
            }
        }
        if (ok && RoomsTouch(a, b, ref dir, ref lo, ref hi, 0) != 0)
        {
            OpenRoomSide(a, b, dir, lo, hi, 1, 0);
        }
        ok = c != -1;
        if (ok)
        {
            for (var i = 0; i < _roomCount[l]; i++)
            {
                if (i != a && c != i && IsTouching(l, i, c) && IsTouching(l, i, a) && IsLinked(l, i, a))
                {
                    ok = false;
                }
                if (!ok)
                {
                    break;
                }
            }
        }
        if (!ok)
        {
            return;
        }
        if (RoomsTouch(a, c, ref dir, ref lo, ref hi, 0) == 0)
        {
            return;
        }
        OpenRoomSide(a, c, dir, lo, hi, 1, 0);
    }

    /// <summary>Opens side <paramref name="dir"/> of room a somewhere in [lo, hi]: a door when <paramref name="door"/> and a spot is found, else a plain opening (when <paramref name="force"/>).</summary>
    // orig: R006+0x548E (1ABF:038E) draft
    // C: p keeps the last position tried when no spot suits; a door-less opening is then made there.
    private void OpenRoomSide(int a, int b, int dir, int lo, int hi, int door, int force)
    {
        var l = _level;
        var n = hi - lo + 1;
        // DORMANT: the callers pass a stretch the touch test found (hi >= lo).
        if (n <= 0)
        {
            return;
        }
        var tries = 0;
        var ok = false;
        var p = 0;
        do
        {
            if (tries >= n)
            {
                break;
            }
            tries++;
            p = Roll(n) + lo - 1;
            ok = DoorSpotFree(a, dir, p, 1, 1);
        }
        while (!ok);
        if (!ok && door != 0)
        {
            for (p = lo; p <= hi && !(ok = DoorSpotFree(a, dir, p, 1, 1)); p++)
            {
            }
        }
        if (!ok && force == 0)
        {
            return;
        }
        if (!ok && door != 0)
        {
            p = Roll(n) + lo - 1;
        }
        int i;
        switch (dir)
        {
            case 0:
                i = CellIndex(l, p, Room(l, a, Y0));
                if (!IsFloor(_w.Kind(i)) || !IsFloor(_w.Kind(i - 1)))
                {
                    return;
                }
                _w.SetWalls(i, _w.Walls(i) & ~1);
                _w.SetWalls(i - 1, _w.Walls(i - 1) & ~4);
                if (door != 0 && ok)
                {
                    _w.SetMark(i, 3);
                    _w.SetMark(i - 1, 5);
                }
                break;
            case 1:
                i = CellIndex(l, Room(l, a, X1), p);
                if (!IsFloor(_w.Kind(i)) || !IsFloor(_w.Kind(i + 40)))
                {
                    return;
                }
                _w.SetWalls(i, _w.Walls(i) & ~2);
                _w.SetWalls(i + 40, _w.Walls(i + 40) & ~8);
                if (door != 0 && ok)
                {
                    _w.SetMark(i, 4);
                    _w.SetMark(i + 40, 6);
                }
                break;
            case 2:
                i = CellIndex(l, p, Room(l, a, Y1));
                if (!IsFloor(_w.Kind(i)) || !IsFloor(_w.Kind(i + 1)))
                {
                    return;
                }
                _w.SetWalls(i, _w.Walls(i) & ~4);
                _w.SetWalls(i + 1, _w.Walls(i + 1) & ~1);
                if (door != 0 && ok)
                {
                    _w.SetMark(i, 5);
                    _w.SetMark(i + 1, 3);
                }
                break;
            case 3:
                i = CellIndex(l, Room(l, a, X0), p);
                if (!IsFloor(_w.Kind(i)) || !IsFloor(_w.Kind(i - 40)))
                {
                    return;
                }
                _w.SetWalls(i, _w.Walls(i) & ~8);
                _w.SetWalls(i - 40, _w.Walls(i - 40) & ~2);
                if (door != 0 && ok)
                {
                    _w.SetMark(i, 6);
                    _w.SetMark(i - 40, 4);
                }
                break;
        }
        if (!IsTouching(l, a, b))
        {
            MarkTouching(a, b);
        }
        if ((door == 0 || !ok) && !IsLinked(l, a, b))
        {
            LinkRooms(a, b);
        }
    }

    /// <summary>Can a door or opening go on side <paramref name="dir"/> of room r at position p? With check, the cells around must be free of doors and stair fronts.</summary>
    // orig: R006+0x586E (1ABF:076E) draft
    private bool DoorSpotFree(int room, int dir, int p, int check, int both)
    {
        var l = _level;
        int c;
        int x;
        int y;
        int x2;
        int y2;
        switch (dir)
        {
            case 0:
                c = Room(l, room, Y0);
                y2 = c - 1;
                x2 = x = p;
                y = c;
                break;
            case 1:
                c = Room(l, room, X1);
                x2 = c + 1;
                y = y2 = p;
                x = c;
                break;
            case 2:
                c = Room(l, room, Y1);
                y2 = c + 1;
                x2 = x = p;
                y = c;
                break;
            case 3:
                c = Room(l, room, X0);
                x2 = c - 1;
                y2 = y = p;
                x = c;
                break;
            default:
                // DORMANT: the callers pass a side 0..3.
                return false;
        }
        bool FloorAt(int fx, int fy) => IsFloor(_w.Kind(CellIndex(l, fx, fy)));
        bool Free(int fx, int fy) => MarkIs(fx, fy, 0, true) || MarkIs(fx, fy, 11, true);
        if (!FloorAt(x, y))
        {
            return false;
        }
        if (both != 0 && !FloorAt(x2, y2))
        {
            return false;
        }
        if (check == 0)
        {
            return true;
        }
        switch (dir)
        {
            case 0:
                return Free(p, c) && Free(p, c - 1) &&
                    MarkIs(p - 1, c, 3, false) && MarkIs(p - 1, c - 1, 5, false) && MarkIs(p + 1, c, 3, false) && MarkIs(p + 1, c - 1, 5, false);
            case 1:
                return Free(c, p) && Free(c + 1, p) &&
                    MarkIs(c, p - 1, 4, false) && MarkIs(c + 1, p - 1, 6, false) && MarkIs(c, p + 1, 4, false) && MarkIs(c + 1, p + 1, 6, false);
            case 2:
                return Free(p, c) && Free(p, c + 1) &&
                    MarkIs(p - 1, c, 5, false) && MarkIs(p - 1, c + 1, 3, false) && MarkIs(p + 1, c, 5, false) && MarkIs(p + 1, c + 1, 3, false);
            default:
                return Free(c, p) && Free(c - 1, p) &&
                    MarkIs(c, p - 1, 6, false) && MarkIs(c - 1, p - 1, 4, false) && MarkIs(c, p + 1, 6, false) && MarkIs(c - 1, p + 1, 4, false);
        }
    }

    /// <summary>Is the mark of cell (x, y) of the current level equal to <paramref name="value"/> (or, with <paramref name="equal"/> false, different)?</summary>
    // orig: R006+0x5C78 (1ABF:0B78) draft
    private bool MarkIs(int x, int y, int value, bool equal) => (_w.Mark(CellIndex(_level, x, y)) == value) == equal;
}
