namespace Darklands.Core.Battles.Generation;

/// <summary>
/// City battlefields (overlay R010): street maps (types 1..6), the open ground by the walls (7),
/// a two-level keep (8) and a castle (9), built from the prepared maps icity.* of IMAPS.CAT with
/// random tiles, the party at a start marker and the enemy groups at markers chosen by the
/// battlefield type.
/// </summary>
// orig: R010 1432 (18 functions; 9 byte-matched per the draft notes, the others "close");
// spec: scratchpad B_battlefield_city.md, which checked 2030, 2B42 and 26F8 against
public static class CityBattlefield
{
    /// <summary>Builds the battlefield; returns the number of levels and sets the start level.</summary>
    // orig: city-builder-entry R010+0x0000 (1432:0000) draft (byte-matched per the draft notes)
    public static int Build(GenerationState g, int battlefield, out int start, (int Type, int Quality, int Count)[] specs)
    {
        var type = BattleType(g, battlefield);
        WriteTileDescription(g, type);
        switch (type)
        {
            case 7:
                start = 0;
                return BuildOutdoor(g, battlefield, specs);
            case 8:
                start = 0;
                return BuildKeep(g, battlefield, specs);
            case 9:
                return BuildCastle(g, battlefield, out start, specs);
            default:
                start = 0;
                return BuildStreet(g, battlefield, type, specs);
        }
    }

    /// <summary>The city battle type (1..9) for a battlefield type; several types choose at random.</summary>
    // orig: city-fight-kind R010+0x0102 (1432:0102) draft (byte-matched per the draft notes)
    // C: each random arm returns for every roll result (no fall-through happens).
    public static int BattleType(GenerationState g, int battlefield) => battlefield switch
    {
        0x20 => g.Roll(2),
        0x13 or 0x14 or 0x1E => g.Roll(3),
        0x22 or 0x23 or 0x25 => g.Roll(3) switch { 1 => 1, 2 => 2, _ => 4 },
        0x11 or 0x12 => g.Roll(2) + 2,
        0x18 or 0x19 or 0x1A => g.Roll(3) + 1,
        0x1B or 0x24 or 0x26 => 5,
        0x15 or 0x16 => 7,
        0x1C or 0x1D or 0x29 or 0x2A or 0x6A => 9,
        0x1F or 0x21 or 0x2C or 0x2D or 0x6B => 8,
        0x2B => 6,
        _ => g.Roll(4),
    };

    /// <summary>The tile description for a city battle type.</summary>
    // orig: R010+0x0292 (1432:0292) draft; R010+0x02CC (city, byte-matched per the draft notes);
    // R010+0x058C (keep); R010+0x079A (outdoor)
    private static void WriteTileDescription(GenerationState g, int type)
    {
        var d = g.Terrain;
        if (type is >= 1 and <= 6)
        {
            d.BackgroundPalette = 3;
            d.SetSetName(TileKind.Floor, "city");
            var floor = type switch
            {
                1 or 5 or 6 => g.Roll(2) == 1 ? 0 : 6,
                2 or 3 => (g.Roll(5) - 1) * 2,
                _ => g.Roll(3) switch { 1 => 2, 2 => 4, _ => 8 },
            };
            d.SetTile(TileKind.Floor, 1, floor);
            d.SetTile(TileKind.Floor, 2, floor + 1);
            d.SetSetName(TileKind.FloorDecoration, "city");
            SetTiles(d, TileKind.FloorDecoration, 1, 0, 16, 17, 18, 19);
            var walls = type switch
            {
                >= 1 and <= 4 => g.Roll(5) - 1,
                5 => g.Roll(2) + 2,
                _ => 5,
            };
            d.SetSetName(TileKind.NorthWall, "citynw1" + walls);
            d.SetSetName(TileKind.WestWall, "cityww1" + walls);
            switch (walls)
            {
                case 1:
                    SetTiles(d, TileKind.NorthWall, 1, 2, 1, 4, 0, 3);
                    break;
                case 3:
                    SetTiles(d, TileKind.NorthWall, 1, 4, 2, 1, 3, 0);
                    break;
                case 4:
                    SetTiles(d, TileKind.NorthWall, 1, 1, 4, 2, 3, 0);
                    break;
                default:
                    SetTiles(d, TileKind.NorthWall, 1, 2, 0, 1, 3, 4);
                    break;
            }
            if (walls == 5)
            {
                SetTiles(d, TileKind.NorthWall, 8, 6, 7);
            }
            d.SetTile(TileKind.NorthWall, 10, 5);
            for (var i = 1; i <= 10; i++)
            {
                // C: copies tiles 1..10 (6 and 7 stay −1 unless set).
                d.SetTile(TileKind.WestWall, i, d.Tile(TileKind.NorthWall, i));
            }
            d.SetSetName(TileKind.Furniture, "city");
            SetTiles(d, TileKind.Furniture, 1, 0, 1, 2, 3, 5, 4, 9);
            d.SetAnimatedBelow(TileKind.Furniture, 7);
        }
        else if (type == 7)
        {
            d.BackgroundPalette = 1;
            d.SetSetName(TileKind.Floor, "out");
            SetTiles(d, TileKind.Floor, 1, 0, 1, 2, 9);
            d.SetSetName(TileKind.FloorDecoration, "out");
            d.SetAnimatedBelow(TileKind.FloorDecoration, 2);
            SetTiles(d, TileKind.FloorDecoration, 9, 18, 19, 20, 21, 22, 23);
            d.SetSetName(TileKind.NorthWall, "out");
            d.SetTile(TileKind.NorthWall, 11, 3);
            d.SetSetName(TileKind.WestWall, "out");
            d.SetTile(TileKind.WestWall, 11, 3);
            d.SetSetName(TileKind.Furniture, "outgrpa");
            SetTiles(d, TileKind.Furniture, 1, 7, 8, 9);
            d.SetAnimatedBelow(TileKind.Furniture, 4);
            SetTiles(d, TileKind.Furniture, 4, 30, 31, 32, 33);
        }
        else
        {
            var castle = type == 9;
            d.BackgroundPalette = 0;
            d.SetSetName(TileKind.Floor, "keep");
            d.SetTile(TileKind.Floor, 1, castle ? 1 : 0);
            d.SetSetName(TileKind.FloorDecoration, "keep");
            d.SetAnimatedBelow(TileKind.FloorDecoration, 2);
            SetTiles(d, TileKind.FloorDecoration, 6, 10, 11, 9);
            d.SetSetName(TileKind.NorthWall, "keep1");
            if (castle)
            {
                d.SetTile(TileKind.NorthWall, 11, 0);
                SetTiles(d, TileKind.NorthWall, 7, 6, 8, 9);
            }
            else
            {
                d.SetTile(TileKind.NorthWall, 11, 1);
            }
            d.SetTile(TileKind.NorthWall, 6, 2);
            d.SetSetName(TileKind.NorthWallDecoration, "keep");
            if (castle)
            {
                d.SetTile(TileKind.NorthWallDecoration, 1, 0);
                d.SetAnimatedBelow(TileKind.NorthWallDecoration, 2);
                SetTiles(d, TileKind.NorthWallDecoration, 2, 8, 14, 10);
            }
            d.SetSetName(TileKind.WestWall, "keep1");
            if (castle)
            {
                d.SetTile(TileKind.WestWall, 11, 0);
                d.SetTile(TileKind.WestWall, 7, 6);
            }
            else
            {
                d.SetTile(TileKind.WestWall, 11, 1);
            }
            d.SetTile(TileKind.WestWall, 6, 5);
            d.SetSetName(TileKind.WestWallDecoration, "keep");
            if (castle)
            {
                d.SetTile(TileKind.WestWallDecoration, 1, 0);
                d.SetAnimatedBelow(TileKind.WestWallDecoration, 2);
            }
            d.SetSetName(TileKind.Furniture, "keep");
            SetTiles(d, TileKind.Furniture, 1, 5, 3, 11, 2, 0, 1, 8, 9, 17, 16, 4, 10, 12);
            d.SetAnimatedBelow(TileKind.Furniture, 10);
        }
    }

