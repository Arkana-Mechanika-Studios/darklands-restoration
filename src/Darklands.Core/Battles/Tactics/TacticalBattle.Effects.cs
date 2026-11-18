using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// An effect cloud (effect-cloud-table, DS:A2EB, 10-byte records): a thrown potion's or a spell's
/// cloud of kind 0..5 (Noxious Aroma, Eyeburn, Black Cloud, Fleadust, Eater Water, Breath of
/// Death; traps may give kinds above 9).
/// </summary>
// orig: struct efa matched
public struct EffectCloud
{
    public int X;
    public int Y;

    /// <summary>+4: high nibble the full size class, low nibble the size it has grown to (bit 3: lay it out again).</summary>
    public byte Size;

    /// <summary>+5: the effect type.</summary>
    public byte Kind;

    public sbyte Byte6;

    /// <summary>+7: the level it is on.</summary>
    public sbyte Level;

    /// <summary>+8: when it ends.</summary>
    public ushort Until;
}

/// <summary>A slowing area (effect-area-table, DS:A342): Stone-tar, type 9.</summary>
// orig: struct efb matched
public struct EffectArea
{
    public int X;
    public int Y;

    /// <summary>+4: as <see cref="EffectCloud.Size"/>.</summary>
    public byte Size;

    /// <summary>+5: how much it slows (4, 5, 6: the speed mask whose bit 0 lets a walker step).</summary>
    public byte Strength;

    /// <summary>+6: the level.</summary>
    public sbyte Level;

    public sbyte Byte7;

    public ushort Until;
}

/// <summary>A flash or blast marker (effect-marker-table, DS:A396): types 6..8 (Sunburst, Thunderbolt, Arabian Fire).</summary>
// orig: struct efc matched
public struct EffectMarker
{
    public int X;
    public int Y;

    /// <summary>+4: 0x40 and up, one more per drawn frame; the marker goes after 0x44 (the low bits pick the frame).</summary>
    public byte Frame;

    /// <summary>+5: the type − 6 (the colour scheme of the frames).</summary>
    public byte Kind;

    public sbyte Level;

    public sbyte Byte7;
}

/// <summary>Where the screen looks: the view's scroll (DS:C26C, DS:C293) and which combatants it shows.</summary>
public interface IBattleViewport
{
    /// <summary>DS:C26C / DS:C293: the map point at the view's origin.</summary>
    int CameraX { get; }

    int CameraY { get; }

    /// <summary>The renderer's clip test of combatant i's sprite box (14F7:347C, 4 pixels round it) against the view.</summary>
    bool CombatantInView(int i);

    /// <summary>Combatant i has a hot spot in the last frame drawn (DS:D848[0..DS:E0A1 - 1]); a view that keeps none says yes.</summary>
    bool HasHotSpot(int i) => true;

    /// <summary>
    /// DS:E176: the sprites the last frame logged for erasing (the ground cursor, the missiles, the damage numbers it queued), which
    /// take the first places of this frame's draw list.
    /// </summary>
    int LoggedSprites => 0;
}

/// <summary>
/// The effects on the field (overlay R019 and the renderer's part in them): the clouds, slowing
/// areas and flash/blast markers of thrown potions, spells and traps, what they do to the
/// combatants (attribute and skill modifiers, holds, damage, worn armour), how they block the
/// line of fire, and their ageing.
/// </summary>
// orig: R019 (all 17 functions matched); the cloud tests of R014 0E76:469E and 1EC9:0142;
// the effect parts of the renderer fight-draw R014+0x6818 (draft)
public sealed partial class EffectField : IEffectClouds
{
    private readonly Action<int> _sound;
    private readonly Action<int, int, int> _readyWeapon;
    private readonly Func<int, string> _attackCode;
    private readonly Func<bool> _paused;

