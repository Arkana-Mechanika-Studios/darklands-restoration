namespace Darklands.Core.Battles.Generation;

/// <summary>
/// The dragon cave (overlay R012, battlefield types 0x60, 0x64 and 0x65): one fixed map, "cavedrag",
/// laid out cell by cell (a ledge, a stepped slope and a scattering of random rocks), with the
/// dragon (0x60), a fire-breathing beast (0x64) or a pack of three kinds of monster (0x65) at the
/// mouth of the cave.
/// </summary>
// orig: R012 1432 (6 functions; the draft matched 5, the cell layout differs only in stack slots)
// C: the original works in the battle's static floor image, so the fields it does not write keep the previous battle's values (door
// and object lists, exits, groups ...) behind counts it zeroes: they are never read, and zeros are equivalent.
public static class DragonCaveBattlefield
{
    private const int Dragon = 0x60;
    private const int FireBeast = 0x64;
    private const int Pack = 0x65;

    /// <summary>The enemy types that turn any cave into the dragon's.</summary>
    private const int DragonType = 0x3F;
    private const int DragonTypeToo = 0x43;

    /// <summary>Builds the cave; always one level.</summary>
    // orig: R012+0x0000 (1432:0000) draft (matched per the draft notes); checked against asm (the dragon test and the call order); tests: Oracle:special.json
    public static int Build(GenerationState g, int battlefield, (int Type, int Quality, int Count)[] specs)
    {
        // C: the test reads the three specs' types in turn; one dragon among them makes it the dragon cave.
        var kind = specs.Take(3).Any(s => s.Type is DragonType or DragonTypeToo) ? Dragon : battlefield;
        var level = new BattlefieldLevel(new byte[BattlefieldLevel.ImageSize]);
        ClearLevel(level);
        WriteTileDescription(g, kind);
        LayCells(g, level, kind);
        g.Save(level);
        PlaceMonsters(g, kind, specs);
        return 1;
    }

    /// <summary>Level 0 with every cell void, no doors, objects, exits or groups, and the exit cards "none".</summary>
    // orig: R012+0x0084 (1432:0084) draft (matched per the draft notes); checked against asm (the byte and word stores); tests: Oracle:special.json
    private static void ClearLevel(BattlefieldLevel level)
    {
        level.LevelNumber = 0;
        level.DoorCount = 0;
        level.ObjectCount = 0;
        level.PartListCount = 0;
        level.ExitCount = 0;
        level.Image[0x300C] = 0;
        level.Image[0x3326] = 0;
        level.ExitCards = new LevelExitCards(-2, -2, -2, -2, -2);
        // C: _fstrcpy(name, "") writes only the terminating zero.
        level.Image[0x33C8] = 0;
        for (var row = 0; row < BattlefieldLevel.Size; row++)
        {
            for (var col = 0; col < BattlefieldLevel.Size; col++)
            {
                level[row, col] = BattlefieldCell.Void;
                level.SetFloorByte(row, col, 0);
            }
        }
    }

    /// <summary>The cave's tile sets and the tiles it uses; the dragon cave and the others differ in four furniture tiles.</summary>
    // orig: R012+0x010A (1432:010A) draft (matched per the draft notes); tests: Oracle:special.json (all 0xC4 bytes of the description)
    private static void WriteTileDescription(GenerationState g, int kind)
    {
        var d = g.Terrain;
        d.BackgroundPalette = 5;
        d.SetSetName(TileKind.Floor, "cavedrag");
        SetTiles(d, TileKind.Floor, 1, 0, 1, 2, 3, 4, 5, 6, 7, 8);
        d.SetSetName(TileKind.FloorDecoration, "cavedrag");
        d.SetAnimatedBelow(TileKind.FloorDecoration, 1);
        SetTiles(d, TileKind.FloorDecoration, 1, 8, 9, 10, 11);
        d.SetSetName(TileKind.NorthWall, "cavedrag");
        SetTiles(d, TileKind.NorthWall, 1, 0, 1);
        d.SetSetName(TileKind.WestWall, "cavedrag");
        d.SetTile(TileKind.WestWall, 1, 0);
        d.SetSetName(TileKind.Furniture, "cavedrag");
        SetTiles(d, TileKind.Furniture, 1, 0, 1, 2, 3, 4);
        d.SetAnimatedBelow(TileKind.Furniture, 5);
        d.SetTile(TileKind.Furniture, 6, 5);
        if (kind == Dragon)
        {
            SetTiles(d, TileKind.Furniture, 7, 10, 12, 11, 13);
        }
        else
        {
            SetTiles(d, TileKind.Furniture, 7, 30, 32, 31, 33);
        }
        SetTiles(d, TileKind.Furniture, 11, 20, 22);
    }