    private static void SetTiles(TerrainDescription d, TileKind kind, int first, params int[] tiles)
    {
        for (var i = 0; i < tiles.Length; i++)
        {
            d.SetTile(kind, first + i, tiles[i]);
        }
    }

    /// <summary>Street maps (types 1..6).</summary>
    // orig: R010+0x08AA (1432:08AA) draft (byte-matched per the draft notes)
    private static int BuildStreet(GenerationState g, int bf, int type, (int Type, int Quality, int Count)[] s)
    {
        int[] variants = [0, 4, 2, 2, 3, 2, 1, 1, 1, 1];
        var mapNumber = g.Roll(variants[type]) - 1 + (type - 1) * 100;
        var level = g.Context.Maps.Load($"icity.{mapNumber:000}");
        var turn = type == 6 ? 0 : g.Roll(8) - 1;
        MapTurn.Apply(level, turn, turnExitTargets: true);
        level.LevelNumber = 0;
        VaryStreetCells(g, level, type);
        var sideA = g.Roll(2) == 1;
        int startId;
        switch (bf)
        {
            case 0x1B or 0x2B:
                startId = 0x15;
                break;
            case 0x24 or 0x26:
                startId = 0x16;
                break;
            case 0x1E:
                // C: the 0x1E arm jumps to the 0x17 label or falls into the 0x24/0x26 arm (0x16).
                startId = sideA ? (mapNumber is 1 or 0xC9 ? 0x17 : 0x16) : (mapNumber != 0xC8 ? 0x17 : 0x16);
                break;
            default:
                startId = sideA ? 0x16 : 0x17;
                break;
        }
        var startMarker = PickMarker(g, level, startId, out _);
        var o = level.Object(startMarker);
        level.PartyStart = o.Centre;
        level.PartyFacing = 0;
        level.PartyRoom = (sbyte)o.Room;
        var (ft1, fq1, fn1) = s[0];
        var (ft2, fq2, fn2) = s[1];
        var (ft3, fq3, fn3) = s[2];
        var reinforcementPool = 0;
        if (bf == 0x20)
        {
            reinforcementPool = fq3;
            fq3 = fq2 - 1;
        }
        var (m1, m2, m3) = MapKinds(g, 0, s[0], s[1], (ft3, fq3, fn3));
        var nearFlag = bf is 0x11 or 0x13 or 0x15 or 0x18 or 0x1A or 0x56;
        var sideB = g.Roll(2) == 1;
        int a, b, c, r;
        switch (bf)
        {
            case 0x1B:
                a = PickMarker(g, level, 2, out _);
                b = PickMarker(g, level, 3, out _);
                c = PickMarker(g, level, 4, out _);
                r = PickMarker(g, level, 5, out _);
                AddGroup(g, level, 0, 0, a, -1, 0, 0, true, 1, -1, 0, m2, 1, -1, 0);
                AddGroup(g, level, 0, 0, b, -1, 0, 0, false, 1, -1, 0, m1, 1, -1, 0);
                AddGroup(g, level, 0, 0, c, r, 2, 8, false, 1, -1, 0, m1, fn1 - 1, m3, fn3);
                break;
            case 0x1E:
                var id = (sideA, mapNumber) switch
                {
                    (true, 0) => 4, (true, 1) => 2, (true, 2) => 10, (true, 3) => 8, (true, 100) => 6, (true, 101) => 5,
                    (true, 200) or (true, 201) => 10,
                    (false, 0) => 9, (false, 1) => 2, (false, 2) => 10, (false, 3) => 3, (false, 100) => 2, (false, 101) => 8,
                    (false, 200) or (false, 201) => 10,
                    // DORMANT: no other map number can occur for 0x1E (types 1..3); the original leaves the marker at −1, which
                    // pick_marker treats as an id with no marker (object 0).
                    _ => -1,
                };
                AddGroup(g, level, 0, 0, PickMarker(g, level, id, out _), -1, 0, 0, true, 1, m1, fn1, m2, fn2, m3, fn3);
                break;
            case 0x20:
                int reinforcementId;
                if (sideA)
                {
                    reinforcementId = 11;
                    (a, b) = mapNumber switch { 2 => (4, 10), 3 => (5, 9), _ => (4, 5) };
                }
                else
                {
                    reinforcementId = 12;
                    (a, b) = mapNumber switch { 0 => (8, 9), 3 => (3, 9), 100 => (2, 8), 101 => (5, 8), _ => (8, 10) };
                }
                a = PickMarker(g, level, a, out _);
                b = PickMarker(g, level, b, out _);
                r = PickMarker(g, level, reinforcementId, out _);
                AddGroup(g, level, 0, 0, b, -1, 0, 0, false, 1, m1, fn1 / 2, m2, fn2 / 2, m3, fn3 / 2);
                AddGroup(g, level, 0, 0, a, r, 4, reinforcementPool, true, 1, m1, fn1 - fn1 / 2, m2, fn2 - fn2 / 2, m3, fn3 - fn3 / 2);
                break;
            case 0x22:
                a = PickMarker(g, level, DirectionCode(sideA, sideB, nearFlag), out _);
                r = PickMarker(g, level, sideA ? 11 : 12, out _);
                AddGroup(g, level, 0, 0, a, r, 4, 8, true, 1, m1, fn1, m2, fn2, m3, fn3);
                break;
            case 0x23 or 0x25:
                (a, r) = ReinforcedSpot(sideA, mapNumber);
                a = PickMarker(g, level, a, out _);
                r = PickMarker(g, level, r, out _);
                if (bf == 0x23)
                {
                    // C: m3 is mapped above but not placed.
                    AddGroup(g, level, 0, 0, a, r, 3, fq3, true, 1, m1, fn1, m2, fn2, -1, 0);
                }
                else
                {
                    AddGroup(g, level, 0, 0, a, r, 4, 8, true, 1, m1, fn1, m2, fn2, m3, fn3);
                }
                break;
            case 0x24:
                a = PickMarker(g, level, 6, out _);
                r = PickMarker(g, level, 7, out _);
                AddGroup(g, level, 0, 0, a, r, 3, fq3, true, 1, m1, fn1, m2, fn2, -1, 0);
                break;
            case 0x26:
                a = PickMarker(g, level, mapNumber == 400 ? 6 : 4, out _);
                r = PickMarker(g, level, 5, out _);
                AddGroup(g, level, 0, 0, a, r, 4, 12, true, 1, m1, fn1, m2, fn2, m3, fn3);
                break;
            case 0x2B:
                AddGroup(g, level, 0, 0, PickMarker(g, level, 2, out _), -1, 0, 0, true, 1, m1, fn1, m2, fn2, m3, fn3);
                break;
            default:
                a = PickMarker(g, level, DirectionCode(sideA, sideB, nearFlag), out _);
                AddGroup(g, level, 0, 0, a, -1, 0, 0, true, 1, m1, fn1, m2, fn2, m3, fn3);
                break;
        }
        if (bf == 0x1A)
        {
            g.Timed = true;
        }
        level.ObjectCount = 0;
        // orig: level-write R010+0x2DE0 (1432:2DE0) draft (fn matched): buffer k is written
        //   as "level%d.flr" of its level number through spill-file-store (R004+0x4BCA); g.Save keeps it in memory under that number.
        // DORMANT: a buffer outside 0..1 is not saved; every caller (08aa, 1516, 1b22, 2526) passes 0 or 1.
        g.Save(level);
        return 1;
    }

