namespace Darklands.Core.Battles.Generation;

/// <summary>One enemy group of the request: an enemy type, its quality and how many.</summary>
internal record struct GroupSpec(int Type, int Quality, int Count);

// Turning the plan into floor images (overlay R007, castle-builder-entry): one image per level, saved as level%d.flr.
internal sealed partial class CastleGenerator
{
    /// <summary>The level buffer, reused for every level like the original's static floor image.</summary>
    // C: the original's buffer (floor-picture) keeps the previous battle's bytes wherever the generator does not write; every reader of those
    // parts is bounded by a count the generator zeroes, so they are never read: zeros are equivalent.
    private readonly BattlefieldLevel _floor = new(new byte[BattlefieldLevel.ImageSize]);

    private readonly int[] _partCount = new int[CastleWork.Levels];                          // DS:DB9B
    private readonly int[] _partOf = new int[CastleWork.Levels * CastleWork.MaxRooms];       // DS:DBBB: connected part of each room

    private readonly List<(int Level, int Kind)> _kindMarks = [];

    private byte[] Img => _floor.Image;

    /// <summary>Builds and saves every level; returns the level the party starts on (relative to the lowest level).</summary>
    /// <param name="battlefield">The battlefield type.</param>
    /// <param name="specs">The three enemy groups of the request (type, quality, count); overwritten for the types that choose their own.</param>
    // orig: castle-builder-entry R007+0x0000 (15AF:0000) draft; checked against asm (order of calls)
    // C: the start level is relative to the lowest level (layout-battlefield-entry subtracts it afterwards).
    public int BuildLevels(int battlefield, GroupSpec[] specs)
    {
        var chests = 0;
        for (var l = _lowestLevel; l <= _highestLevel; l++)
        {
            SplitIntoParts(l);
        }
        WriteTileDescription();
        ChooseStart(battlefield, out var start, out var side);
        ChooseGroups(battlefield, specs);
        for (var l = _lowestLevel; l <= _highestLevel; l++)
        {
            ClearFloor(l);
            WritePartLists(l);
            OpenExitsAndDecorateWalls(battlefield, l);
            if (l == 1)
            {
                MarkGate(l);
            }
            LinkParts(l);
            WriteStairExits(l);
            PlaceForces(l, start, battlefield, side, specs);
            Furnish(battlefield, l, ref chests);
            _g.Save(_floor);
        }
        ApplyKindMarks();
        return start - _lowestLevel;
    }

