namespace Darklands.Core.Battles.Generation;

// The outer wall of the top level (overlay R006, 16e1): up to four wall segments (kind 8), one on each side of the keep, each
// joining the corner towers next to it.
internal sealed partial class CastleGenerator
{
    /// <summary>Builds the outer wall around the keep on the top level.</summary>
    // orig: R006+0x132A (16E1:000A) draft
    // C: adj holds, for the sides 0..3 of the keep, 0 = no wall yet / 1 = done, and for the corners 4..7 the tower room there or −1.
    // C: the draft's side value 8 (a tower that is at no corner) would write past adj in the original; the array has room for it here.
    private void BuildOuterWall()
    {
        var tries = 0;
        var free = 0;
        var done = 0;
        var l = _level;
        if (l != _topLevel || _roomCount[l] <= 0)
        {
            return;
        }
        var adj = new int[9];
        for (var i = 0; i <= 3; i++)
        {
            adj[i] = 0;
        }
        for (var i = 4; i <= 7; i++)
        {
            adj[i] = -1;
        }
        int S(int index) => _w.StripWord(l, index);
        void Set(int index, int value) => _w.SetStripWord(l, index, value);
        _stripCount[l] = 4;
        Set(17, _bounds[Y0]);
        Set(7, _bounds[Y0]);
        Set(2, _bounds[Y0]);
        Set(11, _bounds[X1]);
        Set(1, _bounds[X1]);
        Set(6, _bounds[X1]);
        Set(18, _bounds[Y1]);
        Set(8, _bounds[Y1]);
        Set(13, _bounds[Y1]);
        Set(10, _bounds[X0]);
        Set(0, _bounds[X0]);
        Set(15, _bounds[X0]);
        // The strips run from the bounding box in to the keep (room 0 of level 1).
        Set(3, Room(GroundLevel, 0, Y0));
        Set(5, Room(GroundLevel, 0, X1));
        Set(12, Room(GroundLevel, 0, Y1));
        Set(16, Room(GroundLevel, 0, X0));
        for (var i = 0; i < _roomCount[l]; i++)
        {
            if (!IsReachable(l, i, Outside))
            {
                free++;
            }
            if (RoomKind(l, i) != KindTower)
            {
                continue;
            }
            var corner = TowerCorner(l, i);
            adj[corner] = i;
            var r0 = Room(l, i, X0);
            var r1 = Room(l, i, X1);
            var r2 = Room(l, i, Y0);
            var r3 = Room(l, i, Y1);
            switch (corner)
            {
                case 4:
                    Set(0, r1 + 1);
                    Set(17, r3 + 1);
                    Set(2, Max(S(2), r2));
                    Set(3, Min(S(3), r3));
                    Set(15, Max(S(15), r0));
                    Set(16, Min(r1, S(16)));
                    Set(10, Max(S(10), r0));
                    Set(7, Max(r2, S(7)));
                    break;
                case 5:
                    Set(1, r0 - 1);
                    Set(7, r3 + 1);
                    Set(2, Max(S(2), r2));
                    Set(3, Min(S(3), r3));
                    Set(5, Max(S(5), r0));
                    Set(6, Min(r1, S(6)));
                    Set(11, Min(r1, S(11)));
                    Set(17, Max(r2, S(17)));
                    break;
                case 6:
                    Set(11, r0 - 1);
                    Set(8, r2 - 1);
                    Set(12, Max(r2, S(12)));
                    Set(13, Min(r3, S(13)));
                    Set(5, Max(S(5), r0));
                    Set(6, Min(r1, S(6)));
                    Set(1, Min(S(1), r1));
                    Set(18, Min(S(18), r3));
                    break;
                case 7:
                    Set(10, r1 + 1);
                    Set(18, r2 - 1);
                    Set(12, Max(r2, S(12)));
                    Set(13, Min(r3, S(13)));
                    Set(15, Max(S(15), r0));
                    Set(16, Min(r1, S(16)));
                    Set(0, Max(S(0), r0));
                    Set(8, Min(r3, S(8)));
                    break;
            }
        }
        while (done < 4)
        {
            if (free <= 0 && Roll(_roomCount[l] + 1) != 1)
            {
                return;
            }
            if (tries >= 6)
            {
                return;
            }
            var n = Roll(4 - done);
            var k = 0;
            var side = 8;
            int index;
            for (index = 0; index <= 3 && n > k; index++)
            {
                if (adj[index] == 0)
                {
                    k++;
                }
            }
            if (n == k)
            {
                side = index - 1;
                PlaceWallSegment(side, adj, out var pa, out var pb);
                if (pa >= 0 && pb >= 0)
                {
                    var x = -1;
                    var y = -1;
                    switch (side)
                    {
                        case 0:
                            k = S(0);
                            if (k <= _bounds[X0])
                            {
                                k = Roll(S(16) - S(15) + 1 - 1) + S(15);
                            }
                            if (adj[4] < 0)
                            {
                                Set(15, k);
                                Set(16, k);
                                Set(17, pa + 2);
                                if (adj[7] < 0)
                                {
                                    Set(10, Max(k, S(10)));
                                }
                            }
                            n = S(1);
                            if (n >= _bounds[X1])
                            {
                                n = Roll(S(6) - S(5) + 1 - 1) + S(5);
                            }
                            if (adj[5] < 0)
                            {
                                Set(5, n);
                                Set(6, n);
                                Set(7, pb + 2);
                                if (adj[6] < 0)
                                {
                                    Set(11, Min(n, S(11)));
                                }
                            }
                            if (pb == pa)
                            {
                                AddRoom(KindWall, k, n, pa, pa + 1, 0);
                            }
                            else
                            {
                                var mid = (n + k) / 2;
                                AddRoom(KindWall, k, Min(mid + 1, n - 1), pa, pa + 1, 0);
                                AddRoom(KindWall, Max(mid - 1, k + 1), n, pb, pb + 1, 0);
                            }
                            x = adj[4];
                            y = adj[5];
                            break;
                        case 1:
                            k = S(7);
                            if (k <= _bounds[Y0])
                            {
                                k = Roll(S(3) - S(2) + 1 - 1) + S(2);
                            }
                            if (adj[5] < 0)
                            {
                                Set(2, k);
                                Set(3, k);
                                Set(1, pa - 2);
                                if (adj[4] < 0)
                                {
                                    Set(17, Max(k, S(17)));
                                }
                            }
                            n = S(8);
                            if (n > _bounds[Y1])
                            {
                                n = Roll(S(13) - S(12) + 1 - 1) + S(12);
                            }
                            if (adj[6] < 0)
                            {
                                Set(12, n);
                                Set(13, n);
                                Set(11, pb - 2);
                                if (adj[7] < 0)
                                {
                                    Set(18, Min(n, S(18)));
                                }
                            }
                            if (pb == pa)
                            {
                                AddRoom(KindWall, pa - 1, pa, k, n, 1);
                            }
                            else
                            {
                                var mid = (n + k) / 2;
                                AddRoom(KindWall, pa - 1, pa, k, Min(mid + 1, n - 1), 1);
                                AddRoom(KindWall, pb - 1, pb, Max(mid - 1, k + 1), n, 1);
                            }
                            x = adj[5];
                            y = adj[6];
                            break;
                        case 2:
                            k = S(10);
                            if (k <= _bounds[X0])
                            {
                                k = Roll(S(16) - S(15) + 1 - 1) + S(15);
                            }
                            if (adj[7] < 0)
                            {
                                Set(15, k);
                                Set(16, k);
                                Set(18, pa - 2);
                                if (adj[4] < 0)
                                {
                                    Set(0, Max(k, S(0)));
                                }
                            }
                            n = S(11);
                            if (n > _bounds[X1])
                            {
                                n = Roll(S(6) - S(5) + 1 - 1) + S(5);
                            }
                            if (adj[6] < 0)
                            {
                                Set(5, n);
                                Set(6, n);
                                Set(8, pb - 2);
                                if (adj[5] < 0)
                                {
                                    Set(1, Min(n, S(1)));
                                }
                            }
                            if (pb == pa)
                            {
                                AddRoom(KindWall, k, n, pa - 1, pa, 2);
                            }
                            else
                            {
                                var mid = (n + k) / 2;
                                AddRoom(KindWall, k, Min(mid + 1, n - 1), pa - 1, pa, 2);
                                AddRoom(KindWall, Max(mid - 1, k + 1), n, pb - 1, pb, 2);
                            }
                            x = adj[7];
                            y = adj[6];
                            break;
                        case 3:
                            k = S(17);
                            if (k <= _bounds[Y0])
                            {
                                k = Roll(S(3) - S(2) + 1 - 1) + S(2);
                            }
                            if (adj[4] < 0)
                            {
                                Set(2, k);
                                Set(3, k);
                                Set(0, pa + 2);
                                if (adj[5] < 0)
                                {
                                    Set(7, Max(k, S(7)));
                                }
                            }
                            n = S(18);
                            if (n > _bounds[Y1])
                            {
                                n = Roll(S(13) - S(12) + 1 - 1) + S(12);
                            }
                            if (adj[7] < 0)
                            {
                                Set(12, n);
                                Set(13, n);
                                Set(10, pb + 2);
                                if (adj[6] < 0)
                                {
                                    Set(8, Min(n, S(8)));
                                }
                            }
                            if (pb == pa)
                            {
                                AddRoom(KindWall, pa, pa + 1, k, n, 3);
                            }
                            else
                            {
                                var mid = (n + k) / 2;
                                AddRoom(KindWall, pa, pa + 1, k, Min(mid + 1, n - 1), 3);
                                AddRoom(KindWall, pb, pb + 1, Max(mid - 1, k + 1), n, 3);
                            }
                            x = adj[4];
                            y = adj[7];
                            break;
                    }
                    adj[side] = 1;
                    if (pb == pa)
                    {
                        ConnectRoom(_roomCount[l] - 1, 8, x, y);
                    }
                    else
                    {
                        ConnectRoom(_roomCount[l] - 2, 8, x, -1);
                        ConnectRoom(_roomCount[l] - 1, 8, -1, y);
                    }
                    done++;
                    tries++;
                    free = 0;
                    for (var i = 0; i < _roomCount[l]; i++)
                    {
                        if (!IsReachable(l, i, Outside))
                        {
                            free++;
                        }
                    }
                    continue;
                }
                adj[side] = 1;
                done++;
            }
            tries++;
        }
    }