    /// <summary>The enemy and reinforcement markers of battlefield types 0x23 and 0x25.</summary>
    private static (int Enemy, int Reinforcement) ReinforcedSpot(bool sideA, int mapNumber)
    {
        if (sideA)
        {
            return mapNumber switch
            {
                0 => (7, 13),
                // C: case 2 sets the enemy marker 4 and falls through into case 3 (reinforcements at 11).
                2 => (4, 11),
                3 => (5, 11),
                100 => (4, 13),
                302 => (10, 13),
                _ => (5, 13),
            };
        }
        return mapNumber switch
        {
            0 => (3, 14),
            1 => (2, 14),
            100 => (9, 14),
            101 => (5, 14),
            302 => (9, 14),
            _ => (10, 14),
        };
    }

    /// <summary>The three enemy kinds on a level (−1 for a missing spec), mapped in order.</summary>
    private static (int, int, int) MapKinds(GenerationState g, int level, (int Type, int Quality, int Count) s1,
        (int Type, int Quality, int Count) s2, (int Type, int Quality, int Count) s3)
    {
        int Map((int Type, int Quality, int Count) s) => s.Type >= 0 && s.Count > 0 ? g.Forces.AddKind(level, s.Type, s.Quality) : -1;
        var m1 = Map(s1);
        var m2 = Map(s2);
        var m3 = Map(s3);
        return (m1, m2, m3);
    }

