namespace Darklands.Core.Battles.Generation;

// Furniture, chests and the other objects of the rooms (overlay R007).
internal sealed partial class CastleGenerator
{
    private const int ChestCountAt = 0x300C;
    private const int ChestsAt = 0x300E;
    private const int ChestSize = 0x2C;
    private const int MaxChests = 8;

    /// <summary>Marks the cells in front of the stairs, then scatters objects over the rooms (and a few chests) and furnishes the halls.</summary>
    // orig: R007+0x345A (15AF:345A) draft
    // C: "DS:51AC" in the draft is the number of chests so far (the byte at +0x300C of the floor image): a chest is allowed only while that
    // number is below the level's random limit (1..3) minus 1.
    private void Furnish(int battlefield, int l, ref int chests)
    {
        for (var i = 0; i < _stairCount[l]; i++)
        {
            var x = _w.StairWord(l, i, 0);
            var y = _w.StairWord(l, i, 1);
            var side = _w.StairWord(l, i, 5);
            if (side == 0)
            {
                _w.SetMark(CellIndex(l, x, y) + 2, 11);
            }
            else if (side == 3)
            {
                _w.SetMark(CellIndex(l, x, y) + 80, 11);
            }
        }
        var limit = Roll(3);
        for (var r = 0; r < _roomCount[l]; r++)
        {
            var o0 = Room(l, r, X0);
            var o1 = Room(l, r, X1);
            var o2 = Room(l, r, Y0);
            var o3 = Room(l, r, Y1);
            var kind = RoomKind(l, r);
            if (kind == 2)
            {
                FurnishHall(battlefield, l, r);
            }
            var placed = 0;
            for (var x = o0; x <= o1; x++)
            {
                for (var y = o2; y <= o3; y++)
                {
                    if (!ObjectChance(l, kind) || !CellFree(l, x, y))
                    {
                        continue;
                    }
                    var force = chests == 0 && limit - 1 > 0;
                    var obj = RandomObject(battlefield, l, kind, force);
                    if (obj == 5)
                    {
                        if (limit - 1 <= (sbyte)Img[ChestCountAt] || chests > 19 || placed > 0 || o3 == y || !BlockUnused(l, x, y))
                        {
                            continue;
                        }
                        AddChest(l, x, y, ref chests);
                        placed++;
                    }
                    else if (obj == 9 && battlefield == 8)
                    {
                        obj = 0;
                    }
                    PlaceObject(l, r, x, y, obj);
                }
            }
        }
    }

    /// <summary>Furnishes a keep hall: a table with benches (above the ground), a throne (lowest level) and a raised step (above the ground).</summary>
    // orig: R007+0x3694 (15AF:3694) draft
    private void FurnishHall(int battlefield, int l, int room)
    {
        if (l > 0)
        {
            PlaceTable(battlefield, l, room);
        }
        if (_lowestLevel == l && battlefield != 0)
        {
            PlaceThrone(l, room);
        }
        if (l > 0)
        {
            PlaceStep(l, room);
        }
    }

    /// <summary>A table with benches in the middle of the hall.</summary>
    // orig: R007+0x36E2 (15AF:36E2) draft
    private void PlaceTable(int battlefield, int l, int room)
    {
        var o0 = Room(l, room, X0);
        var o1 = Room(l, room, X1);
        var o2 = Room(l, room, Y0);
        var o3 = Room(l, room, Y1);
        var w = o1 - o0;
        var h = o3 - o2;
        var k = w - 2;
        var x0 = k / 2 + o0;
        if (k % 2 == 1 && Roll(2) == 1)
        {
            x0++;
        }
        var y0 = o2;
        k = h - 2;
        y0 = k / 2 + y0;
        if (k % 2 == 1 && Roll(2) == 1)
        {
            y0++;
        }
        for (var tx = x0; tx <= x0 + 2; tx++)
        {
            for (var ty = y0; ty <= y0 + 2; ty++)
            {
                if (!CellFree(l, tx, ty))
                {
                    return;
                }
            }
        }
        var kind = 8;
        var n = 1;
        if (battlefield != 8 && Roll(4) > 1)
        {
            kind = 9;
            if (Roll(2) == 1)
            {
                n = 2;
                if ((h + 1) * (w + 1) > 0x32 && Roll(2) == 1)
                {
                    n = 3;
                }
            }
        }
        var x1 = x0 + 1;
        if (n == 1 || (n == 2 && Roll(2) == 1))
        {
            y0++;
        }
        var y1 = y0 + n;
        int y;
        for (y = y0; y < y1; y++)
        {
            if (CellFree(l, x1, y))
            {
                PlaceObject(l, 0, x1, y, kind);
            }
        }
        for (var x = x0; x <= x0 + 2; x += 2)
        {
            for (y = y0; y < y1; y++)
            {
                if (CellFree(l, x, y))
                {
                    int side;
                    if (Roll(4) == 1)
                    {
                        side = 2;
                    }
                    else if ((o2 + o3) / 2 < y)
                    {
                        side = 3;
                    }
                    else
                    {
                        side = 4;
                    }
                    PlaceObject(l, 0, x, y, side);
                }
            }
        }
        y = y0 - 1;
        if (CellFree(l, x1, y))
        {
            PlaceObject(l, 0, x1, y, 4);
        }
        if (CellFree(l, x1, y1))
        {
            PlaceObject(l, 0, x1, y1, 3);
        }
        for (var x = x0; x <= x0 + 2; x++)
        {
            for (k = y; k <= y1; k++)
            {
                var cell = CellIndex(l, x, k);
                if (_w.Mark(cell) == 0)
                {
                    _w.SetMark(cell, 13);
                }
            }
        }
    }

