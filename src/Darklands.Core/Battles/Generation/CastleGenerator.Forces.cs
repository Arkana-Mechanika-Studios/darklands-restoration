namespace Darklands.Core.Battles.Generation;

// Where the party starts and where the enemy groups stand (overlay R007), by battlefield type.
internal sealed partial class CastleGenerator
{
    /// <summary>Places the party on level l if it starts there, and the enemy groups the battlefield type calls for.</summary>
    // orig: R007+0x18F8 (15AF:18F8) draft
    private void PlaceForces(int l, int start, int battlefield, int side, GroupSpec[] specs)
    {
        var room = 0;
        var dir = 0;
        if (start == l)
        {
            PlaceParty(l, battlefield, start, out room, out dir);
        }
        switch (battlefield)
        {
            case 8:
                PlaceCampGroup(l, start, battlefield, specs);
                break;
            case 0:
                GuardGate(l, start, battlefield, dir, specs);
                break;
            case 1:
            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
            case 7:
            case 0x7A:
                PlaceRoomGroups(l, start, side, battlefield, room, dir, specs);
                break;
            case 0x27:
            case 0x28:
                PlaceStairGuards(l, start, battlefield, room, dir, specs);
                break;
            case 0x7B:
            case 0x7C:
            case 0x7D:
            case 0x7E:
                PlaceSpreadGroups(l, start, side, battlefield, room, dir, specs);
                break;
        }
    }