    /// <summary>The enemy marker from three choices.</summary>
    // orig: R010+0x114E (1432:114E) draft (byte-matched per the draft notes)
    public static int DirectionCode(bool a, bool b, bool c) => (a, b, c) switch
    {
        (true, true, true) => 2,
        (true, true, false) => 4,
        (true, false, true) => 3,
        (true, false, false) => 5,
        (false, true, true) => 6,
        (false, true, false) => 8,
        (false, false, true) => 7,
        _ => 9,
    };

    /// <summary>Random variation of a street map's floors, walls and furniture.</summary>
    // orig: R010+0x11AA (1432:11AA) draft ("close"; agrees with the old engine for type 6)
    private static void VaryStreetCells(GenerationState g, BattlefieldLevel level, int type)
    {
        var percent = type == 4 ? 15 : type is 1 or 5 or 6 ? 5 : 10;
        for (var x = 1; x <= 38; x++)
        {
            for (var y = 1; y <= 38; y++)
            {
                var v = level[x, y].Bits;
                if ((v & 0x8000_0000) != 0)
                {
                    continue;
                }
                var cell = v;
                if ((level.FloorByte(x, y) & 0xF0) != 0)
                {
                    var high = g.Roll(2);
                    var low = g.Roll(100) <= percent ? g.Roll(5) : 0;
                    level.SetFloorByte(x, y, (byte)(high << 4 | low));
                }
                foreach (var shift in new[] { 0, 8, 16, 24 })
                {
                    var wall = (int)(v >> shift) & 0xF;
                    if (wall == 0)
                    {
                        continue;
                    }
                    if (shift is 0 or 24)
                    {
                        wall = 10;
                    }
                    else if (type == 6)
                    {
                        wall = wall switch { 3 => 4, 4 => 8, 5 => 9, _ => g.Roll(3) };
                    }
                    else if (wall == 3)
                    {
                        wall = 4;
                    }
                    else
                    {
                        wall = g.Roll(5);
                    }
                    cell = (cell & ~(0xFu << shift)) | (uint)wall << shift;
                }
                var furniture = (int)(v >> 4) & 0xF;
                if (furniture != 0)
                {
                    cell &= ~0xF0u;
                    if ((v & 0x0F00_000F) == 0 && g.Roll(3) > 1)
                    {
                        furniture = furniture switch { 11 => g.Roll(6), 12 => 7, _ => furniture };
                        cell |= (uint)furniture << 4;
                        cell |= (uint)(g.Roll(4) - 1) << 28;
                    }
                }
                level[x, y] = new BattlefieldCell(cell);
            }
        }
    }

    /// <summary>The open ground by the walls (type 7, battlefield types 0x15, 0x16).</summary>
    // orig: R010+0x2526 (1432:2526) draft ("close")
    private static int BuildOutdoor(GenerationState g, int bf, (int Type, int Quality, int Count)[] s)
    {
        var level = g.Context.Maps.Load("icity.600");
        MapTurn.Apply(level, g.Roll(8) - 1, turnExitTargets: true);
        level.LevelNumber = 0;
        VaryOutdoorCells(g, level);
        var sideA = g.Roll(2) == 1;
        var o = level.Object(PickMarker(g, level, sideA ? 0x16 : 0x17, out _));
        level.PartyStart = o.Centre;
        level.PartyFacing = 0;
        level.PartyRoom = (sbyte)o.Room;
        var (m1, m2, m3) = MapKinds(g, 0, s[0], s[1], s[2]);
        var nearFlag = bf == 0x15;
        var sideB = g.Roll(2) == 1;
        AddGroup(g, level, 0, 0, PickMarker(g, level, DirectionCode(sideA, sideB, nearFlag), out _), -1, 0, 0, true, 1,
            m1, s[0].Count, m2, s[1].Count, m3, s[2].Count);
        level.ObjectCount = 0;
        g.Save(level);
        return 1;
    }

