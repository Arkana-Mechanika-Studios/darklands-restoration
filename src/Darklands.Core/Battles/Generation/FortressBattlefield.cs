namespace Darklands.Core.Battles.Generation;

/// <summary>
/// The Templar fortress (overlay R011, battlefield type 0x5E): 16 rooms, built from the prepared
/// maps ifortmon.000..015 of IMAPS.CAT, four of them held at a time and saved as the levels
/// level0.flr .. level15.flr once their doors are joined; and the tombs (type 0x5F): one of two
/// prepared maps imisctom.001/002 filled with groups of the dead.
/// </summary>
// orig: R011 1432 (17 functions; 6 byte-matched per the draft notes, the others "close")
public static class FortressBattlefield
{
    private const int Rooms = 16;
    private const int Buffers = 4;
    private const int Size = BattlefieldLevel.Size;

    // The level image's records, by offset (the draft's struct level). The names are the draft's, with a meaning where known.
    private const int FloorAt = 0x1900;
    private const int LevelNumberAt = 0x1F40;
    private const int ExitCardsAt = 0x1F42;
    private const int DoorTableAt = 0x1F52;
    private const int DoorTableStride = 0x1C;
    private const int ObjectCountAt = 0x2184;
    private const int ObjectsAt = 0x2186;
    private const int ObjectStride = BattlefieldLevel.RecordSize;
    private const int ExitCountAt = 0x2FB6;
    private const int ExitsAt = 0x2FB8;
    private const int LootGroupCountAt = 0x300C;
    private const int LootGroupsAt = 0x300E;
    private const int LootGroupStride = 0x2C;
    private const int ContentsAt = 0x316E;
    private const int ContentsStride = 10;
    private const int PlaceholderRecordsAt = 0x3236;
    private const int PlaceholderRecordStride = 12;
    private const int Zone7At = 0x3350;
    private const int Zone8At = 0x3382;
    private const int ZoneStride = 10;
    private const int SetNameAt = 0x33C8;
    private const int Byte33D1At = 0x33D1;
    private const int Byte33D2At = 0x33D2;

    // The object types the loader knows (the first object of another type ends the list).
    private const int TypePlaceholder = 1;
    private const int TypeItem = 2;
    private const int TypeMonster = 4;
    private const int TypeChest = 5;
    private const int TypeZone7 = 7;
    private const int TypeZone8 = 8;
    private const int TypeGroupMarker = BattlefieldObject.GroupMarker;

    /// <summary>The random items a room of the fortress may hold: the 22 item types 0x96..0xAB.</summary>
    private const int FirstRandomItem = 0x96;

    /// <summary>The Templar fortress: 16 levels; the party starts on level 0 (or 1 when the third spec's quality is 1).</summary>
    // orig: R011+0x0000 (1432:0000) draft (byte-matched per the draft notes); call order and arguments checked against asm; tests: Oracle:special.json
    // C: the meaning of the two specs' qualities the fortress tests: the second (== 1) shrinks every group by one, the third (== 1) starts the party on level 1.
    // DORMANT: Context.ForceMap (DS:C100, always -1) would force the start level to a number 0..15.
    // DIVERGENCE: the four level buffers (alloc_buffer 0xCFF0) are arrays; DS:E049 (the room each buffer holds) is written and never read, so it is not kept. Its only store is R011+0x04ED; no instruction of the root or the 136 overlays reads it.
    public static int BuildFortress(GenerationState g, int battlefield, out int start, (int Type, int Quality, int Count)[] specs)
    {
        _ = battlefield;
        var fortress = new Fortress(g);
        g.MarkPersistent();
        WriteFortressTileDescription(g.Terrain);
        fortress.ReducedGroups = specs[1].Quality == 1;
        g.Context.Process.FortressReducedGroups = fortress.ReducedGroups;
        fortress.GroupIds = 0;
        fortress.LoadRoom(0, 1);
        fortress.LoadRoom(1, 2);
        fortress.Save(1);
        fortress.Save(2);
        fortress.LoadRoom(2, 1);
        fortress.LoadRoom(3, 2);
        fortress.LoadRoom(4, 0);
        fortress.Connect(1, 0, 0, 0);
        fortress.CopyExit(2, 0, 1, 0);
        fortress.Save(1);
        fortress.Save(2);
        fortress.LoadRoom(5, 1);
        fortress.LoadRoom(6, 2);
        fortress.LoadRoom(7, 3);
        fortress.Connect(1, 0, 0, 1);
        fortress.Connect(2, 0, 0, 2);
        fortress.Connect(3, 0, 0, 3);
        fortress.Save(1);
        fortress.Save(2);
        fortress.Save(3);
        fortress.LoadRoom(8, 1);
        fortress.Connect(1, 0, 0, 4);
        fortress.Save(1);
        fortress.LoadRoom(9, 1);
        fortress.LoadRoom(10, 2);
        fortress.LoadRoom(11, 3);
        fortress.Connect(1, 0, 0, 5);
        fortress.Connect(2, 0, 1, 1);
        fortress.Connect(3, 0, 2, 1);
        fortress.Save(0);
        fortress.Save(2);
        fortress.Save(3);
        fortress.LoadRoom(12, 2);
        fortress.LoadRoom(13, 3);
        fortress.LoadRoom(14, 0);
        fortress.Connect(2, 0, 1, 2);
        fortress.Connect(3, 0, 2, 1);
        fortress.Connect(0, 0, 2, 2);
        fortress.Save(1);
        fortress.Save(2);
        fortress.Save(3);
        fortress.LoadRoom(15, 1);
        fortress.CopyExit(1, 0, 0, 0);
        fortress.Save(0);
        fortress.Save(1);
        start = specs[2].Quality == 1 ? 1 : 0;
        var forced = g.Context.ForceMap;
        if (forced is >= 0 and <= 15)
        {
            start = forced;
        }
        return Rooms;
    }