    /// <param name="battle">The combatants.</param>
    /// <param name="map">The walls (the shown level) for the lines of sight.</param>
    /// <param name="sound">R014+0x465E: a sound effect.</param>
    /// <param name="readyWeapon">weapon-armed(i, weapon, missile flag) with its animation code.</param>
    /// <param name="attackCode">animation-file-name[slot]: the weapon code of an animation slot ("PT" while casting).</param>
    /// <param name="paused">enemy-ai-paused-flag (DS:A695) is not 0: every use here is a test == 0 of the original (R014+0x2C4C, R014+0x6A83, R014+0x7866).</param>
    public EffectField(Battle battle, BattleMap map, Action<int> sound, Action<int, int, int> readyWeapon, Func<int, string> attackCode, Func<bool> paused)
    {
        Battle = battle;
        Map = map;
        _sound = sound;
        _readyWeapon = readyWeapon;
        _attackCode = attackCode;
        _paused = paused;
    }

    public Battle Battle { get; }

    public BattleMap Map { get; }

    /// <summary>DS:D842 = 0: an effect landing ends the enemies' delayed start.</summary>
    public event Action? Landed;

    /// <summary>damage-resolve drew the party panel rows anew (party-panel-row-set(0x100), R014+0xAEC4).</summary>
    public event Action? PanelRedrawn;

    private void ApplyDamage(int i)
    {
        if (Attacks.ApplyDamage(Battle, i))
        {
            PanelRedrawn?.Invoke();
        }
    }

    private ushort Clock => Battle.Clock;

    private bool Paused => _paused();

    private int Scaled(int n) => Battle.RollScaled(n);

    private BattlefieldLevel Level => Map.Level;

    private void ReadyWeapon(int i, int weapon, int flag) => _readyWeapon(i, weapon, flag);

    /// <summary>DS:2174..DS:223F: the puff offset tables (DS:21B2 / DS:21BC point into them), DS:21DC (size of each type's cloud), DS:21E6
    /// (modifier and flash seconds), DS:2202 (cloud seconds), DS:221D (area seconds), DS:2222 / DS:2226 / DS:222A (blast thresholds) and
    /// DS:222E (facing toward a point). Kept whole: a type above 9 (traps) reads on into the next table, as in the original.</summary>
    // orig: DGROUP DS:2174 (data of R019; dumped from the 483.07 EXE)
    private static readonly byte[] EffectData =
    [
        0xFF, 0xFF, 0xF7, 0x07, 0xF7, 0x07, 0xFB, 0xFB, 0x03, 0x03, 0xEF, 0xFF, 0x0F, 0xEF, 0xFF, 0x0F,  // DS:2174
        0xEF, 0xFF, 0x0F, 0x00, 0xF7, 0xF7, 0xF7, 0xFF, 0xFF, 0xFF, 0x07, 0x07, 0x07, 0x00, 0xE7, 0xF7,  // DS:2184
        0x07, 0x17, 0xE7, 0xF7, 0x07, 0x17, 0xE7, 0xF7, 0x07, 0x17, 0xE7, 0xF7, 0x07, 0x17, 0xF3, 0xF3,  // DS:2194
        0xF3, 0xF3, 0xFB, 0xFB, 0xFB, 0xFB, 0x03, 0x03, 0x03, 0x03, 0x0B, 0x0B, 0x0B, 0x0B, 0x00, 0x00,  // DS:21A4
        0x74, 0x21, 0x76, 0x21, 0x7E, 0x21, 0x92, 0x21, 0x00, 0x00, 0x75, 0x21, 0x7A, 0x21, 0x88, 0x21,  // DS:21B4
        0xA2, 0x21, 0x00, 0x00, 0xFB, 0x03, 0xF7, 0xFF, 0x07, 0x00, 0xF3, 0xFB, 0x03, 0x0B, 0x00, 0x00,  // DS:21C4
        0xC6, 0x21, 0xC8, 0x21, 0xCA, 0x21, 0xCE, 0x21, 0x49, 0x29, 0x49, 0x29, 0x19, 0x29, 0x40, 0x40,  // DS:21D4
        0x40, 0x49, 0x06, 0x09, 0x0C, 0x08, 0x10, 0x18, 0x00, 0x00, 0x00, 0x19, 0x28, 0x3C, 0x05, 0x05,  // DS:21E4
        0x05, 0x05, 0x05, 0x05, 0x04, 0x06, 0x08, 0x05, 0x05, 0x05, 0x05, 0x05, 0x05, 0x00, 0x00, 0x00,  // DS:21F4
        0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x14, 0x3C, 0xB4, 0x03, 0x03, 0x03, 0x01, 0x01, 0x01, 0x02,  // DS:2204
        0x02, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0xF0, 0xF0, 0x06, 0x07,  // DS:2214
        0x08, 0x00, 0x03, 0x04, 0x05, 0x00, 0x01, 0x02, 0x03, 0x00, 0x02, 0x01, 0x00, 0x02, 0x03, 0x04,  // DS:2224
        0x06, 0x05, 0x04, 0x06, 0x07, 0x00, 0x50, 0x54, 0x00, 0x71, 0x25, 0x64,                          // DS:2234
    ];

