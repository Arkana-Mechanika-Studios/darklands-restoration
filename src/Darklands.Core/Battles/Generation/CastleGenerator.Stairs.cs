namespace Darklands.Core.Battles.Generation;

// Stairs between the levels (overlay R006, 18fa). A stair is a cell of kind 9 (up) or 10 (down) on side 0 or 3 of a room, with the two
// cells in front of it marked 7/11 (side 0) or 10/11 (side 3); each stair of a level knows its partner on the neighbouring level.
internal sealed partial class CastleGenerator
{
    /// <summary>On every level except 1: puts a stair arrival where each stair of the neighbouring level (below for the upper levels, level 1 for the cellar) lands.</summary>
    // orig: R006+0x34B8 (18FA:0008) draft
    // C: the draft is a tangle of gotos (the room search, the increment, the call); this is its flow. oko is not reset between stairs: a
    // failed placement leaves it 0 for the next stair, which then skips straight to copying the other level's room.
    // UNCERTAIN: slotn is uninitialised in the original when the placement failed before the strip search; 0 is used here and CutFromStrip
    // then cuts strip 0 (the original cuts whatever the stack held).
    private void ContinueStairs()
    {
        var oko = true;
        // DORMANT: the planner calls this for every level but 1.
        if (_level == 1)
        {
            return;
        }
        int olh;
        int tym;
        int upo;
        if (_level > 1)
        {
            olh = _level - 1;
            tym = 1;
            upo = 0;
        }
        else
        {
            olh = _level + 1;
            tym = 2;
            upo = 1;
        }
        var l = _level;
        var slotn = 0;
        var ig = 0;
        for (var jk = 0; jk < _stairCount[olh]; jk++)
        {
            var ex = _w.StairWord(olh, jk, 0);
            var ey = _w.StairWord(olh, jk, 1);
            var eroom = _w.StairWord(olh, jk, 2);
            var ekind = _w.StairWord(olh, jk, 4);
            var eside = _w.StairWord(olh, jk, 5);
            if (ekind != tym || (eside != 0 && eside != 3))
            {
                continue;
            }
            if (_lastKeepRoom[olh] >= eroom)
            {
                // The stair is in the keep or a tower of the other level: the room of this level that holds its cell.
                oko = false;
                ig = 0;
                while (!oko && ig < _roomCount[l])
                {
                    oko = Room(l, ig, X0) <= ex && Room(l, ig, X1) >= ex && Room(l, ig, Y0) <= ey && Room(l, ig, Y1) >= ey;
                    if (!oko)
                    {
                        ig++;
                    }
                }
                if (!oko)
                {
                    continue;
                }
            }
            else
            {
                Direction(olh, 0, eroom, out var diro, out var gapk);
                var r0 = Room(olh, 0, X0);
                var r1 = Room(olh, 0, X1);
                var r2 = Room(olh, 0, Y0);
                var r3 = Room(olh, 0, Y1);
                int vbd;
                int vao;
                switch (diro)
                {
                    case 0:
                        gapk = r2 - ey;
                        vbd = (gapk >= 4 || (gapk >= 3 && eside == 3)) ? 1 : 0;
                        vao = 1;
                        break;
                    case 1:
                        gapk = ex - r1;
                        vbd = gapk >= 2 ? 1 : 0;
                        vao = eside == 0 ? 1 : 0;
                        break;
                    case 2:
                        gapk = ey - r3;
                        vbd = gapk >= 2 ? 1 : 0;
                        vao = eside == 3 ? 1 : 0;
                        break;
                    case 3:
                        gapk = r0 - ex;
                        vbd = (gapk >= 4 || (gapk >= 3 && eside == 0)) ? 1 : 0;
                        vao = 1;
                        break;
                    default:
                        gapk = -10;
                        vbd = vao = 0;
                        break;
                }
                if (vao == 0 && vbd == 0)
                {
                    oko = false;
                }
                else if (vao != 0 && vbd != 0)
                {
                    if (l < 1 || Roll(2) > 1)
                    {
                        vao = 0;
                    }
                    else
                    {
                        vbd = 0;
                    }
                }
                var x1d = -1;
                var y1d = -1;
                var y0b = 40;
                var x0b = 40;
                var lenc = 0;
                var wh = 0;
                var offl = 0;
                var kindo = 0;
                if (oko && vao != 0)
                {
                    offl = 1;
                    lenc = Dice(offl, 2) + 1;
                    kindo = 4;
                    wh = Dice(1, kindo);
                    wh = wh >= gapk ? wh : gapk;
                }
                else if (oko && vbd != 0)
                {
                    kindo = 5;
                    lenc = Roll(4) + 1;
                    offl = 2;
                    wh = Dice(1, offl) + 1;
                    wh = wh >= gapk - 1 ? wh : gapk - 1;
                }
                if (oko)
                {
                    switch (diro)
                    {
                        case 0:
                            y1d = r2 - offl;
                            if (eside == 0)
                            {
                                y0b = ey;
                                x0b = ex - Roll(lenc - 1) + 1;
                                x0b = x0b >= r0 ? x0b : r0;
                            }
                            else
                            {
                                x0b = ex;
                                y0b = y1d - wh + 1;
                            }
                            x1d = x0b + lenc - 1;
                            break;
                        case 1:
                            x0b = r1 + offl;
                            if (eside == 3)
                            {
                                x1d = x0b + (wh >= 3 ? wh : 3) - 1;
                                y0b = ey - Roll(lenc - 1) + 1;
                                y0b = y0b >= r2 ? y0b : r2;
                            }
                            else
                            {
                                y0b = ey;
                                x1d = x0b + wh - 1;
                                x1d = x1d >= ex + 1 ? x1d : ex + 1;
                            }
                            y1d = y0b + lenc - 1;
                            break;
                        case 2:
                            y0b = r3 + offl;
                            if (eside == 0)
                            {
                                y1d = y0b + (wh >= 3 ? wh : 3) - 1;
                                x0b = ex - Roll(lenc - 1) + 1;
                                x0b = x0b >= r0 ? x0b : r0;
                            }
                            else
                            {
                                x0b = ex;
                                y1d = ey + 1 >= y0b + wh - 1 ? ey + 1 : y0b + wh - 1;
                            }
                            x1d = x0b + lenc - 1;
                            break;
                        case 3:
                            x1d = r0 - offl;
                            if (eside == 3)
                            {
                                x0b = ex;
                                y0b = ey - Roll(lenc - 1) + 1;
                                y0b = y0b >= r2 ? y0b : r2;
                            }
                            else
                            {
                                y0b = ey;
                                x0b = x1d - wh + 1;
                            }
                            y1d = y0b + lenc - 1;
                            break;
                    }
                }
                if (oko && (x1d < x0b || y1d < y0b))
                {
                    oko = false;
                }
                if (oko)
                {
                    // Other stairs of the same kind in rooms outside the keep squeeze the new room.
                    for (var ka = jk + 1; ka < _stairCount[olh]; ka++)
                    {
                        var fx = _w.StairWord(olh, ka, 0);
                        var fy = _w.StairWord(olh, ka, 1);
                        if (_w.StairWord(olh, ka, 4) != tym || _w.StairWord(olh, ka, 2) <= _lastKeepRoom[olh])
                        {
                            continue;
                        }
                        var fside = _w.StairWord(olh, ka, 5);
                        if (fy >= y0b && fy <= y1d)
                        {
                            if (fx > ex)
                            {
                                x1d = fx - 2 <= x1d ? fx - 2 : x1d;
                            }
                            else if (fx < ex)
                            {
                                var limit = fside == 3 ? fx + 4 : fx + 3;
                                x0b = limit >= x0b ? limit : x0b;
                            }
                        }
                        if (fx >= x0b && fx <= x1d)
                        {
                            if (fy > ey)
                            {
                                y1d = fy - 2 <= y1d ? fy - 2 : y1d;
                            }
                            else if (fy < ey)
                            {
                                var limit = fside == 0 ? fy + 4 : fy + 3;
                                y0b = limit >= y0b ? limit : y0b;
                            }
                        }
                    }
                }
                var hitStrip = false;
                if (oko)
                {
                    for (slotn = 0; !hitStrip && slotn < _stripCount[l];)
                    {
                        hitStrip = _w.Strip(l, slotn, 0) <= ex && _w.Strip(l, slotn, 1) >= ex && _w.Strip(l, slotn, 2) <= ey && _w.Strip(l, slotn, 3) >= ey;
                        if (!hitStrip)
                        {
                            slotn++;
                        }
                    }
                }
                if (oko && hitStrip)
                {
                    var s0 = _w.Strip(l, slotn, 0);
                    var s1 = _w.Strip(l, slotn, 1);
                    var s2 = _w.Strip(l, slotn, 2);
                    var s3 = _w.Strip(l, slotn, 3);
                    switch (diro)
                    {
                        case 0:
                        case 2:
                            x0b = s0 + 1 >= x0b ? s0 + 1 : x0b;
                            x1d = s1 - 1 <= x1d ? s1 - 1 : x1d;
                            y0b = s2 >= y0b ? s2 : y0b;
                            y1d = s3 <= y1d ? s3 : y1d;
                            break;
                        case 1:
                        case 3:
                            x0b = s0 >= x0b ? s0 : x0b;
                            x1d = s1 <= x1d ? s1 : x1d;
                            y0b = s2 + 1 >= y0b ? s2 + 1 : y0b;
                            y1d = s3 - 1 <= y1d ? s3 - 1 : y1d;
                            break;
                    }
                }
                else if (oko)
                {
                    slotn = -1;
                }
                if (oko && eside == 3 && (vao == 0 || diro != 3))
                {
                    x1d = x0b + 2 >= x1d ? x0b + 2 : x1d;
                }
                else if (oko && eside == 0 && (vao == 0 || diro != 0))
                {
                    y1d = y0b + 2 >= y1d ? y0b + 2 : y1d;
                }
                if (oko && (ex < x0b || ex > x1d || ey < y0b || ey > y1d))
                {
                    oko = false;
                }
                if (oko && RoomAt(l, x0b, x1d, y0b, y1d) >= 0)
                {
                    oko = false;
                }
                if (oko)
                {
                    var ka = FindStairBeside(0, tym, olh, x0b, x1d, y1d);
                    if (ka < 0)
                    {
                        ka = FindStairBeside(3, tym, olh, y0b, y1d, x1d);
                    }
                    if (ka >= 0)
                    {
                        oko = false;
                    }
                }
                if (!oko)
                {
                    // Placement failed: the new room copies the other level's room that holds the stair.
                    for (var ka = 0; !oko && ka < _stairCount[olh]; ka++)
                    {
                        oko = _w.StairWord(olh, ka, 0) == ex && _w.StairWord(olh, ka, 1) == ey;
                    }
                    if (oko)
                    {
                        ig = eroom;
                        x0b = Room(olh, ig, X0);
                        x1d = Room(olh, ig, X1);
                        y0b = Room(olh, ig, Y0);
                        y1d = Room(olh, ig, Y1);
                        kindo = RoomKind(olh, ig);
                    }
                }
                AddRoom(kindo, x0b, x1d, y0b, y1d, diro);
                ig = _roomCount[l] - 1;
                CutFromStrip(slotn, ig);
            }
            RecordStair(ex, ey, upo, ig, eside, jk);
        }
    }

