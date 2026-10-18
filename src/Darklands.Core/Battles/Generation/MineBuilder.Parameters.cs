namespace Darklands.Core.Battles.Generation;

/// <summary>The mine's look: the tile description, and the wall, floor and decoration parameters of each mine type and section.</summary>
internal sealed partial class MineBuilder
{
    /// <summary>DS:DFF9 / DS:DFFD: the two wall tiles for north and south walls (chosen at random per wall) of the section being built.</summary>
    private int _northWallA;
    private int _northWallB;

    /// <summary>DS:E001 / DS:E005: the two wall tiles for west and east walls.</summary>
    private int _westWallA;
    private int _westWallB;

    /// <summary>DS:E009: the lowest floor tile of the section being built.</summary>
    private int _floorTileBase;

    /// <summary>The tile description for the mines: the tile sets "mine" and which of their tiles fill the numbers.</summary>
    /// <returns>
    /// The word the second pair of random draws leaves in the function's stack frame (<c>r[1]</c>), which a later function reads as an
    /// uninitialised local (see <see cref="OneSectionMine"/>).
    /// </returns>
    // orig: R009+0x3FA2 (182C:0002) draft (byte-matched per the draft notes); checked against asm
    // (the frame: r[0] at bp-4, r[1] at bp-2); tests: Oracle:mine.json
    private int WriteTileDescription(int t)
    {
        var d = _g.Terrain;
        d.BackgroundPalette = 2;
        d.SetSetName(TileKind.Floor, "mine");
        SetTiles(d, TileKind.Floor, 1, 4, 5, 7, 8, 6, 10, 11, 12, 0, 1, 2, 3);
        d.SetSetName(TileKind.FloorDecoration, "mine");
        SetTiles(d, TileKind.FloorDecoration, 2, 9, 10, 8, 11, 12);
        d.SetSetName(TileKind.NorthWall, "mine");
        d.SetTile(TileKind.NorthWall, 1, 0);
        d.SetTile(TileKind.NorthWall, 2, 9);
        d.SetTile(TileKind.NorthWall, 3, 1);
        d.SetTile(TileKind.NorthWall, 7, 11);
        d.SetTile(TileKind.NorthWall, 8, 10);
        d.SetTile(TileKind.NorthWall, 9, 3);
        d.SetTile(TileKind.NorthWall, 10, -1);
        d.SetTile(TileKind.NorthWall, 11, 2);
        var frameWord = 0;
        if (t is 0x0D or 0x83)
        {
            SetTiles(d, TileKind.NorthWall, 4, 4, 7, 8);
        }
        else
        {
            d.SetTile(TileKind.NorthWall, 4, 5);
            frameWord = TwoOfThree(d, TileKind.NorthWall, 5, 4, 7, 8);
        }
        d.SetSetName(TileKind.NorthWallDecoration, "mine");
        d.SetAnimatedBelow(TileKind.NorthWallDecoration, 1);
        SetTiles(d, TileKind.NorthWallDecoration, 1, 6, 16, 8, 10, 15, 14);
        d.SetSetName(TileKind.WestWall, "mine");
        SetTiles(d, TileKind.WestWall, 1, 1, 4, 3);
        d.SetTile(TileKind.WestWall, 7, 6);
        d.SetTile(TileKind.WestWall, 8, 11);
        d.SetTile(TileKind.WestWall, 9, 0);
        d.SetTile(TileKind.WestWall, 10, 10);
        d.SetTile(TileKind.WestWall, 11, 2);
        if (t is 0x0D or 0x83)
        {
            SetTiles(d, TileKind.WestWall, 4, 5, 9, 7);
        }
        else
        {
            d.SetTile(TileKind.WestWall, 4, 8);
            frameWord = TwoOfThree(d, TileKind.WestWall, 5, 5, 9, 7);
        }
        d.SetSetName(TileKind.Furniture, "mine");
        SetTiles(d, TileKind.Furniture, 1, 4, 3, 0x17, 6, 0x15, 0x16, 1, 0);
        d.SetAnimatedBelow(TileKind.Furniture, 9);
        SetTiles(d, TileKind.Furniture, 10, 12, 5, 10, 11, 13, 2);
        switch (t)
        {
            case 0x0B or 0x82:
                d.SetTile(TileKind.Furniture, 9, 9);
                break;
            case 0x0E or 0x84:
                d.SetTile(TileKind.Furniture, 9, 7);
                break;
            case 0x0F or 0x85:
                d.SetTile(TileKind.Furniture, 9, 8);
                break;
            case 0x10 or 0x86:
                d.SetTile(TileKind.Furniture, 9, 20);
                d.SetAnimatedBelow(TileKind.Furniture, 9);
                break;
            default:
                d.SetTile(TileKind.Furniture, 9, 2);
                break;
        }
        return frameWord;
    }

