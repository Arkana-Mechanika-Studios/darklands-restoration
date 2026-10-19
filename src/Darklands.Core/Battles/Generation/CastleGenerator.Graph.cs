namespace Darklands.Core.Battles.Generation;

// The room graph of the current level (overlay R005, 146d): bit 1 of Link(a, b) = the rooms are linked by an open
// passage, 2 = they touch, 4 = one can be reached from the other (room 30 stands for the outside), 8 = a corridor
// between them is still to be tried; the high nibble holds the direction from a to b (see Direction).
internal sealed partial class CastleGenerator
{
    private const int LinkOpen = 1;
    private const int LinkTouching = 2;
    private const int LinkReachable = 4;
    private const int LinkCorridorWanted = 8;
    private const int Outside = 30;

    /// <summary>Bits of the link between two rooms of a level that has been planned (0 for bad arguments).</summary>
    // The three tests below (R005+0x03BC, 017a, 09e0) are the same code with another bit.
    private int LinkBits(int level, int a, int b, int mask)
    {
        if (level <= _highestLevel && level >= _lowestLevel)
        {
            var n = _roomCount[level];
            if (a < n && a >= 0 && b < n && b >= 0)
            {
                return _w.Link(level, a, b) & mask;
            }
        }
        return 0;
    }

    // orig: R005+0x03BC (146D:000C) draft
    private bool IsLinked(int level, int a, int b) => LinkBits(level, a, b, LinkOpen) != 0;

    // orig: R005+0x052A (146D:017A) draft
    private bool IsTouching(int level, int a, int b) => LinkBits(level, a, b, LinkTouching) != 0;

    // orig: R005+0x0D90 (146D:09E0) draft
    private bool IsCorridorWanted(int level, int a, int b) => LinkBits(level, a, b, LinkCorridorWanted) != 0;

    /// <summary>The reachable bit between two rooms (the outside, 30, counts too).</summary>
    // orig: R005+0x0C08 (146D:0858) draft
    private bool IsReachable(int level, int a, int b)
    {
        if (level <= _highestLevel && level >= _lowestLevel && a >= 0)
        {
            var n = _roomCount[level];
            if ((a < n || a == Outside) && b >= 0 && (b < n || b == Outside))
            {
                return (_w.Link(level, a, b) & LinkReachable) != 0;
            }
        }
        return false;
    }

    /// <summary>Links rooms a and b of the current level, and everything already linked to them.</summary>
    // orig: R005+0x0416 (146D:0066) draft
    private void LinkRooms(int a, int b)
    {
        if (_roomCount[_level] > a && a >= 0 && _roomCount[_level] > b && b >= 0)
        {
            if (b == a)
            {
                _w.SetLink(_level, a, b, _w.Link(_level, a, b) | 5);
                return;
            }
            AddLink(a, b, LinkOpen);
            AddLink(b, a, LinkOpen);
            for (var i = 0; i <= 30; i++)
            {
                if (_roomCount[_level] > i && IsLinked(_level, i, a))
                {
                    for (var j = 0; j <= 30; j++)
                    {
                        if (_roomCount[_level] > j && IsLinked(_level, j, b))
                        {
                            AddLink(i, j, LinkOpen);
                            AddLink(j, i, LinkOpen);
                        }
                    }
                }
            }
            MarkReachable(a, b);
        }
    }

    // orig: the statement m[a][b] |= bits, repeated inline in R005+0x0416, R005+0x0BA4 (146D:07F4) and R005+0x0C6E (146D:08BE)
    private void AddLink(int a, int b, int bits) => _w.SetLink(_level, a, b, _w.Link(_level, a, b) | bits);

    /// <summary>Marks that rooms a and b touch.</summary>
    // orig: R005+0x0BA4 (146D:07F4) draft
    private void MarkTouching(int a, int b)
    {
        if (a < _roomCount[_level] && a >= 0 && b < _roomCount[_level] && b >= 0)
        {
            AddLink(a, b, LinkTouching);
            AddLink(b, a, LinkTouching);
            MarkReachable(a, b);
        }
    }