    /// <summary>Trees, bushes and raised edges of the outdoor map.</summary>
    // orig: R010+0x26F8 (1432:26F8) draft ("close"; indexes [x][y] checked against the asm)
    private static void VaryOutdoorCells(GenerationState g, BattlefieldLevel level)
    {
        for (var x = 1; x <= 38; x++)
        {
            for (var y = 1; y <= 38; y++)
            {
                var v = level[x, y].Bits;
                if ((v & 0x8000_0000) != 0)
                {
                    continue;
                }
                var cell = v;
                var furniture = (int)(v >> 4) & 0xF;
                if (furniture != 0)
                {
                    cell &= ~0xF0u;
                    furniture = furniture switch
                    {
                        11 => g.Roll(100) > 25 ? g.Roll(3) : 0,
                        12 => g.Roll(100) > 25 ? g.Roll(3) + 4 : 0,
                        13 => 4,
                        _ => 0,
                    };
                    if (furniture != 0)
                    {
                        cell |= (uint)furniture << 4;
                        cell |= (uint)(g.Roll(4) - 1) << 28;
                    }
                }
                if ((level.FloorByte(x, y) & 0xF0) != 0)
                {
                    var high = g.Roll(3);
                    var low = 0;
                    if (furniture == 0 && g.Roll(100) <= 33)
                    {
                        low = g.Roll(5) + 9;
                    }
                    level.SetFloorByte(x, y, (byte)(high << 4 | low));
                }
                foreach (var shift in new[] { 0, 8, 16, 24 })
                {
                    if (((v >> shift) & 0xF) != 0)
                    {
                        cell = (cell & ~(0xFu << shift)) | 11u << shift;
                    }
                }
                level[x, y] = new BattlefieldCell(cell);
            }
        }
        for (var i = 0; i < level.RectangleCount; i++)
        {
            var (rx, ry, rw, rh) = level.Rectangle(i);
            int x0 = rx / 32, y0 = ry / 16, w = rw / 32, h = rh / 16;
            for (var x = x0; x < x0 + w; x++)
            {
                for (var y = y0; y < y0 + h; y++)
                {
                    level.SetFloorByte(x, y, 0x40);
                }
            }
        }
    }

    /// <summary>The two-level keep (type 8, battlefield types 0x1F, 0x21, 0x2C, 0x2D, 0x6B).</summary>
    // orig: R010+0x1516 (1432:1516) draft ("close")
    private static int BuildKeep(GenerationState g, int bf, (int Type, int Quality, int Count)[] s)
    {
        var levels = new[] { g.Context.Maps.Load("icity.700"), g.Context.Maps.Load("icity.701") };
        levels[0].LevelNumber = 0;
        levels[1].LevelNumber = 1;
        VaryKeepCells(g, levels, 8, 0, 1);
        var inside = false;
        int startId, startLevel, rv = 0;
        switch (bf)
        {
            case 0x1F:
                (startId, startLevel, inside) = (3, 0, true);
                break;
            case 0x2C or 0x2D:
                rv = g.Roll(6) + 1;
                (startId, startLevel) = (rv, 1);
                break;
            default:
                (startId, startLevel) = (2, 0);
                break;
        }
        var startLevelMap = levels[startLevel];
        var o = startLevelMap.Object(PickMarker(g, startLevelMap, startId, out _));
        startLevelMap.PartyStart = o.Centre;
        startLevelMap.PartyFacing = 0;
        if (inside)
        {
            startLevelMap.PartyRoom = -1;
            foreach (var l in levels)
            {
                l.PartListCount = 0;
                for (var e = 0; e < l.ExitCount; e++)
                {
                    l.Exit(e)[7] = 0xFF;
                }
            }
        }
        else
        {
            startLevelMap.PartyRoom = (sbyte)o.Room;
        }
        var (ft1, fq1, fn1) = s[0];
        var (ft2, fq2, fn2) = s[1];
        var (ft3, fq3, fn3) = s[2];
        int g1 = -1, g2 = -1;
        switch (bf)
        {
            case 0x21:
                g.MarkPersistent();
                if ((g.Context.Memory.ClearedLevels & 1) == 0)
                {
                    var x = g.Forces.AddKind(0, 0x18, 1);
                    var y = g.Forces.AddKind(0, ft1, fq1);
                    var a = PickMarker(g, levels[0], 3, out _);
                    var b = PickMarker(g, levels[0], 4, out _);
                    AddGroup(g, levels[0], 0, 0, a, -1, 0, 0, true, 1, x, 1, y, fn1, -1, 0);
                    AddGroup(g, levels[0], 0, 0, b, -1, 0, 0, false, 1, y, 2, -1, 0, -1, 0);
                }
                if ((g.Context.Memory.ClearedLevels & 2) == 0)
                {
                    g1 = g.Forces.AddKind(1, 0x1B, 1);
                    var y = g.Forces.AddKind(1, ft2, fq2);
                    var x = g.Forces.AddKind(1, ft3, fq3);
                    var a = PickMarker(g, levels[1], 5, out _);
                    var b = PickMarker(g, levels[1], 6, out _);
                    AddGroup(g, levels[1], 1, 0, a, -1, 0, 0, false, 1, g1, 1, y, 2, x, 2);
                    AddGroup(g, levels[1], 1, 0, b, -1, 0, 0, false, 1, -1, 0, y, 2, x, 2);
                }
                break;
            case 0x2C or 0x2D:
                g.MarkPersistent();
                if ((g.Context.Memory.ClearedLevels & 1) == 0)
                {
                    g1 = g.Forces.AddKind(0, ft2, fq2);
                    var y = g.Forces.AddKind(0, ft3, fq3);
                    var x = PickMarker(g, levels[0], 2, out _);
                    var b = PickMarker(g, levels[0], 5, out _);
                    AddGroup(g, levels[0], 0, 0, x, -1, 0, 0, false, 1, g1, 1, y, 3, -1, 0);
                    AddGroup(g, levels[0], 0, 0, b, -1, 0, 0, false, 1, g1, 1, y, 3, -1, 0);
                }
                if ((g.Context.Memory.ClearedLevels & 2) == 0)
                {
                    g2 = g.Forces.AddKind(1, ft1, fq1);
                    // C: the result is discarded.
                    g.Forces.AddKind(1, ft2, fq2);
                    int a, y;
                    do
                    {
                        a = g.Roll(6) + 1;
                    }
                    while (a == rv);
                    do
                    {
                        y = g.Roll(6) + 1;
                    }
                    while (y == rv || y == a);
                    var x = PickMarker(g, levels[1], a, out _);
                    var y2 = PickMarker(g, levels[1], y, out _);
                    // C: g1 is the level-0 kind index (or −1): a slip in the original, kept.
                    AddGroup(g, levels[1], 1, 0, x, -1, 0, 0, true, 1, g1, 1, g2, 4, -1, 0);
                    AddGroup(g, levels[1], 1, 0, y2, -1, 0, 0, false, 1, g2, 2, -1, 0, -1, 0);
                }
                break;
            default:
                {
                    var (m1, m2, m3) = MapKinds(g, 0, s[0], s[1], s[2]);
                    AddGroup(g, levels[0], 0, 0, PickMarker(g, levels[0], bf == 0x1F ? 2 : 3, out _), -1, 0, 0, true, 1,
                        m1, fn1, m2, fn2, m3, fn3);
                    break;
                }
        }
        foreach (var l in levels)
        {
            l.ObjectCount = 0;
            g.Save(l);
        }
        // C: city-builder-entry reports start level 0 even when 0x2C/0x2D put the party on level 1: the caller stores 0 through the start-level pointer after
        //   R010+0x1516 returns (R010+0x00BC jumps to R010+0x006F `mov word ptr [bx],0`).
        return 2;
    }