    /// <summary>Plans new stairs (one or two up, and on level 1 one or two down) in the rooms that can still take them.</summary>
    // orig: R006+0x3E1E (18FA:096E) draft
    // C: a level that finds no place for a stair up becomes the last level (and one that finds no stair down on the lowest level drops the cellar).
    private void PlaceStairs(int kind)
    {
        var l = _level;
        var up = 0;
        var down = 0;
        var nup = 0;
        var ndown = 0;
        // DORMANT: battlefield type 0 has one level, so the planner never calls this with kind 0.
        if (kind == 0)
        {
            return;
        }
        // DORMANT: the planner calls this only for levels 1 .. _lastLevel - 1, so a stair up is always wanted.
        if (l < 1 || l >= _lastLevel)
        {
            up = 0;
        }
        else
        {
            up = Roll(2);
        }
        if (l == 1)
        {
            down = Roll(2);
        }
        else
        {
            down = 0;
        }
        var n7 = 0;
        for (var i = 0; i < _roomCount[l]; i++)
        {
            if (_w.StairState(l, i) == 7)
            {
                n7++;
            }
        }
        if (n7 < 2)
        {
            if (up > 1)
            {
                up = 1;
            }
            if (down > 1)
            {
                down = 1;
            }
        }
        else if (n7 == 2 && down + up == 4)
        {
            if (Roll(2) < 2)
            {
                down--;
            }
            else
            {
                up--;
            }
        }
        bool first = l == 1 && (n7 < 2 || down + up == 4 || (n7 == 2 && down + up == 3) || Roll(5) == 1);
        var wantUp = up;
        var wantDown = down;
        for (var t = 0; wantDown + wantUp > 0 && t < 8; t++)
        {
            var isUp = first || wantDown <= wantUp;
            var r = 0;
            var found = false;
            var min = 7;
            var k = 0;
            for (var i = 0; i < 40 && !found; i++)
            {
                if (i == 10)
                {
                    min = 3;
                }
                if (i - 25 > _lastCastleRoom[l])
                {
                    break;
                }
                if (i < 25)
                {
                    if (_topLevel - l == 1 || Roll(2) > 1)
                    {
                        r = Roll(_lastKeepRoom[l] + 1) - 1;
                    }
                    else if (kind == 8 && i == 0 && (first || !isUp))
                    {
                        r = 0;
                    }
                    else
                    {
                        r = Roll(_lastCastleRoom[l] + 1) - 1;
                    }
                }
                else
                {
                    r = i - 25;
                }
                k = _w.StairState(l, r);
                if ((k >= min && (!first || k > 6) && (!isUp || k != 4) && (isUp || k != 5)) || n7 < 1)
                {
                    found = true;
                }
            }
            if (!found)
            {
                if (!first)
                {
                    break;
                }
                first = false;
                continue;
            }
            var x = Room(l, r, X0);
            var y = Room(l, r, Y0);
            var w = Room(l, r, X1) - x;
            var h = Room(l, r, Y1) - y;
            if (RoomKind(l, r) == KindGateHouse)
            {
                if (_w.Kind(CellIndex(l, x + w + 1, y)) != 0)
                {
                    w++;
                }
                if (_w.Kind(l * 1600 + x * 40 + h + y + 1) != 0)
                {
                    h++;
                }
            }
            while (_w.Mark(l * 1600 + (w + x) * 40 + y) != 0 && w > 0)
            {
                w--;
            }
            while (_w.Mark(l * 1600 + x * 40 + h + y) != 0 && h > 0)
            {
                h--;
            }
            if (first)
            {
                w--;
                h--;
            }
            if (w < 1 && h < 1)
            {
                _w.SetStairState(l, r, 0);
                continue;
            }
            var alt = Roll(2) == 1;
            var ok = false;
            var side = 0;
            for (var j = 0; !ok && j < 2; j++)
            {
                int i;
                if (alt)
                {
                    side = 0;
                    i = Roll(w) - 1;
                    ok = StairSpotOk(i + x, y, isUp, 0);
                    if (!ok)
                    {
                        i = 0;
                        while (i < w && !ok)
                        {
                            ok = StairSpotOk(i + x, y, isUp, 0);
                            if (!ok)
                            {
                                i++;
                            }
                        }
                    }
                    if (ok)
                    {
                        x += i;
                    }
                }
                else
                {
                    side = 3;
                    i = Roll(h) - 1;
                    ok = StairSpotOk(x, i + y, isUp, 3);
                    if (!ok)
                    {
                        i = 0;
                        while (i < h && !ok)
                        {
                            ok = StairSpotOk(x, i + y, isUp, 3);
                            if (!ok)
                            {
                                i++;
                            }
                        }
                    }
                    if (ok)
                    {
                        y += i;
                    }
                }
                if (!ok)
                {
                    alt = !alt;
                }
            }
            if (!ok)
            {
                k = first ? 6 : 1;
                _w.SetStairState(l, r, k);
            }
            else if (_topLevel - l == 1 && RoomKind(l, r) != KindTower)
            {
                if (!isUp)
                {
                    wantDown--;
                }
                else
                {
                    wantUp--;
                }
            }
            else if (isUp && k != 4 && k != 2)
            {
                RecordStair(x, y, 1, r, side, -1);
                wantUp--;
                nup++;
                if (first && k != 5)
                {
                    if (side == 0 && w > 0)
                    {
                        x++;
                        ok = StairSpotOk(x, y, false, 0);
                    }
                    else if (side == 3 && h > 0)
                    {
                        y++;
                        ok = StairSpotOk(x, y, false, 3);
                    }
                    else
                    {
                        ok = false;
                    }
                    if (ok)
                    {
                        RecordStair(x, y, 0, r, side, -1);
                        wantDown--;
                        ndown++;
                    }
                }
                first = false;
            }
            else if (!isUp && k != 5 && k != 2)
            {
                RecordStair(x, y, 0, r, side, -1);
                wantDown--;
                ndown++;
            }
        }
        if (up > 0 && nup < 1)
        {
            _lastLevel = l;
            _topLevelBuilt = 0;
        }
        if (down > 0 && ndown < 1 && l <= _lowestLevel)
        {
            _cellarBuilt = 0;
        }
    }