    private static void SetTiles(TerrainDescription d, TileKind kind, int first, params int[] tiles)
    {
        for (var i = 0; i < tiles.Length; i++)
        {
            d.SetTile(kind, first + i, tiles[i]);
        }
    }

    /// <summary>
    /// The cave's cells: 26 hand-placed cells (rocks and ledges), then the rows 8 to 13 of random
    /// rubble; the party enters at the top, a raised-edge rectangle bounds the lair.
    /// </summary>
    // orig: R012+0x028A (1432:028A) draft (differs from the original in stack slots only per the draft notes); tests: Oracle:special.json (every cell, the list and the party start)
    private static void LayCells(GenerationState g, BattlefieldLevel level, int kind)
    {
        var used = new List<int>();
        void Place(int row, int col, int height, int heightLow, int furniture = 0, int north = 0, int south = 0, int east = 0) =>
            PlaceCell(g, level, used, row, col, height, heightLow, furniture, north, south, east, 0);

        Place(3, 4, 7, 0, furniture: 9);
        Place(3, 5, 5, 0, furniture: 0x0A);
        Place(3, 6, 7, 0, furniture: 7);
        Place(3, 7, 8, 0);
        Place(4, 4, 5, 0, furniture: 1, north: 1);
        Place(4, 5, 7, 0, furniture: 8);
        Place(4, 6, 6, 0, furniture: 1);
        Place(4, 7, 7, 0, furniture: 2);
        Place(5, 4, 1, 1, furniture: 0x0B, north: 1);
        Place(5, 5, 1, 1, furniture: kind == FireBeast ? 6 : 0);
        Place(5, 6, 2, 0);
        Place(5, 7, 2, 3);
        Place(5, 8, 7, 0, furniture: 3);
        Place(6, 4, 1, 0, furniture: 5, north: 1);
        Place(6, 5, 2, 0);
        Place(6, 6, 2, 0);
        Place(6, 7, 1, 0);
        Place(6, 8, 7, 0, furniture: 2);
        Place(7, 4, 2, 2, north: 1);
        Place(7, 5, 2, 4);
        Place(7, 6, 2, 2);
        Place(7, 7, 1, 2);
        Place(7, 8, 1, 2, south: 1);
        Place(9, 7, 3, 0, furniture: 0x0C);
        Place(0x0D, 8, 3, 0, furniture: 0x0C, south: 1, east: 1);
        Place(0x0D, 5, 9, 0, north: 2, east: 1);
        for (var row = 8; row <= 13; row++)
        {
            var first = row < 10 ? 4 : 5;
            for (var col = first; col <= 8; col++)
            {
                // C: the roll is made for every cell, also those already placed (which then ignore it).
                var roll = g.Roll(100);
                int height;
                var rubble = 0;
                if ((row + col) % 2 == 0)
                {
                    height = 3;
                    rubble = roll <= 15 ? 3 : roll <= 30 ? 4 : 0;
                }
                else
                {
                    height = 4;
                    rubble = roll <= 60 ? 3 : roll <= 75 ? 4 : 0;
                }
                var south = col == 8 ? 1 : 0;
                var east = row == 13 || (row == 9 && first == col) ? 1 : 0;
                PlaceCell(g, level, used, row, col, height, rubble, 0, first == col ? 1 : 0, south, east, 0);
            }
        }
        level.Image[0x248A] = level.Image[0x248B] = 0;
        for (var i = 0; i < used.Count; i++)
        {
            PutWord(level, 0x24C6 + 2 * i, used[i]);
        }
        PutWord(level, 0x24C6 + 2 * used.Count, -1);
        level.PartyStart = new MapPoint(0x170, 0x68);
        level.PartyFacing = 6;
        level.PartyRoom = -1;
        level.Image[0x3326] = 1;
        PutWord(level, 0x3328, 0x1A0);
        PutWord(level, 0x332C, 0x20);
        PutWord(level, 0x332A, 0x50);
        PutWord(level, 0x332E, 0x10);
    }

