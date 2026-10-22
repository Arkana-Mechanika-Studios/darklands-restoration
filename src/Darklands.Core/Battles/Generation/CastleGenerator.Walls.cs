namespace Darklands.Core.Battles.Generation;

// Walls, doors between the parts of a level, the gate and the stair exits in the floor image (overlay R007).
internal sealed partial class CastleGenerator
{
    private const int PartListsAt = 0x248A;
    private const int CellListAt = 0x24C6;
    private const int CellListCapacity = 0x578;
    private const int PassagesAt = 0x1F54;
    private const int ExitCountAt = 0x2FB6;
    private const int ExitsAt = 0x2FB8;
    private const int RectangleCountAt = 0x3326;
    private const int RectanglesAt = 0x3328;

    /// <summary>The wall pieces chosen for one cell: a wall type for each side (0 none, 1 wall, 2 outer wall, 3 a variant), decorations and an object.</summary>
    private record struct WallPieces(int North, int South, int East, int West, int NorthDecoration, int WestDecoration, int Object);

    /// <summary>Lists the cells of each connected part of the level and clears the cells of all rooms.</summary>
    // orig: R007+0x051E (15AF:051E) draft
    // C: the list holds (x << 8) + y per cell and −1 after each part; it stops when the 0x578 words are nearly full.
    private void WritePartLists(int l)
    {
        Img[0x2488] = (byte)_partCount[l];
        var k = 0;
        for (var c = 0; c < _partCount[l]; c++)
        {
            if (k + 1 >= CellListCapacity)
            {
                break;
            }
            PutWord(PartListsAt + 2 * c, k * 2);
            for (var i = 0; i < _roomCount[l]; i++)
            {
                if (k + 1 >= CellListCapacity)
                {
                    break;
                }
                if (PartOf(l, i) == c)
                {
                    for (var x = Room(l, i, X0); x <= Room(l, i, X1); x++)
                    {
                        for (var y = Room(l, i, Y0); y <= Room(l, i, Y1); y++)
                        {
                            PutWord(CellListAt + 2 * k, ((byte)x << 8) + y);
                            k++;
                            SetCell(x, y, 0);
                        }
                    }
                }
            }
            PutWord(CellListAt + 2 * k, -1);
            k++;
        }
    }

    // orig: the stores of ints into floor_img (F.comp, F.list, door and rectangle records)
    private void PutWord(int at, int value)
    {
        Img[at] = (byte)value;
        Img[at + 1] = (byte)(value >> 8);
    }

