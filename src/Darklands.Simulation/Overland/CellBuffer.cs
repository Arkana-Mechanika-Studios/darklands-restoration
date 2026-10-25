namespace Darklands.Simulation.Overland;

/// <summary>
/// The travel map's cell buffer: the 50 rows by 19 columns of map codes (terrain, place icons laid over it) that the map drawing fills for
/// the view, as the original keeps it in memory, with the bytes that follow it. The camp watch reads it flat, without bounds checks.
/// </summary>
// orig: G = R136+0xFCCC[50][19] (19 bytes per row, index row * 0x13 + column), filled by R030+0x20D2 (1462:20D2); the 70 bytes R136+0x10082..0x100C7
//   after it, then the party icon table R136+0x100C8 ([10][8] words, the picture handles of the marker); tests: OverlandTests
// C: R136 is the data image, loaded once when the program starts: the bytes after G come from the file as zeros and no code writes them by
//   name; only 20d2's icon loop reaches the first 20 of them (G[50][0..19], see Fill). They are never cleared, so they keep the icons of earlier
//   draws for the program's lifetime (a new game or a loaded one included): this object lives as long as the world map (once per process).
public sealed class CellBuffer
{
    public const int Columns = 19;
    public const int Rows = 50;

    /// <summary>The bytes after G up to the party icon table (R136+0x10082..0x100C7).</summary>
    public const int After = 70;

    private readonly WorldMap _map;
    private readonly byte[] _bytes = new byte[Rows * Columns + After];

    public CellBuffer(WorldMap map) => _map = map;

    /// <summary>
    /// The byte at a flat index (row * 19 + column, as the original indexes the buffer): a cell of the window, or one of the bytes after it.
    /// </summary>
    // DIVERGENCE: an index past the 70 bytes after the buffer reads the party icon table R136+0x100C8 in the original: the handles the graphics
    //   driver returned for the marker's 80 pictures (R030+0x1F30's save-rect loop), which depend on where DOS and the driver placed them (memory
    //   layout, other programs loaded): not reproducible; it reads 0 here (no settlement). Only the camp watch reaches it (see CampWatch).
    public int this[int index] => index >= 0 && index < _bytes.Length ? _bytes[index] : 0;

    /// <summary>The cell of buffer row <paramref name="row"/>, column <paramref name="column"/>, read flat as the original does (no bounds check).</summary>
    public int this[int row, int column] => this[row * Columns + column];