    /// <summary>
    /// Fills a cell that is still void: a random furniture orientation, the wall and furniture
    /// tiles, the floor byte (high nibble: floor tile, low nibble: decoration), and lists the cell.
    /// </summary>
    // orig: R012+0x07E2 (1432:07E2) draft (matched per the draft notes); checked against asm (the nibble composition and the draw); tests: Oracle:special.json
    // C: the cell is composed from unsigned parts of 16 and 32 bits; the walls' nibbles are masked with 15.
    private static void PlaceCell(GenerationState g, BattlefieldLevel level, List<int> used, int row, int col, int height, int heightLow,
        int furniture, int north, int south, int east, int west)
    {
        if (!level[row, col].IsVoid)
        {
            return;
        }
        // C: the draw happens inside the expression, only for a cell that is still void.
        var orientation = (g.Roll(4) - 1) & 3;
        var bits = (uint)((orientation << 4 | (south & 15)) << 24)
            | (uint)((west & 15) << 8)
            | (uint)((furniture & 15) << 4)
            | (uint)((north & 15) << 16)
            | (uint)(east & 15);
        level[row, col] = new BattlefieldCell(bits);
        level.SetFloorByte(row, col, (byte)(height << 4 | heightLow));
        used.Add((row << 8) + col);
    }

    private static void PutWord(BattlefieldLevel level, int at, int value)
    {
        level.Image[at] = (byte)value;
        level.Image[at + 1] = (byte)(value >> 8);
    }

    /// <summary>The monsters at the cave's mouth (a group of up to three kinds), by cave type.</summary>
    // orig: R012+0x08A8 (1432:08A8) draft (matched per the draft notes); checked against asm; tests: Oracle:special.json
    // DORMANT: the counts the card passes are only used when the cave type is none of the three; the cave types fix their own monsters.
    private static void PlaceMonsters(GenerationState g, int kind, (int Type, int Quality, int Count)[] specs)
    {
        int first = -1, second = -1, third = -1;
        int n1 = specs[0].Count, n2 = specs[1].Count, n3 = specs[2].Count;
        switch (kind)
        {
            case Dragon:
                first = g.Forces.AddKind(0, 0x3F, 1);
                n1 = 1;
                break;
            case FireBeast:
                first = g.Forces.AddKind(0, 0x2F, 1);
                n1 = g.Roll(3) + g.Context.LoreLevel() / 3 + 3;
                break;
            case Pack:
                first = g.Forces.AddKind(0, 0x27, 1);
                second = g.Forces.AddKind(0, 0x29, 2);
                third = g.Forces.AddKind(0, 0x29, g.Context.LoreLevel() < 5 ? 1 : 3);
                n1 = g.Roll(2) + 2;
                n2 = g.Roll(2) + 1;
                n3 = g.Roll(2);
                break;
        }
        // The group stands on the ledge (dragon) or further in (others).
        var row = kind == Dragon ? 5 : 6;
        var column = row;
        var x = kind == Dragon ? 0x18 : 0x10;
        var y = kind == Dragon ? 0x0F : 8;
        var spread = kind == Dragon ? 0 : 0x10;
        var group = new EnemyGroup
        {
            X = (short)((column << 5) + x),
            Y = (short)((row << 4) + y),
            Room = -1,
            Spread = (short)spread,
            HasReinforcements = 0,
        };
        var k = 0;
        foreach (var (type, count) in new[] { (first, n1), (second, n2), (third, n3) })
        {
            if (type is >= 0 and < 12 && count > 0)
            {
                group.Kinds[k] = (sbyte)type;
                group.Counts[k] = (sbyte)count;
                k++;
            }
        }
        if (k > 0)
        {
            g.Forces.Groups[0].Add(group);
        }
    }
}