    /// <summary>Prepares the buffer for level l: its number, empty record counts, void cells.</summary>
    // orig: the first statements of the level loop of castle-builder-entry R007+0x0000 (15AF:0000) draft; checked against asm
    private void ClearFloor(int l)
    {
        _floor.LevelNumber = l - _lowestLevel;
        Img[0x3326] = Img[0x300C] = Img[0x2FB6] = Img[0x2488] = Img[0x2184] = Img[0x1F52] = 0;
        _floor.ExitCards = new LevelExitCards(-2, -2, -2, -2, -2);
        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 40; y++)
            {
                SetCell(x, y, 0x80000000);
                Img[0x1900 + x * 40 + y] = 0;
            }
        }
    }

    // ---- the floor image's cells ----

    // orig: floor-picture R136+0x0000 (struct level in): cell[x][y], the CB macro for single bytes, and hgt[x][y]
    private uint CellBits(int x, int y) => BitConverter.ToUInt32(Img, (x * 40 + y) * 4);

    private void SetCell(int x, int y, uint bits) => BitConverter.TryWriteBytes(Img.AsSpan((x * 40 + y) * 4, 4), bits);

    /// <summary>Byte <paramref name="k"/> of the cell long: 0 east wall and furniture, 1 west wall and its decoration, 2 north wall and its decoration, 3 south wall and the furniture's facing.</summary>
    private int CellByte(int x, int y, int k) => Img[(x * 40 + y) * 4 + k];

    private void SetCellByte(int x, int y, int k, int value) => Img[(x * 40 + y) * 4 + k] = (byte)value;

    private int Height(int x, int y) => Img[0x1900 + x * 40 + y];

    private void SetHeight(int x, int y, int value) => Img[0x1900 + x * 40 + y] = (byte)value;

    /// <summary>Splits the rooms of level l into connected parts (rooms linked by open passages).</summary>
    // orig: R007+0x0188 (15AF:0188) draft
    private void SplitIntoParts(int l)
    {
        _partCount[l] = 0;
        for (var i = 0; i < 30; i++)
        {
            _partOf[l * 30 + i] = -1;
        }
        var seen = new int[30];
        for (var i = 0; i < _roomCount[l]; i++)
        {
            if (seen[i] == 0)
            {
                for (var j = 0; j < _roomCount[l]; j++)
                {
                    if (seen[j] == 0 && IsLinked(l, i, j))
                    {
                        seen[j] = 1;
                        _partOf[l * 30 + j] = _partCount[l];
                    }
                }
                _partCount[l]++;
            }
        }
    }

    /// <summary>The connected part a room belongs to: DS:DBBB[level][room].</summary>
    // orig: DS:DBBB, filled by R007+0x0188
    private int PartOf(int l, int room) => _partOf[l * 30 + room];

    /// <summary>The tile description of a keep: the hall's floor and the wall tiles depend on a coin toss and the keep's area.</summary>
    // orig: R007+0x025A (15AF:025A) draft
    private void WriteTileDescription()
    {
        var d = _g.Terrain;
        var r = Roll(2);
        var area = (Room(GroundLevel, 0, Y1) - Room(GroundLevel, 0, Y0) + 1) * (Room(GroundLevel, 0, X1) - Room(GroundLevel, 0, X0) + 1);
        d.BackgroundPalette = 0;
        d.SetSetName(TileKind.Floor, "keep");
        d.SetTile(TileKind.Floor, 1, r == 1 ? 1 : 0);
        d.SetSetName(TileKind.FloorDecoration, "keep");
        d.SetAnimatedBelow(TileKind.FloorDecoration, 1);
        d.SetTile(TileKind.FloorDecoration, 1, 10);
        d.SetTile(TileKind.FloorDecoration, 2, 11);
        d.SetTile(TileKind.FloorDecoration, 4, 9);
        d.SetTile(TileKind.FloorDecoration, 3, 8);
        d.SetSetName(TileKind.NorthWall, "keep1");
        d.SetTile(TileKind.NorthWall, 1, r == 1 ? 0 : 1);
        d.SetTile(TileKind.NorthWall, 2, 10);
        d.SetTile(TileKind.NorthWall, 4, area > 0x32 ? 2 : 6);
        d.SetTile(TileKind.NorthWall, 5, 7);
        // DORMANT: dice-roll(1, 2) is never 0, so the second arm (tiles 3 and 4) is not reached.
        if (Roll(2) != 0)
        {
            d.SetTile(TileKind.NorthWall, 8, 8);
            d.SetTile(TileKind.NorthWall, 9, 9);
        }
        else
        {
            d.SetTile(TileKind.NorthWall, 8, 3);
            d.SetTile(TileKind.NorthWall, 9, 4);
        }
        d.SetSetName(TileKind.NorthWallDecoration, "keep");
        d.SetTile(TileKind.NorthWallDecoration, 1, 0);
        d.SetAnimatedBelow(TileKind.NorthWallDecoration, 2);
        d.SetTile(TileKind.NorthWallDecoration, 2, 8);
        d.SetTile(TileKind.NorthWallDecoration, 4, 14);
        d.SetTile(TileKind.NorthWallDecoration, 3, 10);
        d.SetTile(TileKind.NorthWallDecoration, 5, 11);
        d.SetTile(TileKind.NorthWallDecoration, 6, 12);
        d.SetSetName(TileKind.WestWall, "keep1");
        d.SetTile(TileKind.WestWall, 1, r == 1 ? 0 : 1);
        d.SetTile(TileKind.WestWall, 2, 10);
        d.SetTile(TileKind.WestWall, 3, 2);
        d.SetTile(TileKind.WestWall, 4, area > 0x32 ? 5 : 6);
        d.SetTile(TileKind.WestWall, 5, 3);
        d.SetTile(TileKind.WestWall, 8, 7);
        d.SetTile(TileKind.WestWall, 9, 8);
        d.SetSetName(TileKind.WestWallDecoration, "keep");
        d.SetTile(TileKind.WestWallDecoration, 1, 0);
        d.SetAnimatedBelow(TileKind.WestWallDecoration, 2);
        d.SetTile(TileKind.WestWallDecoration, 2, 6);
        d.SetSetName(TileKind.Furniture, "keep");
        int[] furniture = [13, 5, 8, 9, 0, 1, 3, 11, 2, 17, 7, 16, 6];
        for (var number = 1; number <= furniture.Length; number++)
        {
            d.SetTile(TileKind.Furniture, number, furniture[number - 1]);
        }
        d.SetAnimatedBelow(TileKind.Furniture, 12);
        d.SetTile(TileKind.Furniture, 14, 12);
        d.SetTile(TileKind.Furniture, 15, 4);
    }

    /// <summary>Where the party starts: the level (and, for some types, a second level that holds the enemy leader), by battlefield type.</summary>
    // orig: R007+0x1A5E (15AF:1A5E) draft
    // C: the second level (side) is -1 for the types that do not set it.
    private void ChooseStart(int battlefield, out int start, out int side)
    {
        var n = _highestLevel - _lowestLevel + 1;
        side = -1;
        start = 0;
        int m;
        switch (battlefield)
        {
            case 0:
                start = 1;
                break;
            case 1:
            case 2:
            case 4:
            case 0x7A:
                m = _topLevel - 1 > _highestLevel ? _highestLevel : _topLevel - 1;
                start = side = Roll(m - 1) + 1;
                break;
            case 3:
                start = side = 2;
                break;
            case 5:
                start = 1;
                do
                {
                    side = Roll(_highestLevel - _lowestLevel) + _lowestLevel - 1;
                }
                while (side == 1 && n > 1 && Roll(4) > 1);
                break;
            case 6:
            case 7:
                start = side = 1;
                break;
            case 8:
                start = _lowestLevel >= 0 ? _lowestLevel : 0;
                break;
            case 0x27:
                start = _highestLevel;
                side = 2;
                break;
            case 0x28:
                start = 1;
                side = 2;
                break;
            case 0x7B:
                start = Roll(_highestLevel - 1) + 1;
                m = _topLevel - 1 > _highestLevel ? _highestLevel : _topLevel - 1;
                side = Roll(m - _lowestLevel + 1) + _lowestLevel - 1;
                break;
            case 0x7C:
            case 0x7D:
                m = _topLevel - 1 > _highestLevel ? _highestLevel : _topLevel - 1;
                start = 1;
                side = Roll(m - _lowestLevel + 1) + _lowestLevel - 1;
                break;
            case 0x7E:
                start = 1;
                side = Roll(2);
                break;
        }
    }

    /// <summary>Replaces the request's three groups with the ones the battlefield type calls for (nothing changes for types 0, 0x27 and 0x28).</summary>
    // orig: R007+0x1BFE (15AF:1BFE) draft; checked against asm
    // C: type 8 reads the request's second count as the first group's quality and the second group's count.
    private void ChooseGroups(int battlefield, GroupSpec[] specs)
    {
        var typeA = 0;
        var qualityA = 0;
        var typeB = 0;
        var qualityB = 0;
        var countB = 0;
        var typeC = -1;
        var qualityC = 1;
        var lore = _g.Context.LoreLevel();
        switch (battlefield)
        {
            case 1:
            case 2:
            case 4:
            case 5:
            case 6:
            case 7:
            case 0x7A:
                if (specs[0].Type == 0x24 || specs[0].Type == 0x26)
                {
                    typeA = 0x26;
                    qualityA = 2;
                }
                else
                {
                    typeA = 0x23;
                    qualityA = 3;
                }
                typeC = typeB = 0x12;
                qualityB = 4;
                qualityC = 3;
                break;
            case 3:
                typeA = 0x16;
                qualityA = lore / 7 + 1;
                typeC = typeB = 0x12;
                qualityB = 4;
                qualityC = 3;
                break;
            case 8:
                typeA = 0;
                qualityA = specs[1].Count;
                typeB = 3;
                qualityB = 2;
                countB = qualityA;
                break;
            case 0x7B:
            case 0x7C:
            case 0x7D:
            case 0x7E:
                typeA = 0x50;
                qualityA = (lore + 2) / 3;
                if (qualityA > 3)
                {
                    qualityA = 3;
                }
                // DORMANT: the party's lore level is 1..10, so the quotient is at least 1.
                if (qualityA < 1)
                {
                    qualityA = 1;
                }
                typeB = 0x4F;
                qualityB = (lore + 4) / 2;
                if (qualityB > 5)
                {
                    qualityB = 5;
                }
                // DORMANT: the lore level is at least 1, so the quotient is at least 2.
                if (qualityB < 2)
                {
                    qualityB = 2;
                }
                countB = (lore + 6) / 2;
                break;
            default:
                return;
        }
        specs[0] = new GroupSpec(typeA, qualityA, 1);
        specs[1] = new GroupSpec(typeB, qualityB, countB);
        specs[2] = new GroupSpec(typeC, qualityC, 0);
    }
}
