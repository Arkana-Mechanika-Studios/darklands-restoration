namespace Darklands.Core.Battles.Tactics;

/// <summary>The shape of a furniture tile for movement and sight: its footprint, height and box offsets per orientation.</summary>
/// <param name="Width">DS:C12F: footprint width (halved for the box).</param>
/// <param name="Height">DS:C11D: footprint height (halved for the box).</param>
/// <param name="BoxX">DS:D7BE: the box centre's x offset from the cell corner, per orientation.</param>
/// <param name="BoxY">DS:D802: the box centre's y offset, per orientation.</param>
public sealed record FurnitureShape(int Width, int Height, int[] BoxX, int[] BoxY);

/// <summary>What blocked a position (<c>cbt_collisions</c>'s result).</summary>
public readonly record struct Blocking(int Mask)
{
    public const int FurnitureBit = 0x2000;
    public const int WallBit = 0x4000;

    /// <summary>The centre cell is void (the original returns −1).</summary>
    public static Blocking Void => new(-1);

    public bool IsFree => Mask == 0;

    public bool IsVoid => Mask == -1;

    /// <summary>Combatant <paramref name="k"/> is in the way.</summary>
    public bool ByCombatant(int k) => Mask != -1 && k is >= 0 and < Combatant.Count && (Mask & (1 << k)) != 0;
}

/// <summary>
/// The level being fought on, for movement and sight: which positions are free
/// (<c>cbt_collisions</c>) and whether a line is blocked (<c>line_blocked</c>).
/// </summary>
public sealed class BattleMap
{
    private static readonly int[] NeighbourX = [-32, 0, 32, -32, 0, 32, -32, 0, 32];
    private static readonly int[] NeighbourY = [-16, -16, -16, 0, 0, 0, 16, 16, 16];

    private readonly Battle _battle;
    private (int X, int Y) _lastClipPoint;

    public BattleMap(Battle battle, BattlefieldLevel level, IReadOnlyList<FurnitureShape?> furniture, int firstSightBlockingFurniture)
    {
        _battle = battle;
        Level = level;
        Furniture = furniture;
        FirstSightBlockingFurniture = firstSightBlockingFurniture;
    }

    public BattlefieldLevel Level { get; set; }

    /// <summary>Furniture shapes by tile number (1..15).</summary>
    public IReadOnlyList<FurnitureShape?> Furniture { get; set; }

    /// <summary>DS:C1CF: furniture tiles from this number on block sight (terrain description +0xA3).</summary>
    public int FirstSightBlockingFurniture { get; set; }

    /// <summary>DS:E0A2: the stricter wall rules used by path searches, placements and pushes.</summary>
    public bool StrictWalls { get; set; }

    /// <summary>DS:D697 with DS:D6CD: during a path search, moving combatants other than the searcher's target do not block.</summary>
    public bool Searching { get; set; }

    public int SearchTarget { get; set; } = -1;

    /// <summary>DS:C600: the other combatants are ignored (the spell-target hover).</summary>
    public bool IgnorePeople { get; set; }

    /// <summary>DS:E13C: walls and furniture are ignored (the AI's path hook probes only for people).</summary>
    public bool IgnoreScenery { get; set; }

    /// <summary>DS:E11C: the parts of the last test (combatants 0..12, 13 furniture, 14 walls).</summary>
    public int[] LastParts { get; } = new int[16];

    /// <summary>DS:D708: the last non-void result.</summary>
    public int LastMask { get; private set; }