    /// <summary>Chooses the party's room and the side it enters from, and writes its start position and facing.</summary>
    // orig: R007+0x1D78 (15AF:1D78) draft; checked against asm
    private void PlaceParty(int l, int battlefield, int start, out int room, out int dir)
    {
        room = 0;
        dir = -1;
        int i;
        switch (battlefield)
        {
            case 0:
            case 5:
            case 0x28:
            case 0x7D:
            case 0x7E:
                room = _gateRoom;
                dir = _gateSide;
                break;
            case 1:
            case 2:
            case 0x7A:
                room = -1;
                for (i = _lastKeepRoom[l] + 1; RoomKind(l, i) != 5 && _lastCastleRoom[l] >= i && _roomCount[l] > i; i++)
                {
                }
                for (; _lastCastleRoom[l] >= i && _roomCount[l] > i; i++)
                {
                    if (RoomKind(l, i) == 5)
                    {
                        if (-(Room(l, i, X0) - Room(l, i, X1)) >= 2 || Room(l, i, Y1) - Room(l, i, Y0) >= 2)
                        {
                            room = i;
                            break;
                        }
                    }
                }
                if (room <= 0)
                {
                    room = Roll(_lastKeepRoom[l]);
                }
                break;
            case 3:
            case 7:
            case 8:
                room = 0;
                break;
            case 4:
                room = Roll(_lastKeepRoom[l] + 1) - 1;
                break;
            case 6:
            {
                var best = -1;
                var area = 0;
                for (i = 1; _lastCastleRoom[l] > i; i++)
                {
                    var size = (Room(l, i, Y1) - Room(l, i, Y0) + 1) * (Room(l, i, X1) - Room(l, i, X0) + 1);
                    if (size >= area)
                    {
                        area = size;
                        best = i;
                    }
                }
                if (best > 0)
                {
                    room = best;
                }
                else if (RoomKind(l, 0) == 2)
                {
                    room = Roll(_lastKeepRoom[l]);
                }
                else
                {
                    room = Roll(_lastKeepRoom[l] + 1) - 1;
                }
                break;
            }
            case 0x27:
            {
                var best = -1;
                var area = 0;
                for (i = 0; _roomCount[l] > i; i++)
                {
                    var size = (Room(l, i, Y1) - Room(l, i, Y0) + 1) * (Room(l, i, X1) - Room(l, i, X0) + 1);
                    if (size >= area)
                    {
                        area = size;
                        best = i;
                    }
                }
                room = best < 0 ? 0 : best;
                break;
            }
            case 0x7B:
                if (start == _topLevel)
                {
                    room = Roll(_roomCount[l]) - 1;
                }
                else
                {
                    room = Roll(_lastCastleRoom[l]);
                }
                break;
            case 0x7C:
                // BUG: the original draws until the room differs from the gate room, which never happens when the only room it can draw is
                // the gate room (the game then freezes; the emulated original does not return for seed 49955, lore 3, cleared 28, specs
                // (46,4,2) (60,6,8) (41,5,3)).
                // DIVERGENCE: gives up after 1000 draws and keeps the last room.
                var attempts = 0;
                do
                {
                    room = Roll(_lastCastleRoom[l]);
                }
                while (room == _gateRoom && ++attempts < 1000);
                break;
        }
        room = room < 0 ? 0 : room;
        if (dir < 0)
        {
            i = 0;
            var ok = false;
            while (!ok && i < 10)
            {
                int k;
                if (i >= 6)
                {
                    k = i - 6;
                }
                else
                {
                    k = Roll(4) - 1;
                }
                dir = k;
                ok = SideFreeOfStairs(l, room, k);
                i++;
            }
            if (!ok)
            {
                dir = Roll(4) - 1;
            }
        }
        _floor.PartyRoom = PartOf(l, room);
        var w = Room(l, room, X1) - Room(l, room, X0) + 1;
        var h = Room(l, room, Y1) - Room(l, room, Y0) + 1;
        var px = w * 16;
        var py = h * 8;
        var xa = Room(l, room, X0);
        var xb = Room(l, room, X1);
        var ya = Room(l, room, Y0);
        var yb = Room(l, room, Y1);
        int spread;
        switch (dir)
        {
            case 0:
                _floor.PartyFacing = 4;
                py = 16;
                yb = ya + 1;
                spread = (w - 2) / 2;
                xa = spread + Room(l, room, X0);
                xb = Room(l, room, X1) - spread;
                break;
            case 1:
                _floor.PartyFacing = 6;
                px = (w - 1) * 32;
                xa = xb - 1;
                spread = (h - 2) / 2;
                ya = spread + Room(l, room, Y0);
                yb = Room(l, room, Y1) - spread;
                break;
            case 2:
                _floor.PartyFacing = 0;
                py = (h - 1) * 16;
                ya = yb - 1;
                spread = (w - 2) / 2;
                xa = spread + Room(l, room, X0);
                xb = Room(l, room, X1) - spread;
                break;
            case 3:
                _floor.PartyFacing = 2;
                xb = xa + 1;
                px = 32;
                spread = (h - 2) / 2;
                ya = spread + Room(l, room, Y0);
                yb = Room(l, room, Y1) - spread;
                break;
        }
        _floor.PartyStart = new MapPoint(Room(l, room, X0) * 32 + px, Room(l, room, Y0) * 16 + py);
        for (px = xa; px <= xb; px++)
        {
            for (py = ya; py <= yb; py++)
            {
                var cell = CellIndex(l, px, py);
                if (_w.Mark(cell) == 0)
                {
                    _w.SetMark(cell, 0x10);
                }
            }
        }
    }

