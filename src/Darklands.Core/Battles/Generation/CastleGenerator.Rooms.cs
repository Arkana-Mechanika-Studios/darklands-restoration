namespace Darklands.Core.Battles.Generation;

// Adding rooms, their wall bits and the free strips (overlay R006, 1a57).
internal sealed partial class CastleGenerator
{
    /// <summary>Adds a room to the current level: its rectangle, kind and cells, then the room graph (which rooms it touches and may be joined to).</summary>
    // orig: layout-add-room R006+0x4A8C (1A57:000C) draft
    // C: the call that looks for touching rooms overwrites dir, x0 and x1 (they are not used afterwards).
    private void AddRoom(int kind, int x0, int x1, int y0, int y1, int dir)
    {
        var l = _level;
        if (x0 < 0 || x1 >= 40 || y0 < 0 || y1 >= 40 || x1 < x0 || y1 < y0 || _roomCount[l] + 1 >= 30)
        {
            return;
        }
        var n = _roomCount[l]++;
        _w.SetRoom(l, n, X0, x0);
        _w.SetRoom(l, n, X1, x1);
        _w.SetRoom(l, n, Y0, y0);
        _w.SetRoom(l, n, Y1, y1);
        if (kind != KindGatePath)
        {
            _bounds[X0] = _bounds[X0] > x0 ? x0 : _bounds[X0];
            _bounds[X1] = _bounds[X1] < x1 ? x1 : _bounds[X1];
            _bounds[Y0] = _bounds[Y0] > y0 ? y0 : _bounds[Y0];
            _bounds[Y1] = _bounds[Y1] < y1 ? y1 : _bounds[Y1];
        }
        if (kind == KindKeep || kind == KindTower || kind == KindBuilding || kind == KindGateHouse)
        {
            _w.SetStairState(l, n, 7);
            var w = x1 - x0 + 1;
            var h = y1 - y0 + 1;
            if (((w < 3 && h < 3) || w == 1 || h == 1) && (kind != KindGateHouse || (dir != 0 && dir != 3)))
            {
                _w.SetStairState(l, n, 0);
            }
        }
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var cell = CellIndex(l, x, y);
                if (_w.Kind(cell) == 0)
                {
                    _w.SetKind(cell, kind);
                }
            }
        }
        _w.SetRoomKind(l, n, kind);
        for (var y = y0; y <= y1; y++)
        {
            ComputeWalls(l, x0, y);
            if (x0 < x1)
            {
                ComputeWalls(l, x1, y);
            }
        }
        for (var x = x0 + 1; x < x1; x++)
        {
            ComputeWalls(l, x, y0);
            if (y0 < y1)
            {
                ComputeWalls(l, x, y1);
            }
        }
        MarkTouching(n, n);
        LinkRooms(n, n);
        if (kind == KindGatePath)
        {
            MarkReachable(Outside, n);
        }
        for (var j = 0; j < n; j++)
        {
            var touchDir = 0;
            var touchLo = 0;
            var touchHi = 0;
            var k = RoomsTouch(j, n, ref touchDir, ref touchLo, ref touchHi, 2);
            if (k != 0)
            {
                MarkTouching(j, n);
            }
            if (k > 1)
            {
                LinkRooms(j, n);
            }
            if (StoreDirection(j, n))
            {
                WantCorridor(j, n);
            }
        }
    }

    /// <summary>
    /// The wall bits of a cell: a side is walled towards solid cells and towards rooms of another kind (corridors and towers are kept apart;
    /// buildings and the gate path open onto anything), and the neighbour's facing bit is set or cleared to match.
    /// </summary>
    // orig: R006+0x4D28 (1A57:02A8) draft
    // C: the draft packs its locals into a struct only to pin the compiler's stack layout; none of that is behaviour.
    private void ComputeWalls(int level, int x, int y)
    {
        if (x < 1 || x >= 39 || y < 1 || y >= 39)
        {
            return;
        }
        var cell = CellIndex(level, x, y);
        var v = _w.Kind(cell);
        var walls = 0;
        for (var side = 0; side <= 3; side++)
        {
            int dx;
            int dy;
            int bit;
            int neighbourBit;
            switch (side)
            {
                case 0:
                    dx = 0;
                    dy = -1;
                    bit = 1;
                    neighbourBit = 4;
                    break;
                case 1:
                    dx = 1;
                    dy = 0;
                    bit = 2;
                    neighbourBit = 8;
                    break;
                case 2:
                    dx = 0;
                    dy = 1;
                    bit = 4;
                    neighbourBit = 1;
                    break;
                default:
                    dx = -1;
                    dy = 0;
                    bit = 8;
                    neighbourBit = 2;
                    break;
            }
            var j = (level * 40 + x + dx) * 40 + dy + y;
            var w = _w.Kind(j);
            if (w > 1 && w < 9)
            {
                if ((!((v == 7) ^ (w == 7)) || w == 5 || v == 5) && (!((v == 3) ^ (w == 3)) || w == 6 || v == 6))
                {
                    _w.SetWalls(j, _w.Walls(j) & ~neighbourBit);
                }
                else
                {
                    walls |= bit;
                    _w.SetWalls(j, _w.Walls(j) | neighbourBit);
                }
            }
            else
            {
                walls |= bit;
            }
        }
        _w.SetWalls(cell, walls);
    }

    /// <summary>Cuts a room out of a free strip (slot −1: the first strip that overlaps it), splitting the strip in two when the room is in its middle.</summary>
    // orig: R006+0x4EAE (1A57:042E) draft
    // C: slot and room are checked against the level's strip and room counts only.
    private void CutFromStrip(int slot, int room)
    {
        var l = _level;
        var n = _stripCount[l];
        if (slot < -1 || slot >= n || room < 0 || room >= _roomCount[l])
        {
            return;
        }
        var o0 = Room(l, room, X0);
        var o1 = Room(l, room, X1);
        var o2 = Room(l, room, Y0);
        var o3 = Room(l, room, Y1);
        int[] o = [o0, o1, o2, o3];
        if (slot == -1)
        {
            var found = false;
            for (slot = 0; slot < _stripCount[l]; slot++)
            {
                if (o1 < _w.Strip(l, slot, 0) || _w.Strip(l, slot, 1) < o0 || o3 < _w.Strip(l, slot, 2) || _w.Strip(l, slot, 3) < o2)
                {
                    found = false;
                }
                else
                {
                    found = true;
                }
                if (found)
                {
                    break;
                }
            }
            if (!found)
            {
                return;
            }
        }
        int lo;
        int hi;
        int a;
        int b;
        var side = _w.Strip(l, slot, 4);
        if (side == 0 || side == 2)
        {
            lo = 0;
            hi = 1;
            a = 2;
            b = 3;
        }
        else
        {
            lo = 2;
            hi = 3;
            a = 0;
            b = 1;
        }
        var k = b;
        int v;
        if (_w.Strip(l, slot, b) < Room(l, 0, a))
        {
            k = a;
            v = o[a];
            if (v > _w.Strip(l, slot, a))
            {
                v = _w.Strip(l, slot, k);
            }
        }
        else
        {
            v = o[b];
            if (_w.Strip(l, slot, b) > v)
            {
                v = _w.Strip(l, slot, k);
            }
        }
        _w.SetStrip(l, slot, k, v);
        // DORMANT: every caller leaves at least one cell of the strip before and after the room, so only the splitting arm below runs;
        // the arm for a room at the start of the strip and the closing arm (a room at its end) are not reached.
        if (o[lo] <= _w.Strip(l, slot, lo))
        {
            v = o[hi] + 1;
            if (v < _w.Strip(l, slot, lo))
            {
                v = _w.Strip(l, slot, lo);
            }
            _w.SetStrip(l, slot, lo, v);
            return;
        }
        if (o[hi] < _w.Strip(l, slot, hi))
        {
            // The strip becomes two: this slot keeps the part before the room, a new slot the part after it.
            for (var field = 0; field < 5; field++)
            {
                _w.SetStrip(l, n, field, _w.Strip(l, slot, field));
            }
            _w.SetStrip(l, n, lo, o[hi] + 1);
            _w.SetStrip(l, slot, hi, o[lo] - 1);
            _stripCount[l]++;
            return;
        }
        v = o[lo] - 1;
        if (_w.Strip(l, slot, hi) < v)
        {
            v = _w.Strip(l, slot, hi);
        }
        _w.SetStrip(l, slot, hi, v);
    }
}