    private const int EffectDataAt = 0x2174;

    /// <summary>DS:18B6 / DS:18BC: half width and height of an effect of size class n.</summary>
    // orig: DS:18B6, DS:18BC
    private static readonly int[] EffectHalfWidth = [0, 8, 16, 24, 32, 0];
    private static readonly int[] EffectHalfHeight = [0, 4, 8, 12, 16, 0];

    /// <summary>The battlegr.img sprites of the effects (R002 08FE): DS:D4C1, DS:D4C3, DS:D4C5 the hit flashes (the last of the ten loader rounds).</summary>
    public const int FlashSprite0 = 193, FlashSprite1 = 194, FlashSprite2 = 195;

    private readonly EffectCloud[] _clouds = new EffectCloud[9];
    private readonly EffectArea[] _areas = new EffectArea[8];
    private readonly EffectMarker[] _markers = new EffectMarker[8];

    /// <summary>R136+0x3BC0 / 3CC0 (clouds) and R136+0x3754 / 3854 (areas): the puffs' screen points of each slot (0 hidden).</summary>
    private readonly int[,] _cloudPuffX = new int[8, 16], _cloudPuffY = new int[8, 16];
    private readonly int[,] _areaPuffX = new int[8, 16], _areaPuffY = new int[8, 16];

    /// <summary>R136+0x4DB8 / 4E38 and 4C62 / 4CE2: the screen points of each cloud column's south edge and each row's east edge (X −999 none).</summary>
    private readonly (int X, int Y)[,] _cloudSouthEdge = new (int, int)[8, 4], _cloudEastEdge = new (int, int)[8, 4];

    /// <summary>R136+0x706A / 70EA and 6F6A / 6FEA: the map points of each cloud column's south edge and each row's east edge (their depth in the draw list).</summary>
    private readonly (int X, int Y)[,] _cloudSouthEdgeAt = new (int, int)[8, 4], _cloudEastEdgeAt = new (int, int)[8, 4];

    /// <summary>The edge sprites 07EC picked for each cloud (DS:CA41[r], DS:CA71[r]; 0 when it ended).</summary>
    private readonly (int South, int East)[] _cloudEdgeSprites = new (int, int)[8];

    /// <summary>DS:C897 / DS:C8BA / DS:C8F7 / DS:C915: each cloud's puff box of the last frame (screen, X −9999 none); they move with the records.</summary>
    private readonly int[] _lastBoxX = [.. Enumerable.Repeat(-9999, 8)], _lastBoxY = [.. Enumerable.Repeat(-9999, 8)], _lastBoxW = new int[8], _lastBoxH = new int[8];

    // DIVERGENCE: the original never resets DS:8574 / DS:859E / DS:855A / DS:8540 at a battle's start (they are written only by R014+0x8CB4, 0x8D47..0x8E69, and read by it and by R014+0x6FFC; the
    //   start R014+0x0285..0x02A2 fills DS:CA30 and DS:C897 only; no range fill or file load of them was found, not searched exhaustively): a cloud slot that 0x8CB4 skips (the shown level differs, or it has no puff) keeps
    //   the growth its slot had last, possibly from an earlier battle, and its edge boxes (queued whatever the level) use it. Here every field starts at 0. Visual only, rare.
    /// <summary>DS:8574 / DS:859E / DS:855A / DS:8540: how far each cloud's box this frame reaches past its puffs to take in the last frame's
    /// box (x, y, width, height); a cloud the frame skips keeps the old values.</summary>
    private readonly (int X, int Y, int W, int H)[] _cloudBoxGrowth = new (int, int, int, int)[8];

