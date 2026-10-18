namespace Darklands.Core.Battles.Generation;

// Questions about the cells and stairs of a level (overlay R005, 1551).
internal sealed partial class CastleGenerator
{
    /// <summary>Is the side <paramref name="side"/> (0 north, 1 east, 2 south, 3 west) of cell (x, y) walled? −1 for bad arguments, <paramref name="solid"/> for a solid cell.</summary>
    // orig: R005+0x1314 (1551:0124) draft
    private int CellSideBlocked(int level, int x, int y, int side, int solid)
    {
        if (level > _highestLevel || level < _lowestLevel || x < 1 || x >= 39 || y < 1 || y >= 39 || side < 0 || side > 3)
        {
            return -1;
        }
        var i = CellIndex(level, x, y);
        var kind = _w.Kind(i);
        if (kind == 0 || kind == 1)
        {
            return solid;
        }
        switch (side)
        {
            case 0:
                if ((_w.Walls(i) & 1) != 0 || (_w.Walls(i - 1) & 4) != 0)
                {
                    return 1;
                }
                return IsSolid(_w.Kind(i - 1)) ? 1 : 0;
            case 1:
                if ((_w.Walls(i) & 2) != 0 || (_w.Walls(i + 40) & 8) != 0)
                {
                    return 1;
                }
                return IsSolid(_w.Kind(i + 40)) ? 1 : 0;
            case 2:
                if ((_w.Walls(i) & 4) != 0 || (_w.Walls(i + 1) & 1) != 0)
                {
                    return 1;
                }
                return IsSolid(_w.Kind(i + 1)) ? 1 : 0;
            default:
                if ((_w.Walls(i) & 8) != 0 || (_w.Walls(i - 40) & 2) != 0)
                {
                    return 1;
                }
                return IsSolid(_w.Kind(i - 40)) ? 1 : 0;
        }
    }

    // orig: the test kind == 0 || kind == 1 of R005+0x1314 and others
    private static bool IsSolid(int kind) => kind == 0 || kind == 1;

    /// <summary>Can a door be put on side <paramref name="side"/> of cell (x, y)?</summary>
    // orig: R005+0x1492 (1551:02A2) draft
    private bool CanPlaceDoor(int level, int x, int y, int side)
    {
        if (level <= _highestLevel && level >= _lowestLevel && x > 0 && x < 39 && y > 0 && y < 39 && side >= 0 && side < 4)
        {
            var i = CellIndex(level, x, y);
            if (IsFloor(_w.Kind(i)))
            {
                switch (side)
                {
                    case 0:
                        // C: the only side that does not return inside the switch; it falls out to "no".
                        if (IsFloor(_w.Kind(i - 1)) && (_w.Walls(i) & 1) == 0 && (_w.Walls(i - 1) & 4) == 0 && _w.Mark(i) != 3 && _w.Mark(i - 1) != 5)
                        {
                            return true;
                        }
                        break;
                    case 1:
                        return IsFloor(_w.Kind(i + 40)) && (_w.Walls(i) & 2) == 0 && (_w.Walls(i + 40) & 8) == 0 && _w.Mark(i) != 4 && _w.Mark(i + 40) != 6;
                    case 2:
                        return IsFloor(_w.Kind(i + 1)) && (_w.Walls(i) & 4) == 0 && (_w.Walls(i + 1) & 1) == 0 && _w.Mark(i) != 5 && _w.Mark(i + 1) != 3;
                    case 3:
                        return IsFloor(_w.Kind(i - 40)) && (_w.Walls(i) & 8) == 0 && (_w.Walls(i - 40) & 2) == 0 && _w.Mark(i) != 6 && _w.Mark(i - 40) != 4;
                }
            }
        }
        return false;
    }

    /// <summary>How many open cells there are from (x, y) in direction dir (−1 for a bad start).</summary>
    // orig: R005+0x16CA (1551:04DA) draft
    private int OpenRun(int x, int y, int dir)
    {
        // DORMANT: the only caller starts on a floor cell inside the map.
        if (x < 1 || x >= 39 || y < 1 || y >= 39 || dir < 0 || dir > 3 || IsSolid(_w.Kind(CellIndex(_level, x, y))))
        {
            return -1;
        }
        int dx;
        int dy;
        // DORMANT: the only caller asks for directions 1 and 2; the arms for 0 (north) and 3 (west) are not reached.
        switch (dir)
        {
            case 0:
                dx = 0;
                dy = -1;
                break;
            case 1:
                dx = 1;
                dy = 0;
                break;
            case 2:
                dx = 0;
                dy = 1;
                break;
            default:
                dx = -1;
                dy = 0;
                break;
        }
        var n = 0;
        while (CellSideBlocked(_level, x, y, dir, 0) == 0)
        {
            n++;
            y += dy;
            x += dx;
        }
        return n;
    }

    /// <summary>The stair of a level on side 0 or 3 that sits next to the stretch [lo, hi] at <paramref name="position"/>; −1 if none.</summary>
    // orig: R005+0x11F6 (1551:0006) draft
    // C: id 0 matches every direction; otherwise the stair's direction (1 up, 2 down) must equal it.
    private int FindStairBeside(int side, int id, int level, int lo, int hi, int position)
    {
        var n = _stairCount[level];
        for (var i = 0; i < n; i++)
        {
            var x = _w.StairWord(level, i, 0);
            var y = _w.StairWord(level, i, 1);
            var direction = _w.StairWord(level, i, 4);
            var stairSide = _w.StairWord(level, i, 5);
            if (side == 0 && stairSide == 0 && y - position == 1 && x <= hi && x >= lo && (id == 0 || direction == id))
            {
                return i;
            }
            if (side == 3 && stairSide == 3 && x - position == 1 && y <= hi && y >= lo && (id == 0 || direction == id))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The first room of a level that overlaps a rectangle; −1 if none.</summary>
    // orig: R005+0x12AC (1551:00BC) draft
    private int RoomAt(int level, int x0, int x1, int y0, int y1)
    {
        var n = _roomCount[level];
        for (var i = 0; i < n; i++)
        {
            if (x0 <= Room(level, i, X1) && Room(level, i, X0) <= x1 && y0 <= Room(level, i, Y1) && Room(level, i, Y0) <= y1)
            {
                return i;
            }
        }
        return -1;
    }
}
