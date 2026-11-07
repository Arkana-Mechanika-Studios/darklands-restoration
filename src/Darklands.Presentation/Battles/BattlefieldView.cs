using Darklands.Content.Formats.Battles;
using Darklands.Core.Battles;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

/// <summary>
/// The battlefield as the original draws it (<c>floor-view-paint</c>): a window of 14 rows ×
/// 10 cells around a centre cell, in the oblique projection where a map point (x, y) is
/// shown at (x − y + 136, y + 105) relative to the centre cell's corner. Cells are drawn
/// row by row: north wall and its decoration, floor and floor decoration, west wall and its
/// decoration, furniture, then the east and south walls.
/// </summary>
// orig: floor-view-paint R017+0x0000 (1EF8:0000) draft; tests: BattlefieldViewTests
// orig: R000+0x0114 (0E76:0114) draft (the slot screen-position tables, R136+0x415E..4b4a)
// The wall-hiding and corner-variant conditions were checked against the asm (R017+0x01FD..0x029E)
// and agree with the old engine's CleanCombatTerrain.
public sealed class BattlefieldView
{
    /// <summary>Left edge of the view on the screen (the party panel is left of it).</summary>
    public const int Left = 60;
    public const int Width = 0x104;
    public const int Height = 200;
    public const int Rows = 14;
    public const int Columns = 10;
    private const int ProjectionX = 0x88;
    private const int ProjectionY = 0x69;

    public static readonly Clip Area = new(Left, 0, Left + Width, Height);

    /// <summary>The centre cell (the view's scroll position).</summary>
    public int CentreColumn { get; set; }

    public int CentreRow { get; set; }

    public int CameraX => CentreColumn * BattlefieldLevel.CellWidth;

    public int CameraY => CentreRow * BattlefieldLevel.CellHeight;

    /// <summary>The screen position of a map point.</summary>
    // orig: fighter-screen-position R014+0x99F6 (14F7:31E6) draft (foot point: x − y + 0x88, y + 0x69)
    public (int X, int Y) ToScreen(int x, int y) =>
        (Left + x - CameraX - (y - CameraY) + ProjectionX, y - CameraY + ProjectionY);

    /// <summary>
    /// The map point under a screen position (on the floor): the pointer's ground point of the order, group and throw cursors.
    /// It is the exact inverse of <see cref="ToScreen"/>.
    /// </summary>
    // orig: fight-mouse R014+0xB576 (19CD:0006), the hover R014+0xC044 and the click R014+0xC33C
    // R014+0xC33C draft, checked against asm (R014+0xB68A group cursor, R014+0xC22C hover, R014+0xC5FD click): with mx = pointer-column − 60,
    // h = pointer-row − R136+0x41F4, map y = h + DS:C293, map x = h − R136+0x4BE0 + mx − 16 + DS:C26C. R136+0x41F4 / R136+0x4BE0 are entry 75
    // (row 7, column 5) of the slot tables R000+0x0114 fills: the floor y −7 + 16 × 7 = 0x69 and x −(24 + 16) + 32 × 5 = 0x78;
    // DS:C26C / DS:C293 are the view origin (centre cell × 32, × 16). 0x78 + 16 = 0x88, the projection's x offset: no further shift.
    public (int X, int Y) ToMap(int screenX, int screenY)
    {
        var (originX, originY) = SlotFloor(7, 5);
        var h = screenY - originY;
        return (h - originX + (screenX - Left) - 16 + CameraX, h + CameraY);
    }

    /// <summary>Where a cursor sprite of the given size standing on map point (x, y) is drawn: centred on the point, its bottom on it (screen pixels).</summary>
    // orig: fight-draw R014+0x6818 (14F7:0008) draft (the ground cursor:
    // sy = DS:E1B6 − DS:C293, DS:E1C2 = DS:E1B4 − DS:C26C − sy + 0x88 − DS:E1B8 / 2, DS:E1BE = sy − DS:E1C0 + 0x69)
    public (int X, int Y) CursorTopLeft(int x, int y, int width, int height)
    {
        var sy = y - CameraY;
        return (Left + x - CameraX - sy + ProjectionX - width / 2, sy - height + ProjectionY);
    }

    /// <summary>The cell shown in view slot (row, column), relative to the centre.</summary>
    // orig: DS:1FC6 / DS:2052 (the slot offset tables floor-view-paint reads); BattlefieldViewTests compares this formula with the EXE's tables.
    public static (int Dx, int Dy) SlotOffset(int row, int column) => (column + row / 2 - 8, row - 7);