    /// <summary>Records a stair at (x, y) in a room: up is cell kind 9, down is 10; link is the partner stair on the neighbouring level or −1.</summary>
    // orig: R006+0x4484 (18FA:0FD4) draft
    // C: the partner's link is written through gen_exits[level * 4 + link ∓ 4], i.e. stair `link` of the level below (when this stair goes down)
    // or above (when it goes up); the stair count is not bounded by the four slots a level has.
    private void RecordStair(int x, int y, int up, int room, int side, int link)
    {
        var l = _level;
        if (x < 0 || x > 39 || y < 0 || y > 39 || room < 0 || room >= _roomCount[l])
        {
            return;
        }
        var cell = CellIndex(l, x, y);
        var s = _stairCount[l];
        if (up == 0)
        {
            _w.SetKind(cell, 10);
            _w.SetStairWord(l, s, 4, 2);
        }
        else
        {
            _w.SetKind(cell, 9);
            _w.SetStairWord(l, s, 4, 1);
        }
        _w.SetStairWord(l, s, 0, x);
        _w.SetStairWord(l, s, 1, y);
        _w.SetStairWord(l, s, 2, room);
        _w.SetStairWord(l, s, 5, side);
        _w.SetStairWord(l, s, 3, link);
        if (link >= 0)
        {
            if (up == 0)
            {
                _w.SetStairWord(l - 1, link, 3, _stairCount[l]);
            }
            else
            {
                _w.SetStairWord(l + 1, link, 3, _stairCount[l]);
            }
        }
        if (side == 0)
        {
            _w.SetWalls(cell, _w.Walls(cell) | 10);
            _w.SetMark(cell + 1, 7);
            _w.SetMark(cell + 2, 11);
        }
        else if (side == 3)
        {
            _w.SetWalls(cell, _w.Walls(cell) | 5);
            _w.SetMark(cell + 40, 10);
            _w.SetMark(cell + 80, 11);
        }
        else
        {
            return;
        }
        _stairCount[l]++;
        var state = _w.StairState(l, room);
        switch (state)
        {
            case 3:
                _w.SetStairState(l, room, up == 0 ? 5 : 4);
                break;
            case 4:
                if (up == 0)
                {
                    _w.SetStairState(l, room, 2);
                }
                break;
            case 5:
                if (up != 0)
                {
                    _w.SetStairState(l, room, 2);
                }
                break;
            case 6:
            case 7:
                _w.SetStairState(l, room, up == 0 ? 5 : 4);
                break;
        }
        if ((up == 0 && l > 1) || (up != 0 && l < 1))
        {
            MarkReachable(Outside, room);
        }
    }

