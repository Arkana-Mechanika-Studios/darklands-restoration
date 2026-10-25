using Darklands.Content.Formats.Graphics;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Simulation.Overland;

namespace Darklands.Presentation;

/// <summary>The travel map's pictures: the terrain tiles (code by neighbour mask), the place icons and the party's sprites.</summary>
// orig: R030+0x1F30 (1462:1F30, matched): MAPICONS.PIC rows 1..15 and MAPICON2.PIC rows 0..15 cut into 16x12 icons, CHARICON.PIC into 13x13 sprites at (c * 14 + 1, r * 14 + 1) (10 rows of 8), then mapheada and mapbrs00..21
public sealed class OverlandTiles
{
    private const int TileHeight = 12;
    private const int SpriteSize = 13;

    private readonly IndexedImage[] _tiles = new IndexedImage[32 * 16];
    private readonly IndexedImage[] _party = new IndexedImage[10 * 8];

    public OverlandTiles(IndexedImage first, IndexedImage second, IndexedImage party)
    {
        for (var code = 1; code < 32; code++)
        {
            var (sheet, sheetRow) = code < 16 ? (first, code) : (second, code - 16);
            for (var mask = 0; mask < 16; mask++)
            {
                _tiles[code * 16 + mask] = Cut(sheet, mask * 16, sheetRow * TileHeight, 16, TileHeight);
            }
        }
        // Place icons sit in MAPICON2 row 12, which is also code 28's row.
        for (var direction = 0; direction < 10; direction++)
        {
            for (var frame = 0; frame < 8; frame++)
            {
                _party[direction * 8 + frame] = Cut(party, frame * 14 + 1, direction * 14 + 1, SpriteSize, SpriteSize);
            }
        }
    }

    /// <summary>The icon of terrain <paramref name="code"/> (1..31) with neighbour mask <paramref name="mask"/>.</summary>
    // orig: the icon strip is ICONS[r][c] = R136+0xF92C[r * 16 + c]: MAPICONS rows 1..15 are ICONS[0..14], MAPICON2 rows 0..15 are ICONS[15..30]
    public IndexedImage Tile(int code, int mask) => _tiles[code * 16 + mask];

    /// <summary>The party sprite of a direction row (0 standing, 1 north .. 8 north-west, 9 waiting) and a frame 0..7.</summary>
    // orig: R136+0x100C8[direction][frame] (the table at 2798 of the segment DS:7E4A names)
    public IndexedImage Party(int direction, int frame) => _party[Math.Min(direction, 9) * 8 + frame];

    /// <summary>The picture of a place icon (codes 50 and up).</summary>
    // orig: place icons (codes 50 and up) are R136+0xF92C[code + 382] = ICONS[27][code - 50]
    public IndexedImage Icon(int code) => _tiles[28 * 16 + Math.Min(15, code - Core.World.Terrain.FirstPlaceIcon)];

    private static IndexedImage Cut(IndexedImage sheet, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var sx = x + column;
                var sy = y + row;
                pixels[row * width + column] = sx < sheet.Width && sy < sheet.Height ? sheet.Pixels[sy * sheet.Width + sx] : (byte)0;
            }
        }
        return new IndexedImage(width, height, pixels);
    }
}

/// <summary>
/// The travel screen as the original keeps it: drawn once when the view changes (the map, the header, the clock) and then patched every
/// pass by the party's rectangle, so what the party leaves outside its new rectangle stays on the screen.
/// </summary>
// orig: the screen page 0 of the driver (the window at DS:27F4): R030+0x20D2 draws the map and the header there when the view moved or after a
//   redraw, R030+0x1300 the clock picture when an hour passed, R030+0x16C6 the party's 17 x 21 rectangle every pass of 0c44
public sealed class OverlandPage
{
    public const int Left = 60;
    private const int AreaY = 16;
    private const int AreaWidth = 260;
    private const int AreaHeight = 184;

    /// <summary>The map buffer's corner left out of the view: 20d2 draws the cells from (0, 0) of its buffer and copies it from (8, 8) to (60, 16).</summary>
    // orig: gfx_copy_rect(DS:284E, 8, 8, 0x104, 0xB8, screen, 0x3C, 0x10) at R030+0x2599..0x25AD; the view's first cell (column DS:286A, row DS:286E + DS:286C) is drawn at (0 or 8, 0) of the buffer
    private const int BufferCrop = 8;

    // orig: 16c6's own buffer, the window DS:282A (page 1 at (280, 20), 0x37 x 0x32, pictures clipped to its last column 0x37 and row 0x32)
    private const int PatchWidth = 0x38;
    private const int PatchHeight = 0x33;