    /// <summary>Opens the walls beside each stair, then gives every room cell its wall pieces (and objects).</summary>
    // orig: R007+0x0698 (15AF:0698) draft
    private void OpenExitsAndDecorateWalls(int battlefield, int l)
    {
        _ = battlefield;
        for (var i = 0; i < _stairCount[l]; i++)
        {
            var x = _w.StairWord(l, i, 0);
            var y = _w.StairWord(l, i, 1);
            var room = _w.StairWord(l, i, 2);
            var side = _w.StairWord(l, i, 5);
            if (side == 0)
            {
                var r = RoomAt(l, x - 1, x - 1, y, y);
                var idx = CellIndex(l, x, y);
                var v = _w.Kind(idx - 40);
                if (v > 1 && v < 9 && IsLinked(l, r, room))
                {
                    _w.SetWalls(idx, _w.Walls(idx) & ~8);
                }
                r = RoomAt(l, x + 1, x + 1, y, y);
                v = _w.Kind(idx + 40);
                if (v > 1 && v < 9 && IsLinked(l, r, room))
                {
                    _w.SetWalls(idx, _w.Walls(idx) & ~2);
                }
            }
            else if (side == 3)
            {
                var r = RoomAt(l, x, x, y + 1, y + 1);
                var idx = CellIndex(l, x, y);
                var v = _w.Kind(idx + 1);
                if (v > 1 && v < 9 && IsLinked(l, r, room))
                {
                    _w.SetWalls(idx, _w.Walls(idx) & ~4);
                }
                r = RoomAt(l, x, x, y - 1, y - 1);
                v = _w.Kind(idx - 1);
                if (v > 1 && v < 9 && IsLinked(l, r, room))
                {
                    _w.SetWalls(idx, _w.Walls(idx) & ~1);
                }
            }
        }
        for (var r = 0; r < _roomCount[l]; r++)
        {
            var o0 = Room(l, r, X0);
            var o1 = Room(l, r, X1);
            var o2 = Room(l, r, Y0);
            var o3 = Room(l, r, Y1);
            for (var x = o0; x <= o1; x++)
            {
                for (var y = o2; y <= o3; y++)
                {
                    SetHeight(x, y, Height(x, y) | 0x10);
                    var pieces = ChoosePieces(l, r, x, y);
                    var bits = CellBits(x, y);
                    if (pieces.North != 0)
                    {
                        bits |= (uint)(pieces.North & 15) << 16;
                    }
                    if (pieces.East != 0)
                    {
                        bits |= (uint)(pieces.East & 15);
                    }
                    if (pieces.South != 0)
                    {
                        bits |= (uint)(pieces.South & 15) << 24;
                    }
                    if (pieces.West != 0)
                    {
                        bits |= (uint)(pieces.West & 15) << 8;
                    }
                    if (pieces.NorthDecoration != 0)
                    {
                        bits |= (uint)(pieces.NorthDecoration & 15) << 20;
                    }
                    if (pieces.WestDecoration != 0)
                    {
                        bits |= (uint)(pieces.WestDecoration & 15) << 12;
                    }
                    SetCell(x, y, bits);
                    if (pieces.Object != 0)
                    {
                        PlaceObject(l, r, x, y, pieces.Object);
                    }
                }
            }
            if (RoomKind(l, r) == KindCorridor)
            {
                if (o0 < o1)
                {
                    // BUG: a corridor's decoration spot can fall past its end: it is counted from the far end (o1, and
                    // o3 below) rather than from o0 (o2), so only a roll of 1 falls on the corridor; larger rolls test,
                    // and may mark, the cells past its end.
                    var x = o1 + Roll(o1 - o0 + 1) - 1;
                    var y = o2;
                    if ((_w.Walls(CellIndex(l, x, y)) & 1) != 0)
                    {
                        SetCellByte(x, y, 2, (CellByte(x, y, 2) & 0x0F) | 0x10);
                    }
                }
                else if (o2 < o3)
                {
                    var y = o3 + Roll(o3 - o2 + 1) - 1;
                    var x = o0;
                    if ((_w.Walls(CellIndex(l, x, y)) & 8) != 0)
                    {
                        SetCellByte(x, y, 1, (CellByte(x, y, 1) & 0x0F) | 0x10);
                    }
                }
            }
        }
    }