    /// <summary>The castle (type 9, battlefield types 0x1C, 0x1D, 0x29, 0x2A, 0x6A): one or two levels.</summary>
    // orig: R010+0x1B22 (1432:1B22) draft ("close")
    private static int BuildCastle(GenerationState g, int bf, out int start, (int Type, int Quality, int Count)[] s)
    {
        var first = 0;
        var last = 1;
        var unlockStart = false;
        int startId;
        switch (bf)
        {
            case 0x2A:
                (startId, start, unlockStart) = (21, 1, true);
                break;
            case 0x1C:
                (startId, first, start, unlockStart) = (21, 1, 1, true);
                break;
            case 0x29:
                (startId, start) = (22, 0);
                break;
            default:
                (startId, last, start) = (21, 0, 0);
                break;
        }
        var levels = new BattlefieldLevel[2];
        for (var i = first; i <= last; i++)
        {
            levels[i] = g.Context.Maps.Load($"icity.80{i}");
            levels[i].LevelNumber = i - first;
        }
        VaryKeepCells(g, levels, 9, first, last);
        var startLevel = levels[start];
        var k = PickMarker(g, startLevel, startId, out var rank);
        var o = startLevel.Object(k);
        startLevel.PartyStart = o.Centre;
        startLevel.PartyFacing = 0;
        startLevel.PartyRoom = (sbyte)o.Room;
        if (unlockStart)
        {
            UnlockRoomDoors(startLevel, startLevel.PartyRoom);
        }
        var (ft1, fq1, fn1) = s[0];
        var (ft2, fq2, fn2) = s[1];
        var (ft3, fq3, fn3) = s[2];
        switch (bf)
        {
            case 0x1C:
                {
                    var (m1, m2, m3) = MapKinds(g, 0, s[0], s[1], s[2]);
                    AddGroup(g, levels[1], 1, 1, PickMarker(g, levels[1], 2, out _), -1, 0, 0, true, 4, m1, fn1, m2, fn2, m3, fn3);
                    break;
                }
            case 0x1D:
                {
                    var (m1, m2, m3) = MapKinds(g, 0, s[0], s[1], s[2]);
                    var (a, b) = rank switch { 1 => (2, 5), 2 => (4, 5), 3 => (2, 3), 4 => (3, 4), _ => (-1, -1) };
                    var spot = PickMarker(g, levels[0], a, out _);
                    var (h1, h2, h3) = (fn1 / 2, fn2 / 2, fn3 / 2);
                    AddGroup(g, levels[0], 0, 0, PickMarker(g, levels[0], b, out _), -1, 0, 0, false, 1, m1, h1, m2, h2, m3, h3);
                    AddGroup(g, levels[0], 0, 0, spot, -1, 0, 0, true, 1, m1, fn1 - h1, m2, fn2 - h2, m3, fn3 - h3);
                    break;
                }
            case 0x29 or 0x2A:
                g.MarkPersistent();
                if ((g.Context.Memory.ClearedLevels & 1) == 0)
                {
                    var sergeant = g.Forces.AddKind(0, 0x18, 1);
                    var k2 = g.Forces.AddKind(0, ft2, fq2);
                    var k3 = g.Forces.AddKind(0, ft3, fq3);
                    AddGroup(g, levels[0], 0, 0, PickMarker(g, levels[0], 6, out _), -1, 0, 0, true, 2, sergeant, 1, k2, fn2, k3, fn3);
                }
                if ((g.Context.Memory.ClearedLevels & 2) == 0)
                {
                    var lord = g.Forces.AddKind(1, 0x4B, 1);
                    MarkLord(g, lord);
                    // C: ft2 is mapped twice (with quality 5 and with its own quality): a slip in the original, kept.
                    var k2 = g.Forces.AddKind(1, ft2, 5);
                    var k3 = g.Forces.AddKind(1, ft2, fq2);
                    AddGroup(g, levels[1], 1, 0, PickMarker(g, levels[1], 3, out _), -1, 0, 0, true, 1, lord, 1, k2, fn2, k3, fn3);
                }
                break;
            default:
                {
                    var (m1, m2, m3) = MapKinds(g, 0, s[0], s[1], s[2]);
                    AddGroup(g, levels[0], 0, 0, PickMarker(g, levels[0], 6, out _), -1, 0, 0, true, 1, m1, fn1, m2, fn2, m3, fn3);
                    break;
                }
        }
        for (var i = first; i <= last; i++)
        {
            levels[i].ObjectCount = 0;
            g.Save(levels[i]);
        }
        start -= first;
        return last - first + 1;
    }