    /// <summary>The tombs: one level of crypts, one group of the dead on each of the first spec's count of group markers.</summary>
    // orig: R011+0x1FB2 (1432:1FB2) draft (byte-matched per the draft notes); checked against asm; tests: Oracle:special.json
    // DIVERGENCE: the original works in the battle's static floor image; the map fills it completely, so nothing of an earlier battle remains.
    // C: the groups are placed with the "smaller groups" byte DS:E051 the tombs never set: 0 in a fresh program, else the setting of
    // the last fortress built in this program (GenerationProcessMemory).
    public static int BuildTombs(GenerationState g, int battlefield, (int Type, int Quality, int Count)[] specs)
    {
        _ = battlefield;
        g.MarkPersistent();
        WriteTombTileDescription(g.Terrain);
        var level = g.Context.Maps.Load($"imisctom.{g.Roll(2):000}");
        var image = level.Image;
        image[LevelNumberAt] = 0;
        image[ObjectCountAt] = (byte)CountLeadingObjects(image, type: 6);
        level.ExitCards = new LevelExitCards(-2, -2, -2, -2, -2);
        RemapTiles(g, 0, mode: false, image);
        for (var marker = 1; marker <= specs[0].Count; marker++)
        {
            PlaceGroup(g, image, g.Context.Process.FortressReducedGroups, marker, 0x2D, 2, 1, -1, 0, 0, negate: false, 0);
        }
        g.Save(level);
        return 1;
    }

    /// <summary>The length of the run of objects of one type at the start of the object list (at most 55).</summary>
    private static int CountLeadingObjects(byte[] image, int type)
    {
        var n = 0;
        while (n < BattlefieldLevel.MaxObjects && Word(image, ObjectsAt + ObjectStride * n + 6) == type)
        {
            n++;
        }
        return n;
    }

    /// <summary>The fortress's tile sets: all "fortmons", seven kinds of tiles.</summary>
    // orig: R011+0x029E (1432:029E) draft (byte-matched per the draft notes); checked against asm; tests: Oracle:special.json
    // C: the draft stores the north wall's tile 7 (4) twice; the second store changes nothing.
    private static void WriteFortressTileDescription(TerrainDescription d)
    {
        d.BackgroundPalette = 6;
        d.SetSetName(TileKind.Floor, "fortmons");
        SetTiles(d, TileKind.Floor, 1, 1, 2, 3);
        d.SetSetName(TileKind.FloorDecoration, "fortmons");
        d.SetAnimatedBelow(TileKind.FloorDecoration, 2);
        SetTiles(d, TileKind.FloorDecoration, 2, 8, 9, 10, 11, 13, 12);
        d.SetSetName(TileKind.NorthWall, "fortmons");
        d.SetTile(TileKind.NorthWall, 1, 1);
        d.SetTile(TileKind.NorthWall, 4, 2);
        d.SetTile(TileKind.NorthWall, 5, 3);
        d.SetTile(TileKind.NorthWall, 7, 4);
        d.SetSetName(TileKind.NorthWallDecoration, "fortmons");
        d.SetTile(TileKind.NorthWallDecoration, 1, 0);
        d.SetAnimatedBelow(TileKind.NorthWallDecoration, 2);
        d.SetTile(TileKind.NorthWallDecoration, 2, 8);
        d.SetSetName(TileKind.WestWall, "fortmons");
        d.SetTile(TileKind.WestWall, 1, 1);
        d.SetTile(TileKind.WestWall, 4, 2);
        d.SetTile(TileKind.WestWall, 5, 3);
        d.SetTile(TileKind.WestWall, 6, 0);
        d.SetSetName(TileKind.WestWallDecoration, "fortmons");
        d.SetTile(TileKind.WestWallDecoration, 1, 0);
        d.SetAnimatedBelow(TileKind.WestWallDecoration, 2);
        d.SetTile(TileKind.WestWallDecoration, 2, 6);
        d.SetTile(TileKind.WestWallDecoration, 3, 7);
        d.SetSetName(TileKind.Furniture, "fortmons");
        SetTiles(d, TileKind.Furniture, 1, 5, 8, 3, 2, 0, 1, 15, 17, 7);
        d.SetAnimatedBelow(TileKind.Furniture, 12);
        SetTiles(d, TileKind.Furniture, 10, 25, 26);
        SetTiles(d, TileKind.Furniture, 12, 16, 6, 10);
        d.SetVaryingFurniture(20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30);
    }

    /// <summary>The tombs' tile sets: the keep's.</summary>
    // orig: R011+0x209E (1432:209E) draft (byte-matched per the draft notes); checked against asm; tests: Oracle:special.json
    private static void WriteTombTileDescription(TerrainDescription d)
    {
        d.BackgroundPalette = 0;
        d.SetSetName(TileKind.Floor, "keep");
        d.SetTile(TileKind.Floor, 1, 2);
        d.SetSetName(TileKind.NorthWall, "keep1");
        d.SetTile(TileKind.NorthWall, 1, 1);
        d.SetSetName(TileKind.WestWall, "keep1");
        d.SetTile(TileKind.WestWall, 1, 1);
        d.SetSetName(TileKind.Furniture, "keep");
        SetTiles(d, TileKind.Furniture, 1, 13, 14);
        d.SetTile(TileKind.Furniture, 7, 18);
        d.SetAnimatedBelow(TileKind.Furniture, 12);
        d.SetTile(TileKind.Furniture, 12, 16);
    }

    private static void SetTiles(TerrainDescription d, TileKind kind, int first, params int[] tiles)
    {
        for (var i = 0; i < tiles.Length; i++)
        {
            d.SetTile(kind, first + i, tiles[i]);
        }
    }

    // Raw access to a level image (16-bit little endian words, signed as the original's ints).
    private static int Word(byte[] image, int at) => (short)(image[at] | image[at + 1] << 8);

    private static void SetWord(byte[] image, int at, int value)
    {
        image[at] = (byte)value;
        image[at + 1] = (byte)(value >> 8);
    }

    private static uint Cell(byte[] image, int row, int column) =>
        BitConverter.ToUInt32(image, (row * Size + column) * 4);

    private static void SetCell(byte[] image, int row, int column, uint value) =>
        BitConverter.TryWriteBytes(image.AsSpan((row * Size + column) * 4), value);

    private static int ObjectAt(int index) => ObjectsAt + ObjectStride * index;

