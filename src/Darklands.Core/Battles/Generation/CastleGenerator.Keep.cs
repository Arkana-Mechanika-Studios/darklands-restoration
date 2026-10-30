namespace Darklands.Core.Battles.Generation;

// The keep, its gate and the gate path (overlay R006, 15af).
internal sealed partial class CastleGenerator
{
    private const int KindKeep = 2;
    private const int KindTower = 3;
    private const int KindGateHouse = 4;
    private const int KindBuilding = 5;
    private const int KindGatePath = 6;
    private const int KindCorridor = 7;
    private const int KindWall = 8;

    /// <summary>Adds the keep hall as room 0 of the level, and the four free strips around it.</summary>
    /// <remarks>On level 1 the hall is a random square (4..8 across) with one side possibly stretched, centred on the map; the levels above copy it.</remarks>
    // orig: R006+0x0000 (15AF:0000) draft
    private void BuildKeep()
    {
        int x0;
        int x1;
        int y0;
        int y1;
        if (_level == 1)
        {
            var h = Roll(4) + 4;
            var w = h;
            if (Roll(2) > 1)
            {
                if (Roll(2) > 1)
                {
                    w += Roll(3) - 1;
                }
                else
                {
                    h += Roll(3) - 1;
                }
            }
            y0 = 20 - h / 2;
            x0 = 20 - w / 2;
            x1 = w + x0 - 1;
            y1 = h + y0 - 1;
        }
        else
        {
            // The hall of level 1 (room 0 of level 1).
            x0 = Room(GroundLevel, 0, X0);
            x1 = Room(GroundLevel, 0, X1);
            y0 = Room(GroundLevel, 0, Y0);
            y1 = Room(GroundLevel, 0, Y1);
        }
        AddRoom(KindKeep, x0, x1, y0, y1, -1);
        _stripCount[_level] = 4;
        var l = _level;
        // Strips 0 and 2 run along the north and south of the hall, 1 and 3 along its east and west; each spans out to the bounding box.
        _w.SetStrip(l, 0, 3, Room(l, 0, Y0) - 1);
        _w.SetStrip(l, 0, 2, _bounds[Y0] - 1);
        var south = Room(l, 0, Y1) + 1;
        _w.SetStrip(l, 2, 2, south);
        _w.SetStrip(l, 2, 3, _bounds[Y1] + 1);
        var west = Room(l, 0, X0) - 1;
        _w.SetStrip(l, 2, 0, west);
        _w.SetStrip(l, 0, 0, west);
        var east = Room(l, 0, X1) + 1;
        _w.SetStrip(l, 2, 1, east);
        _w.SetStrip(l, 0, 1, east);
        _w.SetStrip(l, 3, 1, west);
        _w.SetStrip(l, 3, 0, _bounds[X0] - 1);
        _w.SetStrip(l, 1, 0, east);
        _w.SetStrip(l, 1, 1, _bounds[X1] + 1);
        _w.SetStrip(l, 3, 2, Room(l, 0, Y0) - 1);
        _w.SetStrip(l, 1, 2, Room(l, 0, Y0) - 1);
        _w.SetStrip(l, 3, 3, south);
        _w.SetStrip(l, 1, 3, south);
        for (var i = 0; i <= 3; i++)
        {
            _w.SetStrip(l, i, 4, i);
        }
    }