    /// <summary>Marks a and b (30 = outside) as reachable from each other, and everything reachable from them.</summary>
    // orig: R005+0x0C6E (146D:08BE) draft
    private void MarkReachable(int a, int b)
    {
        if (a >= 0 && (a < _roomCount[_level] || a == Outside) && b >= 0 && (b < _roomCount[_level] || b == Outside))
        {
            if (b == a)
            {
                AddLink(a, b, LinkReachable);
                return;
            }
            AddLink(a, b, LinkReachable);
            AddLink(b, a, LinkReachable);
            for (var i = 0; i <= 30; i++)
            {
                if ((_roomCount[_level] > i || i == Outside) && IsReachable(_level, i, a))
                {
                    for (var j = 0; j <= 30; j++)
                    {
                        if ((_roomCount[_level] > j || j == Outside) && IsReachable(_level, j, b))
                        {
                            AddLink(i, j, LinkReachable);
                            AddLink(j, i, LinkReachable);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Stores the direction from a to b (and the reverse in b to a); true when a door between them is possible.</summary>
    // orig: R005+0x0DEA (146D:0A3A) draft
    private bool StoreDirection(int a, int b)
    {
        var n = _roomCount[_level];
        if (a < n && a >= 0 && b < n && b >= 0)
        {
            Direction(_level, a, b, out var d, out var gap);
            _w.SetLink(_level, a, b, _w.Link(_level, a, b) | ((d << 4) & 0xFF));
            _w.SetLink(_level, b, a, _w.Link(_level, b, a) | (((d ^ 2) << 4) & 0xFF));
            if (gap <= 5 && gap >= 1 && d < 8 && d >= 0 && (d <= 3 || gap <= 2))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Sets the corridor-wanted bit between a and b.</summary>
    // orig: R005+0x0E98 (146D:0AE8) draft
    private void WantCorridor(int a, int b)
    {
        var n = _roomCount[_level];
        if (a < n && a >= 0 && b < n && b >= 0)
        {
            AddLink(a, b, LinkCorridorWanted);
            AddLink(b, a, LinkCorridorWanted);
        }
    }

    /// <summary>Clears the corridor-wanted bit between a and b.</summary>
    // orig: R005+0x0EF2 (146D:0B42) draft
    private void DropCorridor(int a, int b)
    {
        var n = _roomCount[_level];
        if (a < n && a >= 0 && b < n && b >= 0)
        {
            _w.SetLink(_level, a, b, _w.Link(_level, a, b) & ~LinkCorridorWanted);
            _w.SetLink(_level, b, a, _w.Link(_level, b, a) & ~LinkCorridorWanted);
        }
    }

    /// <summary>The stored direction from room a to room b (−1 for a level outside the planned ones).</summary>
    // orig: R005+0x0F4C (146D:0B9C) draft
    // C: the byte is read as a signed char and shifted, then masked: the same as taking the high nibble.
    private int StoredDirection(int level, int a, int b)
    {
        if (level > _highestLevel || level < _lowestLevel)
        {
            return -1;
        }
        var n = _roomCount[level];
        if (a < n && a >= 0 && b < n && b >= 0)
        {
            return (sbyte)_w.Link(level, a, b) >> 4 & 15;
        }
        return 0;
    }

    /// <summary>Direction (0..3 the side of a, 4..7 the diagonals, 8 none) and gap from room a to room b.</summary>
    // orig: R005+0x0FB2 (146D:0C02) draft
    // C: the outputs are left alone when a room is bad; callers read uninitialised locals then (0 here, never happens with valid rooms).
    private void Direction(int level, int a, int b, out int dir, out int gap)
    {
        dir = 0;
        gap = 0;
        if (_roomCount[level] > a && a >= 0 && _roomCount[level] > b && b >= 0)
        {
            var d0 = RoomDistance(level, a, b, 0);
            var d1 = RoomDistance(level, a, b, 1);
            var d2 = RoomDistance(level, a, b, 2);
            var d3 = RoomDistance(level, a, b, 3);
            var m13 = d3 < d1 ? d1 : d3;
            var m02 = d2 < d0 ? d0 : d2;
            gap = m13 < m02 ? m02 : m13;
            dir = 8;
            if (d0 > 0)
            {
                var t = d0 > 2 ? 2 : d0;
                if (t > m13)
                {
                    dir = 0;
                    return;
                }
            }
            if (d1 > 0)
            {
                var t = d1 > 2 ? 2 : d1;
                if (t > m02)
                {
                    dir = 1;
                    return;
                }
            }
            if (d2 > 0)
            {
                var t = d2 > 2 ? 2 : d2;
                if (t > m13)
                {
                    dir = 2;
                    return;
                }
            }
            if (d3 > 0)
            {
                var t = d3 > 2 ? 2 : d3;
                if (t > m02)
                {
                    dir = 3;
                    return;
                }
            }
            if (d0 > 0 && d3 > 0)
            {
                dir = 4;
                return;
            }
            if (d0 > 0 && d1 > 0)
            {
                dir = 5;
                return;
            }
            if (d2 > 0 && d1 > 0)
            {
                dir = 6;
                return;
            }
            if (d2 > 0 && d3 > 0)
            {
                dir = 7;
            }
        }
    }

    /// <summary>Distance between rooms a and b in a direction (0 for bad arguments or direction 8).</summary>
    // orig: R005+0x112A (146D:0D7A) draft
    private int RoomDistance(int level, int a, int b, int dir)
    {
        var n = _roomCount[level];
        if (n > a && a >= 0 && n > b && b >= 0)
        {
            switch (dir)
            {
                case 0:
                case 4:
                case 5:
                    return Room(level, a, Y0) - Room(level, b, Y1);
                case 1:
                    return Room(level, b, X0) - Room(level, a, X1);
                case 2:
                case 6:
                case 7:
                    return Room(level, b, Y0) - Room(level, a, Y1);
                case 3:
                    return Room(level, a, X0) - Room(level, b, X1);
            }
        }
        return 0;
    }

    /// <summary>Where rooms a and b touch (see <see cref="TouchRects"/>); 0 for bad rooms, leaving the outputs alone.</summary>
    // orig: R005+0x0584 (146D:01D4) draft
    private int RoomsTouch(int a, int b, ref int dir, ref int lo, ref int hi, int mode)
    {
        if (_roomCount[_level] > a && a >= 0 && _roomCount[_level] > b && b >= 0)
        {
            return TouchRects(Room(_level, a, X0), Room(_level, a, X1), Room(_level, a, Y0), Room(_level, a, Y1),
                Room(_level, b, X0), Room(_level, b, X1), Room(_level, b, Y0), Room(_level, b, Y1), ref dir, ref lo, ref hi, mode);
        }
        return 0;
    }

    /// <summary>
    /// Where rectangle b touches rectangle a: <paramref name="dir"/> is the side of a (0 north/y−, 1 east/x+, 2 south/y+, 3 west/x−,
    /// 7 none), [lo, hi] the shared stretch; mode 0 is geometry only, 1 trims it to passable cells, 2 and above also look for
    /// cells that cannot take a door (returns 2 when one is found); 3 is returned for overlapping rectangles.
    /// </summary>
    // orig: R005+0x0606 (146D:0256) draft
    private int TouchRects(int ax0, int ax1, int ay0, int ay1, int bx0, int bx1, int by0, int by1, ref int dir, ref int lo, ref int hi, int mode)
    {
        dir = 7;
        if (ax1 < ax0 || ay1 < ay0 || bx1 < bx0 || by1 < by0)
        {
            return 0;
        }
        if (by1 - ay0 == -1 && bx1 >= ax0 && bx0 <= ax1)
        {
            var found = TouchSide(0, bx0 < ax0 ? ax0 : bx0, bx1 > ax1 ? ax1 : bx1, ay0, mode, ref dir, ref lo, ref hi);
            if (found >= 0)
            {
                return found;
            }
        }
        if (bx0 - ax1 == 1 && ay0 <= by1 && by0 <= ay1)
        {
            var found = TouchSide(1, ay0 < by0 ? by0 : ay0, ay1 > by1 ? by1 : ay1, ax1, mode, ref dir, ref lo, ref hi);
            if (found >= 0)
            {
                return found;
            }
        }
        if (by0 - ay1 == 1 && ax0 <= bx1 && bx0 <= ax1)
        {
            var found = TouchSide(2, bx0 < ax0 ? ax0 : bx0, bx1 > ax1 ? ax1 : bx1, ay1, mode, ref dir, ref lo, ref hi);
            if (found >= 0)
            {
                return found;
            }
        }
        if (bx1 - ax0 == -1 && ay0 <= by1 && by0 <= ay1)
        {
            var found = TouchSide(3, ay0 < by0 ? by0 : ay0, ay1 > by1 ? by1 : ay1, ax0, mode, ref dir, ref lo, ref hi);
            if (found >= 0)
            {
                return found;
            }
        }
        if (mode != 0 && bx1 >= ax0 && bx0 <= ax1 && by1 >= ay0 && by0 <= ay1)
        {
            return 3;
        }
        return 0;
    }

    /// <summary>
    /// One side of <see cref="TouchRects"/>: along the side, the stretch [l, h] of cells shared with the other rectangle, on the
    /// row or column <paramref name="fixedCoordinate"/>. Returns the function's result, or −1 to go on with the next side.
    /// </summary>
    // orig: the four near-identical blocks of R005+0x0606 (146D:0256) draft, written once
    private int TouchSide(int side, int l, int h, int fixedCoordinate, int mode, ref int dir, ref int lo, ref int hi)
    {
        if (mode == 0 && h >= l)
        {
            lo = l;
            hi = h;
            dir = side;
            return 1;
        }
        // Sides 0 and 2 run along x (the cell is (i, fixed)); sides 1 and 3 run along y (the cell is (fixed, i)).
        var alongX = side is 0 or 2;
        int CellKind(int i) => _w.Kind(alongX ? CellIndex(_level, i, fixedCoordinate) : CellIndex(_level, fixedCoordinate, i));
        int Blocked(int i) => alongX ? CellSideBlocked(_level, i, fixedCoordinate, side, 1) : CellSideBlocked(_level, fixedCoordinate, i, side, 1);
        while (h >= l && (Blocked(l) != 0 || CellKind(l) <= 1 || CellKind(l) >= 9))
        {
            l++;
        }
        while (h >= l && (Blocked(h) != 0 || CellKind(h) <= 1 || CellKind(h) >= 9))
        {
            h--;
        }
        if (h >= l)
        {
            lo = l;
            hi = h;
            dir = side;
            // DORMANT: the callers use mode 0 (geometry) and 2 (new rooms); mode 1 is not asked.
            if (mode == 1)
            {
                return 1;
            }
            if (mode > 1)
            {
                for (var i = l; i <= h; i++)
                {
                    if (alongX ? CanPlaceDoor(_level, i, fixedCoordinate, side) : CanPlaceDoor(_level, fixedCoordinate, i, side))
                    {
                        return 2;
                    }
                }
                return 1;
            }
        }
        return -1;
    }
}
