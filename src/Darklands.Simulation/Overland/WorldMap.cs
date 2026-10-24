using Darklands.Content.Formats.Maps;
using Darklands.Core.World;

namespace Darklands.Simulation.Overland;

/// <summary>
/// The world map as the travel screen sees it: the terrain of DARKLAND.MAP with the place icons
/// laid over it. Rows are staggered: odd rows sit half a tile to the right.
/// </summary>
// orig: the terrain buffer G[][] = R136+0xFCCC (19 bytes per row, filled by R030+0x20D2 (1462:20D2) matched from the 0xD2-byte-per-row run-length map rows that R030+0x1DD4 reads on scrolling) and the place-icon list R136+0x10168 built by R030+0x1C76 (1462:1C76) draft (bytes differ only in a relocated jump table); tests: OverlandTests
// DIVERGENCE: the original keeps a window of the map (50 rows by 19 columns) in G and refills it as the view scrolls; this class answers for any cell of the whole decoded map, so the rows are never evicted. A cell outside the map reads 0 here (in the original, whatever the buffer held).
public sealed class WorldMap
{
    public const int TileWidth = 16;
    public const int RowHeight = 4;

    private readonly WorldMapData _terrain;
    private readonly Dictionary<(int Column, int Row), int> _icons = new();

    // orig: R030+0x1C76: for k = 92..413, skip locations whose menu is 0x62 or whose icon is 15, put the rest in the icon list with their x, y and the code of Terrain.PlaceIconCode
    // C: later entries win where two places share a cell (the list is written into G in order). The list keeps a frame (k % 16) for the village icons 8 and 10; nothing in 20d2 or 16c6 reads it, so it is not kept here.
    // DORMANT: the per-village frame k % 16 of R136+0x10168[n].frame (set, never read in the traced drawing code).
    public WorldMap(WorldMapData terrain, IEnumerable<Location> places)
    {
        _terrain = terrain;
        foreach (var place in places.Where(p => !p.IsCity && p.ArrivalCard != 0x62 && p.Icon != 15))
        {
            _icons[(place.X, place.Y)] = Terrain.PlaceIconCode(place.Icon);
        }
    }

    /// <summary>The place icons laid over the terrain: cell and code (one per cell, the last place listed there).</summary>
    public IEnumerable<(int Column, int Row, int Code)> Icons => _icons.Select(i => (i.Key.Column, i.Key.Row, i.Value));

    /// <summary>The terrain at a map cell without the place icons (0 outside the map).</summary>
    public int TerrainCell(int column, int row) => column >= 0 && column < Width && row >= 0 && row < Height ? _terrain.ReadTile(column, row) : 0;

    public int Width => _terrain.Width;

    public int Height => _terrain.Height;

    /// <summary>The code at a map cell: a place icon, or the terrain (0 outside the map).</summary>
    // orig: G[row][col] after 20d2 has laid the icon list over the terrain (the loop over R136+0x10168: G[y - tope][x - col + 1] = icon)
    public int Cell(int column, int row)
    {
        if (_icons.TryGetValue((column, row), out var icon))
        {
            return icon;
        }
        return column >= 0 && column < Width && row >= 0 && row < Height ? _terrain.ReadTile(column, row) : 0;
    }

    /// <summary>The terrain a place icon stands for when walking (icons count as castles).</summary>
    // orig: t = c >= 50 ? 28 : c in R030+0x1300; 16c6 (R030+0x16C6) tests c > 50 for the same thing
    public int TerrainAt(int column, int row)
    {
        var code = Cell(column, row);
        return code >= Terrain.FirstPlaceIcon ? Terrain.Castle : code;
    }

    /// <summary>
    /// How far the cell the original reads under the marker lies from the party's cell, for a view starting at column
    /// <paramref name="viewColumn"/> and shown row <paramref name="viewRows"/>: one column east when the view is at the map's west
    /// border (column 0), one row south when its first shown row is 0.
    /// </summary>
    // C: R030+0x20D2 takes a first column or row of 0 as 1 (R030+0x20D8..0x20E9) when it fills the cell buffer G, while the readers index
    //   G with the view's own DS:286A / DS:286C: the step R030+0x1300 (G[(y + 11) / 4][col] at R030+0x137B..0x1382), the camp watch
    //   R030+0x25BE (R030+0x261D..0x2639, R030+0x26AC..0x26B2) and the party drawing R030+0x16C6 (which also sets DS:D843)
    // C: party_x / party_y (DS:286A + col - 1, row + DS:286E + DS:286C - 1) and the place scans of 1300 use the registers, not G: only the
    //   terrain read moves. DS:286A is 0 whenever the view stands at the west border (overland_open and the scroll block clamp it there);
    //   DS:286C is 0 only after overland_open placed a party at map row 16 or less (the scroll block keeps it at 1 or more).
    public static (int Column, int Row) BufferOffset(int viewColumn, int viewRows) => (viewColumn == 0 ? 1 : 0, viewRows == 0 ? 1 : 0);