    /// <summary>Level 1: chooses the room the gate belongs to (the keep or a tower) and adds the gate path room.</summary>
    // orig: R006+0x01CC (15AF:01CC) draft; checked against asm
    // C: the draft's loops re-roll the gate side until the strip is wide enough (4 cells) and its middle fits.
    private void PlanGate(int battlefield)
    {
        if (_level != 1)
        {
            return;
        }
        int towerIndex;
        if (battlefield == 0 || Roll(2) > 1)
        {
            towerIndex = 0;
        }
        else
        {
            towerIndex = Roll(_lastKeepRoom[GroundLevel] + 1) - 1;
        }
        _w.SetStairState(GroundLevel, towerIndex, 3);
        if (towerIndex == 0)
        {
            int mid;
            int lo;
            int hi;
            int loIndex;
            int hiIndex;
            do
            {
                do
                {
                    _gateSide = Roll(4) - 1;
                    if (_gateSide == 0 || _gateSide == 2)
                    {
                        loIndex = 0;
                        hiIndex = 1;
                    }
                    else
                    {
                        loIndex = 2;
                        hiIndex = 3;
                    }
                    hi = _w.Strip(GroundLevel, _gateSide, hiIndex);
                    lo = _w.Strip(GroundLevel, _gateSide, loIndex);
                }
                while (hi - lo + 1 < 4);
                mid = (Room(GroundLevel, 0, loIndex) + Room(GroundLevel, 0, hiIndex)) / 2;
                if ((Room(GroundLevel, 0, hiIndex) - Room(GroundLevel, 0, loIndex) + 1) % 2 > 0)
                {
                    mid -= Roll(2) - 1;
                }
            }
            while (mid <= lo || hi <= mid + 1);
            if (_gateSide == 0 || _gateSide == 2)
            {
                AddRoom(KindGatePath, mid, mid + 1, _w.Strip(GroundLevel, _gateSide, 2), _w.Strip(GroundLevel, _gateSide, 3), _gateSide);
            }
            else
            {
                AddRoom(KindGatePath, _w.Strip(GroundLevel, _gateSide, 0), _w.Strip(GroundLevel, _gateSide, 1), mid, mid + 1, _gateSide);
            }
            CutFromStrip(_gateSide, _roomCount[GroundLevel] - 1);
            return;
        }
        // The gate is in a tower: the path leads out of it away from the keep.
        if (Roll(2) < 2)
        {
            var k = Roll(Room(GroundLevel, towerIndex, Y1) - Room(GroundLevel, towerIndex, Y0)) - 1 + Room(GroundLevel, towerIndex, Y0);
            if (Room(GroundLevel, towerIndex, X0) < Room(GroundLevel, 0, X0))
            {
                _gateSide = 3;
                AddRoom(KindGatePath, _bounds[X0] - 1, Room(GroundLevel, towerIndex, X0) - 1, k, k + 1, 3);
            }
            else
            {
                _gateSide = 1;
                AddRoom(KindGatePath, Room(GroundLevel, towerIndex, X1) + 1, _bounds[X1] + 1, k, k + 1, 1);
            }
        }
        else
        {
            var k = Roll(Room(GroundLevel, towerIndex, X1) - Room(GroundLevel, towerIndex, X0)) - 1 + Room(GroundLevel, towerIndex, X0);
            if (Room(GroundLevel, towerIndex, Y0) < Room(GroundLevel, 0, Y0))
            {
                _gateSide = 0;
                AddRoom(KindGatePath, k, k + 1, _bounds[Y0] - 1, Room(GroundLevel, towerIndex, Y0) - 1, 0);
            }
            else
            {
                _gateSide = 2;
                AddRoom(KindGatePath, k, k + 1, Room(GroundLevel, towerIndex, Y1) + 1, _bounds[Y1] + 1, 2);
            }
        }
    }