    /// <summary>A throne (object 1) somewhere in the hall, with its surroundings marked.</summary>
    // orig: R007+0x39BA (15AF:39BA) draft
    private void PlaceThrone(int l, int room)
    {
        var o0 = Room(l, room, X0);
        var o1 = Room(l, room, X1);
        var o2 = Room(l, room, Y0);
        var o3 = Room(l, room, Y1);
        var x = 0;
        var y = 0;
        int i;
        for (i = 0; i < 50; i++)
        {
            x = Roll(o1 - o0 + 1) + o0 - 1;
            y = Roll(o3 - o2 + 1) + o2 - 1;
            if (CellFree(l, x, y))
            {
                break;
            }
        }
        if (i < 50)
        {
            PlaceObject(l, 0, x, y, 1);
            for (var a = x - 1; a <= x + 1; a++)
            {
                for (var b = y - 1; b <= y + 1; b++)
                {
                    var cell = CellIndex(l, a, b);
                    if (_w.Mark(cell) == 0)
                    {
                        _w.SetMark(cell, 13);
                    }
                }
            }
        }
    }

    /// <summary>A two-cell raised step (heights 2 and 1) in the hall.</summary>
    // orig: R007+0x3AAC (15AF:3AAC) draft
    private void PlaceStep(int l, int room)
    {
        var w = Room(l, room, X1) - Room(l, room, X0) + 1;
        var h = Room(l, room, Y1) - Room(l, room, Y0) + 1;
        var x0 = Room(l, room, X0);
        var y0 = Room(l, room, Y0);
        var i = 0;
        var x = 0;
        var y = 0;
        do
        {
            x = Roll(w) + x0 - 1;
            y = Roll(h) + y0 - 1;
            if (CellFree(l, x, y) && CellFree(l, x, y + 1))
            {
                break;
            }
        }
        while (++i < 50);
        if (i < 50)
        {
            SetHeight(x, y, (Height(x, y) & 0xF0) | 2);
            SetHeight(x, y + 1, (Height(x, y + 1) & 0xF0) | 1);
            var cell = CellIndex(l, x, y);
            if (_w.Mark(cell) == 0)
            {
                _w.SetMark(cell, 15);
            }
            if (_w.Mark(cell + 1) == 0)
            {
                _w.SetMark(cell + 1, 15);
            }
        }
    }

    /// <summary>Does a cell of this room kind get an object? (1 in 20 in the ground-level halls, 1 in 6 in rooms of kinds 2..5 above, never elsewhere.)</summary>
    // orig: R007+0x3B92 (15AF:3B92) draft
    // C: the original's parameter called l here is the level; dice-roll(1, 0) makes no draw and is never 1.
    private bool ObjectChance(int l, int kind)
    {
        int n;
        if (l == 0)
        {
            n = kind == 2 ? 20 : 0;
        }
        else
        {
            n = kind is 2 or 3 or 4 or 5 ? 6 : 0;
        }
        return Roll(n) == 1;
    }

    /// <summary>Is the cell free for an object: a floor cell without a mark, and its eight neighbours leave a way through (the pattern table)?</summary>
    // orig: R007+0x3BCE (15AF:3BCE) draft
    // C: !f() << n parses as (!f()) << n; the two nibbles are the open/closed state of the four neighbours on either side.
    private bool CellFree(int l, int x, int y)
    {
        var cell = CellIndex(l, x, y);
        var kind = _w.Kind(cell);
        if (kind > 1 && kind < 9 && _w.Mark(cell) == 0)
        {
            int Closed(int cx, int cy, int dir) => CellOpen(l, cx, cy, dir) ? 0 : 1;
            var a = Closed(x, y + 1, 2);
            a |= Closed(x - 1, y + 1, 7) << 1;
            a |= Closed(x - 1, y, 3) << 2;
            a |= Closed(x - 1, y - 1, 4) << 3;
            var b = Closed(x, y - 1, 0);
            b |= Closed(x + 1, y - 1, 5) << 1;
            b |= Closed(x + 1, y, 1) << 2;
            b |= Closed(x + 1, y + 1, 6) << 3;
            return ((1 << b) & _g.Context.Tables.NeighbourhoodPatterns[a]) == 0;
        }
        return false;
    }