    /// <summary>The wall pieces of cell (x, y) of room r: walls on the sides the plan walled, decorations on the north and west walls, and an object, by room kind.</summary>
    // orig: R007+0x0B3E (15AF:0B3E) draft
    // C: the decoration roll is 1 in 4 (or so) per wall cell; the draws are made only when the earlier tests of the chain pass.
    private WallPieces ChoosePieces(int l, int r, int x, int y)
    {
        var t = RoomKind(l, r);
        var w = _w.Walls(CellIndex(l, x, y));
        var o2 = Room(l, r, Y0);
        int north = 0;
        int east = 0;
        int south = 0;
        int west = 0;
        int northDecoration = 0;
        int westDecoration = 0;
        int obj = 0;
        if ((w & 1) != 0)
        {
            north = 1;
        }
        if ((w & 2) != 0)
        {
            east = 1;
        }
        if ((w & 4) != 0)
        {
            south = 1;
        }
        if ((w & 8) != 0)
        {
            west = 1;
        }
        switch (t)
        {
            case 2:
                if (north != 0 && l > 1 && o2 <= _bounds[Y0] + 1 && Roll(6) == 1)
                {
                    northDecoration = 6;
                }
                else if (north != 0 && l > 1 && Roll(16) == 1)
                {
                    northDecoration = 1;
                }
                else if (north != 0 && Roll(4) == 1)
                {
                    switch (Roll(16))
                    {
                        case 1:
                        case 2:
                        case 3:
                        case 4:
                            northDecoration = 1;
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                            northDecoration = 3;
                            break;
                        case 9:
                        case 10:
                        case 11:
                        case 12:
                            northDecoration = 4;
                            break;
                        case 13:
                        case 14:
                            obj = 14;
                            break;
                        case 15:
                        case 16:
                            northDecoration = 5;
                            break;
                    }
                }
                if (west != 0 && north == 0 && Roll(4) == 1)
                {
                    switch (Roll(16))
                    {
                        case 1:
                        case 2:
                        case 3:
                        case 4:
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                        case 10:
                            westDecoration = 1;
                            break;
                        case 11:
                        case 12:
                            obj = 15;
                            break;
                        case 13:
                        case 14:
                        case 15:
                        case 16:
                            west = 3;
                            break;
                    }
                }
                break;
            case 3:
            case 5:
                if (l > 1 && north != 0 && Roll(4) == 1)
                {
                    if (o2 <= _bounds[Y0] + 1 && Roll(5) < 4)
                    {
                        northDecoration = 6;
                    }
                    else
                    {
                        northDecoration = 1;
                    }
                }
                if (west != 0 && north == 0 && Roll(4) == 1)
                {
                    westDecoration = 1;
                }
                break;
            case 4:
                if (l > 1 && north != 0 && o2 <= _bounds[Y0] + 1 && Roll(4) == 1)
                {
                    northDecoration = 6;
                }
                break;
            case 6:
                if (north != 0 && (_gateSide == 1 || _gateSide == 3) && (x + y) % 2 == 0)
                {
                    switch (Roll(4))
                    {
                        case 1:
                            northDecoration = 1;
                            break;
                        case 2:
                            northDecoration = 3;
                            break;
                        case 3:
                            northDecoration = 4;
                            break;
                        case 4:
                            northDecoration = 5;
                            break;
                    }
                }
                if (west != 0 && (_gateSide == 0 || _gateSide == 2) && (x + y) % 2 == 0)
                {
                    westDecoration = 1;
                }
                break;
            case 7:
                if (l > 1 && north != 0 && o2 <= _bounds[Y0] + 1 && Roll(8) == 1)
                {
                    northDecoration = 6;
                }
                else if (north != 0 && Roll(8) == 1)
                {
                    switch (Roll(4))
                    {
                        case 1:
                            northDecoration = 1;
                            break;
                        case 2:
                            northDecoration = 3;
                            break;
                        case 3:
                            northDecoration = 4;
                            break;
                        case 4:
                            northDecoration = 5;
                            break;
                    }
                }
                if (west != 0 && north == 0 && Roll(8) == 1)
                {
                    westDecoration = 1;
                }
                break;
            case 8:
                if (north != 0)
                {
                    var k = RoomAt(l, x, x, y - 1, y - 1);
                    if (k < 0 || RoomKind(l, k) == 8)
                    {
                        north = 2;
                    }
                }
                if (east != 0)
                {
                    var k = RoomAt(l, x + 1, x + 1, y, y);
                    if (k < 0 || RoomKind(l, k) == 8)
                    {
                        east = 2;
                    }
                }
                if (south != 0)
                {
                    var k = RoomAt(l, x, x, y + 1, y + 1);
                    if (k < 0 || RoomKind(l, k) == 8)
                    {
                        south = 2;
                    }
                }
                if (west != 0)
                {
                    var k = RoomAt(l, x - 1, x - 1, y, y);
                    if (k < 0 || RoomKind(l, k) == 8)
                    {
                        west = 2;
                    }
                }
                break;
        }
        if (l == 0)
        {
            if (northDecoration != 1)
            {
                northDecoration = 0;
            }
            if (westDecoration != 1)
            {
                westDecoration = 0;
            }
            obj = 0;
        }
        return new WallPieces(north, south, east, west, northDecoration, westDecoration, obj);
    }