    /// <summary>
    /// Whether combatant <paramref name="who"/> may stand at (<paramref name="x"/>, <paramref name="y"/>):
    /// free, void centre cell, or the mask of what is in the way (walls, furniture, combatants).
    /// </summary>
    /// <param name="who">The combatant whose size is used (record +0x1C) and who is ignored among the others.</param>
    // orig: cbt_collisions R014+0x5ECA (1462:000A) draft (spec D §4.2: the single-wall
    // clauses, furniture and combatants checked against the asm); tests: Oracle:tactics.json (500 cases)
    public Blocking Collides(int who, int x, int y)
    {
        Array.Clear(LastParts);
        var me = _battle[who];
        int w = me.HalfWidth, h = me.HalfHeight;
        int right = x + w - 1, left = x - w + 1, bottom = y + h - 1, top = y - h + 1;
        int cx = x >> 5, cy = y >> 4;
        int cellLeft = cx << 5, cellTop = cy << 4;
        // DIVERGENCE: the original reads the 3 × 3 cells without a bounds check (edge cells read neighbouring memory); outside the level is void here.
        var centre = Level[cx, cy];
        if (centre.IsVoid)
        {
            return Blocking.Void;
        }
        var north = Level[cx, cy - 1];
        var south = Level[cx, cy + 1];
        var east = Level[cx + 1, cy];
        var west = Level[cx - 1, cy];
        int cR16 = cellLeft + 16, cB15 = cellTop + 15, cR32 = cellLeft + 32, cM8 = cellTop + 8;
        if (!IgnoreScenery)
        {
            if (StrictWalls ? WallsStrict() : WallsSingle())
            {
                LastParts[14] = Blocking.WallBit;
            }
            FurnitureTest();
        }
        if (!IgnorePeople)
        {
            CombatantTest();
        }
        var mask = 0;
        foreach (var part in LastParts)
        {
            mask |= part;
        }
        LastMask = mask;
        return new Blocking(mask);

        // orig: R014+0x60B0 (1462:01F0) unmatched (walls, individual movement)
        bool WallsSingle() =>
            (Blocks(centre.NorthWall) && y - cellTop < h + 2)
            || (Blocks(centre.SouthWall) && cB15 - y < h + 2)
            || (Blocks(centre.EastWall) && cR32 - x < w + 4 && (cR32 - 5 < right || cM8 - bottom != 9))
            || (Blocks(centre.WestWall) && x - cellLeft < w + 4)
            || (Blocks(north.EastWall) && cR32 - x < w + 4 && y - cM8 + 16 < h + 8 && (cR32 - 3 < right || cM8 - top != 8) && (cR32 - 5 < right || cM8 - top != 9))
            || (Blocks(south.EastWall) && cR32 - x < w + 4 && cM8 - y + 16 < h + 8 && (cR32 - 3 < right || cM8 - bottom != -8))
            || (Blocks(east.NorthWall) && cR16 - x + 32 < w + 16 && y - cellTop < h + 2 && (cR32 + 1 < right || cM8 - 6 > top))
            || (Blocks(east.SouthWall) && cR16 - x + 32 < w + 16 && cB15 - y < h + 2 && (cR32 + 1 < right || cM8 + 6 < bottom))
            || (Blocks(north.WestWall) && x - cellLeft < w + 4 && y - cM8 + 16 < h + 8 && (cellLeft + 3 > left || cM8 - top != 8))
            || (Blocks(south.WestWall) && x - cellLeft < w + 4 && cM8 - y + 16 < h + 8 && (cellLeft + 3 > left || cM8 - bottom != -8))
            || (Blocks(west.SouthWall) && x - cR16 + 32 < w + 16 && cB15 - y < h + 2 && (cellLeft - 1 > left || cM8 + 6 < bottom))
            || (Blocks(west.NorthWall) && x - cR16 + 32 < w + 16 && y - cellTop < h + 2 && (cellLeft - 1 > left || cM8 - 6 > top));

        // Clauses 4..12 come from the draft (spec D §4.2 checked 1..3 against the asm); all twelve agree with the original on Oracle:tactics.json.
        // orig: R014+0x63CC (1462:050C) unmatched (walls, DS:E0A2 = 1: plain box test)
        bool WallsStrict() =>
            (Blocks(centre.NorthWall) && y - cellTop < h + 2)
            || (Blocks(centre.SouthWall) && cB15 - y < h + 2)
            || (Blocks(centre.EastWall) && cR32 - x < w + 4)
            || (Blocks(centre.WestWall) && x - cellLeft < w + 4)
            || (Blocks(north.EastWall) && cR32 - x < w + 4 && y - cellTop < h + 2)
            || (Blocks(south.EastWall) && cR32 - x < w + 4 && cB15 - y < h + 2)
            || (Blocks(east.NorthWall) && cR32 - x < w + 4 && y - cellTop < h + 2)
            || (Blocks(east.SouthWall) && cR32 - x < w + 4 && cB15 - y < h + 2)
            || (Blocks(north.WestWall) && x - cellLeft < w + 4 && y - cellTop < h + 2)
            || (Blocks(south.WestWall) && x - cellLeft < w + 4 && cB15 - y < h + 2)
            || (Blocks(west.SouthWall) && x - cellLeft < w + 4 && cB15 - y < h + 2 && (cellLeft - 1 > left || cM8 + 6 < bottom))
            || (Blocks(west.NorthWall) && x - cellLeft < w + 4 && y - cellTop < h + 2 && (cellLeft - 1 > left || cM8 - 6 > top));

        // orig: R014+0x66FE (1462:083E) unmatched
        void FurnitureTest()
        {
            for (var n = 0; n < 9; n++)
            {
                var cell = Level[cx + n % 3 - 1, cy + n / 3 - 1];
                var f = cell.Furniture;
                if (f == 0 || f >= Furniture.Count || Furniture[f] is not { } shape)
                {
                    continue;
                }
                // C: the sizes are signed chars shifted right (sar).
                int hh = (sbyte)shape.Height >> 1, hw = (sbyte)shape.Width >> 1;
                var o = cell.FurnitureOrientation;
                var fx = NeighbourX[n] + shape.BoxX[o] + cellLeft;
                var fy = NeighbourY[n] + shape.BoxY[o] + cellTop;
                if (Math.Abs(fx - x) < hw + w && Math.Abs(fy - y) < h + hh)
                {
                    int fl = fx - hw, fr = fx + hw, ft = fy - hh, fb = fy + hh;
                    // Boxes that only touch edge to edge do not block.
                    if ((fl != right || (ft != bottom && fb != top)) && (fr != left || (ft != bottom && fb != top)))
                    {
                        LastParts[13] = Blocking.FurnitureBit;
                    }
                }
            }
        }

        // orig: R014+0x6600 (1462:0740) unmatched
        void CombatantTest()
        {
            for (var i = 0; i < Combatant.Count; i++)
            {
                var o = _battle[i];
                if (i == who || !o.IsAble || o.Floor != me.Floor)
                {
                    continue;
                }
                if (Searching && i != SearchTarget && o.Moving)
                {
                    continue;
                }
                int ow = o.HalfWidth, oh = o.HalfHeight;
                if (Math.Abs(o.Position.X - x) < ow + w && Math.Abs(o.Position.Y - y) < oh + h)
                {
                    int l = o.Position.X - ow, r = o.Position.X + ow, t = o.Position.Y - oh, b = o.Position.Y + oh;
                    if ((l != right || (bottom != t && top != b)) && (left != r || (bottom != t && b != top)))
                    {
                        LastParts[i] = 1 << i;
                    }
                }
            }
        }
    }