    /// <summary>
    /// Remaps a prepared room's tile numbers to the fortress's tile sets (or, with mode off, the
    /// tombs'), giving each furniture tile a random orientation and each raised floor a random height.
    /// </summary>
    // orig: R011+0x05C6 (1432:05C6) draft ("close"); checked against asm (both jump tables, the widths and the order of draws); tests: Oracle:special.json
    // C: furniture orientation bits are ORed over the ones already there; the wall nibbles are remapped from the cell as it was read.
    private static void RemapTiles(GenerationState g, int room, bool mode, byte[] image)
    {
        for (var row = 1; row < Size - 1; row++)
        {
            for (var column = 1; column < Size - 1; column++)
            {
                var original = Cell(image, row, column);
                if ((original & 0x80000000) != 0)
                {
                    continue;
                }
                var cell = original;
                var furniture = (int)(original & 0xF0) >> 4;
                furniture = mode ? FortressFurniture(furniture) : TombFurniture(furniture);
                cell &= ~0xF0u;
                if (furniture != 0)
                {
                    cell |= (uint)(furniture & 0xF) << 4;
                    cell |= (uint)((g.Roll(4) - 1) & 3) << 28;
                }
                RemapFloor(g, image, row, column, room, mode);
                for (var shift = 0; shift <= 24; shift += 8)
                {
                    var wall = (int)(original >> shift & 0xF);
                    if (wall != 0)
                    {
                        wall = wall switch { 7 => 4, 8 => 5, 9 => 1, 10 => 6, _ => 1 };
                        cell = cell & ~(0xFu << shift) | (uint)(wall & 0xF) << shift;
                    }
                }
                for (var shift = 12; shift <= 20; shift += 8)
                {
                    var decoration = (int)(original >> shift & 0xF);
                    if (decoration != 0)
                    {
                        decoration = decoration switch { 1 => 2, 2 => 3, 3 => 1, _ => decoration };
                        cell = cell & ~(0xFu << shift) | (uint)(decoration & 0xF) << shift;
                    }
                }
                SetCell(image, row, column, cell);
            }
        }
    }

    private static int FortressFurniture(int tile) => tile switch
    {
        1 => 4, 2 => 3, 3 => 1, 4 => 2, 5 => 5, 6 => 6, 7 => 14, 8 => 13, 9 => 9, 10 => 12, 11 => 8, 12 => 7, 13 => 10, 14 => 11,
        _ => 0,
    };

    private static int TombFurniture(int tile) => tile switch { 1 => 1, 2 => 2, 3 => 7, 10 => 12, _ => 0 };

    /// <summary>The floor byte of a cell: its decoration (low nibble) remapped, its height (high nibble) 1 or a random 2..3.</summary>
    private static void RemapFloor(GenerationState g, byte[] image, int row, int column, int room, bool mode)
    {
        var at = FloorAt + row * Size + column;
        var low = (image[at] & 0xF) switch { 1 => 2, 2 => 3, 3 => 4, 4 => 5, _ => 0 };
        int high;
        if (image[at] >> 4 == 4)
        {
            high = g.Roll(2) + 1;
            if (mode && room < 4 && low == 0 && g.Roll(2) == 1)
            {
                low = 7;
            }
        }
        else
        {
            high = 1;
        }
        image[at] = (byte)(high << 4 | low & 0xF);
    }

    /// <summary>
    /// Finds the group marker object of a group and places the group of monsters there: up to two
    /// kinds, each with its number; the party's reduced setting takes one off each kind.
    /// </summary>
    // orig: R011+0x1A2A (1432:1A2A) draft ("close"); checked against asm; tests: Oracle:special.json
    // C: the monster record of the first kind is flagged (+0x0C) even when it is -1 (then the record before the level's first kind is hit).
    // DIVERGENCE: the original writes the group into slot (level × 10 + count) even past the tenth group of a level (into the next level's slots); the list here grows.
    private static void PlaceGroup(GenerationState g, byte[] image, bool reduced, int marker, int type, int count, int number,
        int type2, int count2, int number2, bool negate, int endsBattle)
    {
        var level = (sbyte)image[LevelNumberAt];
        var id = marker + 11;
        int i;
        for (i = 0; i < BattlefieldLevel.MaxObjects; i++)
        {
            var at = ObjectAt(i);
            if (Word(image, at + 6) == TypeGroupMarker && Word(image, at + 0xA) == id)
            {
                break;
            }
        }
        if (i >= BattlefieldLevel.MaxObjects)
        {
            return;
        }
        if (reduced)
        {
            count = Math.Max(count - 1, 1);
            count2 = Math.Max(count2 - 1, 1);
        }
        var first = g.Forces.AddKind(level, type, count);
        FlagKind(g, level, first, endsBattle);
        var second = g.Forces.AddKind(level, type2, count2);
        if (first < 0 && second < 0)
        {
            return;
        }
        var group = new EnemyGroup();
        var used = 0;
        if (first >= 0 && number > 0)
        {
            group.Kinds[0] = (sbyte)first;
            group.Counts[0] = (sbyte)number;
            used = 1;
        }
        if (second >= 0 && number2 > 0)
        {
            group.Kinds[used] = (sbyte)second;
            group.Counts[used] = (sbyte)number2;
            used++;
        }
        if (used > 0)
        {
            var o = ObjectAt(i);
            group.X = (short)(Word(image, o) + 16);
            group.Y = (short)(Word(image, o + 2) + 8);
            group.Room = (sbyte)image[o + 0xC];
            group.Spread = 16;
            group.HasReinforcements = 0;
            if (negate)
            {
                // C: the position is negated as a 16-bit int.
                group.X = (short)-group.X;
            }
            g.Forces.Groups[level].Add(group);
        }
    }

    /// <summary>bf_monsters[level * 12 + kind].+0x0C = value, with the original's unchecked index.</summary>
    // DIVERGENCE: a kind of −1 addresses the last kind of the level before (or, for level 0, the enemy buffer's header, which nothing reads); only the former is kept.
    private static void FlagKind(GenerationState g, int level, int kind, int value)
    {
        if (kind >= 0)
        {
            g.Forces.Kinds[level][kind].EndsBattleWhenKilled = (sbyte)value;
        }
        else if (level > 0 && g.Forces.Kinds[level - 1].Count == BattlefieldForces.MaxKinds)
        {
            g.Forces.Kinds[level - 1][BattlefieldForces.MaxKinds - 1].EndsBattleWhenKilled = (sbyte)value;
        }
    }