    /// <summary>The draw entries of the clouds' puffs (DS:E167, DS:E169 of them): each box clipped to the view.</summary>
    private readonly (int X, int Y, int W, int H)[] _puffBoxes = new (int, int, int, int)[8];

    /// <summary>The sprite the renderer drew for each cloud's puffs, each area's puffs and each combatant's flash this tick (−1 none).</summary>
    private readonly int[] _cloudSprite = new int[8], _flashSprite = [.. Enumerable.Repeat(-1, Combatant.Count)];
    private readonly int[,] _areaSprite = new int[8, 16];
    private (int X, int Y) _lastCamera = (int.MinValue, 0);

    /// <summary>effect-cloud-total (DS:A33D), DS:A395, DS:A531.</summary>
    public int CloudCount { get; private set; }

    public int AreaCount { get; private set; }

    public int MarkerCount { get; private set; }

    public ReadOnlySpan<EffectCloud> Clouds => _clouds.AsSpan(0, CloudCount);

    public ReadOnlySpan<EffectArea> Areas => _areas.AsSpan(0, AreaCount);

    public ReadOnlySpan<EffectMarker> Markers => _markers.AsSpan(0, MarkerCount);

    /// <summary>Every slot of the three lists, the ones past the counts included (they keep old records, which the original reads).</summary>
    public Span<EffectCloud> CloudSlots => _clouds.AsSpan(0, 8);

    public Span<EffectArea> AreaSlots => _areas;

    public Span<EffectMarker> MarkerSlots => _markers;

    /// <summary>Sets the lists' counts (a saved battle, a test).</summary>
    public void SetCounts(int clouds, int areas, int markers)
    {
        CloudCount = Math.Clamp(clouds, 0, 8);
        AreaCount = Math.Clamp(areas, 0, 8);
        MarkerCount = Math.Clamp(markers, 0, 8);
    }

    /// <summary>The screen point (relative to the view) of puff j of cloud k as laid out this tick, or (0, 0) hidden.</summary>
    public (int X, int Y) CloudPuff(int k, int j) => (_cloudPuffX[k, j], _cloudPuffY[k, j]);

    public (int X, int Y) AreaPuff(int k, int j) => (_areaPuffX[k, j], _areaPuffY[k, j]);

    /// <summary>The screen points (relative to the view) of the south edge of column c and the east edge of row c of cloud k (X −999 none).</summary>
    public (int X, int Y) CloudSouthEdge(int k, int c) => _cloudSouthEdge[k, c];

    public (int X, int Y) CloudEastEdge(int k, int c) => _cloudEastEdge[k, c];

    /// <summary>The map points of the south edge of column c and the east edge of row c of cloud k (meaningful while the screen point is not −999).</summary>
    public (int X, int Y) CloudSouthEdgeAt(int k, int c) => _cloudSouthEdgeAt[k, c];

    public (int X, int Y) CloudEastEdgeAt(int k, int c) => _cloudEastEdgeAt[k, c];

    /// <summary>The edge sprites of cloud k this tick (105/106 and 107/108; 0 none).</summary>
    public (int South, int East) CloudEdgeSprites(int k) => _cloudEdgeSprites[k];

    /// <summary>How far cloud k's edge boxes grow this frame (DS:8574, DS:859E, DS:855A, DS:8540): x, y, width, height.</summary>
    public (int X, int Y, int W, int H) CloudBoxGrowth(int k) => _cloudBoxGrowth[k];

    /// <summary>The puff draw entries of this frame: one per cloud on the level shown with a puff (DS:E169).</summary>
    public int PuffBoxCount { get; private set; }

    /// <summary>The screen box (relative to the view, clipped to it) that entry e's puffs are drawn in; entry e draws cloud e's puffs.</summary>
    public (int X, int Y, int W, int H) PuffBox(int e) => _puffBoxes[e];

