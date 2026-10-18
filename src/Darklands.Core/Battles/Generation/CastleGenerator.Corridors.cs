namespace Darklands.Core.Battles.Generation;

// Corridors between rooms that are not yet joined (overlay R006, 17df): kind 7 rooms one cell wide, straight between rooms side by
// side and with one bend between rooms that are diagonal.
internal sealed partial class CastleGenerator
{
    /// <summary>Joins rooms of the current level with corridors until every room is reachable (or the passes run out).</summary>
    // orig: R006+0x230A (17DF:000A) draft
    // C: masks and link sets are 32-bit longs (a room count below 29 keeps every shift inside them); the pass counter is reset to 3 once
    // all rooms had a turn, which re-runs the passes with the rejected pairs wanted again.
    private void BuildCorridors(int kind)
    {
        var l = _level;
        long mask = 0;
        var links = new long[30];
        var again = true;
        var pass = 1;
        var all = 0;
        var b = 0;
        var side = 0;
        var x0 = 0;
        var x1 = 0;
        var y0 = 0;
        var y1 = 0;
        for (; again && pass <= 5 && _roomCount[l] < 29; pass++)
        {
            again = false;
            b = 0;
            for (var a = 0; a < _roomCount[l] && _roomCount[l] < 29; a++)
            {
                var k = pass * 20;
                bool f;
                if (IsReachable(l, a, Outside) && IsReachable(l, a, 0))
                {
                    f = false;
                }
                else
                {
                    f = true;
                }
                if (kind == 8 && l == 0)
                {
                    f = !IsReachable(l, a, 0);
                }
                if (!f)
                {
                    mask |= 1L << a;
                }
                if (!f && Roll(k) > 1)
                {
                    continue;
                }
                bool anyRoom;
                if (!f || Roll(5) == 1)
                {
                    anyRoom = true;
                }
                else
                {
                    anyRoom = false;
                }
                var candidates = 0;
                for (var i = 0; i < _roomCount[l]; i++)
                {
                    if (i != a && IsCorridorWanted(l, i, a) && !IsTouching(l, i, a) && (anyRoom || !IsReachable(l, i, a)))
                    {
                        candidates++;
                    }
                }
                var done = false;
                for (var tries = candidates * 2; !done && candidates > 0 && tries > 0; tries--)
                {
                    var pick = Roll(candidates);
                    int i;
                    k = 0;
                    for (i = 0; i < _roomCount[l] && k < pick; i++)
                    {
                        if (i != a && IsCorridorWanted(l, i, a) && !IsTouching(l, i, a) && (anyRoom || !IsReachable(l, i, a)))
                        {
                            k++;
                        }
                    }
                    b = i - 1;
                    if (k == pick && pick != 0)
                    {
                        side = StoredDirection(l, a, b);
                        var typ = RoomDistance(l, a, b, side);
                        if (typ <= 5 && typ >= 1)
                        {
                            switch (side)
                            {
                                case 0:
                                    done = StraightCorridor(b, a, 2, ref y0, ref y1, ref x0, ref x1, all);
                                    break;
                                case 1:
                                    done = StraightCorridor(a, b, 0, ref x0, ref x1, ref y0, ref y1, all);
                                    break;
                                case 2:
                                    done = StraightCorridor(a, b, 2, ref y0, ref y1, ref x0, ref x1, all);
                                    break;
                                case 3:
                                    done = StraightCorridor(b, a, 0, ref x0, ref x1, ref y0, ref y1, all);
                                    break;
                                case 4:
                                    done = BentCorridor(a, b, typ, 0, 2, ref x1, ref x0, ref y1, ref y0, all);
                                    break;
                                case 5:
                                    done = BentCorridor(a, b, typ, 1, 2, ref x0, ref x1, ref y1, ref y0, all);
                                    break;
                                case 6:
                                    done = BentCorridor(a, b, typ, 1, 3, ref x0, ref x1, ref y0, ref y1, all);
                                    break;
                                case 7:
                                    done = BentCorridor(a, b, typ, 0, 3, ref x1, ref x0, ref y0, ref y1, all);
                                    break;
                                default:
                                    // DORMANT: side 8 (no direction) has a distance of 0, outside 1..5, so the switch is not entered.
                                    done = false;
                                    break;
                            }
                            if (done)
                            {
                                continue;
                            }
                        }
                    }
                    DropCorridor(a, b);
                    links[a] |= 1L << b;
                    candidates--;
                }
                if (!done)
                {
                    continue;
                }
                if (f)
                {
                    again = true;
                }
                AddRoom(KindCorridor, x0, x1, y0, y1, side);
                ConnectRoom(_roomCount[l] - 1, 7, a, b);
            }
            if (!again && all == 0)
            {
                long m = 1;
                for (var a = 0; a < _roomCount[l] && all == 0; a++, m <<= 1)
                {
                    if ((mask & m) == 0)
                    {
                        all = 1;
                    }
                }
                if (all != 0)
                {
                    pass = 3;
                    again = true;
                    for (var i = 0; i < 30; i++)
                    {
                        m = 1;
                        for (var pick = 0; pick < 30; pick++, m <<= 1)
                        {
                            if ((links[i] & m) != 0)
                            {
                                WantCorridor(i, pick);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>A straight corridor between rooms a and b side by side (dir 0: they are side by side along x, 2: along y).</summary>
    // orig: R006+0x2880 (17DF:0580) draft
    // C: u is the axis the corridor runs along and v the one across it; the caller swaps the output variables to suit the direction.
    private bool StraightCorridor(int a, int b, int dir, ref int u0, ref int u1, ref int v0, ref int v1, int flag)
    {
        int q;
        int o;
        int p;
        bool vert;
        if (dir == 2)
        {
            q = 3;
            o = 0;
            vert = true;
            p = 1;
        }
        else if (dir == 0)
        {
            vert = false;
            q = 1;
            o = 2;
            p = 3;
        }
        else
        {
            return false;
        }
        var l = _level;
        var ka = RoomKind(l, a);
        var kb = RoomKind(l, b);
        u0 = Room(l, a, q) + 1;
        u1 = Room(l, b, dir) - 1;
        if (u1 < u0)
        {
            return false;
        }
        var lb = Room(l, b, o);
        var la = Room(l, a, o);
        var lo = (lb > la ? lb : la) - 1;
        if (lo < _bounds[o])
        {
            lo = _bounds[o];
        }
        var hb = Room(l, b, p);
        var ha = Room(l, a, p);
        var hi = (hb < ha ? hb : ha) + 1;
        if (hi > _bounds[p])
        {
            hi = _bounds[p];
        }
        var n = hi - lo + 1;
        if (n < 2 && u0 < u1)
        {
            return false;
        }
        var t = 0;
        var once = false;
        var ok = false;
        for (; t < n * 3 && !ok; t++)
        {
            u0 = Room(l, a, q) + 1;
            u1 = Room(l, b, dir) - 1;
            if (n > 2 || once || u1 != u0 || Roll(2) < 2)
            {
                int v;
                if (n * 2 - t <= 0)
                {
                    v = lo - (n * 2 - t);
                }
                else
                {
                    v = Roll(n) + lo - 1;
                }
                if (v > hi)
                {
                    v = hi;
                }
                if (v < lo)
                {
                    v = lo;
                }
                v0 = v1 = v;
                if (v < la)
                {
                    if (ka == 5)
                    {
                        u0--;
                    }
                    else
                    {
                        u0 = WalkToFloor(vert, u0 - 1, Room(l, a, dir), la);
                    }
                }
                if (v0 > ha)
                {
                    if (ka == 5)
                    {
                        u0--;
                    }
                    else
                    {
                        u0 = WalkToFloor(vert, u0 - 1, Room(l, a, dir), ha);
                    }
                }
                if (v0 < lb)
                {
                    if (kb == 5)
                    {
                        u1++;
                    }
                    else
                    {
                        u1 = WalkToFloor(vert, u1 + 1, Room(l, b, q), lb);
                    }
                }
                if (v0 > hb)
                {
                    if (kb == 5)
                    {
                        u1++;
                    }
                    else
                    {
                        u1 = WalkToFloor(vert, u1 + 1, Room(l, b, q), hb);
                    }
                }
            }
            else
            {
                v1 = lo + 1;
                v0 = hi - 1;
                if (v0 < la)
                {
                    if (kb != 5)
                    {
                        v0 = WalkToFloor(!vert, v0, lb, Room(l, b, dir));
                    }
                    if (ka != 5)
                    {
                        v1 = WalkToFloor(!vert, v1, ha, Room(l, a, q));
                    }
                }
                else
                {
                    if (ka != 5)
                    {
                        v0 = WalkToFloor(!vert, v0, la, Room(l, a, q));
                    }
                    if (kb != 5)
                    {
                        v1 = WalkToFloor(!vert, v1, hb, Room(l, b, dir));
                    }
                }
                once = true;
            }
            ok = !vert ? CorridorFits(u0, u1, v0, v1, a, b, flag) : CorridorFits(v0, v1, u0, u1, a, b, flag);
        }
        return ok;
    }

    /// <summary>A corridor with one bend between room a (left at side h: 0 west, 1 east) and room b (v: 2 reached from the north, 3 from the south).</summary>
    // orig: R006+0x2CB2 (17DF:09B2) draft
    // C: p6 and p7 are the x ends, p8 and p9 the y ends of the corridor (the caller permutes the variables by the direction).
    private bool BentCorridor(int a, int b, int typ, int h, int v, ref int p6, ref int p7, ref int p8, ref int p9, int flag)
    {
        int oh;
        int dh;
        int ov;
        int dv;
        if (h == 0)
        {
            oh = 1;
            dh = -1;
        }
        else if (h == 1)
        {
            oh = 0;
            dh = h;
        }
        else
        {
            return false;
        }
        if (v == 2)
        {
            ov = 3;
            dv = -1;
        }
        else if (v == 3)
        {
            ov = 2;
            dv = 1;
        }
        else
        {
            // DORMANT: the caller passes h 0 or 1 and v 2 or 3.
            return false;
        }
        var alt = Roll(2) == 1;
        var l = _level;
        var ka = RoomKind(l, a);
        var kb = RoomKind(l, b);
        var t = 0;
        var ok = false;
        do
        {
            if (t >= 4 || (t >= 2 && typ != 1))
            {
                break;
            }
            if (alt && t < 2)
            {
                p7 = p6 = Room(l, a, h) + dh;
                if (ka == 5)
                {
                    p8 = Room(l, a, v);
                }
                else
                {
                    p8 = WalkToFloor(alt, Room(l, a, v), Room(l, a, ov), Room(l, a, h));
                }
                if (typ == 1)
                {
                    p9 = Room(l, a, v);
                }
                else if (kb == 5)
                {
                    p9 = Room(l, b, ov);
                }
                else
                {
                    p9 = WalkToFloor(alt, Room(l, b, ov), Room(l, b, v), Room(l, b, oh));
                }
            }
            else if (alt && t >= 2)
            {
                p7 = p6 = Room(l, a, h);
                p8 = Room(l, a, v) + dv;
                if (kb == 5)
                {
                    p9 = Room(l, b, ov);
                }
                else
                {
                    p9 = WalkToFloor(alt, Room(l, b, ov), Room(l, b, v), Room(l, b, oh));
                }
            }
            else if (!alt && t < 2)
            {
                p9 = p8 = Room(l, a, v) + dv;
                if (ka == 5)
                {
                    p6 = Room(l, a, h);
                }
                else
                {
                    p6 = WalkToFloor(alt, Room(l, a, h), Room(l, a, oh), Room(l, a, v));
                }
                if (typ == 1)
                {
                    p7 = Room(l, a, h);
                }
                else if (kb == 5)
                {
                    p7 = Room(l, b, oh);
                }
                else
                {
                    p7 = WalkToFloor(alt, Room(l, b, oh), Room(l, b, h), Room(l, b, ov));
                }
            }
            else if (!alt && t >= 2)
            {
                p9 = p8 = Room(l, a, v);
                p6 = Room(l, a, h) + dh;
                if (kb == 5)
                {
                    p7 = Room(l, b, oh);
                }
                else
                {
                    p7 = WalkToFloor(alt, Room(l, b, oh), Room(l, b, h), Room(l, b, ov));
                }
            }
            ok = CorridorFits(p7 < p6 ? p7 : p6, p7 < p6 ? p6 : p7, p9 < p8 ? p9 : p8, p9 < p8 ? p8 : p9, a, b, flag);
            if (!ok)
            {
                alt = !alt;
            }
            t++;
        }
        while (!ok);
        return ok;
    }

    /// <summary>Walks from <paramref name="from"/> towards <paramref name="to"/> along row/column <paramref name="fixedCoordinate"/> to the first free floor cell.</summary>
    // orig: R006+0x3016 (17DF:0D16) draft
    // C: when the walk finds nothing (it ends beyond `to`) the start is returned.
    private int WalkToFloor(bool vert, int from, int to, int fixedCoordinate)
    {
        var i = _level;
        var up = from < to;
        var baseCell = _level * 1600;
        bool Free(int c) => _w.Kind(baseCell + c) > 1 && _w.Kind(baseCell + c) < 9 && _w.Mark(baseCell + c) == 0;
        if (vert)
        {
            if (up)
            {
                for (i = from; i <= to; i++)
                {
                    if (Free(fixedCoordinate * 40 + i))
                    {
                        break;
                    }
                }
            }
            else
            {
                for (i = from; i >= to; i--)
                {
                    if (Free(fixedCoordinate * 40 + i))
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            if (up)
            {
                for (i = from; i <= to; i++)
                {
                    if (Free(i * 40 + fixedCoordinate))
                    {
                        break;
                    }
                }
            }
            else
            {
                for (i = from; i >= to; i--)
                {
                    if (Free(i * 40 + fixedCoordinate))
                    {
                        break;
                    }
                }
            }
        }
        if ((up && i > to) || (!up && i < to))
        {
            i = from;
        }
        return i;
    }

    /// <summary>Can a corridor occupy x0..x1 × y0..y1 between rooms ra and rb?</summary>
    // orig: R006+0x31C4 (17DF:0EC4) draft
    // C: bad is unsigned; the neighbour tests walk along the corridor's long side and look for a solid wall on both sides.
    private bool CorridorFits(int x0, int x1, int y0, int y1, int ra, int rb, int flag)
    {
        var l = _level;
        var bad = 0;
        if (x0 <= x1 && y0 <= y1 && RoomAt(l, x0, x1, y0, y1) < 0)
        {
            if (flag == 0)
            {
                var h = y1 - y0 + 1;
                var w = x1 - x0 + 1;
                var n = h < w ? w : h;
                int k;
                if (n == w)
                {
                    for (k = 0; k < n; k++)
                    {
                        if (IsSolid(_w.Kind(CellIndex(l, x0 + k, y0 - 1))))
                        {
                            break;
                        }
                    }
                    if (k >= n)
                    {
                        if (n > 1)
                        {
                            return false;
                        }
                        bad = 1;
                    }
                    for (k = 0; k < n; k++)
                    {
                        if (IsSolid(_w.Kind(CellIndex(l, x0 + k, y1 + 1))))
                        {
                            break;
                        }
                    }
                    if (k >= n)
                    {
                        if (n > 1)
                        {
                            return false;
                        }
                        bad++;
                    }
                }
                if (h == n)
                {
                    for (k = 0; k < n; k++)
                    {
                        if (IsSolid(_w.Kind(CellIndex(l, x0 - 1, y0 + k))))
                        {
                            break;
                        }
                    }
                    if (k >= n)
                    {
                        if (n > 1)
                        {
                            return false;
                        }
                        bad++;
                    }
                    for (k = 0; k < n; k++)
                    {
                        if (IsSolid(_w.Kind(CellIndex(l, x1 + 1, y0 + k))))
                        {
                            break;
                        }
                    }
                    if (k >= n)
                    {
                        if (n > 1)
                        {
                            return false;
                        }
                        bad++;
                    }
                }
                if (bad > 2)
                {
                    return false;
                }
            }
            if ((flag != 0 || (FindStairBeside(0, 0, l, x0, x1, y1) < 0 && FindStairBeside(3, 0, l, y0, y1, x1) < 0)) &&
                OpenRoomWall(x0, x1, y0, y1, ra, flag) && OpenRoomWall(x0, x1, y0, y1, rb, flag))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Opens the wall of a room where a corridor rectangle touches it.</summary>
    // orig: R006+0x3412 (17DF:1112) draft
    // C: the draft has no return when the rectangles do not touch; the compiled code leaves AX as the touch test left it: 0.
    private bool OpenRoomWall(int x0, int x1, int y0, int y1, int room, int flag)
    {
        var l = _level;
        var k = RoomKind(l, room);
        var dir = 0;
        var lo = 0;
        var hi = 0;
        if (TouchRects(x0, x1, y0, y1, Room(l, room, X0), Room(l, room, X1), Room(l, room, Y0), Room(l, room, Y1), ref dir, ref lo, ref hi, 0) != 0)
        {
            var door = (flag == 0 && k != 5 && k != 7) ? 1 : 0;
            var ok = false;
            for (k = lo; k <= hi;)
            {
                ok = DoorSpotFree(room, dir ^ 2, k, door, 0);
                k++;
                if (ok)
                {
                    break;
                }
            }
            return ok;
        }
        return false;
    }
}