    /// <summary>
    /// Finds where a wall segment can cross one side of the keep: the cell next to the tower (or the end of the strip) at each end
    /// of the side, one cell for both when they agree and two close cells otherwise; −1 for both when there is no place.
    /// </summary>
    // orig: R006+0x1D48 (16E1:0A28) draft
    // C: the draft's gotos (try 8 random cells, then every cell of the strip, up to 25 tries) are the loop below.
    // C: b1 and b2 are local arrays of 40 ints; the search reads them up to two cells outside [0, 40), which the original reads from the
    // neighbouring locals; those read as 0 here (DIVERGENCE only if a room lies within two cells of the map's edge).
    private void PlaceWallSegment(int side, int[] adj, out int pa, out int pb)
    {
        pa = 0;
        pb = 0;
        var l = _level;
        var b2 = new int[40];
        var b1 = new int[40];
        int firstCorner;
        int secondCorner;
        int step;
        switch (side)
        {
            case 0:
                firstCorner = 4;
                secondCorner = 5;
                step = 1;
                break;
            case 1:
                firstCorner = 5;
                secondCorner = 6;
                step = -1;
                break;
            case 2:
                firstCorner = 7;
                secondCorner = 6;
                step = -1;
                break;
            case 3:
                firstCorner = 4;
                secondCorner = 7;
                step = 1;
                break;
            default:
                return;
        }
        int lowField;
        int highField;
        int columnField;
        int firstDoorSide;
        int secondDoorSide;
        if (side == 0 || side == 2)
        {
            lowField = 2;
            columnField = 0;
            highField = 3;
            secondDoorSide = 3;
            firstDoorSide = 1;
        }
        else
        {
            highField = 1;
            lowField = 0;
            secondDoorSide = 0;
            firstDoorSide = columnField = 2;
        }
        int Strip(int field) => _w.StripWord(l, side * 5 + field);
        int At(int[] a, int i) => (uint)i < 40 ? a[i] : 0;
        if (adj[firstCorner] < 0)
        {
            for (var i = Strip(lowField); i <= Strip(highField); i++)
            {
                b1[i] |= 7;
            }
        }
        else
        {
            var room = adj[firstCorner];
            for (var i = Room(l, room, lowField) - 1; i <= Room(l, room, highField) + 1; i++)
            {
                b1[i] |= 1;
                if (DoorSpotFree(room, firstDoorSide, i, 1, 0))
                {
                    b1[i] |= 2;
                }
            }
            for (var i = Room(l, room, lowField); i <= Room(l, room, highField); i++)
            {
                if ((b1[i] & 1) != 0 && (At(b1, i + step) & 1) != 0 && ((b1[i] & 2) != 0 || (At(b1, i + step) & 2) != 0))
                {
                    b1[i] |= 4;
                }
            }
        }
        if (adj[secondCorner] < 0)
        {
            for (var i = Strip(lowField); i <= Strip(highField); i++)
            {
                b2[i] |= 7;
            }
        }
        else
        {
            var room = adj[secondCorner];
            var i = 0;
            var ok = true;
            do
            {
                if (i >= _stairCount[l])
                {
                    break;
                }
                var stairRoom = _w.StairWord(l, i, 2);
                var stairX = _w.StairWord(l, i, 0);
                var stairY = _w.StairWord(l, i, 1);
                var stairSide = _w.StairWord(l, i, 5);
                if (stairRoom == room && ((secondDoorSide == 3 && stairSide == 3 && stairX == Room(l, room, X0)) ||
                    (secondDoorSide == 0 && stairSide == 0 && Room(l, room, Y0) == stairY)))
                {
                    ok = false;
                }
                i++;
            }
            while (ok);
            for (i = Room(l, room, lowField) - 1; i <= Room(l, room, highField) + 1; i++)
            {
                if (ok || (secondDoorSide == 3 && _w.Kind(l * 1600 + Room(l, room, columnField) * 40 + i) != 10) ||
                    (secondDoorSide == 0 && _w.Kind(l * 1600 + Room(l, room, columnField) + i * 40) != 10))
                {
                    b2[i] |= 1;
                }
                if (DoorSpotFree(room, secondDoorSide, i, 1, 0))
                {
                    b2[i] |= 2;
                }
            }
            for (i = Room(l, room, lowField); i <= Room(l, room, highField); i++)
            {
                if ((b2[i] & 1) != 0 && (At(b2, i + step) & 1) != 0 && ((b2[i] & 2) != 0 || (At(b2, i + step) & 2) != 0))
                {
                    b2[i] |= 4;
                }
            }
        }
        var best = Strip(highField) - Strip(lowField) + 1;
        var found = false;
        var tries = 0;
        var cell = lowField;
        while (!found && tries < 25 && tries - 8 < best)
        {
            if (tries < 8)
            {
                cell = Roll(best) + Strip(lowField) - 1;
            }
            else
            {
                cell = Strip(lowField) + tries - 8;
            }
            found = (At(b1, cell) & 4) != 0 && (At(b2, cell) & 4) != 0;
            tries++;
        }
        if (found)
        {
            pa = pb = cell;
            return;
        }
        var first = -1;
        var second = -1;
        found = false;
        best = 10;
        for (var i = 0; !found && i < 40; i++)
        {
            if ((b1[i] & 4) != 0)
            {
                for (var k = i - 2; !found && k <= i + 2; k++)
                {
                    if ((At(b2, k) & 4) != 0 && Math.Abs(i - k) < best)
                    {
                        best = Math.Abs(i - k);
                        first = i;
                        second = k;
                        found = true;
                    }
                }
            }
        }
        if (best < 10)
        {
            pa = first;
            pb = second;
        }
        else
        {
            pa = pb = -1;
        }
    }
}