    /// <summary>Two different wall tiles out of three, drawn one after the other (the second is redrawn until it differs); returns the second draw.</summary>
    private int TwoOfThree(TerrainDescription d, TileKind kind, int firstNumber, int a, int b, int c)
    {
        var tiles = new[] { a, b, c };
        var first = Roll(3);
        int second;
        do
        {
            second = Roll(3);
        }
        while (second == first);
        d.SetTile(kind, firstNumber, tiles[first - 1]);
        d.SetTile(kind, firstNumber + 1, tiles[second - 1]);
        return second;
    }

    private static void SetTiles(TerrainDescription d, TileKind kind, int first, params int[] tiles)
    {
        for (var i = 0; i < tiles.Length; i++)
        {
            d.SetTile(kind, first + i, tiles[i]);
        }
    }

    /// <summary>
    /// The wall tiles of the section (DS:DFF9, DS:DFFD, DS:E001, DS:E005) by mine type and section; returns how long a run of solid walls
    /// tends to be (0 none).
    /// </summary>
    // orig: R009+0x436A (182C:03CA) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int WallRunLength(int t, int section)
    {
        _northWallA = _westWallA = _northWallB = _westWallB = 9;
        if (Roll(2) == 1)
        {
            _westWallB = 10;
        }
        var run = 0;
        switch (t)
        {
            case 0x0A:
                SetRandomWalls();
                break;
            case 0x0B or 0x82:
                switch (section)
                {
                    case 1 or 2 or 3:
                        SetRandomWalls();
                        break;
                    case 11:
                        run = 6;
                        break;
                    case 13:
                        if (Roll(2) == 1)
                        {
                            SetAllWalls(11);
                        }
                        run = 6;
                        break;
                    case 14:
                        SetAllWalls(11);
                        run = 6;
                        break;
                }
                break;
            case 0x0D or 0x83:
                switch (section)
                {
                    case 1:
                        SetRandomWalls();
                        break;
                    case 2 or 6 or 7:
                        break;
                    case 8:
                        SetAllWalls(11);
                        break;
                    case 12:
                        SetAllWalls(3);
                        run = 1;
                        break;
                    default:
                        run = 6;
                        break;
                }
                break;
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                switch (section)
                {
                    case 1:
                        SetRandomWalls();
                        break;
                    case 2 or 3 or 4 or 9 or 13:
                        break;
                    case 8 or 12:
                        SetAllWalls(11);
                        run = 6;
                        break;
                    default:
                        if (Roll(2) == 1)
                        {
                            SetAllWalls(11);
                        }
                        run = 6;
                        break;
                }
                break;
            case 0x6C:
                SetAllWalls(11);
                break;
            case 0x6D:
                SetAllWalls(3);
                break;
        }
        return run;
    }

    /// <summary>Walls of tile 1 or 2 (one draw) in both directions.</summary>
    private void SetRandomWalls() => _northWallA = _westWallA = Roll(2) == 1 ? 1 : 2;

    private void SetAllWalls(int tile) => _northWallA = _westWallA = _northWallB = _westWallB = tile;

    /// <summary>The wall tile (base and number of choices) for the door frames of the section.</summary>
    // orig: R009+0x4542 (182C:05A2) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private static (int Base, int Range) DoorWallParameters(int t, int section)
    {
        var range = 2;
        var tile = 5;
        switch (t)
        {
            case 0x0A:
                (range, tile) = (1, 4);
                break;
            case 0x0B or 0x82:
                if (section is 1 or 2 or 3 or 12)
                {
                    (range, tile) = (1, 4);
                }
                break;
            case 0x0D or 0x83:
                (range, tile) = (1, 4);
                break;
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                if (section == 1)
                {
                    (range, tile) = (1, 4);
                }
                break;
        }
        return (tile, range);
    }

