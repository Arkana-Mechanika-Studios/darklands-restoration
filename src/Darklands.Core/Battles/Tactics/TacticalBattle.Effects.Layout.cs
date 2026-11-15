using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

// The effects on the field: the renderer's part: the frame's draw-list bookkeeping and the clouds' and areas' puffs laid out for drawing. The slots and the state are in TacticalBattle.Effects.cs.
public sealed partial class EffectField
{
    /// <summary>
    /// What the renderer does with the effects each time it draws a frame (fight-draw, once per
    /// combat tick): it lays out every cloud's puffs anew with a random jitter, picks random
    /// sprites for the clouds, the areas and the hit flashes, runs the flash countdown (which
    /// makes the hit show), and — when not paused — ages the markers, areas and clouds. The
    /// random numbers come from the game's generator, so they are drawn here in the original's
    /// order whatever the screen shows.
    /// </summary>
    // orig: fight-draw R014+0x6818 (14F7:0008) draft, its effect parts: 3682, 3A46,
    // R014+0x6FFC (with 24A4, 296E, R014+0x9542), R014+0x719E (the flash), R014+0x76AE (ageing)
    // C: while paused the ticks still come every 8 timer callbacks (TacticalBattle.Tick), so these draws go on.
    // orig: the draw list DS:E162 (R014+0x6919 starts it at 1), its limit 157 at each add (R014+0x7BD0 for 13AE, 0x82AA for 1A6A, 0x72F7 for
    // the flash, checked against the asm): the last frame's logged sprites, the animated pieces (1A6A: roll-n(2) after the limit test,
    // R014+0x8383/0x843A/0x84B6), the cloud edges, then per combatant its body (able ones), its flash (roll-n(3) only below the limit,
    // R014+0x7327) and its damage number; the floor pieces go to list B (limit 0x8A); tests: DrawListTests
    // C: an entry whose box the view clips to nothing gives its place back in the original (R014+0x7BBE: after the clip R014+0x8680 a zero
    //   width or height decrements DS:E162 or DS:E166, R014+0x7CF5 / 0x7CFC); here every piece, edge and number of a combatant in view
    //   counts (the boxes are not clipped). The count only matters against the limits, which no frame reaches: at most 1 + 18 logged
    //   sprites of the last frame (the ground cursor and 0x11 missiles) + 15 + 15 animated wall pieces + 2 x 8 cloud edges + 3 x 13
    //   combatant entries + 18 of this frame = 122 < 157 (list B: 15 floor pieces + 13 fallen bodies < 0x8A), so the places given back
    //   change no draw and no random number.
    public void Drawn(IBattleViewport viewport)
    {
        // DIVERGENCE: host timing: fight-draw passes the timer's clock DS:07D4 as it is when the drawing starts (R014+0x6818), after the
        //   pass's work; a long computation before it (a cloud laid out: 135 timer callbacks in the original's emulated time, battle_oracle
        //   fight [0, 0x50, 4414, 62/2/5], clock 1847 -> 1982, now 1995) moves it on, and the clouds' edge and puff sprites drawn by lot
        //   against it then take other random numbers (here 3 more; read-only hook at R014+0x72E8, round battlelead). Here the clock
        //   moves between passes only.
        var now = Paused ? (ushort)0 : Clock;
        var camera = (viewport.CameraX, viewport.CameraY);
        if (camera != _lastCamera)
        {
            // floor-view-paint sets DS:E0AB: a full redraw lays the areas out again.
            for (var k = 0; k < AreaCount; k++)
            {
                _areas[k].Size |= 8;
            }
            _lastCamera = camera;
        }
        for (var k = 0; k < CloudCount; k++)
        {
            // R014+0x6890..0x68BA: a cloud still growing forgets its last box (DS:C897[k] = −9999)
            if (_clouds[k].Size >> 4 != (_clouds[k].Size & 0x0F))
            {
                _lastBoxX[k] = -9999;
            }
            LayOutCloud(k);
        }
        for (var k = 0; k < AreaCount; k++)
        {
            if ((_areas[k].Size & 0x0F) >= 8)
            {
                LayOutArea(k);
            }
        }
        // the draw list: the sprites the last frame logged, then the animated pieces (1A6A): the floor's in list B (DS:E166 starts at 0,
        // limit 0x89), the north and west walls' in the draw list; each draws its frame (roll-n(2)) once it has its place
        var count = 1 + viewport.LoggedSprites;
        var listB = 0;
        _pieceFrames.Clear();
        var (floorPieces, northPieces, westPieces) = AnimatedPieces(viewport);
        foreach (var (x, y, _) in floorPieces)
        {
            if (listB > 0x89)
            {
                break;
            }
            listB++;
            _pieceFrames[(2, x, y)] = Scaled(2);
        }
        foreach (var (layer, pieces) in new[] { (0, northPieces), (1, westPieces) })
        {
            foreach (var (x, y, _) in pieces)
            {
                if (count >= DrawListLimit)
                {
                    continue;
                }
                count++;
                _pieceFrames[(layer, x, y)] = Scaled(2);
            }
        }
        // 07EC: the clouds' puff boxes (24A4), the cloud edges (two random sprites each), then the areas' puffs (R014+0x917E, 14F7:296E).
        PuffBoxCount = 0;
        if (CloudCount > 0)
        {
            LayOutPuffBoxes();
            for (var k = 0; k < CloudCount; k++)
            {
                // C: s1 is drawn before s2.
                _cloudEdgeSprites[k] = now >= _clouds[k].Until ? (0, 0) : (105 + Scaled(2), 0);
                if (now < _clouds[k].Until)
                {
                    _cloudEdgeSprites[k].East = 107 + Scaled(2);
                }
            }
        }
        for (var k = 0; k < AreaCount; k++)
        {
            for (var j = 0; j < 16; j++)
            {
                _areaSprite[k, j] = -1;
            }
            if (_areas[k].Level != Battle.LevelShown || !AnyPuff(_areaPuffX, _areaPuffY, k, _areas[k].Size & 7))
            {
                continue;
            }
            var n = _areas[k].Size & 7;
            for (var j = 0; j < n * n; j++)
            {
                // C: an area past its time reads DS:CA83[2 + r], past the two-entry table (it is removed after this frame).
                var sprite = (_areas[k].Until <= now ? 2 : 0) + Scaled(2) + 196;
                if (j < 16)
                {
                    _areaSprite[k, j] = sprite;
                }
            }
        }
        // 07EC's edges in the draw list: a south and an east edge for each cloud (see the UNCERTAIN note above)
        for (var k = 0; k < CloudCount; k++)
        {
            if (_clouds[k].Level == Battle.LevelShown && AnyPuff(_cloudPuffX, _cloudPuffY, k, _clouds[k].Size & 7))
            {
                count = Math.Min(count + 2, Math.Max(count, DrawListLimit));
            }
        }
        // 098E: the combatants in view: the body of an able one, the hit flash, the damage number, each while the list has room.
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            _flashSprite[i] = -1;
            _bodyQueued[i] = _numberQueued[i] = false;
            if (c.Status is not (CharacterStatus.Able or CharacterStatus.Dead or CharacterStatus.Unconscious) || c.Level != Battle.LevelShown
                || !viewport.CombatantInView(i))
            {
                continue;
            }
            // C: the bodies of the unconscious and the dead go to list B (13AE with toB set, R014+0x724F)
            if (c.Status == CharacterStatus.Able && count < DrawListLimit)
            {
                count++;
                _bodyQueued[i] = true;
            }
            else if (c.Status != CharacterStatus.Able && Battle.DragonFight == Battle.CaveDragonFight && count < DrawListLimit)
            {
                // C: in the cave dragon's fight list B is not used: R014+0x7BBE puts the fallen in list C too (0x7BC3..0x7BC9)
                count++;
                _bodyQueued[i] = true;
            }
            var fx = c.Flash;
            if (fx[0] is < 0 and > -10)
            {
                if (++fx[0] == 0)
                {
                    fx[0] = FlashSprite0;
                    c.WasHit = true;
                }
            }
            else if (fx[0] != 0 && now < c.FlashUntil && count < DrawListLimit)
            {
                // C: past the limit the flash is neither drawn nor its sprite drawn by lot (the random number is not taken)
                _flashSprite[i] = fx[Scaled(3)];
                count++;
            }
            if (fx[0] != 0 && c.FlashUntil <= now)
            {
                fx[0] = 0;
            }
            if (c.DamageNumber != 0 && count < DrawListLimit)
            {
                count++;
                _numberQueued[i] = true;
            }
        }
        DrawListCount = count;
        // 0E9E: one entry per cloud on this level with a puff in view (24A4); C: entry k draws cloud k's sprite even when an
        // earlier cloud had no entry (the entries are not the clouds' slots): faithful.
        var entries = PuffBoxCount;
        Array.Fill(_cloudSprite, -1);
        for (var k = 0; k < entries; k++)
        {
            if (now < _clouds[k].Until)
            {
                _cloudSprite[k] = 103 + Scaled(2);
            }
        }
        if (Paused)
        {
            return;
        }
        for (var k = MarkerCount - 1; k >= 0; k--)
        {
            if (++_markers[k].Frame > 0x44)
            {
                RemoveMarker(k);
            }
        }
        for (var k = AreaCount - 1; k >= 0; k--)
        {
            if (_areas[k].Until <= now)
            {
                RemoveArea(k);
            }
        }
        for (var k = 0; k < AreaCount; k++)
        {
            if ((_areas[k].Size & 0x0F) < _areas[k].Size >> 4)
            {
                _areas[k].Size++;
                _areas[k].Size |= 8;
            }
        }
        for (var k = CloudCount - 1; k >= 0; k--)
        {
            if (_clouds[k].Until <= now)
            {
                RemoveCloud(k);
            }
        }
        for (var k = 0; k < CloudCount; k++)
        {
            if ((_clouds[k].Size & 0x0F) < _clouds[k].Size >> 4)
            {
                _clouds[k].Size++;
            }
        }
    }

    private static bool AnyPuff(int[,] px, int[,] py, int k, int n)
    {
        for (var j = 0; j < n * n && j < 16; j++)
        {
            if (px[k, j] != 0 && py[k, j] != 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The clouds' puff boxes: for each cloud on the level shown with a puff, the box of its puffs (25 wider and 8 higher than their
    /// points) taken together with the last frame's box, as a draw entry clipped to the view; the growth past the puffs is kept for the
    /// cloud's edges.
    /// </summary>
    // orig: R014+0x8CB4 (14F7:24A4) draft, the box and its growth checked against the asm (R014+0x8D40..0x8F09)
    // C: a cloud the loop skips (another level, no puff) gets DS:C897 / DS:C8BA = −9999 and keeps its growth DS:8574.. of an earlier frame;
    // the dirty screen tiles the entry marks (R014+0x8F69..) are not kept here (the whole view is drawn each frame).
    private void LayOutPuffBoxes()
    {
        for (var k = 0; k < CloudCount; k++)
        {
            var n = _clouds[k].Size & 7;
            int minX = 9999, minY = 9999, maxX = -9999, maxY = -9999;
            if (_clouds[k].Level == Battle.LevelShown)
            {
                for (var j = 0; j < n * n && j < 16; j++)
                {
                    int x = _cloudPuffX[k, j], y = _cloudPuffY[k, j];
                    if (x != 0 && y != 0)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
            if (minX == 9999)
            {
                _lastBoxX[k] = _lastBoxY[k] = -9999;
                continue;
            }
            int gx = 0, gy = 0, gw = 0, gh = 0;
            short w = (short)(maxX - minX + 25), h = (short)(maxY - minY + 8);
            if (_lastBoxX[k] != -9999)
            {
                // the box reaches out to take in the last one: x from the further left, the right end at the further right (y likewise)
                int ox = _lastBoxX[k], oxe = ox + _lastBoxW[k], oy = _lastBoxY[k], oye = oy + _lastBoxH[k];
                int nxe = minX + w, nye = minY + h;
                if (ox > minX)
                {
                    if (oxe > nxe)
                    {
                        gw = ox + (_lastBoxW[k] - w) - minX;
                    }
                }
                else
                {
                    gx = ox - minX;
                    gw = minX - ox;
                    if (oxe > nxe)
                    {
                        gw = oxe + (minX - ox) - nxe;
                    }
                }
                if (oy > minY)
                {
                    if (oye > nye)
                    {
                        gh = oy + (_lastBoxH[k] - h) - minY;
                    }
                }
                else
                {
                    gy = oy - minY;
                    gh = minY - oy;
                    if (oye > nye)
                    {
                        gh = oye + (minY - oy) - nye;
                    }
                }
            }
            _cloudBoxGrowth[k] = (gx, gy, gw, gh);
            _lastBoxX[k] = minX;
            _lastBoxY[k] = minY;
            _lastBoxW[k] = w;
            _lastBoxH[k] = h;
            if (PuffBoxCount > 0x7F)
            {
                return;
            }
            _puffBoxes[PuffBoxCount++] = ClipToView(minX + gx, minY + gy, gw + w, gh + h);
        }
    }

    /// <summary>A screen box (relative to the view) clipped to the view's 0x104 × 200 pixels; an empty one has width or height 0.</summary>
    // orig: R014+0x8680 (14F7:1E70) matched
    public static (int X, int Y, int W, int H) ClipToView(int x, int y, int w, int h)
    {
        if (x < 0)
        {
            w = Math.Max(0, w + x);
            x = 0;
        }
        if (x > 0x103)
        {
            w = 0;
        }
        if (x + w - 1 > 0x103 && (w = 0x104 - x) < 0)
        {
            w = 0;
        }
        if (y < 0)
        {
            h = Math.Max(0, h + y);
            y = 0;
        }
        if (y > 199)
        {
            h = 0;
        }
        if (y + h - 1 > 199 && (h = 200 - y) < 0)
        {
            h = 0;
        }
        return (x, y, w, h);
    }

    /// <summary>Lays out the puffs of cloud k: each at its offset with 0..2 pixels of jitter, kept off walls, hidden behind walls from the centre.</summary>
    // orig: R014+0x9E92 (14F7:3682) draft
    private void LayOutCloud(int k)
    {
        var n = _clouds[k].Size & 7;
        _clouds[k].Size &= 0xF7;
        int cx = _clouds[k].X, cy = _clouds[k].Y;
        var mapX = new int[16];
        var mapY = new int[16];
        for (var j = 0; j < n * n; j++)
        {
            var (ox, oy) = PuffOffset(n, j);
            // C: x is drawn before y.
            var px = Scaled(3) + ox + cx;
            var py = Scaled(3) + oy + cy;
            if (j >= 16)
            {
                // DIVERGENCE: a size above 4 (the trap kinds 10..15 have sizes 6 and 9: the low nibble) writes past the 16-puff
                // arrays on the original's stack; the random numbers are drawn, the extra puffs are not kept.
                continue;
            }
            (px, py) = KeepOffWalls(px, py);
            (mapX[j], mapY[j]) = SetPuff(_cloudPuffX, _cloudPuffY, k, j, cx, cy, px, py, 0x7C, 0x45);
        }
        // The edges: the last puff of each column (south) and of each row (east).
        // C: ex/ey keep the last value found across the columns and rows (and start as whatever the stack held: 0 here).
        int ex = 0, ey = 0;
        for (var c = 0; c < n && c < 4; c++)
        {
            for (var r = 0; r < n; r++)
            {
                var i = n * r + c;
                if (mapX[i] != 0)
                {
                    (ex, ey) = (mapX[i], mapY[i]);
                }
            }
            _cloudSouthEdge[k, c] = (-999, 0);
            if (ex != 0)
            {
                _cloudSouthEdgeAt[k, c] = (ex, ey + 4);
                var sy = ey + 4 - _lastCamera.Y;
                var sx = ex - _lastCamera.X - sy + 0x7D;
                if (!(sx > 0x103 || sy + 0x49 > 199))
                {
                    _cloudSouthEdge[k, c] = (sx, sy + 0x49);
                }
            }
            for (var r = 0; r < n; r++)
            {
                var i = n * c + r;
                if (mapX[i] != 0)
                {
                    (ex, ey) = (mapX[i], mapY[i]);
                }
            }
            _cloudEastEdge[k, c] = (-999, 0);
            if (ex != 0)
            {
                _cloudEastEdgeAt[k, c] = (ex + 7, ey);
                var sy = ey - _lastCamera.Y;
                var sx = ex + 7 - _lastCamera.X - sy + 0x84;
                if (!(sx > 0x103 || sy + 0x45 > 199))
                {
                    _cloudEastEdge[k, c] = (sx, sy + 0x45);
                }
            }
        }
    }

    /// <summary>Lays out the puffs of area k (no jitter, one pixel down and right).</summary>
    // orig: R014+0xA256 (14F7:3A46) draft
    private void LayOutArea(int k)
    {
        var n = _areas[k].Size & 7;
        _areas[k].Size &= 0xF7;
        int cx = _areas[k].X, cy = _areas[k].Y;
        for (var j = 0; j < n * n && j < 16; j++)
        {
            var (ox, oy) = PuffOffset(n, j);
            var (px, py) = KeepOffWalls(ox + cx + 1, oy + cy + 1);
            SetPuff(_areaPuffX, _areaPuffY, k, j, cx, cy, px, py, 0x7C, 0x65);
        }
    }

    /// <summary>A puff in a cell keeps 7 pixels from a west wall, 8 from an east wall, and so on (the cell's wall nibbles).</summary>
    // C: the adjustments are 16-bit masks on the coordinates (x &amp; 0xFFE7 | 7 ...); a hidden cell (bit 31) hides the puff.
    private (int X, int Y) KeepOffWalls(int px, int py)
    {
        int mx = px >> 5, my = py >> 4;
        var bits = Level[mx, my].Bits;
        int lo = (int)(bits & 0xFFFF), hi = (int)(bits >> 16);
        if ((hi & 0x8000) == 0x8000)
        {
            return (0, 0);
        }
        int fx = px & 0x1F, fy = py & 0x0F;
        if ((lo & 0xF) == 0)
        {
            if ((lo & 0xF00) != 0 && fx < 7)
            {
                px = (short)(px & 0xFFE7 | 7);
            }
        }
        else if (fx > 0x17)
        {
            px = (short)(px & 0xFFF7 | 0x17);
        }
        if ((hi & 0xF) == 0)
        {
            if ((hi & 0xF00) != 0 && fy > 10)
            {
                py = (short)(py & 0xFFFA | 10);
            }
        }
        else if (fy < 4)
        {
            py = (short)(py & 0xFFF4 | 4);
        }
        // C: the east neighbour is the cell one column on (index + 40), with the cell of the ORIGINAL point's column.
        if (fx > 0x16 && ((Level[mx + 1, my].Bits >> 16) & 0xF00) != 0 && fy > 10)
        {
            py = (short)(py & 0xFFFA | 10);
        }
        return (px, py);
    }

    /// <summary>Stores the puff's screen point (0 when hidden behind a wall from the centre or off the view's right or bottom); returns its map point (0 when hidden).</summary>
    private (int X, int Y) SetPuff(int[,] qx, int[,] qy, int k, int j, int cx, int cy, int px, int py, int dx, int dy)
    {
        if (px != 0 && py != 0 && Map.LineBlocked(new MapPoint(cx, cy), new MapPoint(px, py)) == 1)
        {
            px = py = 0;
        }
        if (px == 0 || py == 0)
        {
            qx[k, j] = qy[k, j] = 0;
            return (px, py);
        }
        var sy = py - _lastCamera.Y;
        var sx = px - _lastCamera.X - sy + dx;
        if (sx > 0x103 || sy + dy > 199)
        {
            qx[k, j] = qy[k, j] = 0;
            return (0, 0);
        }
        qx[k, j] = sx;
        qy[k, j] = sy + dy;
        return (px, py);
    }
}