    /// <summary>Is the 3 × 3 block around (x, y) free of marks?</summary>
    // orig: R007+0x3D2E (15AF:3D2E) draft
    private bool BlockUnused(int l, int x, int y)
    {
        for (var i = x - 1; i <= x + 1; i++)
        {
            for (var j = y - 1; j <= y + 1; j++)
            {
                if (_w.Mark(CellIndex(l, i, j)) != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Is the cell a floor cell that is open towards direction k (0..3 the sides, 4..7 the corners)?</summary>
    // orig: R007+0x3DA0 (15AF:3DA0) draft
    private bool CellOpen(int l, int x, int y, int k)
    {
        var i = CellIndex(l, x, y);
        var v = _w.Kind(i);
        if (v <= 1 || v >= 9)
        {
            return false;
        }
        var b = _w.Walls(i);
        switch (k)
        {
            case 0:
                b &= 4;
                break;
            case 1:
                b &= 8;
                break;
            case 2:
                b &= 1;
                break;
            case 3:
                b &= 2;
                break;
            case 4:
                b &= 6;
                break;
            case 5:
                b &= 12;
                break;
            case 6:
                b &= 9;
                break;
            case 7:
                b &= 3;
                break;
        }
        return b == 0;
    }

    /// <summary>A random object for a room of this kind: 5 chest, 2/3/4/7/8/9 furniture (0 none); a forced pick is always a chest.</summary>
    // orig: R007+0x3E3E (15AF:3E3E) draft
    // DORMANT: kind 3 rolls 1..4 and has an arm for 5 (object 4) that no roll reaches.
    private int RandomObject(int battlefield, int l, int kind, bool force)
    {
        var r = 0;
        if (l == 0)
        {
            if (kind == 2)
            {
                r = Roll(3) switch { 1 => 5, 2 => 2, 3 => 7, _ => 0 };
            }
        }
        else
        {
            switch (kind)
            {
                case 2:
                    r = Roll(3) switch { 1 => 5, 2 => 7, 3 => 2, _ => 0 };
                    break;
                case 3:
                    r = Roll(4) switch { 1 => 5, 2 => 7, 3 => 2, 4 => 3, 5 => 4, _ => 0 };
                    break;
                case 4:
                    r = Roll(6) switch { 1 => 5, 2 => 7, 3 => 2, 4 => 3, 5 => 4, 6 => 8, _ => 0 };
                    break;
                case 5:
                    r = Roll(7) switch { 1 => 5, 2 => 7, 3 => 2, 4 => 3, 5 => 4, 6 => 8, 7 => 9, _ => 0 };
                    break;
            }
        }
        if (r != 0 && force)
        {
            r = 5;
        }
        if (battlefield == 0 && r == 9)
        {
            r = 8;
        }
        return r;
    }

    /// <summary>Puts object v on cell (x, y) of room r: furniture tile v with a random facing; the cell becomes occupied.</summary>
    // orig: R007+0x3F8A (15AF:3F8A) draft
    // C: v is a long; objects 3 and 4 (a bench) face north or south, by which end of the room the cell is nearer.
    private void PlaceObject(int l, int r, int x, int y, int v)
    {
        if (v >= 3 && v <= 4)
        {
            if (y - Room(l, r, Y0) < 2)
            {
                v = 4;
            }
            else if (Room(l, r, Y1) - y < 2)
            {
                v = 3;
            }
        }
        SetHeight(x, y, Height(x, y) & 0xF0);
        SetCellByte(x, y, 0, CellByte(x, y, 0) & 0x0F);
        var bits = CellBits(x, y);
        bits |= (uint)(v & 15) << 4;
        bits |= (uint)((Roll(4) - 1) & 3) << 4 << 24;
        SetCell(x, y, bits);
        var cell = CellIndex(l, x, y);
        if (_w.Kind(cell) > 1 && _w.Kind(cell) < 9)
        {
            _w.SetKind(cell, 11);
        }
    }

    /// <summary>A chest with contents at (x, y): a record at +0x300E (0x2C bytes) with up to four item stacks.</summary>
    // orig: R007+0x4076 (15AF:4076) draft
    // C: the record's item count (+0x0B) is never cleared: it keeps what an earlier level's chest left there (the floor buffer is reused).
    private void AddChest(int l, int x, int y, ref int chests)
    {
        if ((sbyte)Img[ChestCountAt] >= MaxChests)
        {
            return;
        }
        var at = ChestsAt + ChestSize * (sbyte)Img[ChestCountAt];
        Img[ChestCountAt]++;
        Img[at + 0x0C] = (byte)chests;
        chests++;
        PutWord(at, x * 32 - 16);
        PutWord(at + 2, y * 16 - 8);
        Img[at + 4] = 0x40;
        Img[at + 5] = 0x20;
        Img[at + 6] = (byte)x;
        Img[at + 7] = (byte)y;
        for (var i = x - 1; i <= x + 1; i++)
        {
            for (var j = y - 1; j <= y + 1; j++)
            {
                var cell = CellIndex(l, i, j);
                if (_w.Mark(cell) == 0)
                {
                    _w.SetMark(cell, 12);
                }
            }
        }
        var w = Room(GroundLevel, 0, X1) - Room(GroundLevel, 0, X0) + 1;
        var h = Room(GroundLevel, 0, Y1) - Room(GroundLevel, 0, Y0) + 1;
        if (w < h)
        {
            w = h;
        }
        Img[at + 8] = (byte)((w + 5) << 1);
        Img[at + 8] |= 0x80;
        PutWord(at + 0x0E, Roll(3) - 1);
        PutWord(at + 0x10, Roll(6) - 1);
        var perm = new int[17];
        for (var i = 0; i < 16; i++)
        {
            perm[i] = i;
        }
        for (var n = 16; n > 0; n--)
        {
            if (Img[at + 0x0B] >= 4)
            {
                return;
            }
            var r = Roll(n);
            if (r != n)
            {
                // DORMANT: perm is never read; the switch below uses r itself (as the compiled code does).
                (perm[r], perm[n]) = (perm[n], perm[r]);
            }
            switch (r)
            {
                case 1: AddChestItem(5, at, 2, 4); break;
                case 2: AddChestItem(5, at, 3, 5); break;
                case 3: AddChestItem(5, at, 7, 0); break;
                case 4: AddChestItem(5, at, 14, 14); break;
                case 5: AddChestItem(4, at, 0x45, 0x26); break;
                case 6: AddChestItem(4, at, 0x44, 0x25); break;
                case 7: AddChestItem(6, at, 0x47, 0x28); break;
                case 8: AddChestItem(6, at, 0x46, 0x27); break;
                case 9: AddChestItem(8, at, 0x48, 0x29); break;
                case 10: AddChestItem(8, at, 0x4A, 0x2B); break;
                case 11: AddChestItem(4, at, 0x4E, 0x2F); break;
                case 12: AddChestItem(4, at, 0x4D, 0x2E); break;
                case 13: AddChestItem(6, at, 0x50, 0x31); break;
                case 14: AddChestItem(6, at, 0x4F, 0x30); break;
                case 15: AddChestItem(8, at, 0x51, 0x32); break;
                case 16: AddChestItem(8, at, 0x53, 0x34); break;
            }
        }
    }

    /// <summary>Adds an item stack to a chest record: always the first, otherwise with a 1 in <paramref name="chance"/> chance; its quality may rise and its count grow.</summary>
    // orig: R007+0x42DC (15AF:42DC) draft
    // C: item records are 6 bytes at +0x14 of the chest: code (word), kind, quality, count, weight (the weight comes from DARKLAND.LST).
    private void AddChestItem(int chance, int chest, int itemKind, int itemCode)
    {
        if (Img[chest + 0x0B] < 4 && (Img[chest + 0x0B] == 0 || Roll(chance) == 1))
        {
            Img[chest + 8]++;
            var e = chest + 0x14 + 6 * Img[chest + 0x0B];
            Img[chest + 0x0B]++;
            PutWord(e, itemCode);
            Img[e + 2] = (byte)itemKind;
            Img[e + 3] = 0x23;
            for (var i = 0; i < 3; i++)
            {
                if (Roll(8) != 1)
                {
                    break;
                }
                Img[e + 3] += 10;
                Img[chest + 8]++;
            }
            Img[e + 4] = 1;
            for (var i = 0; i < 3; i++)
            {
                if (Roll(chance) != 1)
                {
                    break;
                }
                Img[e + 4]++;
                Img[chest + 8]++;
            }
            Img[e + 5] = (byte)_g.Context.Items[itemCode].Weight;
        }
    }
}