    /// <summary>The wall decoration (base tile, number of kinds and the odds 1 in n per wall) of the section.</summary>
    // orig: R009+0x45BA (182C:061A) draft (close per the draft notes: jump-table layout); checked
    // against asm (every arm of both jump tables); tests: Oracle:mine.json
    private static (int Base, int Kinds, int Odds) WallDecorationParameters(int t, int section)
    {
        int kinds = 6, tile = 1, odds = 8;
        switch (t)
        {
            case 0x0A:
                kinds = 4;
                break;
            case 0x0B or 0x82:
                switch (section)
                {
                    case 1 or 2 or 3:
                        kinds = 4;
                        break;
                    case 9 or 10:
                        kinds = 5;
                        break;
                    case 11:
                        tile = kinds = 3;
                        break;
                    case 13 or 14:
                        (kinds, tile) = (4, 3);
                        break;
                }
                break;
            case 0x0C or 0x7F or 0x80 or 0x81:
                (kinds, tile, odds) = (2, 5, 3);
                break;
            case 0x0D or 0x83:
                switch (section)
                {
                    case 1:
                        kinds = 4;
                        break;
                    case 2:
                        break;
                    case 5 or 7:
                        (kinds, tile, odds) = (4, 3, 6);
                        break;
                    case 8:
                        (kinds, tile, odds) = (4, 3, 12);
                        break;
                    case 10:
                        (kinds, odds) = (5, 6);
                        break;
                    case 11:
                        (tile, kinds, odds) = (3, 3, 12);
                        break;
                    case 12:
                        odds = kinds = 0;
                        break;
                    default:
                        odds = 6;
                        break;
                }
                break;
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                switch (section)
                {
                    case 1:
                        kinds = 4;
                        break;
                    case 2 or 3 or 4:
                        break;
                    case 6 or 10:
                        (tile, kinds, odds) = (3, 3, 6);
                        break;
                    default:
                        (kinds, tile, odds) = (4, 3, 6);
                        break;
                }
                break;
            case >= 0x66 and <= 0x69:
                (kinds, tile, odds) = (2, 4, 10);
                break;
            case 0x6C:
                (kinds, tile, odds) = (4, 3, 12);
                break;
            case 0x6D:
                odds = kinds = 0;
                break;
        }
        return (tile, kinds, odds);
    }

    /// <summary>The lowest floor tile (DS:E009) and the number of floor tiles to choose from, by mine type and section.</summary>
    // orig: R009+0x4770 (182C:07D0) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int FloorTileRange(int t, int section)
    {
        var range = 1;
        _floorTileBase = 1;
        switch (t)
        {
            case 0x0A:
                range = RandomFloorTiles(range);
                break;
            case 0x0B or 0x82:
                switch (section)
                {
                    case 1 or 2 or 3:
                        range = RandomFloorTiles(range);
                        break;
                    case 11 or 13 or 14:
                        _floorTileBase = _northWallA == 11 ? 9 : 10;
                        break;
                    default:
                        (_floorTileBase, range) = (5, 4);
                        break;
                }
                break;
            case 0x0D or 0x83:
                switch (section)
                {
                    case 1:
                        range = RandomFloorTiles(range);
                        break;
                    case 8:
                        (_floorTileBase, range) = (11, 2);
                        break;
                    case 12:
                        _floorTileBase = 10;
                        break;
                    default:
                        (_floorTileBase, range) = (5, 4);
                        break;
                }
                break;
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                switch (section)
                {
                    case 1:
                        range = RandomFloorTiles(range);
                        break;
                    case 2 or 3 or 4:
                        (_floorTileBase, range) = (5, 4);
                        break;
                    case 9 or 13:
                        (_floorTileBase, range) = (11, 2);
                        break;
                    default:
                        _floorTileBase = _northWallA == 11 ? 9 : 10;
                        break;
                }
                break;
            case 0x6C:
                (_floorTileBase, range) = (11, 2);
                break;
            case 0x6D:
                _floorTileBase = 10;
                break;
        }
        return range;
    }

    /// <summary>One draw: floor tile 1 (range 1), 2 (range 1) or 3 (range 2).</summary>
    private int RandomFloorTiles(int range)
    {
        switch (Roll(3))
        {
            case 2:
                _floorTileBase = 2;
                break;
            case 3:
                _floorTileBase = 3;
                range = 2;
                break;
        }
        return range;
    }

    /// <summary>The floor decoration (base tile, number of kinds and the odds 1 in n per cell) of the section.</summary>
    // orig: R009+0x48D8 (182C:0938) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private static (int Base, int Kinds, int Odds) FloorDecorationParameters(int t, int section)
    {
        int tile = 0, kinds = 0, odds = 0;
        switch (t)
        {
            case 0x0D or 0x83:
                switch (section)
                {
                    case 3 or 4 or 5 or 6 or 7 or 10 or 11:
                        (tile, kinds, odds) = (2, 1, 0x40);
                        break;
                    case 8:
                        (tile, kinds, odds) = (2, 1, 12);
                        break;
                }
                break;
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                if (section is 9 or 13)
                {
                    (tile, kinds, odds) = (2, 1, 12);
                }
                break;
            case 0x6C:
                (tile, kinds, odds) = (2, 1, 12);
                break;
        }
        return (tile, kinds, odds);
    }
}