    /// <summary>
    /// Fills the buffer for a view (first column, first row of the row buffer, first shown row), as the map drawing does: the terrain of
    /// the window, then the place icons in list order.
    /// </summary>
    // orig: R030+0x20D2(col = DS:286A, row = DS:286C): `if (col == 0) col = 1; if (row == 0) row = 1;` then for y = 0..49 the run-length row
    //   (row + y - 1) of the row buffer (map row DS:286E + row - 1 + y) gives G[y][0..18] = map columns col - 1 .. col + 17
    // orig: then `tope = row + DS:286E - 1` and for each entry of the icon list R136+0x10168 (until flag -1) with y >= tope, x >= col - 1,
    //   x <= col + 18 and y <= tope + 50: G[y - tope][x - col + 1] = icon
    // BUG: the icon loop's bounds are one row and one column too wide: an icon in column col + 18 is written at G[y - tope][19], which is
    //   G[y - tope + 1][0] (the next row's first cell, over its terrain, or past the buffer for the last row), and one in row tope + 50 at
    //   G[50][..], the bytes after the buffer, which no fill clears. Reproduced (the camp watch reads both).
    // DIVERGENCE: for the view at the map's bottom (DS:286C = 0x53 with the last 0x83 rows in the row buffer) buffer row 49 is the row past
    //   the map: the original decodes the bytes after the row buffer there (a block of its own, [DS:2870]:0000, 0x83 * 0xD2 bytes: whatever
    //   memory follows it), here it is 0 (WorldMap.TerrainCell outside the map). The camp watch reads that row for a marker in buffer row 40 or
    //   lower in such a view.
    public void Fill(int viewColumn, int viewTop, int viewRows)
    {
        var column = Math.Max(viewColumn, 1);
        var top = viewTop + Math.Max(viewRows, 1) - 1;
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns; x++)
            {
                _bytes[y * Columns + x] = (byte)_map.TerrainCell(column - 1 + x, top + y);
            }
        }
        foreach (var (x, y, code) in _map.IconList)
        {
            if (y >= top && x >= column - 1 && x <= column + 18 && y <= top + 50)
            {
                _bytes[(y - top) * Columns + x - column + 1] = (byte)code;
            }
        }
    }

    /// <summary>
    /// The buffer cell the original reads under the marker at (<paramref name="markerX"/>, <paramref name="markerY"/>) of the view, whose
    /// row buffer starts at <paramref name="viewTop"/> and shows from row <paramref name="viewRows"/>.
    /// </summary>
    // orig: row = (DS:2868 + 11) / 4; col = ((row + DS:286E + DS:286C - 1) % 2 ? DS:2866 + 16 : DS:2866 + 24) / 16 (R030+0x25C4.., the same in 1300 and 16c6)
    // C: the registers are the view's own (DS:286C unclamped), while the buffer was filled from the clamped ones: this is the one-cell shift at
    //   the map's west / north border (WorldMap.BufferOffset).
    public static (int Row, int Column) MarkerCell(int markerX, int markerY, int viewTop, int viewRows)
    {
        var row = (markerY + 11) / WorldMap.RowHeight;
        var column = ((row + viewTop + viewRows - 1) % 2 != 0 ? markerX + 16 : markerX + 24) / WorldMap.TileWidth;
        return (row, column);
    }

    /// <summary>
    /// The party's watch value for camping on the map: three per member, more when a settlement icon lies in the ten-by-ten block that starts
    /// at the party's buffer cell, less on rough terrain.
    /// </summary>
    // orig: R030+0x25BE (1462:25BE) draft (bytes differ: a si/bx register swap only); tests: OverlandTests
    // C: the block is G[row + j][col + i] for i, j in 0..9, i.e. it starts AT the party's cell and reaches nine cells right and down (not centred on it); the scan is column by column (i outer), the first settlement icon (code 0x1D, a village or any 29) found counts.
    // BUG: the block is read through the buffer's flat layout with no bounds check (R030+0x261D..0x2639): a column past 18 is the next row's
    //   cell (col + i - 19 of row + j + 1: the west side of the window, one row down) and a row past 49 is the bytes after the buffer (zeros and
    //   the stale icons of Fill, then the party icon table). The marker's column is up to 16 and its row up to 47, so up to 9 columns wrap and
    //   up to 7 rows fall past the buffer. Reproduced: a settlement there changes the bonus; one in the true cells beyond is not seen.
    // C: the distances are measured from the block's middle: i, j become i > 5 ? i - 5 : |i - 5| and the same for j, j is then capped at i, and `res` = j; the scan ends by forcing the outer counter to 99. A distance of 0 adds nothing; otherwise speed += |res - 6|.
    // C: the terrain bonuses test the cell under the party by its raw code: 4, 5, 15, 19 -1; else 16, 20 -2; else 17, 21 -3; else 22 -5; else 23 -6; then 24 (road) +1 and 8, 9 +2 as separate ifs.
    public int CampWatch(int partySize, int row, int column)
    {
        var speed = partySize * 3;
        var res = 0;
        for (var i = 0; i < 10; i++)
        {
            for (var j = 0; j < 10; j++)
            {
                if (this[row + j, column + i] == 0x1D)
                {
                    i = i > 5 ? i - 5 : Math.Abs(i - 5);
                    j = j > 5 ? j - 5 : Math.Abs(j - 5);
                    if (j > i)
                    {
                        j = i;
                    }
                    res = j;
                    i = 99;
                    break;
                }
            }
        }
        if (res != 0)
        {
            speed += Math.Abs(res - 6);
        }
        var here = this[row, column];
        if (here is 5 or 4 or 15 or 19)
        {
            speed--;
        }
        else if (here is 16 or 20)
        {
            speed -= 2;
        }
        else if (here is 17 or 21)
        {
            speed -= 3;
        }
        else if (here == 22)
        {
            speed -= 5;
        }
        else if (here == 23)
        {
            speed -= 6;
        }
        if (here == 24)
        {
            speed++;
        }
        if (here is 8 or 9)
        {
            speed += 2;
        }
        return speed;
    }
}