    /// <summary>Is side dir of room r free of stairs (cell kinds 9 and 10)?</summary>
    // orig: R007+0x3372 (15AF:3372) draft
    private bool SideFreeOfStairs(int l, int room, int dir)
    {
        var x = Room(l, room, X0);
        var y = Room(l, room, Y0);
        var x1 = Room(l, room, X1);
        var dy = 0;
        var dx = 0;
        if (dir == 0)
        {
            dx = 1;
        }
        else if (dir == 1)
        {
            dy = 1;
            x = x1;
        }
        else if (dir == 2)
        {
            dx = 1;
            y = Room(l, room, Y1);
        }
        else if (dir == 3)
        {
            dy = 1;
        }
        for (; x <= x1 && y <= Room(l, room, Y1); x += dx, y += dy)
        {
            var v = _w.Kind(l * 1600 + x * 40 + y);
            if (v == 9 || v == 10)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The pixel position of a group in a room: its centre, or next to the party's side in the party's own room.</summary>
    // orig: the macro GPOS of R007+0x23D4, 2a44 and 2cdc (15AF) draft
    private (int X, int Y) GroupPosition(int l, int start, int room, int i, int dir)
    {
        var o0 = Room(l, i, X0);
        var o1 = Room(l, i, X1);
        var o2 = Room(l, i, Y0);
        var o3 = Room(l, i, Y1);
        var px = (o0 + o1 + 1) * 16;
        var py = (o2 + o3 + 1) * 8;
        if (start == l && i == room)
        {
            if (dir == 0)
            {
                py = o3 << 4;
            }
            else if (dir == 1)
            {
                px = (o0 + 1) << 5;
            }
            else if (dir == 2)
            {
                py = (o2 + 1) << 4;
            }
            else if (dir == 3)
            {
                px = o1 << 5;
            }
        }
        return (px, py);
    }

    /// <summary>The enemy kind of a request group when that group may be placed on this level; −1 otherwise.</summary>
    // orig: the pattern if (30e6(...)) m = monster_type_map(lv, type, quality) of R007+0x224E, 23d4 and 2cdc; monster_type_map is R004+0x06AA
    private int KindFor(int battlefield, int l, int k, int side, GroupSpec spec) =>
        MayPlaceGroup(battlefield, l, k, side) ? _g.Forces.AddKind(l - _lowestLevel, spec.Type, spec.Quality) : -1;

    /// <summary>Type 0: the guards of the gate on level 1, next to the keep's gate.</summary>
    // orig: R007+0x224E (15AF:224E) draft
    private void GuardGate(int l, int start, int battlefield, int dir, GroupSpec[] specs)
    {
        if (start != l)
        {
            return;
        }
        var m1 = KindFor(battlefield, l, 1, -1, specs[0]);
        var m2 = KindFor(battlefield, l, 2, -1, specs[1]);
        var m3 = KindFor(battlefield, l, 3, -1, specs[2]);
        if (m1 < 0 && m2 < 0 && m3 < 0)
        {
            return;
        }
        int i;
        for (i = 0; i < _roomCount[GroundLevel]; i++)
        {
            if (IsLinked(GroundLevel, i, _gateRoom))
            {
                break;
            }
        }
        if (i >= _roomCount[GroundLevel])
        {
            i = _gateRoom;
        }
        var o0 = Room(GroundLevel, i, X0);
        var o1 = Room(GroundLevel, i, X1);
        var o2 = Room(GroundLevel, i, Y0);
        var o3 = Room(GroundLevel, i, Y1);
        var px = (o1 + o0 + 1) * 16;
        var py = (o2 + o3 + 1) * 8;
        if (dir == 0)
        {
            py = o3 << 4;
        }
        else if (dir == 1)
        {
            px = (o0 + 1) << 5;
        }
        else if (dir == 2)
        {
            py = (o2 + 1) << 4;
        }
        else if (dir == 3)
        {
            px = o1 << 5;
        }
        AddGroup(l, px, py, 0x10, i, m1, specs[0].Count, m2, specs[1].Count, m3, specs[2].Count);
    }

    /// <summary>Types 1..7 and 0x7A: a group in each room of the castle (the boss group in one), counts by room kind.</summary>
    // orig: R007+0x23D4 (15AF:23D4) draft
    // C: the table of rooms with stairs is filled for exits 0..count inclusive (one slot too many: an unused exit whose room is -1 or stale),
    // and the switch falls through from 0x7A to 1 to 2.
    // UNCERTAIN: for the unused slot the original writes has[-1] (the byte before its local table) when the slot holds -1; that byte is not
    // read by anything visible, and the write is skipped here (DIVERGENCE).
    private void PlaceRoomGroups(int l, int start, int side, int battlefield, int room, int dir, GroupSpec[] specs)
    {
        var m1 = KindFor(battlefield, l, 1, side, specs[0]);
        var m2 = KindFor(battlefield, l, 2, side, specs[1]);
        var m3 = KindFor(battlefield, l, 3, side, specs[2]);
        if (m1 < 0 && m2 < 0 && m3 < 0)
        {
            return;
        }
        var boss = -1;
        if (side == l)
        {
            switch (battlefield)
            {
                case 3:
                case 0x7A:
                    boss = room;
                    break;
                case 1:
                case 2:
                case 4:
                case 5:
                    do
                    {
                        boss = Roll(_lastCastleRoom[l] + 1) - 1;
                    }
                    while (start == l && boss == room && _lastCastleRoom[l] > 1);
                    break;
                case 6:
                case 7:
                    boss = 0;
                    break;
            }
        }
        MarkKind(l, m1);
        var hasStair = new bool[31];
        for (var j = 0; j <= _stairCount[l]; j++)
        {
            var stairRoom = _w.StairWord(l, j, 2);
            if (stairRoom >= 0 && stairRoom < 30)
            {
                hasStair[stairRoom] = true;
            }
        }
        for (var i = 0; i <= _lastCastleRoom[l]; i++)
        {
            var g3 = -1;
            var g2 = -1;
            var g1 = -1;
            var n3 = 0;
            var n2 = 0;
            if (boss == i)
            {
                g1 = m1;
            }
            var skip = battlefield == 0x7A && boss == i;
            if (!skip && (battlefield == 0x7A || battlefield == 1) && i == room && i >= 1)
            {
                skip = true;
            }
            if (!skip && (battlefield == 0x7A || battlefield is 1 or 2))
            {
                var k = RoomKind(l, i);
                if (k == 2)
                {
                    n2 = Roll(3) - 1;
                    n3 = Roll(2) - 1;
                }
                else if (k == 3)
                {
                    n2 = Roll(3) - 2;
                    n3 = Roll(2) - 1;
                    if (l == _topLevel && i == 0 && n3 < 1)
                    {
                        n3 = 1;
                    }
                }
                else if ((l != 0 && l != _topLevel) || Roll(3) < 2)
                {
                    n2 = Roll(5) == 1 ? 1 : 0;
                    if (Roll(4) == 1)
                    {
                        n2 = Roll(2);
                    }
                }
            }
            else if (!skip)
            {
                switch (battlefield)
                {
                    case 3:
                    case 6:
                    case 7:
                        if (start == l)
                        {
                            if (i == room)
                            {
                                n2 = Roll(3) + 1;
                                n3 = Roll(2);
                            }
                            if (boss == i)
                            {
                                n3 += Roll(2);
                            }
                            if (RoomKind(l, i) == 3)
                            {
                                n3 = Roll(2) - 1;
                            }
                        }
                        else
                        {
                            var k = RoomKind(l, i);
                            if (k == 2)
                            {
                                g2 = m2;
                                n2 = Roll(3) + 1;
                            }
                            else if (k == 3)
                            {
                                g3 = m3;
                                n3 = Dice(1, 2);
                            }
                        }
                        break;
                    case 4:
                    case 5:
                    {
                        var j = 1;
                        if (battlefield == 5)
                        {
                            if (l == 1)
                            {
                                j = 2;
                            }
                            else if (_topLevel - l == 1)
                            {
                                j = 0;
                            }
                        }
                        var k = RoomKind(l, i);
                        if (k == 2)
                        {
                            n2 = Dice(j, 3);
                            if (hasStair[i])
                            {
                                n2++;
                            }
                        }
                        else if (k == 3)
                        {
                            if (hasStair[i] || boss == i || Roll(2) == 1)
                            {
                                n3 = Dice(j, 2);
                            }
                        }
                        else if (hasStair[i] || boss == i)
                        {
                            n3 = Dice(j, 2);
                            n2 = Dice(j, 2) - j;
                        }
                        break;
                    }
                }
            }
            if (n2 > 0)
            {
                g2 = m2;
            }
            if (n3 > 0)
            {
                g3 = m3;
            }
            var (px, py) = GroupPosition(l, start, room, i, dir);
            AddGroup(l, px, py, 0x10, i, g1, boss == i ? 1 : 0, g2, n2, g3, n3);
        }
    }

    /// <summary>Marks an enemy kind (+0x0C): the one the leader group is made of.</summary>
    // C: the original indexes the kind records with the absolute level (l * 12 + kind), which is the level's own record only when the lowest
    // level is 0; with a lowest level of 1 the mark lands on the record of level index 1, which may not exist yet. A kind made there later
    // keeps the mark (kind creation does not write the byte), so the marks are kept and applied once all kinds exist.
    // UNCERTAIN: what the mark means (see WildBattlefield.MarkSlot); it is only ever set here for the leader's kind.
    private void MarkKind(int l, int kind)
    {
        if (kind >= 0)
        {
            _kindMarks.Add((l, kind));
        }
    }

    /// <summary>Sets the marks whose kind record exists by now.</summary>
    private void ApplyKindMarks()
    {
        foreach (var (level, kind) in _kindMarks)
        {
            if (level < BattlefieldForces.MaxLevels && kind < _g.Forces.Kinds[level].Count)
            {
                _g.Forces.Kinds[level][kind].EndsBattleWhenKilled = 1;
            }
        }
    }

    /// <summary>Type 8: one camp group in the hall of level l.</summary>
    // orig: R007+0x2964 (15AF:2964) draft
    private void PlaceCampGroup(int l, int start, int battlefield, GroupSpec[] specs)
    {
        _ = start;
        if (RoomKind(l, 0) == 2)
        {
            var m1 = KindFor(battlefield, l, 1, -1, specs[0]);
            var m2 = KindFor(battlefield, l, 2, -1, specs[1]);
            if (m1 >= 0 || m2 >= 0)
            {
                // C: the room argument is the part number of room 0 (0 in practice); AddGroup reads the kind of that room number.
                AddGroup(l, (Room(l, 0, X1) + Room(l, 0, X0) + 1) * 16, (Room(l, 0, Y0) + Room(l, 0, Y1) + 1) * 8, 0x10, PartOf(l, 0),
                    m1, 1, m2, Roll(specs[1].Count) + 1, -1, 0);
            }
        }
    }

    /// <summary>Types 0x27 and 0x28: guards at the stairs, kinds by level.</summary>
    // orig: R007+0x2A44 (15AF:2A44) draft
    // C: ex holds the indexes of the first two matching stairs, which the loop below compares with the room index (a quirk of the original:
    // checked against asm R007+0x2A44, the loop over the exits and the compares at 0x2BA2/0x2BF0).
    private void PlaceStairGuards(int l, int start, int battlefield, int room, int dir, GroupSpec[] specs)
    {
        var lv = l - _lowestLevel;
        var m1 = -1;
        var m2 = -1;
        var m3 = -1;
        switch (l)
        {
            case 1:
                m1 = _g.Forces.AddKind(lv, 0x18, 1);
                m2 = _g.Forces.AddKind(lv, specs[1].Type, specs[1].Quality);
                m3 = _g.Forces.AddKind(lv, specs[2].Type, specs[2].Quality);
                break;
            case 2:
                m1 = _g.Forces.AddKind(lv, 0x18, 1);
                m2 = _g.Forces.AddKind(lv, specs[1].Type, 5);
                m3 = _g.Forces.AddKind(lv, specs[1].Type, specs[1].Quality);
                break;
            case 3:
                if (_topLevel > l)
                {
                    m2 = _g.Forces.AddKind(lv, specs[1].Type, 5);
                    m3 = _g.Forces.AddKind(lv, specs[1].Type, specs[1].Quality);
                }
                break;
            default:
                return;
        }
        if (m1 < 0 && m2 < 0 && m3 < 0)
        {
            return;
        }
        var n = 0;
        var ex0 = -1;
        var ex1 = -1;
        var want = l == 1 ? 1 : l == 3 ? 2 : 0;
        if (want != 0)
        {
            var taken = 0;
            for (var i = 0; i < _stairCount[l]; i++)
            {
                if (_w.StairWord(l, i, 4) == want)
                {
                    n++;
                    if (taken < 2)
                    {
                        if (taken == 0)
                        {
                            ex0 = i;
                        }
                        else
                        {
                            ex1 = i;
                        }
                        taken++;
                    }
                }
            }
        }
        for (var i = 0; i <= _lastCastleRoom[l]; i++)
        {
            var a3 = 0;
            var a2 = 0;
            var a1 = 0;
            if (l == 1)
            {
                if (ex0 == i)
                {
                    if (n != 1)
                    {
                        a3 = 1;
                        a1 = 1;
                        a2 = 2;
                    }
                    else
                    {
                        a1 = 1;
                        a2 = 4;
                        a3 = 3;
                    }
                }
                else if (ex1 == i)
                {
                    a1 = 0;
                    a3 = 2;
                    a2 = 2;
                }
            }
            else if (l == 2)
            {
                if (i != 0)
                {
                    continue;
                }
                a1 = 1;
                a2 = 3;
                a3 = 4;
            }
            else if (l == 3 && _topLevel > 3)
            {
                if (ex0 == i || ex1 == i)
                {
                    a1 = 0;
                    a3 = 2;
                    a2 = 2;
                }
            }
            else
            {
                continue;
            }
            if (a1 != 0 || a2 != 0 || a3 != 0)
            {
                var (px, py) = GroupPosition(l, start, room, i, dir);
                AddGroup(l, px, py, 0x10, i, m1, a1, m2, a2, m3, a3);
            }
        }
        _ = battlefield;
    }

    /// <summary>Types 0x7B..0x7E: the enemies spread over the rooms of each level.</summary>
    // orig: R007+0x2CDC (15AF:2CDC) draft; checked against asm
    private void PlaceSpreadGroups(int l, int start, int side, int battlefield, int room, int dir, GroupSpec[] specs)
    {
        var m1 = KindFor(battlefield, l, 1, side, specs[0]);
        var m2 = KindFor(battlefield, l, 2, side, specs[1]);
        if (m1 < 0 && m2 < 0)
        {
            return;
        }
        var gate = -2;
        int i;
        if (l == 1)
        {
            for (i = 0; i < _roomCount[GroundLevel] && !IsLinked(GroundLevel, i, _gateRoom); i++)
            {
            }
            if (i < _roomCount[GroundLevel])
            {
                gate = i;
            }
        }
        var boss = -1;
        if (side == l && battlefield > 0x7A)
        {
            // C: the draft left type 0x7B without a leader; the asm picks one for 0x7B, 0x7C and 0x7D (checked against asm R007+0x2CDC).
            if (battlefield == 0x7B || battlefield == 0x7C || battlefield == 0x7D)
            {
                boss = Roll(_lastCastleRoom[l] + 1) - 1;
            }
            else if (battlefield == 0x7E)
            {
                if (l == 1 && Roll(2) == 1)
                {
                    boss = gate;
                }
                else
                {
                    boss = _w.StairWord(l, 0, 2);
                }
            }
        }
        MarkKind(l, m1);
        var perRoom = new int[30];
        var count = specs[1].Count;
        if (battlefield == 0x7E)
        {
            if (_topLevel == l)
            {
                for (i = 0; i < count; i++)
                {
                    AddToRoom(perRoom, Roll(_lastKeepRoom[l]) - 1, 1);
                }
            }
            if (l < _highestLevel - 1)
            {
                var up = 1;
                for (i = 0; i < _stairCount[l]; i++)
                {
                    if (_w.StairWord(l, i, 4) == 1 && _w.StairWord(l, i, 2) != 0)
                    {
                        up++;
                    }
                }
                var k = (count + 2) / 3;
                perRoom[0] += k;
                k = count - k;
                for (i = 0; i < _stairCount[l]; i++)
                {
                    if (_w.StairWord(l, i, 4) == 1 && _w.StairWord(l, i, 2) != 0)
                    {
                        if (up == 2)
                        {
                            perRoom[_w.StairWord(l, i, 2)] += k;
                        }
                        else
                        {
                            perRoom[_w.StairWord(l, i, 2)] += k / 2;
                            k -= k / 2;
                        }
                    }
                }
            }
            if (l == 1)
            {
                AddToRoom(perRoom, gate, count);
            }
        }
        else if (_topLevel == l)
        {
            perRoom[0]++;
        }
        else
        {
            for (i = 0; i < count; i++)
            {
                AddToRoom(perRoom, Roll(_lastCastleRoom[l]) - 1, 1);
            }
        }
        for (i = 0; i <= _lastCastleRoom[l]; i++)
        {
            if (perRoom[i] > 0 || boss == i)
            {
                var g2 = -1;
                var g1 = -1;
                var n2 = 0;
                if (boss == i)
                {
                    g1 = m1;
                }
                if (perRoom[i] > 0)
                {
                    g2 = m2;
                    n2 = perRoom[i];
                }
                var (px, py) = GroupPosition(l, start, room, i, dir);
                AddGroup(l, px, py, 0x10, i, g1, boss == i ? 1 : 0, g2, n2, -1, 0);
            }
        }
        _ = dir;
    }

    /// <summary>Adds to a room's count; a negative room (a roll of 0 on an empty range, or no gate room) is dropped.</summary>
    // C: the original writes per[-1] and per[-2], which are the segment and offset words of its far pointer `o`; that pointer is set again
    // from the room table before every use, so the write has no effect.
    // orig: the stores per[...]++ of R007+0x2CDC (15AF:2CDC) draft
    // C: an index of −1 or −2 increments [bp-42h]/[bp-44h], the far pointer o that is reloaded before every use: the write is dead and
    // skipping it is exact.
    private static void AddToRoom(int[] perRoom, int room, int count)
    {
        if (room >= 0)
        {
            perRoom[room] += count;
        }
    }

    /// <summary>May group k (1..3) of the request be placed on level l?</summary>
    // orig: R007+0x30E6 (15AF:30E6) draft; checked against asm
    private bool MayPlaceGroup(int battlefield, int l, int k, int side)
    {
        if (l < _lowestLevel || l > _highestLevel || k < 1 || k > 3)
        {
            return false;
        }
        switch (battlefield)
        {
            case 0:
                if (l == 1)
                {
                    return true;
                }
                break;
            case 1:
            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
            case 7:
            case 0x7A:
                if (k == 3)
                {
                    return true;
                }
                if (k == 2)
                {
                    return true;
                }
                if (k != 1)
                {
                    break;
                }
                if (l == side)
                {
                    return true;
                }
                break;
            case 8:
                if (k >= 3)
                {
                    break;
                }
                if (RoomKind(l, 0) == 2)
                {
                    return true;
                }
                break;
            case 0x7B:
            case 0x7C:
            case 0x7D:
            case 0x7E:
                if (k == 2)
                {
                    return true;
                }
                if (k != 1)
                {
                    break;
                }
                if (l == side)
                {
                    return true;
                }
                break;
        }
        return false;
    }

    /// <summary>Adds an enemy group (up to three kinds with counts) to the level's list and marks its cell.</summary>
    // orig: R007+0x316C (15AF:316C) draft; checked against asm
    // C: outside the keep halls a group holds at most four enemies in all; a cleared level (memory bit) halves the counts, rounding up.
    // C: bf_groups is one flat array of 16 × 10 records and the slot is lv × 10 + group_count[lv] with no limit: an eleventh group of
    // a level is written into the next level's first record, which that level's first group (written later) fills again; both
    // levels then share the record (fields a group does not write keep what the earlier one left). Reachable: a level of the
    // room-per-group battlefields (1..7, 0x7A) can get 10 or 11 groups.
    private void AddGroup(int l, int px, int py, int radius, int room, int m1, int n1, int m2, int n2, int m3, int n3)
    {
        if (m1 < 0 && m2 < 0 && m3 < 0)
        {
            return;
        }
        if (m1 < 0)
        {
            n1 = 0;
        }
        if (m2 < 0)
        {
            n2 = 0;
        }
        if (m3 < 0)
        {
            n3 = 0;
        }
        if (RoomKind(l, room) != 2)
        {
            if (n1 > 4)
            {
                n1 = 4;
            }
            if (n2 > 4 - n1)
            {
                n2 = 4 - n1;
            }
            if (n2 < 0)
            {
                n2 = 0;
            }
            if (n3 > 4 - n1 - n2)
            {
                n3 = 4 - n1 - n2;
            }
            if (n3 < 0)
            {
                n3 = 0;
            }
            if (n1 < 1)
            {
                m1 = -1;
            }
            if (n2 < 1)
            {
                m2 = -1;
            }
            if (n3 < 1)
            {
                m3 = -1;
            }
        }
        if (m1 < 0 && m2 < 0 && m3 < 0)
        {
            return;
        }
        if ((_g.Context.Memory.ClearedLevels & (1 << l)) != 0)
        {
            if (n1 > 0)
            {
                n1 = (n1 + 1) / 2;
            }
            if (n2 > 0)
            {
                n2 = (n2 + 1) / 2;
            }
            if (n3 > 0)
            {
                n3 = (n3 + 1) / 2;
            }
        }
        var lv = l - _lowestLevel;
        var slot = lv * BattlefieldForces.MaxGroups + _groupCount[lv];
        var group = GroupSlot(slot);
        var k = 0;
        if (m1 >= 0 && n1 > 0)
        {
            group.Kinds[0] = (sbyte)m1;
            group.Counts[0] = (sbyte)n1;
            k = 1;
        }
        if (m2 >= 0 && n2 > 0)
        {
            group.Kinds[k] = (sbyte)m2;
            group.Counts[k] = (sbyte)n2;
            k++;
        }
        if (m3 >= 0 && n3 > 0)
        {
            group.Kinds[k] = (sbyte)m3;
            group.Counts[k] = (sbyte)n3;
            k++;
        }
        if (k != 0)
        {
            group.X = (short)px;
            group.Y = (short)py;
            group.Spread = (short)radius;
            group.Room = (sbyte)PartOf(l, room);
            group.HasReinforcements = 0;
            _groupCount[lv]++;
            SyncGroups();
            var cell = CellIndex(l, px / 32, py / 16);
            if (_w.Mark(cell) == 0)
            {
                _w.SetMark(cell, 14);
            }
        }
    }

    /// <summary>bf_groups as the original lays it out (16 levels × 10 records, the defaults of groups_clear until written).</summary>
    private readonly EnemyGroup?[] _groupSlots = new EnemyGroup?[BattlefieldForces.MaxLevels * BattlefieldForces.MaxGroups];

    /// <summary>group_count of each level (not limited to 10).</summary>
    private readonly int[] _groupCount = new int[BattlefieldForces.MaxLevels];

    /// <summary>Each level's list is its group_count records from slot lv × 10 on (an overflowing level reads into the next).</summary>
    private void SyncGroups()
    {
        for (var lv = 0; lv < BattlefieldForces.MaxLevels; lv++)
        {
            var list = _g.Forces.Groups[lv];
            if (_groupCount[lv] == 0 && list.Count == 0)
            {
                continue;
            }
            list.Clear();
            for (var j = 0; j < _groupCount[lv]; j++)
            {
                list.Add(GroupSlot(lv * BattlefieldForces.MaxGroups + j));
            }
        }
    }

    // DIVERGENCE: past the 160th record the original writes into mon_templates (B4C0); such records are kept apart here.
    private EnemyGroup GroupSlot(int slot)
    {
        if (slot < _groupSlots.Length)
        {
            return _groupSlots[slot] ??= new EnemyGroup();
        }
        if (!_groupsPast.TryGetValue(slot, out var g))
        {
            _groupsPast[slot] = g = new EnemyGroup();
        }
        return g;
    }

    private readonly Dictionary<int, EnemyGroup> _groupsPast = [];
}