    /// <summary>The fortress while it is built: four room buffers, which of the rooms are cleared, the group numbering.</summary>
    private sealed class Fortress(GenerationState g)
    {
        private readonly BattlefieldLevel[] _buffers = new BattlefieldLevel[Buffers];

        /// <summary>DS:E051: groups one smaller.</summary>
        public bool ReducedGroups { get; set; }

        /// <summary>DS:DFF7: the next group number (also used by the mines).</summary>
        public int GroupIds { get; set; }

        private byte[] Image(int buffer) => _buffers[buffer].Image;

        private bool Cleared(int room) => (g.Context.Memory.ClearedLevels & 1 << room) != 0;

        /// <summary>Loads room <paramref name="room"/> into a buffer, gives it its level number, exit cards and name, remaps its tiles and sets it up.</summary>
        // orig: R011+0x0484 (1432:0484) draft ("close"); checked against asm; tests: Oracle:special.json
        // DORMANT: the buffer number is always 0..3.
        public void LoadRoom(int room, int buffer)
        {
            if (buffer is < 0 or >= Buffers)
            {
                return;
            }
            _buffers[buffer] = g.Context.Maps.Load($"ifortmon.{room:000}");
            var image = Image(buffer);
            image[LevelNumberAt] = (byte)room;
            var n = 0;
            for (; n < BattlefieldLevel.MaxObjects; n++)
            {
                var type = Word(image, ObjectAt(n) + 6);
                if (type is not (6 or 0 or 1 or 2 or 4 or 5 or 7 or 8))
                {
                    break;
                }
            }
            image[ObjectCountAt] = (byte)n;
            SetWord(image, ExitCardsAt, -2);
            SetWord(image, ExitCardsAt + 2, 0x12F);
            SetWord(image, ExitCardsAt + 6, 0x12F);
            SetWord(image, ExitCardsAt + 8, room >= 4 ? -2 : 12);
            SetWord(image, ExitCardsAt + 4, room >= 4 ? -2 : 12);
            image[Byte33D1At] = 11;
            System.Text.Encoding.ASCII.GetBytes("fortmons\0").CopyTo(image, SetNameAt);
            RemapTiles(g, room, mode: true, image);
            SetUpRoom(buffer);
        }

        /// <summary>Saves the room in a buffer as level<c>N</c>.flr.</summary>
        // orig: R011+0x1F5C (1432:1F5C) draft (byte-matched per the draft notes); checked against asm; tests: Oracle:special.json
        // DORMANT: the buffer number is always 0..3.
        public void Save(int buffer)
        {
            if (buffer is >= 0 and < Buffers)
            {
                g.Save(_buffers[buffer]);
            }
        }

        /// <summary>Copies exit <paramref name="from"/> of one buffer over exit <paramref name="to"/> of another.</summary>
        // C: the original assigns the whole 14-byte struct (DS:E047[a].ex[0] = DS:E047[b].ex[0]).
        public void CopyExit(int toBuffer, int toExit, int fromBuffer, int fromExit) =>
            Image(fromBuffer).AsSpan(ExitsAt + 14 * fromExit, 14).CopyTo(Image(toBuffer).AsSpan(ExitsAt + 14 * toExit));

        /// <summary>
        /// Joins exit <paramref name="exitA"/> of buffer <paramref name="bufferA"/> with exit <paramref name="exitB"/> of buffer
        /// <paramref name="bufferB"/>: each names the other's room and exit, and the two exits' records are swapped.
        /// </summary>
        // orig: R011+0x1DDC (1432:1DDC) draft ("close"); checked against asm; tests: Oracle:special.json
        // DORMANT: the buffers and exit numbers are always in range.
        public void Connect(int bufferA, int exitA, int bufferB, int exitB)
        {
            if (bufferA is < 0 or >= Buffers || bufferB is < 0 or >= Buffers || exitA is < 0 or >= BattlefieldLevel.MaxExits || exitB is < 0 or >= BattlefieldLevel.MaxExits)
            {
                return;
            }
            var a = Image(bufferA);
            var b = Image(bufferB);
            // C: the exit counts are signed chars.
            a[ExitCountAt] = (byte)Math.Max((sbyte)a[ExitCountAt], exitA + 1);
            b[ExitCountAt] = (byte)Math.Max((sbyte)b[ExitCountAt], exitB + 1);
            var at = ExitsAt + 14 * exitA;
            var other = ExitsAt + 14 * exitB;
            a[at + 6] = b[LevelNumberAt];
            b[other + 6] = a[LevelNumberAt];
            a[at + 0xD] = (byte)exitB;
            b[other + 0xD] = (byte)exitA;
            foreach (var (offset, length) in new[] { (7, 1), (8, 2), (0xA, 2), (0xC, 1) })
            {
                for (var i = 0; i < length; i++)
                {
                    (a[at + offset + i], b[other + offset + i]) = (b[other + offset + i], a[at + offset + i]);
                }
            }
        }