    /// <summary>Screen position (relative to the view) of the floor tile of a slot.</summary>
    // orig: R000+0x0114 (0E76:0114) draft: rows v = −7 + 16·r, columns c = −(24 + 16·(r & 1)) + 32·j; north
    // walls at (c + 16, v − 25), west walls and furniture at (c, v − 25)/(c, v), east walls at (c + 32, v − 25), south walls at (c, v − 9).
    public static (int X, int Y) SlotFloor(int row, int column) => (-(24 + 16 * (row & 1)) + 32 * column, -7 + 16 * row);

    public void Draw(Canvas canvas, BattlefieldLevel level, BattlefieldTiles tiles)
    {
        canvas.Fill(Left, 0, Width, Height, 0);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                DrawSlot(canvas, level, tiles, row, column);
            }
        }
    }

    /// <summary>Draws the tiles of one cell.</summary>
    // C: R017+0x0000 also redraws the party panel and draws the loot piles of the cell between the floor and the west wall: the battle
    // screen draws the panel (BattleScreen.DrawPanel) and the piles (tb.LootPiles) itself. A missing tile clears DS:E22F and then DS:E230,
    // two latches only R017+0x0000 itself reads (the root and all other overlays never touch them), so a missing tile is simply not drawn
    // here with no other effect. The animated decorations (EffectField.AnimatedPieces) show the
    // frame R014+0x6818 drew by lot for them (R014+0x8286..: 20A5:6EEA / 6F2A / 3474[n × 2 + roll-n(2)], the second frame
    // possibly none).
    /// <summary>The frame (0, 1; −1 none) the renderer drew of the animated piece of a layer (0 north wall, 1 west wall, 2 floor) at a cell.</summary>
    public Func<int, int, int, int>? PieceFrame { get; set; }

    private RowRunSprite? Frame(TileDecoration decoration, int layer, int x, int y) =>
        PieceFrame?.Invoke(layer, x, y) == 1 ? decoration.SecondFrame : decoration.Sprite;

    public void DrawSlot(Canvas canvas, BattlefieldLevel level, BattlefieldTiles tiles, int row, int column)
    {
        var (dx, dy) = SlotOffset(row, column);
        var x = CentreColumn + dx;
        var y = CentreRow + dy;
        if (x <= 0 || x >= BattlefieldLevel.Size - 1 || y <= 0 || y >= BattlefieldLevel.Size - 1)
        {
            return;
        }
        var cell = level[x, y];
        if (cell.IsVoid)
        {
            return;
        }
        var (fx, fy) = SlotFloor(row, column);
        fx += Left;
        var wallY = fy - 25;
        var clip = Area;

        var northWall = NorthWallPiece(level, tiles, x, y, cell);
        SpriteDrawing.Draw(canvas, northWall, fx + 16, wallY, clip);
        var northHidden = cell.NorthWall != 0 && HasContent(level[x, y - 1]);
        if (!northHidden && tiles.NorthWallDecorations[cell.NorthWallDecoration] is { } northDecoration)
        {
            SpriteDrawing.Draw(canvas, Frame(northDecoration, 0, x, y), fx + 16 + northDecoration.OffsetX, wallY + northDecoration.OffsetY, clip);
        }
        SpriteDrawing.Draw(canvas, tiles.Floors[level.FloorTile(x, y)], fx, fy, clip);
        if (tiles.FloorDecorations[level.FloorDecoration(x, y)] is { } floorDecoration)
        {
            SpriteDrawing.Draw(canvas, Frame(floorDecoration, 2, x, y), fx, fy, clip);
        }
        var westWall = WestWallPiece(level, tiles, x, y, cell);
        SpriteDrawing.Draw(canvas, westWall, fx, wallY, clip);
        var westHidden = cell.WestWall != 0 && HasContent(level[x - 1, y]);
        if (!westHidden && tiles.WestWallDecorations[cell.WestWallDecoration] is { } westDecoration)
        {
            SpriteDrawing.Draw(canvas, Frame(westDecoration, 1, x, y), fx + westDecoration.OffsetX, wallY + westDecoration.OffsetY, clip);
        }
        if (tiles.Furniture[cell.Furniture] is { } furniture)
        {
            var o = cell.FurnitureOrientation;
            SpriteDrawing.Draw(canvas, furniture.Sprite, fx + furniture.SpriteX[o], fy + furniture.SpriteY[o], clip);
        }
        // C: the original tests "east wall nibble or the east neighbour's west wall" but draws the west-wall tile of this
        // cell's own nibble; with a zero nibble that tile slot is 0 (cleared by battle_state_reset), so nothing is drawn.
        if (cell.EastWall != 0)
        {
            SpriteDrawing.Draw(canvas, tiles.WestWalls[cell.EastWall]?.JoinedBothEnds, fx + 32, wallY, clip);
        }
        // C: likewise "south wall nibble or the south neighbour's north wall", drawn with this cell's nibble.
        if (cell.SouthWall != 0)
        {
            SpriteDrawing.Draw(canvas, tiles.NorthWalls[cell.SouthWall]?.JoinedBothEnds, fx, fy - 9, clip);
        }
    }

    /// <summary>The cell shown in view slot (row, column), or null where the view draws nothing (the map's edge or a void cell).</summary>
    public (int X, int Y)? SlotCell(BattlefieldLevel level, int row, int column)
    {
        var (dx, dy) = SlotOffset(row, column);
        var x = CentreColumn + dx;
        var y = CentreRow + dy;
        if (x <= 0 || x >= BattlefieldLevel.Size - 1 || y <= 0 || y >= BattlefieldLevel.Size - 1 || level[x, y].IsVoid)
        {
            return null;
        }
        return (x, y);
    }

    /// <summary>
    /// The wall pieces of view slot (row, column) as the battle's redraw queues them into its depth-sorted list, in its order: the
    /// north wall, its decoration, the west wall, its decoration, the east wall, the furniture, the south wall. An animated decoration
    /// (number below the tile set's limit) is left out: it has its own entry each frame. A decoration or furniture whose sprite is
    /// missing still has an entry (it draws nothing); a missing wall has none. Points are relative to the view.
    /// </summary>
    // orig: R014+0x88E6 (14F7:20D6) checked against the asm: the slot's record R136+0x4EB8 + 0x12 × slot (filled by R017+0x0000: the
    // C: wall sprites drawn, the decorations' and the furniture's numbers) queued through R014+0x8724 with the slot's map row × 16 (r)
    // C: and column × 32 (c) (R136+0x36C8 / 0x363C): 0x7D00 north wall (c, r, r), 0x7D01 its decoration if its number >= DS:C12E
    // C: (c + 4, r, r), 0x7D03 west wall (c, r + 1, r + 8), 0x7D04 its decoration if >= DS:C17F (c + 1, r + 8, r + 8), 0x7D05 east wall
    // C: (c + 0x1F, r + 9, r + 0xF), 0x7D0A furniture t in orientation o (c + DS:D7BE[4t + o], max(r + DS:D802[4t + o] − DS:C11D[t] / 2,
    // C: r + 1), min(r + DS:D802[4t + o] + DS:C11D[t] / 2, r + 0xE)), 0x7D14 south wall (c + 0x10, r + 0xF, r + 0xF). The record's
    // C: last two words (the floor tile and its decoration) go to lists A and B (R014+0x87E4 / R014+0x8862), drawn before the sorted list.
    public List<WallPiece> WallPieces(BattlefieldLevel level, BattlefieldTiles tiles, TerrainDescription terrain, int row, int column)
    {
        var pieces = new List<WallPiece>();
        if (SlotCell(level, row, column) is not var (x, y))
        {
            return pieces;
        }
        var cell = level[x, y];
        var (fx, fy) = SlotFloor(row, column);
        var wallY = fy - 25;
        int c = x * BattlefieldLevel.CellWidth, r = y * BattlefieldLevel.CellHeight;
        if (NorthWallPiece(level, tiles, x, y, cell) is { } northWall)
        {
            pieces.Add(new WallPiece(0x7D00, northWall, fx + 16, wallY, new DepthKey(c, r, r)));
        }
        var northHidden = cell.NorthWall != 0 && HasContent(level[x, y - 1]);
        if (!northHidden && cell.NorthWallDecoration != 0 && cell.NorthWallDecoration >= terrain.AnimatedBelow(TileKind.NorthWallDecoration))
        {
            var d = tiles.NorthWallDecorations[cell.NorthWallDecoration];
            pieces.Add(new WallPiece(0x7D01, d?.Sprite, fx + 16 + (d?.OffsetX ?? 0), wallY + (d?.OffsetY ?? 0), new DepthKey(c + 4, r, r)));
        }
        if (WestWallPiece(level, tiles, x, y, cell) is { } westWall)
        {
            pieces.Add(new WallPiece(0x7D03, westWall, fx, wallY, new DepthKey(c, r + 1, r + 8)));
        }
        var westHidden = cell.WestWall != 0 && HasContent(level[x - 1, y]);
        if (!westHidden && cell.WestWallDecoration != 0 && cell.WestWallDecoration >= terrain.AnimatedBelow(TileKind.WestWallDecoration))
        {
            var d = tiles.WestWallDecorations[cell.WestWallDecoration];
            pieces.Add(new WallPiece(0x7D04, d?.Sprite, fx + (d?.OffsetX ?? 0), wallY + (d?.OffsetY ?? 0), new DepthKey(c + 1, r + 8, r + 8)));
        }
        if (cell.EastWall != 0 && tiles.WestWalls[cell.EastWall]?.JoinedBothEnds is { } eastWall)
        {
            pieces.Add(new WallPiece(0x7D05, eastWall, fx + 32, wallY, new DepthKey(c + 0x1F, r + 9, r + 0xF)));
        }
        if (cell.Furniture != 0)
        {
            var f = tiles.Furniture[cell.Furniture];
            var o = cell.FurnitureOrientation;
            int dy = f?.BoxY[o] ?? 0, half = (f?.Height ?? 0) >> 1;
            var key = new DepthKey(c + (f?.BoxX[o] ?? 0), Math.Max(r + dy - half, r + 1), Math.Min(r + dy + half, r + 0xE));
            pieces.Add(new WallPiece(0x7D0A, f?.Sprite, fx + (f?.SpriteX[o] ?? 0), fy + (f?.SpriteY[o] ?? 0), key));
        }
        if (cell.SouthWall != 0 && tiles.NorthWalls[cell.SouthWall]?.JoinedBothEnds is { } southWall)
        {
            pieces.Add(new WallPiece(0x7D14, southWall, fx, fy - 9, new DepthKey(c + 0x10, r + 0xF, r + 0xF)));
        }
        return pieces;
    }

    /// <summary>
    /// The north wall piece: shown only when the cell to the north is open ground or void; its
    /// ends join walls of the cells around.
    /// </summary>
    private static RowRunSprite? NorthWallPiece(BattlefieldLevel level, BattlefieldTiles tiles, int x, int y, BattlefieldCell cell)
    {
        if (cell.NorthWall == 0 || tiles.NorthWalls[cell.NorthWall] is not { } wall || HasContent(level[x, y - 1]))
        {
            return null;
        }
        var west = level[x - 1, y];
        var joinsWest = Nibble(level[x - 1, y - 1], 0) != 0 || (HasContent(west) && (cell.WestWall != 0 || Nibble(west, 0) != 0));
        var east = level[x + 1, y];
        var joinsEast = cell.EastWall != 0 || Nibble(east, 8) != 0
            || (HasContent(level[x + 1, y - 1]) && HasContent(east) && (Nibble(level[x + 1, y - 1], 24) != 0 || Nibble(east, 16) != 0));
        return (joinsWest, joinsEast) switch
        {
            (true, true) => wall.JoinedBothEnds,
            (true, false) => wall.JoinedAtStart,
            (false, true) => wall.JoinedAtEnd,
            _ => wall.Plain,
        };
    }

    /// <summary>The west wall piece: shown only when the cell to the west is open ground or void.</summary>
    private static RowRunSprite? WestWallPiece(BattlefieldLevel level, BattlefieldTiles tiles, int x, int y, BattlefieldCell cell)
    {
        if (cell.WestWall == 0 || tiles.WestWalls[cell.WestWall] is not { } wall || HasContent(level[x - 1, y]))
        {
            return null;
        }
        var south = level[x, y + 1];
        var southWest = level[x - 1, y + 1];
        var joinsSouth = cell.SouthWall != 0 || Nibble(south, 16) != 0 || Nibble(southWest, 0) != 0
            || (HasContent(south) && HasContent(southWest) && Nibble(south, 8) != 0);
        var north = level[x, y - 1];
        var joinsNorth = Nibble(level[x - 1, y - 1], 24) != 0 || (HasContent(north) && (cell.NorthWall != 0 || Nibble(north, 24) != 0));
        return (joinsSouth, joinsNorth) switch
        {
            (true, true) => wall.JoinedBothEnds,
            (true, false) => wall.JoinedAtStart,
            (false, true) => wall.JoinedAtEnd,
            _ => wall.Plain,
        };
    }

    /// <summary>A cell that is neither void nor bare (the original tests the cell word as a positive number).</summary>
    // C: the cell is a signed 32-bit long; "> 0" excludes both void cells (bit 31) and bare cells (0).
    private static bool HasContent(BattlefieldCell cell) => (int)cell.Bits > 0;

    /// <summary>A nibble of a neighbouring cell, 0 when that cell is void or bare.</summary>
    private static int Nibble(BattlefieldCell cell, int shift) => HasContent(cell) ? (int)(cell.Bits >> shift) & 0xF : 0;
}

/// <summary>A wall piece of a view slot in the battle's depth-sorted list: its type (0x7D00..0x7D14), sprite (null: draws nothing), point
/// relative to the view and depth.</summary>
public readonly record struct WallPiece(int Type, RowRunSprite? Sprite, int X, int Y, DepthKey Key);