    /// <summary>floor-view-paint's end (R017+0x0B82..0x0B8A): DS:C897[0..7] = −9999, the clouds' last boxes forgotten.</summary>
    public void ForgetCloudBoxes() => Array.Fill(_lastBoxX, -9999);

    /// <summary>The sprite chosen for cloud k's body this tick (DS:CA2C[r], 103 or 104), −1 when it ended.</summary>
    public int CloudSprite(int k) => _cloudSprite[k];

    /// <summary>The sprite chosen for puff j of area k (DS:CA83[r], 196 or 197; beyond the table when it ended).</summary>
    public int AreaSprite(int k, int j) => _areaSprite[k, j];

    /// <summary>The hit flash sprite drawn on combatant i this tick, −1 none.</summary>
    public int FlashSpriteOf(int i) => _flashSprite[i];

    /// <summary>The draw list's limit: an entry is added while the count is below it (slot 0 is the sort's sentinel).</summary>
    // orig: `cmp word ptr [E162h], 9Dh` before every add to the list (R014+0x7BD0, 0x82AA, 0x72F7, 0x872D, 0x6DB5, 0x6E5D)
    public const int DrawListLimit = 157;

    /// <summary>The tile set's animation limits (DS:C12D, DS:C12E, DS:C17F); null: no animated pieces (tests).</summary>
    public TerrainDescription? Terrain { get; set; }

    /// <summary>DS:E162 after the combatants' entries of the last frame (the screen goes on counting with the cursors and missiles).</summary>
    public int DrawListCount { get; private set; }

    /// <summary>Combatant i's body made the draw list in the last frame (its hot spot, member and target cursors come with it).</summary>
    public bool BodyQueued(int i) => _bodyQueued[i];

    /// <summary>Combatant i's damage number made the draw list in the last frame.</summary>
    public bool NumberQueued(int i) => _numberQueued[i];

    /// <summary>The frame (0 or 1) the last frame drew of the animated piece of a layer (0 north wall, 1 west wall, 2 floor) at a cell, −1 none.</summary>
    public int PieceFrame(int layer, int x, int y) => _pieceFrames.TryGetValue((layer, x, y), out var f) ? f : -1;

    private readonly bool[] _bodyQueued = new bool[Combatant.Count];
    private readonly bool[] _numberQueued = new bool[Combatant.Count];
    private readonly Dictionary<(int, int, int), int> _pieceFrames = [];

    /// <summary>
    /// The animated pieces of the view as R017+0x0000 collects them, at most 15 per kind, in the order of the view's 14 × 10
    /// cells: floor decorations below the floor limit (DS:E13D), north-wall decorations below theirs, not behind a north wall whose
    /// neighbour to the north has content (DS:E178), west-wall decorations likewise (DS:E196).
    /// </summary>
    // orig: R017+0x0000 draft: the cells x = centre + DS:1FC6[k], y = centre + DS:2052[k] (k = row × 10 + column), skipped
    // at the map's edge or void; `if (obj < DS:C12E && DS:E15B < 15)`, `if (fl < DS:C12D && DS:E15D < 15)`, `if (furn < DS:C17F && DS:E15C < 15)`
    // C: the lists are made anew each frame here; the original makes them when it redraws the whole view (the camera moved, a door
    // or wall changed), which shows the same cells.
    private (List<(int X, int Y, int Piece)> Floor, List<(int X, int Y, int Piece)> North, List<(int X, int Y, int Piece)> West) AnimatedPieces(IBattleViewport viewport)
    {
        List<(int, int, int)> floor = [], north = [], west = [];
        if (Terrain is null)
        {
            return (floor, north, west);
        }
        var level = Map.Level;
        int floorBelow = Terrain.AnimatedBelow(TileKind.FloorDecoration), northBelow = Terrain.AnimatedBelow(TileKind.NorthWallDecoration);
        var westBelow = Terrain.AnimatedBelow(TileKind.WestWallDecoration);
        static bool HasContent(BattlefieldCell cell) => (int)cell.Bits > 0;
        var (centreX, centreY) = (viewport.CameraX >> 5, viewport.CameraY >> 4);
        for (var row = 0; row < 14; row++)
        {
            for (var column = 0; column < 10; column++)
            {
                var x = centreX + column + row / 2 - 8;
                var y = centreY + row - 7;
                if (x <= 0 || x >= BattlefieldLevel.Size - 1 || y <= 0 || y >= BattlefieldLevel.Size - 1 || level[x, y].IsVoid)
                {
                    continue;
                }
                var cell = level[x, y];
                var obj = cell.NorthWall != 0 && HasContent(level[x, y - 1]) ? 0 : cell.NorthWallDecoration;
                if (obj != 0 && obj < northBelow && north.Count < 15)
                {
                    north.Add((x, y, obj));
                }
                var fl = level.FloorDecoration(x, y);
                if (fl != 0 && fl < floorBelow && floor.Count < 15)
                {
                    floor.Add((x, y, fl));
                }
                var furn = cell.WestWall != 0 && HasContent(level[x - 1, y]) ? 0 : cell.WestWallDecoration;
                if (furn != 0 && furn < westBelow && west.Count < 15)
                {
                    west.Add((x, y, furn));
                }
            }
        }
        return (floor, north, west);
    }