        /// <summary>The room's loot, groups, doors and start according to its number.</summary>
        // orig: R011+0x096A (1432:096A) draft ("close"); asm read for the jump table and cases 0..4 and 14; every room in both cleared states compared with the original
        // C: case 0 falls through into cases 1..3.
        // C: in case 14 the draft passes two group pointers it never set; the asm passes the groups 1 and 2 of the room.
        // DORMANT: the buffer number is always 0..3.
        private void SetUpRoom(int buffer)
        {
            if (buffer is < 0 or >= Buffers)
            {
                return;
            }
            var lv = Image(buffer);
            int room = (sbyte)lv[LevelNumberAt];
            var cleared = Cleared(room);
            switch (room)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    SetUpEntranceRooms(lv, room, cleared);
                    break;
                case 4:
                    SetUpRoom4(lv);
                    break;
                case 5:
                    PlaceGroup(g, lv, ReducedGroups, 0, 0x28, 2, 7, -1, 0, 0, negate: true, 0);
                    lv[Byte33D2At] = 0x17;
                    ShapeZone8(lv, 0, 0x12, 0x12, 0x0F, 0x0F, upper: false, 0x12E, flag: false);
                    ShapeZone7(lv, 0, 0x15, 0x17, 0x0E, 0x10, 0x16C, 1, false, 0, 0, 0, 0);
                    break;
                case 6:
                    SetDoorValue(lv, 1, 0x14, flag: false);
                    if (!cleared)
                    {
                        PlaceGroup(g, lv, ReducedGroups, 0, 0x35, 1, 4, -1, 0, 0, negate: false, 0);
                        PlaceGroup(g, lv, ReducedGroups, 1, 0x35, 1, 5, -1, 0, 0, negate: false, 0);
                    }
                    lv[Byte33D2At] = 0x1D;
                    ShapeZone7(lv, 0, 0x0C, 0x0E, 0x19, 0x1B, 0x12C, 0, false, 0x0D, 0x1B, 0, 0);
                    break;
                case 7:
                    if (!cleared)
                    {
                        PlaceGroup(g, lv, ReducedGroups, 0, 0x1E, 3, 1, 0x28, 2, 5, negate: false, 0);
                    }
                    PlaceGroup(g, lv, ReducedGroups, 1, 0x28, 1, 4, -1, 0, 0, negate: true, 0);
                    lv[Byte33D2At] = 0x14;
                    ShapeZone7(lv, 0, 0x12, 0x14, 0x1E, 0x20, 0x12B, 0, false, 0x13, 0x1F, 0, 0);
                    break;
                case 8:
                    SetUpRoom8(lv);
                    break;
                case 9:
                    SetUpRoom9(lv, cleared);
                    break;
                case 10:
                    SetUpRoom10(lv, cleared);
                    break;
                case 11:
                    SetUpRoom11(lv, cleared);
                    break;
                case 12:
                    SetDoorValue(lv, 0, 0x14, flag: false);
                    SetDoorValue(lv, 1, 0x1E, flag: false);
                    if (cleared)
                    {
                        break;
                    }
                    ShapeZone7(lv, 0, 0x10, 0x10, 0x17, 0x1A, 0x136, 0, false, 0x10, 0x18, 0, 0);
                    SetChestValue(lv, 0, 0x137);
                    break;
                case 13:
                    PlaceGroup(g, lv, ReducedGroups, 0, 0x46, 3, 7, 0x45, 2, 1, negate: false, 0);
                    PlaceGroup(g, lv, ReducedGroups, 1, 0, 5, 4, -1, 0, 0, negate: false, 0);
                    PlaceGroup(g, lv, ReducedGroups, 2, 0, 5, 4, -1, 0, 0, negate: false, 0);
                    PlaceGroup(g, lv, ReducedGroups, 3, 0x51, 3, 1, 0x28, 2, 2, negate: true, 0);
                    lv[Byte33D2At] = 0x16;
                    ShapeZone7(lv, 0, 0x1B, 0x1E, 7, 0x0D, 0x139, 0, false, 0x1C, 8, 0, 0);
                    break;
                case 14:
                    SetUpRoom14(lv);
                    break;
                case 15:
                    lv[LootGroupCountAt] = 0;
                    PlaceGroup(g, lv, ReducedGroups, 0, 0x37, 1, 1, -1, 0, 0, negate: false, 1);
                    SetWord(lv, ExitCardsAt, 0x138);
                    break;
            }
        }

        // Rooms 0..3: the entrance rooms (a fresh set of guards, the loot for the room's key and its shape).
        private void SetUpEntranceRooms(byte[] lv, int room, bool cleared)
        {
            if (room == 0 && !cleared)
            {
                PlaceGroup(g, lv, ReducedGroups, 0, 0x28, 1, 5, -1, 0, 0, negate: false, 0);
                PlaceGroup(g, lv, ReducedGroups, 1, 0x28, 1, 5, -1, 0, 0, negate: false, 0);
            }
            if (room is 0 or 2)
            {
                PlaceGroup(g, lv, ReducedGroups, 2, 0x3D, 1, 5, -1, 0, 0, negate: true, 0);
            }
            AddLootEntry(lv, 0, 0x94, 0xA0, 0x23, 1);
            if (!cleared)
            {
                SetLootGroup(lv, 0, GroupIds++, word10: 10);
                AddLootEntry(lv, 0, 0x73, 0x98, 0x2D, 1);
                SetLootGroup(lv, 1, GroupIds++, word10: 12);
                AddLootEntry(lv, 1, 0x70, 0x95, 0x2D, 2);
            }
            lv[Byte33D2At] = 0x18;
            if (room is 0 or 2)
            {
                ShapeZone7(lv, 0, 0x0D, 0x12, 0x1E, 0x22, 0x129, 0, false, 0, 0, 0, 0);
            }
            else
            {
                ShapeZone7(lv, 0, 0x0E, 0x10, 0x1F, 0x21, 0x129, 0, false, 0, 0, 0, 0);
            }
        }