    /// <summary>
    /// The party's lift in pixels by the pixel row (0..3) and column (0..15) of the marker inside its cell: terrain 16..19 and 20..23 raise
    /// it, the minor river, the tidal marsh and the ford (3, 4, 25) lower it.
    /// </summary>
    // orig: the signed byte tables at 391B (16..19), 38DB (20..23) and 395B (3, 4, 25) of the segments DS:7E44 / DS:7E46 / DS:7E48 name, [fy][fx]
    public static readonly sbyte[] Lift16To19 =
    [
        0, 0, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 0, 0,
        1, 1, 2, 2, 3, 3, 3, 3, 3, 3, 2, 3, 2, 2, 1, 1,
        1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 2, 2, 1, 1,
        0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 1, 0, 0,
    ];

    /// <inheritdoc cref="Lift16To19"/>
    public static readonly sbyte[] Lift20To23 =
    [
        0, 0, 1, 1, 2, 2, 2, 3, 3, 2, 2, 2, 1, 1, 0, 0,
        1, 1, 2, 2, 3, 4, 5, 6, 7, 5, 4, 3, 2, 2, 1, 1,
        1, 1, 2, 2, 2, 3, 3, 4, 4, 4, 3, 2, 2, 1, 1, 1,
        0, 0, 0, 1, 1, 2, 2, 2, 2, 2, 2, 1, 1, 1, 0, 0,
    ];

    /// <inheritdoc cref="Lift16To19"/>
    public static readonly sbyte[] LiftRiverMarshFord =
    [
        0, 0, -1, -1, -2, -2, -2, -1, 0, 0, -1, -2, -1, -1, 0, 0,
        -1, -1, -2, -2, -3, -2, -2, -1, -1, -2, -2, -3, -2, -2, -1, -1,
        -1, -1, -2, -2, -2, -1, -1, -2, -2, -2, -1, -1, -2, -2, -1, -1,
        0, 0, 0, -1, -1, -2, -2, -1, -1, -2, -2, -1, -1, -1, 0, 0,
    ];

    private readonly Screen _page = new();
    private readonly Canvas _canvas;
    private readonly byte[] _patch = new byte[PatchWidth * PatchHeight];
    private CellBuffer? _cells;

    public OverlandPage()
    {
        _canvas = new Canvas(_page);
    }

    /// <summary>The page's pixels (only the columns from <see cref="Left"/> on are drawn).</summary>
    public Screen Screen => _page;

    /// <summary>The lift of the party on terrain <paramref name="terrain"/> (as 16c6 reads it: place icons above 50 count as 28).</summary>
    // orig: the switch of R030+0x16C6 (R030+0x17BC, table at R030+0x17CC): 16..19 the table at 391B, 20..23 the one at 38DB, 3, 4, 25 the one at 395B, else 0
    public static int Lift(int terrain, int fy, int fx) => terrain switch
    {
        >= 16 and <= 19 => Lift16To19[fy * 16 + fx],
        >= 20 and <= 23 => Lift20To23[fy * 16 + fx],
        3 or 4 or 25 => LiftRiverMarshFord[fy * 16 + fx],
        _ => 0,
    };

    /// <summary>
    /// Whether 16c6 draws the party after the tiles of row <paramref name="row"/>, for the party's row <paramref name="partyRow"/>: the rows
    /// drawn after it cover it. The sprite can be drawn twice (after the row above and after its own), the second time over the first.
    /// </summary>
    // orig: R030+0x1B3E..0x1B80: (row == r && tl <= 9) || (row - r == 1 && fy == 0 && tl >= 20) || (row == r && tl >= 15 && fy != 0)
    //   || (row - r == 1 && tl >= 9 && tl <= 19)
    public static bool DrawnAfter(int partyRow, int row, int terrain, int fy) =>
        (partyRow == row && terrain <= 9) || (partyRow - row == 1 && fy == 0 && terrain >= 20)
        || (partyRow == row && terrain >= 15 && fy != 0) || (partyRow - row == 1 && terrain >= 9 && terrain <= 19);