    /// <summary>A wall nibble blocks when it is 1..11 (12 and up are open doorways).</summary>
    private static bool Blocks(int wall) => wall is >= 1 and <= 11;

    /// <summary>
    /// Whether the line from <paramref name="from"/> to <paramref name="to"/> is blocked by walls or
    /// sight-blocking furniture and, unless <paramref name="a"/> is −1, by other combatants (who block
    /// less the higher the shooter's skill: from 90 on not at all).
    /// </summary>
    /// <param name="a">−1 walls and furniture only; −2 people block fully and the result is 50 + who; else the shooter.</param>
    /// <param name="b">The target (never blocks).</param>
    /// <param name="mode">0x100 (a throw) uses the shooter's thrown-weapons skill (+0x6F), else the readied skill (+0x59).</param>
    // orig: line_blocked R014+0xEF3C (1D69:000C) draft (asm-verified per spec D §4.3); tests: Oracle:tactics.json (400 cases)
    public int LineBlocked(MapPoint from, MapPoint to, int a = -1, int b = -1, int mode = 0)
    {
        int x0 = from.X, y0 = from.Y, x1 = to.X, y1 = to.Y;
        int r0 = y0 / 16, r1 = y1 / 16, c0 = x0 / 32, c1 = x1 / 32;
        var firstColumn = Math.Min(c0, c1);
        var firstRow = Math.Min(r0, r1);
        for (var col = firstColumn; col <= firstColumn + Math.Abs(c0 - c1); col++)
        {
            var xp = col * 32 - 1;
            for (var row = firstRow; row <= firstRow + Math.Abs(r0 - r1); row++)
            {
                var yp = row * 16 - 1;
                var cell = Level[col, row];
                if ((Blocks(cell.NorthWall) && Clips(xp, yp, xp + 33, yp + 2))
                    || (Blocks(cell.SouthWall) && Clips(xp, yp + 15, xp + 33, yp + 17))
                    || (Blocks(cell.EastWall) && Clips(xp + 31, yp, xp + 33, yp + 17))
                    || (Blocks(cell.WestWall) && Clips(xp, yp, xp + 2, yp + 17)))
                {
                    return 1;
                }
                var f = cell.Furniture;
                if (f >= (sbyte)FirstSightBlockingFurniture && f > 0 && f < Furniture.Count && Furniture[f] is { } shape)
                {
                    var o = cell.FurnitureOrientation;
                    var bx = xp + 1 + shape.BoxX[o];
                    var by = yp + 1 + shape.BoxY[o];
                    if (Clips(bx, by, bx + shape.Width, by + shape.Height))
                    {
                        return 1;
                    }
                }
            }
        }
        if (a == -1)
        {
            return 0;
        }
        // DIVERGENCE: with a two-argument call the original's mode is stack garbage; callers here pass 0.
        // DIVERGENCE: a throw (mode 0x100) with a = −2 reads the record before combatant 0 in the original; 0 here.
        var skill = mode == 0x100 ? (a >= 0 ? _battle[a].Skills[4] : 0) : a == -2 ? 0 : _battle[a].ReadiedSkill;
        if (skill >= 90)
        {
            return 0;
        }
        for (var i = 0; i < Combatant.Count; i++)
        {
            var o = _battle[i];
            if (!o.IsAble || o.Floor != _battle.FloorShown || i == a || i == b)
            {
                continue;
            }
            // C: the people's boxes shrink with the skill: w + (w·skill) / −90 (signed division).
            var ex = o.HalfWidth + o.HalfWidth * skill / -90;
            var ey = o.HalfHeight + o.HalfHeight * skill / -90;
            if (Clips(o.Position.X - ex, o.Position.Y - ey, o.Position.X + ex, o.Position.Y + ey))
            {
                return a == -2 ? 50 + i : 1;
            }
        }
        return 0;

        bool Clips(int left, int top, int right, int bottom) => SegmentHitsBox(x0, y0, x1, y1, left, top, right, bottom);
    }