        private void SetUpRoom4(byte[] lv)
        {
            SetLootGroup(lv, 0, GroupIds++);
            SetWord(lv, LootGroupAt(0) + 0xE, 0x18);
            SetLootGroup(lv, 1, GroupIds++);
            lv[LootGroupAt(1) + 0xA] = 5;
            lv[LootGroupAt(1) + 0xA] |= 0x30;
            lv[LootGroupAt(1) + 9] = 0x32;
            AddLootEntry(lv, 1, 0x1B, 0x1C, 0x23, 6);
            AddLootEntry(lv, 1, 3, 1, 0x28, 6);
            AddLootEntry(lv, 1, 0x21, 0x40, 0x23, 0x28);
            SetLootGroup(lv, 2, GroupIds++);
            lv[LootGroupAt(2) + 0xA] = 0x35;
            lv[LootGroupAt(2) + 9] = 0x32;
            AddLootEntry(lv, 2, 0x2C, 0x4B, 0x28, 6);
            AddLootEntry(lv, 2, 0x35, 0x54, 0x28, 6);
            SetLootGroup(lv, 3, GroupIds++);
            lv[LootGroupAt(3) + 0xA] = 0x35;
            lv[LootGroupAt(3) + 9] = 0x32;
            AddLootEntry(lv, 3, 0x50, 0x75, 0x23, 10);
            AddLootEntry(lv, 3, 0x52, 0x77, 0x23, 10);
            AddLootEntry(lv, 3, 0x4B, 0x70, 0x23, 10);
            ArmPlaceholder(lv, 0, 0x88, 0x2D, 1, 0x19);
            ArmPlaceholder(lv, 1, 0x84, 0x14, 1, 0x0F);
            ArmPlaceholder(lv, 2, 0x84, 0x14, 1, 0x0F);
            ArmPlaceholder(lv, 3, 0x8B, 0x23, 1, 0x1E);
            ArmPlaceholder(lv, 4, 0x8B, 0x23, 1, 0x1E);
            SetDoorValue(lv, 0, 0x14, flag: true);
            SetDoorValue(lv, 0x12, 0x1E, flag: true);
            SetDoorValue(lv, 1, 0x1E, flag: true);
            SetDoorValue(lv, 0x13, 0x1E, flag: true);
            PlaceGroup(g, lv, ReducedGroups, 0, 0x28, 2, 6, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 1, 0x3A, 1, 3, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 2, 0x3A, 1, 4, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 3, 0x28, 2, 6, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 4, 0x3A, 1, 4, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 5, 0x28, 2, 6, -1, 0, 0, negate: false, 0);
            lv[Byte33D2At] = 0x1A;
            ShapeZone7(lv, 0, 0x12, 0x14, 0x12, 0x14, 0x12D, 0, false, 0x13, 0x13, 0, 0);
        }

        private void SetUpRoom8(byte[] lv)
        {
            SetLootGroup(lv, 0, GroupIds++, word10: 3, word0E: 10);
            lv[LootGroupAt(0) + 0xA] = 0x35;
            lv[LootGroupAt(0) + 9] = 0x32;
            AddLootEntry(lv, 0, 10, 10, 0x2D, 1);
            AddLootEntry(lv, 0, 0x5A, 0x7F, 0x23, 10);
            AddLootEntry(lv, 0, 0x38, 0x61, 0x2D, 2);
            SetDoorValue(lv, 0, 0x19, flag: true);
            lv[DoorTableAt + DoorTableStride + 0x17] = 0x9E;
            lv[DoorTableAt + DoorTableStride + 9] = 0;
            PlaceGroup(g, lv, ReducedGroups, 0, 0x2D, 1, 7, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 1, 0x2D, 2, 7, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 2, 0x2F, 1, 7, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 3, 0x28, 2, 6, -1, 0, 0, negate: true, 0);
            lv[Byte33D2At] = 0x1B;
            ShapeZone7(lv, 0, 0x20, 0x23, 0x20, 0x22, 0x130, 0, false, 0x22, 0x21, 0, 0);
            ShapeZone7(lv, 1, 0x0D, 0x0D, 0x0C, 0x0C, 0x16B, 0, false, 0x0D, 0x0C, 0, 0);
            var cell = Cell(lv, 13, 12);
            SetCell(lv, 13, 12, cell & ~0xF00u | 0x500u);
        }

        private void SetUpRoom9(byte[] lv, bool cleared)
        {
            ArmPlaceholder(lv, 0, 0x8B, 0x2D, 1, 0x23);
            ArmPlaceholder(lv, 1, 0x8B, 0x2D, 1, 0x23);
            SetDoorValue(lv, 2, 0x32, flag: false);
            PlaceGroup(g, lv, ReducedGroups, 0, 0x28, 2, 6, -1, 0, 0, negate: true, 0);
            if (!cleared)
            {
                PlaceGroup(g, lv, ReducedGroups, 1, 0x3B, 1, 2, -1, 0, 0, negate: false, 0);
            }
            ShapeZone8(lv, 0, 0x15, 0x15, 0x0D, 0x0D, upper: true, 0x133, flag: false);
            ShapeZone7(lv, 0, 2, 3, 0x1B, 0x1D, 0x135, 0, false, 0, 0, 0, 0);
            lv[LootGroupCountAt] = 0;
        }

        private void SetUpRoom10(byte[] lv, bool cleared)
        {
            if (!cleared)
            {
                SetLootGroup(lv, 0, GroupIds++, word10: 6, word0E: 0x11, word12: 11);
                AddLootEntry(lv, 0, 0x20, 0x21, 0x32, 1);
                AddLootEntry(lv, 0, 0x3B, 0x64, 0x23, 1);
                AddLootEntry(lv, 0, 0x42, 0x6B, 0x23, 1);
                AddLootEntry(lv, 0, 0x40, 0x69, 0x23, 1);
            }
            SetDoorValue(lv, 1, 0x31, flag: false);
            SetDoorValue(lv, 2, 0x23, flag: true);
            if (!cleared)
            {
                PlaceGroup(g, lv, ReducedGroups, 1, 0x3D, 1, 7, -1, 0, 0, negate: false, 0);
                PlaceGroup(g, lv, ReducedGroups, 2, 0x18, 2, 1, 0, 3, 6, negate: false, 0);
                PlaceGroup(g, lv, ReducedGroups, 3, 0x28, 2, 1, 0, 5, 5, negate: false, 0);
            }
            PlaceGroup(g, lv, ReducedGroups, 0, 0x28, 2, 6, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 4, 0x47, 3, 1, -1, 0, 0, negate: true, 1);
            SetWord(lv, ExitCardsAt, 0x13C);
            lv[Byte33D2At] = 0x1C;
            ShapeZone7(lv, 0, 3, 4, 0x21, 0x21, 0x13B, 1, false, 4, 0x21, 0, 0);
            ShapeZone7(lv, 1, 9, 0x0B, 0x22, 0x23, 0x13C, 1, false, 0x0C, 0x23, 0, 0);
        }