    /// <summary>
    /// The map, the header and the clock: the visible cells into the 260x184 area at (60, 16), from the view's first column and first row
    /// (the map row of the view's top line of cells).
    /// </summary>
    // orig: R030+0x20D2 (map tiles into the 260x184 buffer cleared to DS:2860, then the header picture at (60, 0), the clock picture mapbrs{hour/3*3} at (289, 0) and the copy to the screen at (60, 16))
    // C: the pictures go through the driver's entry 32 (MGRAPHIC.EXE 0D7D): colour 0 is transparent, nothing is clipped.
    // orig: 20d2 takes a first column or row of 0 as 1 (R030+0x20D8..0x20E9: cmp word [bp+6], 0 / mov word [bp+6], 1, the same for [bp+8]):
    //   at the map's west or north border its cell buffer, the tiles' row parity (R030+0x2541..0x254D) and the icons' window start one column (row) further
    // C: the cells are the world map's cell buffer, filled here from the row buffer as it stands (CellBuffer.Fill: the map screen keeps the
    //   row buffer as the original reads and scrolls it).
    public void DrawMap(WorldMap map, OverlandTiles tiles, int viewColumn, int viewTop, int viewRows, byte background, IndexedImage? header, IndexedImage? clock)
    {
        _cells = map.Buffer;
        _cells.Fill(viewColumn, viewTop, viewRows);
        viewColumn = Math.Max(viewColumn, 1);
        var firstRow = viewTop + Math.Max(viewRows, 1);
        _canvas.Fill(Left, AreaY, AreaWidth, AreaHeight, background);
        for (var y = 1; y <= 48; y++)
        {
            var odd = (firstRow + y - 1) % 2;
            for (var x = 1; x <= 17; x++)
            {
                if (CellAt(y, x) != 0)
                {
                    DrawClipped(TileAt(tiles, y, x, odd), Left - BufferCrop + (x - 1) * 16 + odd * 8, AreaY - BufferCrop + (y - 1) * 4);
                }
            }
        }
        if (header is not null)
        {
            _canvas.Draw(header, Left, 0, transparent: true);
        }
        DrawClock(clock);
    }

    /// <summary>The clock picture at (289, 0), as the step draws it when an hour passed.</summary>
    // orig: gfx_draw_pic(screen, 289, 0, DS:E400[hour / 3]) in R030+0x20D2
    // C: the step draws the same picture the same way after advance_hours(1) (OverlandScreen's HourPassed)
    public void DrawClock(IndexedImage? clock)
    {
        if (clock is not null)
        {
            _canvas.Draw(clock, 289, 0, transparent: true);
        }
    }

    /// <summary>
    /// The party at marker pixel (<paramref name="x"/>, <paramref name="y"/>) of the view: 16c6 draws ten rows of four cells around the
    /// marker into its own buffer, the sprite after the row that terrain asks for (the rows after it cover its lower part), and copies a
    /// 17 x 21 rectangle of it to the screen, raised by the terrain's lift.
    /// </summary>
    // orig: R030+0x16C6 (1462:16C6, draft): fill (0, 0, 0x37, 0x32) of the window DS:282A with DS:2860; fy = (y + 11) % 4, row = (y + 11) / 4,
    //   xa = x + 16 (odd map row) or x + 24, column xa / 16, fx = xa % 16; tl = the cell (> 50: 28), DS:D843 = tl; lift by tl's table;
    //   rows row - 6 .. row + 3 (4 pixels apart from 0), columns column - 1 .. column + 2 (16 apart from 0, or 8 on odd map rows), the same
    //   neighbour masks as 20d2; the sprite R136+0x100C8[dir][frame] at (8 * parity + fx + 10, fy - lift + 18) after the rows DrawnAfter
    //   names; then the copy of (8 * parity + 8 + fx, fy - lift + 14, 17, 21) to the screen at (x + 0x34, y - lift + 5) (R030+0x1B96..0x1C3C)
    // C: the copy is the driver's entry 20 (MGRAPHIC.EXE 0804): opaque, not clipped: with a lift the rectangle reaches above the map area into
    //   the header (at the map's north border, y 13 .. 19) and stays there until 20d2 draws the header again.
    // C: DS:D843 is OverlandTravel's terrain (the step sets it too; 16c6's value differs only for code 50: see OverlandTravel.Step).
    // C: the cells are 20d2's cell buffer (CellBuffer, filled by DrawMap), read as one array: a column past the row's end is the next row's first cell.
    // orig: at the map's south border the rectangle can reach screen lines 200..202, which the driver's row table (MGRAPHIC.EXE 1816) maps to
    //   offsets 0, 1, 0: those lines land on the screen's top line (the second one a byte to the right), in the order of the copy.
    public void DrawParty(OverlandTiles tiles, int firstRow, int x, int y, int direction, int frame, byte background)
    {
        var fy = (y + 11) % 4;
        var row = (y + 11) / 4;
        var partyRow = firstRow + row - 1;
        var parity = partyRow % 2;
        var xa = x + (parity != 0 ? 16 : 24);
        var column = xa / 16;
        var fx = xa % 16;
        var code = CellAt(row, column);
        var terrain = code > 50 ? 28 : code;
        var lift = Lift(terrain, fy, fx);
        Array.Fill(_patch, background, 0, _patch.Length);
        var sprite = tiles.Party(direction, frame);
        for (var r = row - 6; r <= row + 3; r++)
        {
            var odd = (firstRow + r - 1) % 2;
            var px = odd * 8;
            var py = (r - row + 6) * 4;
            for (var c = column - 1; c <= column + 2; c++, px += 16)
            {
                if (CellAt(r, c) != 0)
                {
                    Patch(TileAt(tiles, r, c, odd), px, py);
                }
            }
            if (DrawnAfter(row, r, terrain, fy))
            {
                Patch(sprite, parity * 8 + fx + 10, fy - lift + 18);
            }
        }
        var sx = (parity + 1) * 8 + fx;
        var sy = fy - lift + 14;
        var dx = x + 0x34;
        var dy = y - lift + 5;
        for (var j = 0; j < 21; j++)
        {
            var line = dy + j;
            // the driver's row table: lines 0..199 their own, 200 and 202..204 the offset 0, 201 the offset 1, below 0 or past 204 none
            var offset = line switch { >= 0 and < Screen.Height => line * Screen.Width, 201 => 1, >= 200 and <= 204 => 0, _ => -1 };
            if (offset < 0)
            {
                continue;
            }
            for (var i = 0; i < 17; i++)
            {
                var at = offset + dx + i;
                if ((uint)at < Screen.Width * Screen.Height)
                {
                    _canvas[at % Screen.Width, at / Screen.Width] = _patch[(sy + j) * PatchWidth + sx + i];
                }
            }
        }
    }