    /// <summary>The lord's kind gets byte +0x0C of its record set to 1.</summary>
    // orig: R010+0x1B22 (1432:1B22), the store at +0x1F8E, matched (`mov byte es:[bx+0A14h],1` with bx = kind*0xD6:
    // 0xA14 = 12*0xD6 + 0x0C, level 1's kind, byte +0x0C; the draft draft's x8c is wrong)
    private static void MarkLord(GenerationState g, int kind)
    {
        g.Forces.Kinds[1][kind].EndsBattleWhenKilled = 1;
    }

    /// <summary>Furniture, floor decorations and walls of the keep and castle maps; the two levels' exits linked.</summary>
    // orig: R010+0x2030 (1432:2030) draft ("close"; furniture table, the stale
    // furniture value and the decoration rolls checked against the asm)
    private static void VaryKeepCells(GenerationState g, BattlefieldLevel[] levels, int type, int first, int last)
    {
        var twoLevels = last > first;
        // C: origF keeps the furniture of the last earlier cell that had any (uninitialised before the first such cell).
        var lastFurniture = 0;
        for (var lv = first; lv <= last; lv++)
        {
            var level = levels[lv];
            for (var x = 1; x <= 38; x++)
            {
                for (var y = 1; y <= 38; y++)
                {
                    var v = level[x, y].Bits;
                    if ((v & 0x8000_0000) != 0)
                    {
                        continue;
                    }
                    var cell = v;
                    var furniture = (int)(v >> 4) & 0xF;
                    if (furniture != 0)
                    {
                        lastFurniture = furniture;
                        cell &= ~0xF0u;
                        furniture = furniture switch
                        {
                            1 => twoLevels ? 10 : 0,
                            2 => twoLevels ? 9 : 0,
                            3 => 7,
                            4 => 8,
                            5 or 6 => lv == 0 ? 1 : 2,
                            7 => 2,
                            8 => 3,
                            9 => 4,
                            10 => 11,
                            11 => 13,
                            12 => 12,
                            13 => 1,
                            _ => 0,
                        };
                        if (furniture != 0)
                        {
                            cell |= (uint)furniture << 4;
                            cell |= (uint)(g.Roll(4) - 1) << 28;
                        }
                    }
                    if (level.FloorByte(x, y) >> 4 != 0)
                    {
                        var low = (level.FloorByte(x, y) & 0xF) switch { 1 => 6, 2 => 7, 3 => 8, _ => 0 };
                        level.SetFloorByte(x, y, (byte)(0x10 | low));
                    }
                    foreach (var shift in new[] { 0, 8, 16, 24 })
                    {
                        var wall = (int)(v >> shift) & 0xF;
                        if (wall == 0)
                        {
                            continue;
                        }
                        wall = wall switch
                        {
                            2 => 11,
                            4 => type == 9 && shift >= 16 ? 8 : 6,
                            5 => type == 9 && shift >= 16 ? 9 : 6,
                            7 => 6,
                            8 => type == 9 ? 7 : 6,
                            _ => 0,
                        };
                        cell = (cell & ~(0xFu << shift)) | (uint)wall << shift;
                        if (type == 9 && shift is 8 or 16)
                        {
                            var decoration = 0;
                            if (shift == 8)
                            {
                                if (lv == 0 && g.Roll(100) < 5)
                                {
                                    decoration = 1;
                                }
                            }
                            else if (lastFurniture == 2)
                            {
                                decoration = 2;
                            }
                            else if (lv == 0 && g.Roll(100) < 5)
                            {
                                decoration = 1;
                            }
                            else if (lv == 1 && g.Roll(100) < 5)
                            {
                                decoration = 4;
                            }
                            cell = (cell & ~(0xFu << (shift + 4))) | (uint)decoration << (shift + 4);
                        }
                    }
                    level[x, y] = new BattlefieldCell(cell);
                }
            }
        }
        if (!twoLevels)
        {
            return;
        }
        var l0 = levels[0];
        var l1 = levels[1];
        for (var i = 0; i < l0.ExitCount; i++)
        {
            var e0 = l0.Exit(i);
            var e1 = l1.Exit(i);
            e0[6] = (byte)l1.LevelNumber;
            e1[6] = (byte)l0.LevelNumber;
            e0[0x0D] = e1[0x0D] = (byte)i;
            (e0[7], e1[7]) = (e1[7], e0[7]);
            for (var b = 8; b <= 0x0C; b++)
            {
                // Arrival x (+8), arrival y (+0x0A) and facing (+0x0C) are swapped.
                (e0[b], e1[b]) = (e1[b], e0[b]);
            }
        }
    }

