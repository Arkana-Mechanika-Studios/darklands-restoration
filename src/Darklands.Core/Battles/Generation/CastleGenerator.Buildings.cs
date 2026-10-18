namespace Darklands.Core.Battles.Generation;

// Buildings in the free strips and gate houses on the keep walls (overlay R006, 168e).
internal sealed partial class CastleGenerator
{
    /// <summary>Puts a building (kind 5) into each free strip that is wide enough, with a chance growing with the strip's area.</summary>
    // orig: R006+0x0DF8 (168E:0008) draft
    // C: the strips are walked from the last to the first; cutting a room out of a strip may append a strip, which is not visited.
    private void PlaceBuildings()
    {
        var l = _level;
        for (var i = _stripCount[l] - 1; i >= 0; i--)
        {
            var side = _w.Strip(l, i, 4);
            // DORMANT: every strip of this pass is one of the keep's four sides (or a part cut from one): side 0..3.
            if (side < 0 || side >= 4)
            {
                continue;
            }
            var w = _w.Strip(l, i, 1) - _w.Strip(l, i, 0);
            var wide = w + 1;
            var h = _w.Strip(l, i, 3) - _w.Strip(l, i, 2);
            var high = h + 1;
            var length = (side == 0 || side == 2) ? wide : high;
            if (length <= 3 || Roll(100) > (h + 2) / 2 * ((w + 2) / 2) * 5)
            {
                continue;
            }
            int e;
            int f;
            int c;
            int dx;
            if (side == 0 || side == 2)
            {
                w--;
                if (w > 5)
                {
                    w = 5;
                }
                e = Roll(w - 1) + 1;
                f = Roll(2) + 1;
                c = 0;
                dx = Roll(wide - e - 1) - 1;
                wide = dx + _w.Strip(l, i, 0) + 1;
            }
            else
            {
                h--;
                if (h > 5)
                {
                    h = 5;
                }
                f = Roll(h - 1) + 1;
                e = Roll(2) + 1;
                dx = 0;
                c = Roll(high - f - 1) - 1;
                high = c + _w.Strip(l, i, 2) + 1;
            }
            int a;
            int b;
            if (side == 0)
            {
                b = _w.Strip(l, i, 3) - c - 1;
                a = b - f;
                AddRoom(KindBuilding, wide, wide + e - 1, a + 1, b, 0);
                if (a > _w.Strip(l, i, 2))
                {
                    a = _w.Strip(l, i, 2);
                }
                _w.SetStrip(l, i, 2, a);
            }
            else if (side == 1)
            {
                a = _w.Strip(l, i, 0) + dx + 1;
                b = a + e;
                AddRoom(KindBuilding, a, b - 1, high, high + f - 1, 1);
                if (b < _w.Strip(l, i, 1))
                {
                    b = _w.Strip(l, i, 1);
                }
                _w.SetStrip(l, i, 1, b);
            }
            else if (side == 2)
            {
                a = _w.Strip(l, i, 2) + c + 1;
                b = a + f;
                AddRoom(KindBuilding, wide, wide + e - 1, a, b - 1, 2);
                if (b < _w.Strip(l, i, 3))
                {
                    b = _w.Strip(l, i, 3);
                }
                _w.SetStrip(l, i, 3, b);
            }
            else
            {
                b = _w.Strip(l, i, 1) - dx - 1;
                a = b - e;
                AddRoom(KindBuilding, a + 1, b, high, high + f - 1, 3);
                if (a > _w.Strip(l, i, 0))
                {
                    a = _w.Strip(l, i, 0);
                }
                _w.SetStrip(l, i, 0, a);
            }
            CutFromStrip(i, _roomCount[l] - 1);
        }
    }

    /// <summary>Puts gate houses (kind 4) against the walls of the keep, along the free strips.</summary>
    // orig: R006+0x10A2 (168E:02B2) draft
    // C: the strip count is re-read each time round (cutting a room can add a strip); the strip a house is cut from is the one at index
    // "side", while the house is placed from strip i (the draft reads it so: t = strips + side).
    private void PlaceGateHouses()
    {
        var l = _level;
        for (var i = 0; i < _stripCount[l]; i++)
        {
            var side = _w.Strip(l, i, 4);
            int n;
            int baseCell;
            if (side == 0 || side == 2)
            {
                n = _w.Strip(l, i, 1) - _w.Strip(l, i, 0);
                baseCell = _w.Strip(l, i, 0);
            }
            else
            {
                n = _w.Strip(l, i, 3) - _w.Strip(l, i, 2);
                baseCell = _w.Strip(l, i, 2);
            }
            var length = n + 1;
            if (length <= 3 || Roll(10) > n - 1)
            {
                continue;
            }
            n -= 2;
            if (n > 2)
            {
                n = 2;
            }
            var a = Roll(n);
            var d = Roll(4);
            var p0 = Roll(length - (a + 1) - 1) + baseCell;
            var p1 = p0 + a;
            if (side == 0)
            {
                var k = Room(l, 0, Y0) - 1;
                if (FindStairBeside(0, 0, l, p0, p1, k) >= 0)
                {
                    continue;
                }
                d = k - d;
                AddRoom(KindGateHouse, p0, p1, d + 1, k, 0);
                if (d > _w.Strip(l, side, 2))
                {
                    d = _w.Strip(l, side, 2);
                }
                _w.SetStrip(l, side, 2, d);
            }
            else if (side == 1)
            {
                d = Room(l, 0, X1) + 1 + d;
                AddRoom(KindGateHouse, Room(l, 0, X1) + 1, d - 1, p0, p1, 1);
                if (d < _w.Strip(l, side, 1))
                {
                    d = _w.Strip(l, side, 1);
                }
                _w.SetStrip(l, side, 1, d);
            }
            else if (side == 2)
            {
                d = Room(l, 0, Y1) + 1 + d;
                AddRoom(KindGateHouse, p0, p1, Room(l, 0, Y1) + 1, d - 1, 2);
                if (d < _w.Strip(l, side, 3))
                {
                    d = _w.Strip(l, side, 3);
                }
                _w.SetStrip(l, side, 3, d);
            }
            else if (side == 3)
            {
                var k = Room(l, 0, X0) - 1;
                if (FindStairBeside(3, 0, l, p0, p1, k) >= 0)
                {
                    continue;
                }
                d = k - d;
                AddRoom(KindGateHouse, d + 1, k, p0, p1, 3);
                if (d > _w.Strip(l, side, 0))
                {
                    d = _w.Strip(l, side, 0);
                }
                _w.SetStrip(l, side, 0, d);
            }
            CutFromStrip(i, _roomCount[l] - 1);
        }
    }
}