    // C: 20d2's cell buffer read as one array (CellBuffer's flat index, 19 per row): a column past the row's end is the next row's first cell,
    //   rows 50 and 51 (16c6's last rows and masks for a marker at the view's bottom, DS:2868 0xAD..0xB1) the 70 bytes after the buffer (zeros
    //   and the icons fills wrote into row 50, CellBuffer), row -1 (the masks at the top of the view) the bytes before it (CellBuffer's index)
    private int CellAt(int row, int column) => _cells is { } cells ? cells[row * CellBuffer.Columns + column] : 0;

    /// <summary>A cell's picture: a place icon, or the terrain's icon by which of its four diagonal neighbours continue it.</summary>
    // orig: the masks of R030+0x20D2 and R030+0x16C6 (the same tests): bit 1 the cell up-left, 2 up-right, 4 down-left, 8 down-right (G[r -+ 1][c - 1 + odd]
    //   and G[r -+ 1][c + odd]); rivers 1, 2, 3, 25, 27 continue each other, lakes 4..7 each other, the road 24 continues into 24, 25 and up; other codes
    //   only themselves; icons (50 and up) have no mask
    private IndexedImage TileAt(OverlandTiles tiles, int row, int column, int odd)
    {
        var code = CellAt(row, column);
        if (code >= Core.World.Terrain.FirstPlaceIcon)
        {
            return tiles.Icon(code);
        }
        var mask = 0;
        if (Continues(code, CellAt(row - 1, column - 1 + odd))) mask |= 1;
        if (Continues(code, CellAt(row - 1, column + odd))) mask |= 2;
        if (Continues(code, CellAt(row + 1, column - 1 + odd))) mask |= 4;
        if (Continues(code, CellAt(row + 1, column + odd))) mask |= 8;
        return tiles.Tile(code, mask);
    }

    private static bool Continues(int code, int neighbour) => code switch
    {
        1 or 2 or 3 or 25 or 27 => neighbour is 1 or 2 or 3 or 25 or 27,
        >= 4 and <= 7 => neighbour is >= 4 and <= 7,
        24 => neighbour >= 24,
        _ => neighbour == code,
    };

    /// <summary>The page's columns from <see cref="Left"/> on, onto the screen.</summary>
    public void CopyTo(Canvas canvas) =>
        canvas.CopyFrom(new SavedScreen(_page.Pixels, _page.Palette, _page.Owners, _page.Texels), Left, 0, Screen.Width - Left, Screen.Height);

    // C: the driver's entry 6 (MGRAPHIC.EXE 0C67): a picture clipped to the window (columns 0..0x37, rows 0..0x32), colour 0 transparent
    private void Patch(IndexedImage image, int x, int y)
    {
        for (var row = 0; row < image.Height; row++)
        {
            var sy = y + row;
            if ((uint)sy >= PatchHeight)
            {
                continue;
            }
            for (var column = 0; column < image.Width; column++)
            {
                var sx = x + column;
                var color = image.Pixels[row * image.Width + column];
                if (color != 0 && (uint)sx < PatchWidth)
                {
                    _patch[sy * PatchWidth + sx] = color;
                }
            }
        }
    }

    private void DrawClipped(IndexedImage image, int x, int y)
    {
        for (var row = 0; row < image.Height; row++)
        {
            var sy = y + row;
            if (sy < AreaY || sy >= AreaY + AreaHeight)
            {
                continue;
            }
            for (var column = 0; column < image.Width; column++)
            {
                var sx = x + column;
                var color = image.Pixels[row * image.Width + column];
                if (color != 0 && sx >= Left && sx < Left + AreaWidth)
                {
                    _canvas[sx, sy] = color;
                }
            }
        }
    }
}