        private void SetUpRoom11(byte[] lv, bool cleared)
        {
            if (!cleared)
            {
                SetLootGroup(lv, 0, GroupIds++, word10: 12);
                lv[LootGroupAt(0) + 0xA] = 0x35;
                lv[LootGroupAt(0) + 9] = 0x32;
                AddLootEntry(lv, 0, 0x48, 0x6D, 0x23, 6);
                AddLootEntry(lv, 0, 0x4F, 0x74, 0x23, 3);
                AddLootEntry(lv, 0, 0x4D, 0x72, 0x23, 3);
                AddLootEntry(lv, 0, 0x4E, 0x73, 0x23, 3);
            }
            ArmPlaceholder(lv, 0, 0x8B, 0x2D, 1, 0x19);
            ArmPlaceholder(lv, 1, 0x8B, 0x2D, 1, 0x1E);
            ArmPlaceholder(lv, 2, 0x8B, 0x2D, 1, 0x23);
            SetDoorValue(lv, 1, 0x0F, flag: false);
            if (!cleared)
            {
                PlaceGroup(g, lv, ReducedGroups, 0, 0x2D, 2, 8, -1, 0, 0, negate: false, 0);
                PlaceGroup(g, lv, ReducedGroups, 1, 0x2D, 1, 5, -1, 0, 0, negate: false, 0);
            }
            lv[Byte33D2At] = 0x15;
            ShapeZone7(lv, 0, 0x17, 0x19, 0x17, 0x19, 0x13A, 0, false, 0x18, 0x18, 0, 0);
        }

        // Room 14: three of the 22 random items (all different) lie in loot groups, a door is set, four groups of guards.
        private void SetUpRoom14(byte[] lv)
        {
            var a = RandomItem();
            int b;
            do
            {
                b = RandomItem();
            }
            while (b == a);
            int c;
            do
            {
                c = RandomItem();
            }
            while (c == a || b == c);
            SetLootGroup(lv, 0, GroupIds++);
            lv[LootGroupAt(0) + 8] = 0x2D;
            lv[LootGroupAt(0) + 8] |= 0x80;
            // BUG: the fortress relics are forced to quality 35: the three relics get quality 0x23 and type 0x9F
            // whatever DARKLAND.LST gives them (the relic weapons have qualities 50..85 and weapon types).
            AddLootEntry(lv, 0, a, 0x9F, 0x23, 1);
            SetLootGroup(lv, 1, GroupIds++);
            lv[LootGroupAt(1) + 8] = 0xAD;
            AddLootEntry(lv, 1, b, 0x9F, 0x23, 1);
            SetLootGroup(lv, 2, GroupIds++);
            lv[LootGroupAt(2) + 8] = 0xAD;
            AddLootEntry(lv, 2, c, 0x9F, 0x23, 1);
            SetDoorValue(lv, 0, 0x1E, flag: false);
            PlaceGroup(g, lv, ReducedGroups, 0, 0x48, 2, 4, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 1, 0x49, 3, 6, -1, 0, 0, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 2, 0x18, 2, 1, 0x28, 2, 5, negate: false, 0);
            PlaceGroup(g, lv, ReducedGroups, 3, 0x37, 2, 1, -1, 0, 0, negate: true, 1);
            lv[Byte33D2At] = 0x1E;
            ShapeZone7(lv, 0, 0x13, 0x15, 0x0C, 0x0D, 0x138, 1, true, 0x13, 8, 0, 0);
            SetWord(lv, ExitCardsAt, 0x138);
        }

        /// <summary>One of the 22 random items (0x96..0xAB).</summary>
        // orig: R011+0x16D2 (1432:16D2) draft ("close"); the 22-way table checked against asm
        // DORMANT: the draft's switch falls off the end for a roll outside 1..22, which cannot happen.
        private int RandomItem() => FirstRandomItem - 1 + g.Roll(22);

        private static int LootGroupAt(int group) => LootGroupsAt + LootGroupStride * group;

        /// <summary>Starts loot group <paramref name="group"/>: its number and, for some rooms, its fields +0x0E, +0x10 and +0x12 (−1: left as it is).</summary>
        private static void SetLootGroup(byte[] lv, int group, int number, int word0E = -1, int word10 = -1, int word12 = -1)
        {
            lv[LootGroupAt(group) + 0xC] = (byte)number;
            if (word0E >= 0)
            {
                SetWord(lv, LootGroupAt(group) + 0xE, word0E);
            }
            if (word10 >= 0)
            {
                SetWord(lv, LootGroupAt(group) + 0x10, word10);
            }
            if (word12 >= 0)
            {
                SetWord(lv, LootGroupAt(group) + 0x12, word12);
            }
        }

        /// <summary>Adds an entry (an item type with three bytes and its weight) to loot group <paramref name="group"/> if it holds fewer than four.</summary>
        // orig: R011+0x1794 (1432:1794) draft (byte-matched per the draft notes); checked against asm
        // C: the meaning of the loot groups (+0x300E, 0x2C bytes) and their entries; named after what they hold (item types, up to four).
        private void AddLootEntry(byte[] lv, int group, int type, int b, int c, int d)
        {
            var at = LootGroupAt(group);
            var n = lv[at + 0xB];
            if (n >= 4)
            {
                return;
            }
            var entry = at + 0x14 + 6 * n;
            lv[at + 0xB]++;
            SetWord(lv, entry, type);
            lv[entry + 2] = (byte)b;
            lv[entry + 3] = (byte)c;
            lv[entry + 4] = (byte)d;
            // C: the weight is read from the item list unchecked (the list has more than 171 items).
            lv[entry + 5] = (byte)g.Context.Items[type].Weight;
        }