    /// <summary>Can a stair (up or down) stand at (x, y), facing side 0 or 3?</summary>
    // orig: R006+0x46A4 (18FA:11F4) draft
    private bool StairSpotOk(int x, int y, bool up, int side)
    {
        var l = _level;
        if (x < 0 || x >= 40 || y < 0 || y >= 40)
        {
            return false;
        }
        var o = l * 1600 + x * 40 + y;
        if (_w.Kind(o) < 2 || _w.Kind(o) > 8)
        {
            return false;
        }
        if (_w.Mark(o) != 0)
        {
            return false;
        }
        if (!((side == 0 && _w.Mark(o + 1) == 0) || (side == 3 && _w.Mark(o + 40) == 0)))
        {
            return false;
        }
        if (!((side == 3 && _w.Kind(o - 40) == 0) || (side == 0 && _w.Kind(o - 1) == 0)))
        {
            return false;
        }
        if (_w.Kind(o + 40) == 0 || _w.Kind(o + 1) == 0)
        {
            return false;
        }
        var open = (side == 0 && OpenRun(x, y, 2) > 1) || (side == 3 && OpenRun(x, y, 1) > 1);
        if (open && StairSurroundingOk(l, x, y))
        {
            var ok = !((!up || _w.Kind(o - 40) == 10 || _w.Kind(o - 1) == 10) && (up || _w.Kind(o + 40) == 9 || _w.Kind(o + 1) == 9));
            if (ok)
            {
                var i = 0;
                ok = true;
                do
                {
                    if (i >= _stairCount[l])
                    {
                        break;
                    }
                    var kind = _w.StairWord(l, i, 4);
                    if ((up && kind == 1) || (!up && kind == 2))
                    {
                        ok = ChebyshevDistance(x, y, _w.StairWord(l, i, 0), _w.StairWord(l, i, 1)) > 3;
                    }
                    i++;
                }
                while (ok);
                if (ok)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Is the 3 × 3 neighbourhood of (x, y) one of the allowed surroundings of a stair?</summary>
    // orig: R006+0x491A (18FA:146A) draft
    // C: the surroundings form two nibbles (the cell towards y+ and the three towards x−, then the rest) that index a table of 16 words
    // read from the program (DS:13D0); the bit for the second nibble must be clear in the first nibble's word.
    private bool StairSurroundingOk(int level, int x, int y)
    {
        var o = x * 40 + y;
        int Wall(int index) => IsFloor(_w.Kind(level * 1600 + index)) ? 0 : 1;
        var a = Wall(o + 1);
        a |= Wall(o - 39) << 1;
        a |= Wall(o - 40) << 2;
        a |= Wall(o - 41) << 3;
        var b = Wall(o - 1);
        b |= Wall(o + 39) << 1;
        b |= Wall(o + 40) << 2;
        var c = Wall(o + 41);
        return ((1 << ((c << 3) | b)) & _g.Context.Tables.NeighbourhoodPatterns[a]) == 0;
    }

    /// <summary>The larger of the two coordinate differences between two points.</summary>
    // orig: R004+0x213C (0E76:213C) draft (byte-matched per the draft notes)
    private static int ChebyshevDistance(int x0, int y0, int x1, int y1)
    {
        var a = Math.Abs(x1 - x0);
        var b = Math.Abs(y1 - y0);
        return b < a ? a : b;
    }
}