    /// <summary>The map cell under a point in map pixels (the party's position).</summary>
    // orig: rowk = (DS:2868 + 11) / 4; cold = ((rowk + DS:286E + DS:286C - 1) % 2 ? DS:2866 + 16 : DS:2866 + 24) / 16; party_x = DS:286A + cold - 1; party_y = rowk + DS:286E + DS:286C - 1 (R030+0x0C44)
    // C: these are the formulas with the view offsets folded into one absolute pixel: x = DS:2866 + 16 * DS:286A, y = DS:2868 + 4 * (DS:286E + DS:286C); the row parity (odd rows shifted right by half a tile) is that of the absolute row.
    public static (int Column, int Row) CellAt(int x, int y)
    {
        var row = (y + 11) / RowHeight - 1;
        var column = (x + (row % 2 == 1 ? 16 : 24)) / TileWidth - 1;
        return (column, row);
    }

    /// <summary>The top-left map pixel of a cell.</summary>
    // orig: pxo = 8 for an odd absolute row else 0, advancing 16 per column, and pyg += 4 per row (20d2)
    public static (int X, int Y) PixelOf(int column, int row) => (column * TileWidth + (row % 2 == 1 ? 8 : 0), row * RowHeight);

    /// <summary>
    /// The party's watch value for camping on the map: three per member, more when a settlement
    /// icon lies in the ten-by-ten block that starts at the party's cell, less on rough terrain.
    /// </summary>
    // orig: R030+0x25BE (1462:25BE) draft (bytes differ: a si/bx register swap only); tests: OverlandTests
    // C: the block is G[row + j][col + i] for i, j in 0..9, i.e. it starts AT the party's cell and reaches nine cells right and down (not centred on it); the scan is column by column (i outer), the first settlement icon (code 0x1D, a village or any 29) found counts.
    // C: the distances are measured from the block's middle: i, j become i > 5 ? i - 5 : |i - 5| and the same for j, j is then capped at i, and `res` = j; the scan ends by forcing the outer counter to 99. A distance of 0 adds nothing; otherwise speed += |res - 6|.
    // C: the terrain bonuses test the cell under the party by its raw code: 4, 5, 15, 19 -1; else 16, 20 -2; else 17, 21 -3; else 22 -5; else 23 -6; then 24 (road) +1 and 8, 9 +2 as separate ifs.
    public int CampWatch(int partySize, int column, int row)
    {
        var speed = partySize * 3;
        var res = 0;
        for (var i = 0; i < 10; i++)
        {
            for (var j = 0; j < 10; j++)
            {
                if (Cell(column + i, row + j) == 0x1D)
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
        var here = Cell(column, row);
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

    // orig: the maskf computation in 20d2 and 16c6: neighbours (y-1, x-1+odd), (y-1, x+odd), (y+1, x-1+odd), (y+1, x+odd) give bits 1, 2, 4, 8; the class that counts as "continuing" depends on the cell: water 1/2/3/25/27 together, 4..7 together, the road 24 with everything from 24 up, any other terrain only itself (Continues)
    // C: place icons (50 and up) are drawn from the icon strip without a mask; the generic icons 0x1C and 0x1D (28, 29) take the generic "same code" rule.
    /// <summary>
    /// Which neighbours continue a cell's terrain (bit 1 north-west, 2 north-east, 4 south-west,
    /// 8 south-east); the tile picture is chosen by this mask.
    /// </summary>
    public int NeighbourMask(int column, int row)
    {
        var code = Cell(column, row);
        var shift = row % 2;
        var mask = 0;
        if (Continues(code, Cell(column - 1 + shift, row - 1))) mask |= 1;
        if (Continues(code, Cell(column + shift, row - 1))) mask |= 2;
        if (Continues(code, Cell(column - 1 + shift, row + 1))) mask |= 4;
        if (Continues(code, Cell(column + shift, row + 1))) mask |= 8;
        return mask;
    }

    private static bool Continues(int code, int neighbour) => code switch
    {
        1 or 2 or 3 or Terrain.Ford or Terrain.Bridge => neighbour is 1 or 2 or 3 or Terrain.Ford or Terrain.Bridge,
        >= 4 and <= 7 => neighbour is >= 4 and <= 7,
        Terrain.Road => neighbour >= Terrain.Road,
        _ => neighbour == code,
    };
}