    /// <summary>Whether the segment touches the box (an outcode clip test).</summary>
    // orig: R014+0xF284 (1D69:0354) draft; outcode R014+0xF3E2 (1D69:04B2) draft (asm-verified per spec D §4.3)
    internal bool SegmentHitsBox(int x0, int y0, int x1, int y1, int left, int top, int right, int bottom)
    {
        int Code(int x, int y) => (x < left ? 1 : 0) | (x > right ? 2 : 0) | (y > bottom ? 4 : 0) | (y < top ? 8 : 0);
        var k0 = Code(x0, y0);
        var k1 = Code(x1, y1);
        if ((k0 & k1) != 0)
        {
            return false;
        }
        if (k0 == 0 || k1 == 0)
        {
            return true;
        }
        if ((k0 | k1) is 12 or 3)
        {
            return true;
        }
        // C: the intersection point is an uninitialised local when neither bottom nor top applies (known original bug DL 0036):
        // in the game it holds whatever the stack held (line_blocked's locals, or interrupt residue).
        // DIVERGENCE: here it keeps the point of the previous clip test (a field starting at (0, 0)); that agrees with the
        // original on Oracle:tactics.json, where each case starts on a clean stack.
        var p = _lastClipPoint;
        if ((k0 & 4) != 0)
        {
            p = ((int)((long)(x1 - x0) * (bottom - y1) / (y1 - y0)) + x1, bottom);
        }
        else if ((k0 & 8) != 0)
        {
            p = ((int)((long)(top - y1) * (x1 - x0) / (y1 - y0)) + x1, top);
        }
        if (Code(p.X, p.Y) != 0)
        {
            if ((k0 & 2) != 0)
            {
                p = (right, (int)((long)(right - x1) * (y1 - y0) / (x1 - x0)) + y1);
            }
            else if ((k0 & 1) != 0)
            {
                p = (left, (int)((long)(y1 - y0) * (left - x1) / (x1 - x0)) + y1);
            }
        }
        _lastClipPoint = p;
        return Code(p.X, p.Y) == 0;
    }
}