    /// <summary>The screen point of marker k (R136+0x59A8 / 59B8: the frame's top left relative to the view).</summary>
    public (int X, int Y) MarkerPoint(int k)
    {
        var m = _markers[k];
        var y = m.Y - _lastCamera.Y;
        return (-_lastCamera.X - (y - m.X) + 0x5F, y + 0x44);
    }

    private static sbyte Signed(int offset) => (sbyte)EffectData[offset - EffectDataAt];

    private static byte Unsigned(int offset) => EffectData[offset - EffectDataAt];

    /// <summary>DS:21B2[n][k] / DS:21BC[n][k]: the offset of puff k of a size class n effect (through the pointer tables).</summary>
    private static (int X, int Y) PuffOffset(int n, int k)
    {
        int Pointer(int table) => Unsigned(table + 2 * n) | Unsigned(table + 2 * n + 1) << 8;
        int px = Pointer(0x21B2), py = Pointer(0x21BC);
        // C: a null pointer (size class 0 or 5) is never indexed: the loops run n·n = 0 times, or class 5 is never made.
        return (Read(px + k), Read(py + k));

        static int Read(int at) => at >= EffectDataAt && at < EffectDataAt + EffectData.Length ? Signed(at) : 0;
    }

    /// <summary>The clock was rebased: the clouds' and areas' end times move with it.</summary>
    // orig: fight-main-loop R014+0x0000 (0E76:0000) draft (the rebase loops over effect-cloud-table and effect-area-table)
    public void Rebase(Func<ushort, ushort> rebase)
    {
        for (var k = 0; k < CloudCount; k++)
        {
            _clouds[k].Until = rebase(_clouds[k].Until);
        }
        for (var k = 0; k < AreaCount; k++)
        {
            _areas[k].Until = rebase(_areas[k].Until);
        }
    }
}

public sealed partial class TacticalBattle
{
    /// <summary>Whoever attacks (or casts at a spot) turns toward its target; a fighter the AI engaged keeps that facing for its pushes.</summary>
    // orig: fighter-screen-position R014+0x99F6 (14F7:31E6) draft (the turning part; the
    // screen positions are the screen's)
    private void TurnTowardTargets()
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status == CharacterStatus.Empty || !Levels.IsShown(c))
            {
                continue;
            }
            if (((ushort)c.Action & 0x1BE) != 0 && (c.Target != -1 || c.Action == CombatAction.UseItem)
                && c.Status is not (CharacterStatus.Unconscious or CharacterStatus.Dead))
            {
                var f = FacingToTarget(i);
                c.SpriteFacing = f;
                c.Facing = f;
                if (c.EngagedByAi)
                {
                    c.AttackFacing = f;
                    c.EngagedByAi = false;
                }
            }
        }
    }
}