        /// <summary>
        /// Turns the type-1 placeholder object <paramref name="k"/> into an item pile (type 2, for item types 0x84..0x99) or a
        /// monster (type 4), registering what it holds.
        /// </summary>
        // orig: R011+0x17FA (1432:17FA) draft ("close"); checked against asm
        private static void ArmPlaceholder(byte[] lv, int k, int type, int b, int c, int value)
        {
            var count = (sbyte)lv[ObjectCountAt];
            int i;
            for (i = 0; i < count; i++)
            {
                var at = ObjectAt(i);
                // C: the id is the word at +0x0A (the byte at +0x0B must be 0 too).
                if (Word(lv, at + 6) == TypePlaceholder && Word(lv, at + 0xA) == k)
                {
                    break;
                }
            }
            if (i >= count)
            {
                return;
            }
            var o = ObjectAt(i);
            var old = Word(lv, o + 0xC);
            SetWord(lv, o + 0xC, value);
            var record = PlaceholderRecordsAt + PlaceholderRecordStride * k;
            for (var j = 0; j < 4; j++)
            {
                if (lv[record + 8 + j] != 0)
                {
                    lv[record + 8 + j] = 6;
                }
            }
            var contents = ContentsAt + ContentsStride * old;
            if (type is >= 0x84 and <= 0x99)
            {
                lv[contents + 2] = (byte)(type + 0x7C);
                lv[contents + 3] = (byte)b;
                SetWord(lv, contents, i);
                SetWord(lv, o + 6, TypeItem);
                SetWord(lv, o + 8, 0);
            }
            else
            {
                lv[contents + 8] = (byte)type;
                lv[contents + 9] = (byte)b;
                lv[contents + 6] = (byte)c;
                SetWord(lv, o + 6, TypeMonster);
                SetWord(lv, o + 8, 0);
                SetWord(lv, contents, i);
            }
        }

        /// <summary>
        /// Sets the value of a door: without <paramref name="flag"/> the door table's entry <paramref name="index"/> (both its
        /// bytes, with the top bit set); with it the type-0 object with that index and the object it points to.
        /// </summary>
        // orig: R011+0x1968 (1432:1968) draft ("close"); checked against asm
        // C: the object the first points to is addressed by the unsigned byte at +0x0B (the draft declares it a signed char).
        private static void SetDoorValue(byte[] lv, int index, int value, bool flag)
        {
            if (!flag)
            {
                var entry = DoorTableAt + DoorTableStride * index;
                lv[entry + 9] = lv[entry + 0x17] = (byte)(value | 0x80);
                return;
            }
            var count = (sbyte)lv[ObjectCountAt];
            int i;
            for (i = 0; i < count; i++)
            {
                var at = ObjectAt(i);
                if (Word(lv, at + 6) == 0 && lv[at + 0xA] == index)
                {
                    break;
                }
            }
            if (i < count)
            {
                var o = ObjectAt(i);
                SetWord(lv, o + 0xC, value);
                SetWord(lv, ObjectAt(lv[o + 0xB]) + 0xC, value);
            }
        }

        /// <summary>Gives the type-7 object <paramref name="k"/> its extent in cells and its value and record.</summary>
        // orig: R011+0x1BC0 (1432:1BC0) draft ("close"); checked against asm
        // C: the draft calls type 7 and 8 objects doors; they are rectangles of cells with a value (+0x0C) and a record.
        // C: the width and height are bytes: (b − a + 1) × 32 and (d − c + 1) × 16 wrap at 256.
        private static void ShapeZone7(byte[] lv, int k, int a, int b, int c, int d, int value, int p16, bool flag, int p1a, int p1c, int p1e, int p20)
        {
            if (k is < 0 or >= 5)
            {
                return;
            }
            int room = (sbyte)lv[LevelNumberAt];
            var i = FindObject(lv, TypeZone7, k);
            if (i >= BattlefieldLevel.MaxObjects)
            {
                return;
            }
            var o = ObjectAt(i);
            SetWord(lv, o, a << 5);
            lv[o + 4] = (byte)(b - a + 1 << 5);
            SetWord(lv, o + 2, c << 4);
            lv[o + 5] = (byte)(d - c + 1 << 4);
            if (flag)
            {
                lv[o + 0xB] |= 2;
            }
            SetWord(lv, o + 0xC, value);
            var record = Zone7At + ZoneStride * k;
            lv[record] = (byte)p16;
            lv[record + 1] = (byte)p1a;
            lv[record + 2] = (byte)p1c;
            lv[record + 3] = (byte)p1e;
            lv[record + 4] = (byte)p20;
            if (room == 10 && k == 1)
            {
                SetWord(lv, record + 6, Word(lv, record + 6) - 16);
            }
        }

        /// <summary>Gives the type-8 object <paramref name="k"/> its extent in cells and its value, moving the old value into its record.</summary>
        // orig: R011+0x1CB4 (1432:1CB4) draft ("close"); checked against asm
        // C: the width and height are bytes and wrap at 256.
        private static void ShapeZone8(byte[] lv, int k, int a, int b, int c, int d, bool upper, int value, bool flag)
        {
            if (k is < 0 or >= 5)
            {
                return;
            }
            var i = FindObject(lv, TypeZone8, k);
            if (i >= BattlefieldLevel.MaxObjects)
            {
                return;
            }
            var o = ObjectAt(i);
            SetWord(lv, o, a << 5);
            lv[o + 4] = (byte)(-(a - b) + 1 << 5);
            SetWord(lv, o + 2, c << 4);
            lv[o + 5] = (byte)(d - c + 1 << 4);
            if (flag)
            {
                lv[o + 0xB] |= 1;
            }
            var old = upper ? lv[o + 0xD] : lv[o + 0xC];
            SetWord(lv, o + 0xC, value);
            var record = Zone8At + ZoneStride * k;
            lv[record] = 0;
            lv[record + 3] = old;
        }

        /// <summary>Gives the <paramref name="n"/>th type-5 object its value.</summary>
        // orig: R011+0x1D94 (1432:1D94) draft ("close"); checked against asm
        private static void SetChestValue(byte[] lv, int n, int value)
        {
            var seen = 0;
            for (var i = 0; i < BattlefieldLevel.MaxObjects; i++)
            {
                if (Word(lv, ObjectAt(i) + 6) == TypeChest)
                {
                    if (n == seen)
                    {
                        SetWord(lv, ObjectAt(i) + 0xC, value);
                        return;
                    }
                    seen++;
                }
            }
        }

        /// <summary>The first of the 55 objects with this type and index byte (55 when there is none).</summary>
        private static int FindObject(byte[] lv, int type, int index)
        {
            int i;
            for (i = 0; i < BattlefieldLevel.MaxObjects; i++)
            {
                if (Word(lv, ObjectAt(i) + 6) == type && lv[ObjectAt(i) + 0xA] == index)
                {
                    break;
                }
            }
            return i;
        }
    }
}