    /// <summary>Level 1: marks the gate (two gate-post wall pieces at the ends of the gate path) and the rectangle around it.</summary>
    // orig: R007+0x10A6 (15AF:10A6) draft
    private void MarkGate(int l)
    {
        if (l == GroundLevel && _gateRoom >= 0 && RoomKind(GroundLevel, _gateRoom) == KindGatePath)
        {
            Img[RectangleCountAt] = 1;
            var y0 = Room(GroundLevel, _gateRoom, Y0);
            var y1 = Room(GroundLevel, _gateRoom, Y1);
            var x0 = Room(GroundLevel, _gateRoom, X0);
            var x1 = Room(GroundLevel, _gateRoom, X1);
            void Post(int px, int py, int k, int piece) => SetCellByte(px, py, k, (CellByte(px, py, k) & 0xF0) | piece);
            if (_gateSide == 0)
            {
                Post(x0, y0, 2, 8);
                Post(x1, y0, 2, 9);
                y1 = y0;
            }
            else if (_gateSide == 1)
            {
                Post(x1, y0, 0, 9);
                Post(x1, y1, 0, 8);
                x0 = x1;
            }
            else if (_gateSide == 2)
            {
                Post(x0, y1, 3, 8);
                Post(x1, y1, 3, 9);
                y0 = y1;
            }
            else if (_gateSide == 3)
            {
                Post(x0, y0, 1, 9);
                Post(x0, y1, 1, 8);
                x1 = x0;
            }
            PutWord(RectanglesAt, x0 * 32 + 1);
            PutWord(RectanglesAt + 4, (x1 - x0) * 32 + 31);
            PutWord(RectanglesAt + 2, y0 * 16 + 1);
            PutWord(RectanglesAt + 6, (y1 - y0) * 16 + 15);
        }
    }

    /// <summary>Passages through the wall between two different parts of a level (doors marked 3 and 6 in the plan): wall pieces 4 or 5 and a pair of door records.</summary>
    // orig: R007+0x1234 (15AF:1234) draft
    // C: the part numbers are read before the room tests, so a −1 room reads the entry before it; those read as −1 here.
    private void LinkParts(int l)
    {
        var n = Img[0x1F52];
        int kind;
        int size;
        if (l == 0)
        {
            kind = 5;
            var w = Room(GroundLevel, 0, X1) - Room(GroundLevel, 0, X0) + 1;
            var h = Room(GroundLevel, 0, Y1) - Room(GroundLevel, 0, Y0) + 1;
            if (w < h)
            {
                w = h;
            }
            size = (((w + 5) | 0x40) << 1) & 0xFF;
        }
        else
        {
            kind = 4;
            size = 0;
        }
        for (var x = _bounds[X0]; x <= _bounds[X1]; x++)
        {
            for (var y = _bounds[Y0]; y <= _bounds[Y1]; y++)
            {
                var mark = _w.Mark(CellIndex(l, x, y));
                if (mark == 3)
                {
                    var b = RoomAt(l, x, x, y, y);
                    var a = RoomAt(l, x, x, y - 1, y - 1);
                    var va = a >= 0 && a < _roomCount[l];
                    var vb = b >= 0 && b < _roomCount[l];
                    if (PartOfOrNone(l, a) != PartOfOrNone(l, b) && va && vb)
                    {
                        SetCellByte(x, y, 2, CellByte(x, y, 2) & 0xF0);
                        SetCellByte(x, y, 2, CellByte(x, y, 2) | kind);
                        SetCellByte(x, y - 1, 3, CellByte(x, y - 1, 3) & 0xF0);
                        SetCellByte(x, y - 1, 3, CellByte(x, y - 1, 3) | kind);
                        WritePassage(n, x * 32, y * 16, PartOf(l, a), size, x | 0x80, y, vb ? n + 1 : -1);
                        WritePassage(n + 1, x * 32, y * 16 - 16, PartOf(l, b), size, x, (y - 1) | 0x80, va ? n : -1);
                        n += 2;
                    }
                }
                else if (mark == 6)
                {
                    var b = RoomAt(l, x, x, y, y);
                    var a = RoomAt(l, x - 1, x - 1, y, y);
                    var va = a >= 0 && a < _roomCount[l];
                    var vb = b >= 0 && b < _roomCount[l];
                    if (PartOfOrNone(l, a) != PartOfOrNone(l, b) && va && vb)
                    {
                        SetCellByte(x, y, 1, CellByte(x, y, 1) & 0xF0);
                        SetCellByte(x, y, 1, CellByte(x, y, 1) | kind);
                        SetCellByte(x - 1, y, 0, CellByte(x - 1, y, 0) & 0xF0);
                        SetCellByte(x - 1, y, 0, CellByte(x - 1, y, 0) | kind);
                        WritePassage(n, x * 32, y * 16, PartOf(l, a), size, x, y | 0x40, vb ? n + 1 : -1);
                        WritePassage(n + 1, x * 32 - 32, y * 16, PartOf(l, b), size, (x - 1) | 0x40, y, va ? n : -1);
                        n += 2;
                    }
                }
            }
        }
        Img[0x1F52] = (byte)n;
    }

