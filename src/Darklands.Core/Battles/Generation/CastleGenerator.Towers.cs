namespace Darklands.Core.Battles.Generation;

// The corner towers of the keep (overlay R006, 1637).
internal sealed partial class CastleGenerator
{
    /// <summary>Adds the towers: copied from the level below, random ones on level 1, or those of the level above for the cellar.</summary>
    // orig: R006+0x0886 (1637:0006) draft
    // C: a level above 1 copies every room of level 1 from 1 to its last keep room; the cellar copies the rooms of level 1 that are towers with a stair down.
    private void PlaceTowers(int battlefield)
    {
        if (_level > 1)
        {
            for (var i = 1; i <= _lastKeepRoom[_level - 1]; i++)
            {
                var k = StoredDirection(GroundLevel, 0, i);
                AddRoom(KindTower, Room(GroundLevel, i, X0), Room(GroundLevel, i, X1), Room(GroundLevel, i, Y0), Room(GroundLevel, i, Y1), k);
                ShrinkStripsByTower(_roomCount[_level] - 1);
                ConnectRoom(_roomCount[_level] - 1, 3, 0, 0);
            }
        }
        else if (_level < 1)
        {
            for (var i = 0; i < _stairCount[_level + 1]; i++)
            {
                if (_w.StairWord(_level + 1, i, 4) == 2)
                {
                    var r = _w.StairWord(_level + 1, i, 2);
                    if (RoomKind(_level + 1, r) == KindTower)
                    {
                        var k = StoredDirection(GroundLevel, 0, r);
                        AddRoom(KindTower, Room(_level + 1, r, X0), Room(_level + 1, r, X1), Room(_level + 1, r, Y0), Room(_level + 1, r, Y1), k);
                        ShrinkStripsByTower(_roomCount[_level] - 1);
                        ConnectRoom(_roomCount[_level] - 1, 3, 0, 0);
                    }
                }
            }
        }
        else if (_level == 1)
        {
            var n = Roll(5) - 1;
            if (battlefield == 0x27)
            {
                n = Max(n, 2);
            }
            // C: used and bit are unsigned; one bit per corner, drawn until a free one comes up.
            var used = 0;
            for (; n > 0; n--)
            {
                int bit;
                do
                {
                    bit = Roll(4) - 1;
                    bit = 1 << bit;
                }
                while ((used & bit) != 0);
                used |= bit;
                var a = Roll(2) + 2;
                var b = Roll(2) + 2;
                int c;
                int d;
                if (Roll(2) > 1)
                {
                    if (Roll(2) > 1)
                    {
                        d = Roll(3) - 1;
                        c = Roll(2) - 1;
                    }
                    else
                    {
                        d = Roll(2) - 1;
                        c = Roll(3) - 1;
                    }
                }
                else
                {
                    c = 0;
                    d = c;
                }
                int x0;
                int x1;
                int y0;
                int y1;
                switch (bit)
                {
                    case 1:
                        y1 = Room(GroundLevel, 0, Y0) - 2;
                        y0 = y1 - b + c + 1;
                        y1 += c;
                        x1 = Room(GroundLevel, 0, X0) - 2;
                        x0 = x1 - a + d + 1;
                        x1 += d;
                        AddRoom(KindTower, x0, x1, y0, y1, 4);
                        break;
                    case 2:
                        y1 = Room(GroundLevel, 0, Y0) - 2;
                        y0 = y1 - b + c + 1;
                        y1 += c;
                        x0 = Room(GroundLevel, 0, X1) - d + 2;
                        x1 = x0 + a - 1;
                        AddRoom(KindTower, x0, x1, y0, y1, 5);
                        break;
                    case 4:
                        y0 = Room(GroundLevel, 0, Y1) - c + 2;
                        y1 = y0 + b - 1;
                        x0 = Room(GroundLevel, 0, X1) - d + 2;
                        x1 = x0 + a - 1;
                        AddRoom(KindTower, x0, x1, y0, y1, 6);
                        break;
                    default:
                        y0 = Room(GroundLevel, 0, Y1) - c + 2;
                        y1 = y0 + b - 1;
                        x1 = Room(GroundLevel, 0, X0) - 2;
                        x0 = x1 - a + d + 1;
                        x1 += d;
                        AddRoom(KindTower, x0, x1, y0, y1, 7);
                        break;
                }
                ShrinkStripsByTower(_roomCount[_level] - 1);
                ConnectRoom(_roomCount[GroundLevel] - 1, 3, 0, 0);
            }
        }
    }

    /// <summary>Shrinks the free strips around the keep by the tower at a corner.</summary>
    // orig: R006+0x0BE6 (1637:0366) draft
    // C: indexes the level's strips as ints (slot * 5 + field).
    private void ShrinkStripsByTower(int room)
    {
        var l = _level;
        var o0 = Room(l, room, X0);
        var o1 = Room(l, room, X1);
        var o2 = Room(l, room, Y0);
        var o3 = Room(l, room, Y1);
        int S(int index) => _w.StripWord(l, index);
        void Set(int index, int value) => _w.SetStripWord(l, index, value);
        switch (TowerCorner(l, room))
        {
            case 4:
                Set(0, Max(o1 + 1, S(0)));
                Set(2, Min(o2 - 1, S(2)));
                Set(17, Max(o3 + 1, S(17)));
                Set(15, Min(o0 - 1, S(15)));
                break;
            case 5:
                Set(1, Min(S(1), o0 - 1));
                Set(2, Min(o2 - 1, S(2)));
                Set(7, Max(o3 + 1, S(7)));
                Set(6, Max(o1 + 1, S(6)));
                break;
            case 6:
                Set(11, Min(S(11), o0 - 1));
                Set(13, Max(o3 + 1, S(13)));
                Set(8, Min(o2 - 1, S(8)));
                Set(6, Max(o1 + 1, S(6)));
                break;
            case 7:
                Set(10, Max(o1 + 1, S(10)));
                Set(13, Max(o3 + 1, S(13)));
                Set(18, Min(o2 - 1, S(18)));
                Set(15, Min(S(15), o0 - 1));
                break;
        }
    }

    /// <summary>The corner of the keep (room 0 of level 1) a room stands at: 4 north-west, 5 north-east, 6 south-east, 7 south-west, 8 none.</summary>
    // orig: R006+0x0D50 (1637:04D0) draft
    private int TowerCorner(int level, int room)
    {
        var o0 = Room(level, room, X0);
        var o1 = Room(level, room, X1);
        var o2 = Room(level, room, Y0);
        var o3 = Room(level, room, Y1);
        var k0 = Room(GroundLevel, 0, X0);
        var k1 = Room(GroundLevel, 0, X1);
        var k2 = Room(GroundLevel, 0, Y0);
        var k3 = Room(GroundLevel, 0, Y1);
        if (o0 < k0 && k2 > o2)
        {
            return 4;
        }
        if (o1 > k1 && o2 < k2)
        {
            return 5;
        }
        if (o0 > k0 && o3 > k3)
        {
            return 6;
        }
        if (o0 < k0 && o3 > k3)
        {
            return 7;
        }
        // DORMANT: a tower always stands at one of the keep's corners; 8 (none) is the value for a room that is not.
        return 8;
    }
}