    /// <summary>Unlocks the doors of a room (and their other halves).</summary>
    // orig: R010+0x1ABC (1432:1ABC) draft ("close")
    private static void UnlockRoomDoors(BattlefieldLevel level, int room)
    {
        for (var i = 0; i < level.DoorCount; i++)
        {
            var door = level.Door(i);
            if ((sbyte)door[6] == room)
            {
                door[7] = 0;
                level.Door((sbyte)door[0x0C])[7] = 0;
            }
        }
    }

    /// <summary>The object of the <paramref name="rank"/>-th group marker with an id (rank drawn when several); object 0 when there is none.</summary>
    // orig: R010+0x2A2C (1432:2A2C) draft ("close"; agrees with the old engine)
    // orig: R008+0x28A0 (1432:28A0) draft (the wilderness generator's copy, "close", asm re-read;
    // used by WildBattlefield; the original counts the objects as a signed byte, which makes no difference for maps of at most a few dozen)
    public static int PickMarker(GenerationState g, BattlefieldLevel level, int id, out int rank)
    {
        var count = 0;
        for (var i = 0; i < level.ObjectCount; i++)
        {
            if (IsMarker(level.Object(i), id))
            {
                count++;
            }
        }
        if (count == 0)
        {
            rank = 0;
            return 0;
        }
        rank = g.Roll(count);
        var seen = 0;
        var at = 0;
        for (; at < level.ObjectCount && rank > seen; at++)
        {
            if (IsMarker(level.Object(at), id))
            {
                seen++;
            }
        }
        return at - 1;
    }

    private static bool IsMarker(BattlefieldObject o, int id) => o.Type == BattlefieldObject.GroupMarker && o.Id == id;

    /// <summary>An enemy group at a marker, with up to three kinds; the second kind may have reinforcements at another marker.</summary>
    // orig: R010+0x2B42 (1432:2B42) draft ("close"; reinforcement index test and the signed /32, /16 checked against the asm)
    // DIVERGENCE: the original writes into the next group slot even past 10 (into the next level's groups); the C# list grows.
    private static void AddGroup(GenerationState g, BattlefieldLevel level, int levelIndex, int levelBase, int marker, int reinforcementMarker,
        int batch, int pool, bool faceParty, int spread, int k1, int n1, int k2, int n2, int k3, int n3)
    {
        if (k1 < 0 && k2 < 0 && k3 < 0)
        {
            return;
        }
        if (n1 <= 0 && n2 <= 0 && n3 <= 0)
        {
            return;
        }
        if (marker < 0 || marker >= BattlefieldLevel.MaxObjects)
        {
            return;
        }
        var group = new EnemyGroup();
        var o = level.Object(marker);
        group.X = (short)(o.X + 16);
        group.Y = (short)(o.Y + 8);
        group.Room = (sbyte)o.Room;
        group.Spread = (short)(spread << 4);
        group.HasReinforcements = 0;
        if (faceParty)
        {
            level.PartyFacing = GenerationState.DirectionBetween(level.PartyStart.X / 32, level.PartyStart.Y / 16, group.X / 32, group.Y / 16);
        }
        var j = 0;
        if (k1 is >= 0 and < 12 && n1 > 0)
        {
            group.Kinds[0] = (sbyte)k1;
            group.Counts[0] = (sbyte)n1;
            j = 1;
        }
        if (k2 is >= 0 and < 12 && (n2 > 0 || reinforcementMarker >= 0))
        {
            group.Kinds[j] = (sbyte)k2;
            group.Counts[j] = (sbyte)n2;
            j++;
            // C: the test compares the reinforcement OBJECT index with 12 (always true in the icity maps).
            if (reinforcementMarker is >= 0 and < 12)
            {
                var r = level.Object(reinforcementMarker);
                group.HasReinforcements = 1;
                group.ReinforcementKind = (sbyte)k2;
                group.Byte15 = 16;
                group.ReinforcementX = (short)(r.X + 16);
                group.ReinforcementY = (short)(r.Y + 8);
                group.ReinforcementBatch = (sbyte)batch;
                group.ReinforcementPool = (sbyte)pool;
                group.ReinforcementFacing = (sbyte)GenerationState.DirectionBetween(group.ReinforcementX / 32, group.ReinforcementY / 16,
                    level.PartyStart.X / 32, level.PartyStart.Y / 16);
            }
        }
        if (k3 is >= 0 and < 12 && n3 > 0)
        {
            group.Kinds[j] = (sbyte)k3;
            group.Counts[j] = (sbyte)n3;
            j++;
        }
        if (j > 0)
        {
            g.Forces.Groups[levelIndex - levelBase].Add(group);
        }
    }
}