    /// <summary>The part of a room, or −1 for a room number outside the table.</summary>
    // orig: the reads of DS:DBBB[l][a] in R007+0x1234 (15AF:1234) draft
    // C: the original reads the table at a negative index (a neighbouring entry); the value only feeds a test ANDed with the room validity
    // tests, so the branch taken is the same.
    private int PartOfOrNone(int l, int room) => room is >= 0 and < CastleWork.MaxRooms ? PartOf(l, room) : -1;

    /// <summary>Writes a door record (14 bytes at +0x1F54): where, size 32 × 16, the part it leads out of, a size code, the cell, and its partner record.</summary>
    // orig: the stores through q in R007+0x1234 (15AF:1234) draft
    private void WritePassage(int n, int x, int y, int part, int size, int cellX, int cellY, int partner)
    {
        var at = PassagesAt + 14 * n;
        PutWord(at, x);
        PutWord(at + 2, y);
        Img[at + 4] = 0x20;
        Img[at + 5] = 0x10;
        Img[at + 6] = (byte)part;
        Img[at + 7] = (byte)size;
        Img[at + 8] = (byte)cellX;
        Img[at + 9] = (byte)cellY;
        Img[at + 10] = Img[at + 11] = 0;
        Img[at + 12] = (byte)partner;
    }

    /// <summary>Writes the exit records for the level's stairs (14 bytes each at +0x2FB8) and the stair pieces of their cells.</summary>
    // orig: R007+0x16BA (15AF:16BA) draft
    private void WriteStairExits(int l)
    {
        var n = (sbyte)Img[ExitCountAt];
        for (var i = 0; i < _stairCount[l]; i++, n++)
        {
            var at = ExitsAt + 14 * n;
            var link = _w.StairWord(l, i, 3);
            var kindUp = _w.StairWord(l, i, 4) == 1;
            if (kindUp)
            {
                Img[at + 6] = (byte)(_floor.LevelNumber + 1);
                Img[at + 7] = (byte)_partOf[(l + 1) * 30 + _w.StairWord(l + 1, link, 2)];
            }
            else
            {
                Img[at + 6] = (byte)(_floor.LevelNumber - 1);
                Img[at + 7] = (byte)_partOf[(l - 1) * 30 + _w.StairWord(l - 1, link, 2)];
            }
            Img[at + 13] = (byte)link;
            var x = _w.StairWord(l, i, 0);
            var y = _w.StairWord(l, i, 1);
            var side = _w.StairWord(l, i, 5);
            int piece;
            if (side == 0)
            {
                PutWord(at + 2, (y + 1) * 16);
                PutWord(at, x * 32);
                PutWord(at + 8, x * 32 + 16);
                PutWord(at + 10, (y + 1) * 16 + 0x18);
                Img[at + 12] = 0;
                SetCellByte(x, y, 2, CellByte(x, y, 2) & 0x0F);
                if (kindUp)
                {
                    piece = 12;
                }
                else
                {
                    piece = 10;
                    SetCellByte(x, y, 2, CellByte(x, y, 2) | 0x20);
                }
            }
            else if (side == 3)
            {
                PutWord(at + 2, y * 16);
                PutWord(at, (x + 1) * 32);
                PutWord(at + 8, (x + 1) * 32 + 0x30);
                PutWord(at + 10, y * 16 + 8);
                Img[at + 12] = 6;
                SetCellByte(x, y, 1, 0);
                SetCellByte(x, y, 1, CellByte(x, y, 1) | 1);
                if (kindUp)
                {
                    piece = 13;
                }
                else
                {
                    piece = 11;
                    SetCellByte(x, y, 1, CellByte(x, y, 1) | 0x20);
                }
            }
            else
            {
                // DORMANT: stairs are on side 0 or 3.
                break;
            }
            SetCellByte(x, y, 0, CellByte(x, y, 0) & 0x0F);
            SetCellByte(x, y, 0, CellByte(x, y, 0) | (piece << 4));
            Img[at + 4] = 0x20;
            Img[at + 5] = 0x10;
        }
        Img[ExitCountAt] = (byte)n;
    }
}