    /// <summary>Stretches the gate path room out to (or past) the bounding box of the castle and redraws its cells.</summary>
    // orig: R006+0x0470 (15AF:0470) draft
    // C: also called once after the last level, with whatever layout-depth the loop left (0 when there is a cellar).
    private void WidenGate()
    {
        var one = _level == 1;
        var changed = false;
        var gate = _lastKeepRoom[GroundLevel] + 1;
        if (_w.RoomKind(GroundLevel, gate) != KindGatePath)
        {
            return;
        }
        switch (_gateSide)
        {
            case 0:
            {
                var e = Room(GroundLevel, gate, Y0);
                if (one)
                {
                    changed = _bounds[Y0] <= e;
                    if (changed)
                    {
                        _w.SetRoom(GroundLevel, gate, Y0, _bounds[Y0] - 1);
                    }
                }
                else
                {
                    changed = !(e < _bounds[Y0] && _bounds[Y0] < _bounds[Y1] && GroundMark(Room(GroundLevel, gate, X0), e) == 0 && GroundMark(Room(GroundLevel, gate, X0) + 1, e) == 0);
                    if (changed)
                    {
                        e = Room(GroundLevel, gate, Y0) - 1;
                        if (e > _bounds[Y0] - 1)
                        {
                            e = _bounds[Y0] - 1;
                        }
                        _w.SetRoom(GroundLevel, gate, Y0, e);
                        _bounds[Y0] = e;
                    }
                    else
                    {
                        _bounds[Y0] = Room(GroundLevel, gate, Y0);
                    }
                    SetGroundMark(Room(GroundLevel, gate, X0), e, 1);
                    SetGroundMark(Room(GroundLevel, gate, X0) + 1, e, 1);
                }
                break;
            }
            case 1:
            {
                var e = Room(GroundLevel, gate, X1);
                if (one)
                {
                    changed = e <= _bounds[X1];
                    if (changed)
                    {
                        _w.SetRoom(GroundLevel, gate, X1, _bounds[X1] + 1);
                    }
                }
                else
                {
                    changed = !(_bounds[X1] < e && _bounds[X0] < _bounds[X1] && GroundMark(e, Room(GroundLevel, gate, Y0)) == 0 && GroundMark(e, Room(GroundLevel, gate, Y0) + 1) == 0);
                    if (changed)
                    {
                        e = Room(GroundLevel, gate, X1) + 1;
                        if (e < _bounds[X1] + 1)
                        {
                            e = _bounds[X1] + 1;
                        }
                        _w.SetRoom(GroundLevel, gate, X1, e);
                        _bounds[X1] = e;
                    }
                    else
                    {
                        _bounds[X1] = Room(GroundLevel, gate, X1);
                    }
                    SetGroundMark(e, Room(GroundLevel, gate, Y0), 1);
                    SetGroundMark(e, Room(GroundLevel, gate, Y0) + 1, 1);
                }
                break;
            }
            case 2:
            {
                var e = Room(GroundLevel, gate, Y1);
                if (one)
                {
                    changed = e <= _bounds[Y1];
                    if (changed)
                    {
                        _w.SetRoom(GroundLevel, gate, Y1, _bounds[Y1] + 1);
                    }
                }
                else
                {
                    changed = !(_bounds[Y1] < e && _bounds[Y0] < _bounds[Y1] && GroundMark(Room(GroundLevel, gate, X0), e) == 0 && GroundMark(Room(GroundLevel, gate, X0) + 1, e) == 0);
                    if (changed)
                    {
                        e = Room(GroundLevel, gate, Y1) + 1;
                        if (e < _bounds[Y1] + 1)
                        {
                            e = _bounds[Y1] + 1;
                        }
                        _w.SetRoom(GroundLevel, gate, Y1, e);
                        _bounds[Y1] = e;
                    }
                    else
                    {
                        _bounds[Y1] = Room(GroundLevel, gate, Y1);
                    }
                    SetGroundMark(Room(GroundLevel, gate, X0), e, 1);
                    SetGroundMark(Room(GroundLevel, gate, X0) + 1, e, 1);
                }
                break;
            }
            case 3:
            {
                var e = Room(GroundLevel, gate, X0);
                if (one)
                {
                    changed = _bounds[X0] <= e;
                    if (changed)
                    {
                        _w.SetRoom(GroundLevel, gate, X0, _bounds[X0] - 1);
                    }
                }
                else
                {
                    changed = !(e < _bounds[X0] && _bounds[X0] < _bounds[X1] && GroundMark(e, Room(GroundLevel, gate, Y0)) == 0 && GroundMark(e, Room(GroundLevel, gate, Y0) + 1) == 0);
                    if (changed)
                    {
                        e = Room(GroundLevel, gate, X0) - 1;
                        if (e > _bounds[X0] - 1)
                        {
                            e = _bounds[X0] - 1;
                        }
                        _w.SetRoom(GroundLevel, gate, X0, e);
                        _bounds[X0] = e;
                    }
                    else
                    {
                        _bounds[X0] = Room(GroundLevel, gate, X0);
                    }
                    SetGroundMark(e, Room(GroundLevel, gate, Y0), 1);
                    SetGroundMark(e, Room(GroundLevel, gate, Y0) + 1, 1);
                }
                break;
            }
        }
        if (!changed)
        {
            return;
        }
        for (var x = Room(GroundLevel, gate, X0); x <= Room(GroundLevel, gate, X1); x++)
        {
            for (var y = Room(GroundLevel, gate, Y0); y <= Room(GroundLevel, gate, Y1); y++)
            {
                _w.SetKind(CellIndex(1, x, y), KindGatePath);
            }
        }
        for (var x = Room(GroundLevel, gate, X0); x <= Room(GroundLevel, gate, X1); x++)
        {
            for (var y = Room(GroundLevel, gate, Y0); y <= Room(GroundLevel, gate, Y1); y++)
            {
                ComputeWalls(1, x, y);
            }
        }
    }

    /// <summary>The mark of a cell of level 1 (w[x][y] with w = layout-cell-door + 1600).</summary>
    // orig: the pointer w of R006+0x0470 (15AF:0470) draft
    private int GroundMark(int x, int y) => _w.Mark(CellIndex(1, x, y));

    private void SetGroundMark(int x, int y, int value) => _w.SetMark(CellIndex(1, x, y), value);
}
