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
public sealed class EffectField : IEffectClouds
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

    /// <summary>
    /// effect-cast: an effect of <paramref name="type"/> lands at (x, y) — a slowing area (9), a
    /// flash or blast marker (6..8), or a cloud (any other type) — and acts at once on whoever
    /// is in it on <paramref name="floor"/>.
    /// </summary>
    /// <param name="thrower">The thrower (p3; for the flash: whose opponents are blinded).</param>
    /// <param name="quality">The potion's quality (clamped to 20..49: three grades).</param>
    /// <param name="target">The combatant aimed at (−1 a spot): it gets the stronger effect.</param>
    // orig: effect-cast R019+0x0000 (1EF8:0000) matched; tests: Oracle:effects.json
    /// <summary>The level an effect is made on: the level shown (R019+0x0000 / 0x0300), or with FixCombatAcrossLevels its missile's.</summary>
    // BUG: OriginalBehaviour.FixCombatAcrossLevels - a spell or potion effect lands on the level SHOWN, whatever level its missile flew on
    //   (split audit row 7)
    private int EffectLevel(int floor) => Battle.Behaviour.FixCombatAcrossLevels && floor >= 0 ? floor : Battle.FloorShown;

    public void Lands(int x, int y, int thrower, int type, int quality, int target, int floor)
    {
        // C: root 0410:001A clamps signed.
        var dur = Math.Clamp(quality, 0x14, 0x31);
        Landed?.Invoke();
        if (type == 9)
        {
            if (AreaCount >= 8)
            {
                return;
            }
            ref var a = ref _areas[AreaCount];
            a.X = x;
            a.Y = y;
            a.Size = 0x49;
            a.Level = (sbyte)EffectLevel(floor);
            switch (dur / 10)
            {
                case 2:
                    a.Strength = 4;
                    break;
                case 3:
                    a.Strength = 5;
                    break;
                case 4:
                    a.Strength = 6;
                    break;
            }
            a.Until = (ushort)(Unsigned(0x221D + dur / 10) * 60 + Clock);
            AreaCount++;
            _sound(0x0C);
            return;
        }
        if (type is >= 6 and <= 8)
        {
            if (MarkerCount >= 8)
            {
                return;
            }
            ref var m = ref _markers[MarkerCount];
            m.X = x;
            m.Y = y;
            m.Frame = 0x40;
            m.Level = (sbyte)EffectLevel(floor);
            m.Kind = (byte)(type - 6);
            MarkerActs(++MarkerCount - 1, type, thrower, dur, target, floor);
            _sound(0x0D);
            return;
        }
        if (CloudCount >= 8)
        {
            int i;
            for (i = 0; i < CloudCount; i++)
            {
                if (_clouds[i].Kind != 2)
                {
                    break;
                }
            }
            if (i == CloudCount)
            {
                return;
            }
            RemoveCloud(i);
            ref var r = ref _clouds[CloudCount];
            r.X = x;
            r.Y = y;
            r.Kind = (byte)type;
            r.Size = Unsigned(0x21DC + type);
            // C: the level byte is not written on this path: the slot keeps the level of the cloud that was shifted down into it.
        }
        else
        {
            ref var r = ref _clouds[CloudCount];
            r.X = x;
            r.Y = y;
            r.Kind = (byte)type;
            r.Size = Unsigned(0x21DC + type);
            r.Level = (sbyte)EffectLevel(floor);
        }
        _clouds[CloudCount].Until = (ushort)(Unsigned(0x2202 + type * 3 + dur / 10) * 60 + Clock);
        CloudActs(++CloudCount - 1, dur, target, floor);
        _sound(0x0C);
    }

    /// <summary>The cloud acts on everybody of the floor inside it (and in sight of its centre) who does not resist its type.</summary>
    // orig: R019+0x0300 (1EF8:0300) matched; tests: Oracle:effects.json
    private void CloudActs(int n, int dur, int target, int floor)
    {
        var kind = _clouds[n].Kind;
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status != CharacterStatus.Able || c.Floor != floor || !Inside(_clouds[n].X, _clouds[n].Y, _clouds[n].Size, i)
                || (c.EffectImmunity & (1 << kind)) != 0)
            {
                continue;
            }
            var lvl = dur / 10 - 2;
            var t = dur / 10 + kind * 3 - 2;
            c.FlashUntil = (ushort)(Unsigned(0x21E6 + t) * 60 + Clock);
            c.Flash[1] = FlashSprite1;
            c.Flash[2] = FlashSprite2;
            c.Flash[0] = -(Unsigned(0x21DC + kind) >> 4);
            switch (kind)
            {
                case 0:
                    EndAttributeModifier(i);
                    EndSkillModifier(i);
                    var pct = target == i ? 0x32 : 0x19;
                    LowerAttributes(i, pct, t);
                    LowerSkills(i, pct, t);
                    break;
                case 1:
                    EndAttributeModifier(i);
                    EndSkillModifier(i);
                    if (target == i)
                    {
                        EndHold(i);
                        LowerAttributes(i, 0x21, t);
                        LowerSkills(i, 0x21, t);
                        Hold(i, t);
                    }
                    else
                    {
                        LowerAttributes(i, 0x19, t);
                        LowerSkills(i, 0x19, t);
                    }
                    break;
                case 3:
                    EndSkillModifier(i);
                    LowerSkills(i, (c.ArmourValue[1] + c.ArmourValue[0]) * 5, t);
                    break;
                case 4:
                    {
                        var d = Battle.RollOneTo(8) + (lvl + 2) * 2;
                        if (c.ArmourType[0] != -1)
                        {
                            WearItem(i, c.ArmourType[0], (byte)c.ArmourQuality[0], d);
                            c.ArmourQuality[0] = (sbyte)(c.ArmourQuality[0] - d);
                        }
                        if (c.ArmourType[1] != -1)
                        {
                            WearItem(i, c.ArmourType[1], (byte)c.ArmourQuality[1], d);
                            c.ArmourQuality[1] = (sbyte)(c.ArmourQuality[1] - d);
                        }
                        if (c.ArmourQuality[0] <= 0 || c.ArmourType[0] == -1)
                        {
                            if (c.ArmourType[0] != -1)
                            {
                                d = -c.ArmourQuality[0];
                            }
                            Attacks.QueueDamage(Battle, d, i, 1, 0);
                            ApplyDamage(i);
                        }
                        DropWornOut(i);
                        break;
                    }
                case 5:
                    {
                        var d = (lvl + 2) * 2 + Battle.RollOneTo(8);
                        Attacks.QueueDamage(Battle, ((lvl + 2) * 10 - c.MaximumEndurance) / 10 + d, i, c.ArmourValue[0] + 1, c.ArmourValue[0]);
                        ApplyDamage(i);
                        break;
                    }
            }
        }
    }

    /// <summary>A flash (6: the thrower's opponents who look toward it are held) or a blast (7 Thunderbolt, 8 Arabian Fire: damage, armour worn).</summary>
    // orig: R019+0x058A (1EF8:058A) matched; tests: Oracle:effects.json
    private void MarkerActs(int n, int type, int thrower, int dur, int target, int floor)
    {
        var m = _markers[n];
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status != CharacterStatus.Able || c.Floor != floor || (c.EffectImmunity & (1 << type)) != 0)
            {
                continue;
            }
            var lvl = dur / 10 - 2;
            var idx = Math.Min(lvl, 2) + type * 3;
            if (type == 6)
            {
                if ((thrower < Combatant.PartySlots && i >= Combatant.PartySlots) || (thrower >= Combatant.PartySlots && i < Combatant.PartySlots))
                {
                    int dx = m.X - c.Position.X, dy = m.Y - c.Position.Y;
                    int adx = Math.Abs(dx), ady = Math.Abs(dy);
                    var r = adx != 0 ? ady * 4 / adx : 0x14;
                    var s = r == 0 ? 0 : r is >= 1 and < 4 ? 1 : 2;
                    var q = dx >= 0 && dy < 0 ? 0 : dx >= 0 ? 1 : dy >= 0 ? 2 : 3;
                    // C: heading-add (root 0410:005A) subtracts and heading-subtract adds (the names are swapped; checked against the asm):
                    // d = (toward − 1) − (facing − 1), all mod 8, as a signed byte above 4.
                    var toward = (Unsigned(0x222E + q * 3 + s) - 1) & 7;
                    var facing = (c.SpriteFacing - 1) & 7;
                    var d = (toward - facing) & 7;
                    if (d > 4)
                    {
                        d -= 8;
                    }
                    if (Math.Abs(d) <= 2 || target == i)
                    {
                        Hold(i, idx);
                        c.FlashUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
                        c.Flash[1] = FlashSprite1;
                        c.Flash[2] = FlashSprite2;
                        c.Flash[0] = -2;
                    }
                }
                continue;
            }
            if (!Inside(m.X, m.Y, m.Frame, i))
            {
                continue;
            }
            // C: the distance is taken from x twice (dx / 2 and dx, no dy): faithful.
            var near = Distance((m.X - c.Position.X) / 2, m.X - c.Position.X, 0);
            c.FlashUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
            c.Flash[1] = FlashSprite1;
            c.Flash[2] = FlashSprite2;
            c.Flash[0] = -4;
            int dmg;
            switch (type)
            {
                case 7:
                    {
                        dmg = Battle.RollOneTo(8) + (lvl + 2) * 2;
                        var th = target == i ? Signed(0x2222 + lvl) : near <= 0x10 ? Signed(0x2226 + lvl) : Signed(0x222A + lvl);
                        int sk = c.ArmourValue[0];
                        var r = sk < th ? dmg : sk == th ? dmg / 3 : dmg / 8;
                        Attacks.QueueDamage(Battle, ((lvl + 2) * 10 - c.ArmourQuality[0]) / 10 + r, i, th, sk);
                        ApplyDamage(i);
                        break;
                    }
                case 8:
                    dmg = Battle.RollOneTo(6) + (lvl + 2) * 2;
                    if (near > 0x10)
                    {
                        dmg >>= 2;
                    }
                    Attacks.QueueDamage(Battle, (c.ArmourQuality[0] + c.PiercingResistance) / -10 + dmg, i, c.ArmourValue[0] + 1, c.ArmourValue[0]);
                    ApplyDamage(i);
                    break;
                default:
                    continue;
            }
            if (dmg >> 2 > 0)
            {
                dmg >>= 2;
                WearItem(i, c.ArmourType[0], (byte)c.ArmourQuality[0], dmg);
                WearItem(i, c.ArmourType[1], (byte)c.ArmourQuality[1], dmg);
                c.ArmourQuality[0] = (sbyte)(c.ArmourQuality[0] - dmg);
                c.ArmourQuality[1] = (sbyte)(c.ArmourQuality[1] - dmg);
                DropWornOut(i);
            }
        }
    }

    /// <summary>three-way-distance with its third word (always 0 here).</summary>
    private static int Distance(int a, int b, int c)
    {
        _ = c;
        return TacticalBattle.Distance(a, b);
    }

    /// <summary>Removes cloud n (the later ones move down; the last slot keeps its old contents).</summary>
    // orig: R019+0x01E2 (1EF8:01E2) matched
    // C: the renderer's last boxes DS:C8F7/DS:C915/DS:C897/DS:C8BA move with the records (the growths DS:8574.. stay by slot).
    private void RemoveCloud(int n)
    {
        if (CloudCount == 0)
        {
            return;
        }
        for (var i = n; i < CloudCount - 1; i++)
        {
            _clouds[i] = _clouds[i + 1];
            _lastBoxW[i] = _lastBoxW[i + 1];
            _lastBoxH[i] = _lastBoxH[i + 1];
            _lastBoxX[i] = _lastBoxX[i + 1];
            _lastBoxY[i] = _lastBoxY[i + 1];
        }
        CloudCount--;
    }

    // orig: R019+0x0260 (1EF8:0260) matched
    private void RemoveArea(int n)
    {
        if (AreaCount == 0)
        {
            return;
        }
        for (var i = n; i < AreaCount - 1; i++)
        {
            _areas[i] = _areas[i + 1];
        }
        AreaCount--;
    }

    // orig: R019+0x02B4 (1EF8:02B4) matched
    private void RemoveMarker(int n)
    {
        if (MarkerCount == 0)
        {
            return;
        }
        for (var i = n; i < MarkerCount - 1; i++)
        {
            _markers[i] = _markers[i + 1];
        }
        MarkerCount--;
    }

    /// <summary>Armour worn through (quality 0 or less) is taken off and thrown away.</summary>
    // orig: R019+0x0914 (1EF8:0914) matched
    private void DropWornOut(int i)
    {
        var c = Battle[i];
        for (var k = 0; k <= 1; k++)
        {
            if (c.ArmourQuality[k] <= 0)
            {
                // BUG: items are consumed until none of that type and quality is left: the spares stacked with the worn piece
                // (worn down with it by WearItem) are thrown away with it.
                while (Attacks.ConsumeItem(Battle, i, (byte)c.ArmourType[k], (byte)c.ArmourQuality[k]) != -1)
                {
                }
                c.ArmourType[k] = -1;
                c.ArmourValue[k] = 0;
                c.ArmourQuality[k] = 0x19;
            }
        }
    }

    /// <summary>The first carried item of type <paramref name="code"/> and quality <paramref name="quality"/> loses <paramref name="amount"/> quality.</summary>
    // orig: R019+0x0968 (1EF8:0968) matched
    // C: the type is compared as an int with the item's unsigned byte (a type of −1 never matches); the quality is an
    // unsigned byte compare and subtraction.
    private void WearItem(int i, int code, byte quality, int amount)
    {
        var items = Battle[i].Items;
        for (var k = 0; k < items.Count; k++)
        {
            if ((byte)items[k].Type == code && (byte)items[k].Quality == quality)
            {
                // BUG: armour wear hits the whole stack: the stack loses the quality, not one piece, so identical spare
                // pieces stacked with the worn armour wear down with it.
                items[k] = items[k] with { Quality = (byte)(items[k].Quality - amount) };
                return;
            }
        }
    }

    /// <summary>The attribute modifier ends: agility and perception get back what was taken.</summary>
    // orig: R019+0x0A7C (1EF8:0A7C) matched
    private void EndAttributeModifier(int i)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        c.Attributes[2] = (sbyte)(c.Attributes[2] + m.Attributes[0]);
        m.Attributes[0] = 0;
        c.Attributes[3] = (sbyte)(c.Attributes[3] + m.Attributes[1]);
        m.Attributes[1] = 0;
        m.AttributesUntil = EffectModifiers.Off;
    }

    // orig: R019+0x0AAC (1EF8:0AAC) matched
    // C: char × int / 100 is a signed int division truncated into the char.
    private void LowerAttributes(int i, int pct, int idx)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        m.Attributes[0] = (sbyte)(c.Attributes[2] * pct / 100);
        c.Attributes[2] = (sbyte)(c.Attributes[2] - m.Attributes[0]);
        m.Attributes[1] = (sbyte)(c.Attributes[3] * pct / 100);
        c.Attributes[3] = (sbyte)(c.Attributes[3] - m.Attributes[1]);
        m.AttributesUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
    }

    // orig: R019+0x0B00 (1EF8:0B00) matched
    private void EndSkillModifier(int i)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        for (var k = 0; k < 7; k++)
        {
            c.Skills[k] = (sbyte)(c.Skills[k] + m.Skills[k]);
            m.Skills[k] = 0;
        }
        ReReady(i);
        m.SkillsUntil = EffectModifiers.Off;
    }

    // orig: R019+0x0B44 (1EF8:0B44) matched
    private void LowerSkills(int i, int pct, int idx)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        for (var k = 0; k < 7; k++)
        {
            m.Skills[k] = (sbyte)(c.Skills[k] * pct / 100);
            c.Skills[k] = (sbyte)(c.Skills[k] - m.Skills[k]);
        }
        ReReady(i);
        m.SkillsUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
    }

    /// <summary>The readied weapon is readied again (its values follow the changed skills).</summary>
    // orig: R019+0x0B9E (1EF8:0B9E) matched
    internal void ReReady(int i)
    {
        var c = Battle[i];
        if ((c.FullStrength & 0x80) == 0x80 && !string.Equals(_attackCode(c.AnimationSlot), "PT", StringComparison.OrdinalIgnoreCase))
        {
            ReadyWeapon(i, c.MissileWeapon, 0x80);
        }
        else
        {
            ReadyWeapon(i, c.DefaultWeapon, 0);
        }
    }

    /// <summary>The hold ends; a fighter goes back to its attack.</summary>
    // orig: R019+0x0BF8 (1EF8:0BF8) matched
    private void EndHold(int i)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        m.Held = false;
        m.HeldUntil = EffectModifiers.Off;
        if (((ushort)m.SavedAction & 2) == 2)
        {
            c.Action = m.SavedAction;
        }
    }

    /// <summary>Held: a fighter parries (0x22); a shooter or caster stops.</summary>
    // orig: R019+0x0C28 (1EF8:0C28) matched
    private void Hold(int i, int idx)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        m.Held = true;
        m.HeldUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
        m.SavedAction = c.Action;
        if (((ushort)c.Action & 2) == 2)
        {
            c.Action = CombatAction.ParryAttack;
        }
        if (c.Action is CombatAction.Missile or CombatAction.UseItem)
        {
            c.State = ActionState.Finished;
        }
    }

    /// <summary>Takes one of the potion thrown (party members only; enemies cast from their charges).</summary>
    // orig: R019+0x0DFE (1EF8:0DFE) matched
    public void UseUpPotion(int i, int potion, int quality)
    {
        if (i < Combatant.PartySlots)
        {
            Attacks.ConsumeItem(Battle, i, (byte)(potion + 0x84), (byte)quality);
        }
    }

    /// <summary>The Throw submenu of member i: its throwable potions (types 0x5F..0x68 but Black Cloud, quality 20..49), one line per potion and grade, at most 20.</summary>
    /// <returns>The lines (potion 0..9, quality, count), empty when there is none (or the member is held: DS:97DD = 2 either way).</returns>
    // orig: R019+0x0C80 (1EF8:0C80) matched
    // C: a member whose action word has bit 1 (fighting hand to hand) gets no list (the menu entry is disabled).
    public List<(int Potion, int Quality, int Count)> ThrowList(int i)
    {
        var c = Battle[i];
        var lines = new List<(int, int, int)>();
        if (((ushort)c.Action & 2) == 2)
        {
            return lines;
        }
        var list = new (int Id, int Quality, int Count)[30];
        Array.Fill(list, (-1, -1, -1));
        var count = 0;
        foreach (var s in c.Items)
        {
            if (s.Code is >= 0x5F and <= 0x68 && (byte)s.Quality is >= 0x14 and < 0x32 && s.Code != 0x61)
            {
                var a = s.Code - 0x5F;
                var n = a * 3 + (byte)s.Quality / 10 - 2;
                list[n] = (a, (byte)s.Quality, (byte)s.Count);
                count++;
            }
        }
        // C: count counts stacks, not distinct lines (two stacks of one grade count twice): faithful.
        for (var k = 1; count > 20 && k < 30; k += 3)
        {
            if (list[k].Id != -1)
            {
                list[k].Id = -1;
                count--;
            }
        }
        foreach (var e in list)
        {
            if (e.Id != -1)
            {
                lines.Add((e.Id, e.Quality, e.Count));
            }
        }
        return lines;
    }

    /// <summary>
    /// Combatant i stands inside the effect at (x, y) of size byte <paramref name="size"/> (its
    /// class in the high nibble) and the line from the centre to its puff is clear.
    /// </summary>
    // orig: R014+0x469E (0E76:469E) draft (asm-verified per spec D §1.11); tests: Oracle:effects.json
    public bool Inside(int x, int y, byte size, int i)
    {
        var s = size >> 4;
        var w = s < EffectHalfWidth.Length ? EffectHalfWidth[s] : 0;
        var h = s < EffectHalfHeight.Length ? EffectHalfHeight[s] : 0;
        int lx = -(w - x), ly = -(h - y), hx = w + x, hy = h + y;
        var p = Battle[i].Position;
        if (lx < p.X && p.X < hx && ly < p.Y && p.Y < hy)
        {
            var r = ((p.Y - ly) * 4 >> 5) * s;
            var c = (p.X - lx) * 4 >> 6;
            var (ox, oy) = PuffOffset(s, r + c);
            return Map.LineBlocked(new MapPoint(x, y), new MapPoint(ox + x, oy + y)) == 0;
        }
        return false;
    }

    /// <summary>The enemies' area test of a spell (the same test with the type's size).</summary>
    public bool InArea(MapPoint centre, int type, int k) => Inside(centre.X, centre.Y, Unsigned(0x21DC + type), k);

    /// <summary>
    /// The four words the stack holds where cloud-on-line-test keeps its saved line (bp−6, −8, −0x0A, −0x0C) before it writes
    /// them: in the game the frame of rand() left them, from hit-roll's roll-one-to-n(100) just before (the long multiply's
    /// operands: 3, 0x43FD — the multiplier 214013 — and the generator's state before that draw, high and low word).
    /// </summary>
    // orig: rand (root 06A1:29B6, MSC run-time) as found by running hit-roll R014+0xA738 from the original bytes
    // (tools/oracle run_unit with a frame trace at 1EC9:0242)
    // C: the four words are below the caller's SP from rand()'s return to cloud-on-line-test's own writes; the only other writer in that
    //   stretch (a few hundred instructions) is the timer interrupt (root 047C:01EA), whose FLAGS / CS / IP and handler frame land there
    //   when a tick falls inside it.
    // DIVERGENCE: a tick in that stretch is the phase of the PC's timer against the code, not a state the game keeps; the rewrite always
    //   takes the words rand() left.
    public (int X0, int Y0, int X1, int Y1) StackResidue()
    {
        var before = Battle.Random is Randomness.MscRandom m ? m.PreviousState() : 0u;
        return (3, 0x43FD, (short)(before >> 16), (short)before);
    }

    /// <summary>cloud-on-line-test: the line from a to t passes through one of the clouds (its box, then a puff seen from its centre).</summary>
    public bool LineCrossesCloud(int attacker, int target) => LineCrossesCloud(attacker, target, StackResidue());

    /// <summary>cloud-on-line-test with the stack words its saved line starts as.</summary>
    // orig: cloud-on-line-test R014+0x10672 (1EC9:0142) draft, checked against asm
    // (the draft's restore after a missed box is only a remark; the asm reloads the line from the saved words there);
    // tests: Oracle:effects.json
    // BUG: the loop runs to the count inclusive, so the slot after the last cloud (an old cloud's copy, or zeros) is tested too
    // (OriginalBehaviour.FixCloudLineOneTooMany stops at the last cloud).
    // BUG: a missed box reloads the line from the saved words, which are the stack residue until a box was hit: the clouds
    // after it are tested against that line (3, 17405)-(state words), not a → t. Kept.
    // C: clouds of every level are tested against the shown level's walls; the puff tests restore only the line's start.
    // DIVERGENCE: a ninth cloud slot (the bytes after the array, DS:A33B) reads as an empty cloud at (0, 0).
    public bool LineCrossesCloud(int attacker, int target, (int X0, int Y0, int X1, int Y1) residue)
    {
        if (CloudCount == 0)
        {
            return false;
        }
        var a = Battle[attacker].Position;
        var t = Battle[target].Position;
        // sight-line-x0, sight-line-y0, sight-line-x1, sight-line-y1 (DS:E0A5, DS:E0A3, DS:E0A9, DS:E0A7) and the saved line (bp-6, -8, -0x0A, -0x0C).
        int x0 = a.X, y0 = a.Y, x1 = t.X, y1 = t.Y;
        var (sx0, sy0, sx1, sy1) = residue;
        // orig: its own clip tests (R014+0xF284 from this frame) and those of the line tests it calls (one frame deeper) keep two runs (BattleMap.ClipResidue)
        BattleMap.ClipResidue boxes = new(), lines = new();
        var last = Battle.Behaviour.FixCloudLineOneTooMany ? CloudCount - 1 : CloudCount;
        for (var i = 0; i <= last; i++)
        {
            var cl = _clouds[i];
            var n = cl.Size >> 4;
            int w = n < EffectHalfWidth.Length ? EffectHalfWidth[n] : 0, h = n < EffectHalfHeight.Length ? EffectHalfHeight[n] : 0;
            if (Map.SegmentHitsBox(x0, y0, x1, y1, cl.X - w, cl.Y - h, w + cl.X, h + cl.Y, boxes))
            {
                (sx0, sy0, sx1, sy1) = (x0, y0, x1, y1);
                x0 = cl.X;
                y0 = cl.Y;
                for (var k = 0; k < n * n; k++)
                {
                    var (ox, oy) = PuffOffset(n, k);
                    x1 = ox + cl.X;
                    y1 = oy + cl.Y;
                    if (Map.LineBlocked(new MapPoint(x0, y0), new MapPoint(x1, y1), residue: lines) != 0)
                    {
                        continue;
                    }
                    var (px, py) = (x0, y0);
                    var box = (x1 - 8, y1 - 4, x1 + 8, y1 + 4);
                    (x0, y0, x1, y1) = (sx0, sy0, sx1, sy1);
                    if (Map.SegmentHitsBox(x0, y0, x1, y1, box.Item1, box.Item2, box.Item3, box.Item4, boxes))
                    {
                        return true;
                    }
                    (x0, y0) = (px, py);
                }
            }
            (x0, y0, x1, y1) = (sx0, sy0, sx1, sy1);
        }
        return false;
    }

    /// <summary>The shooter stands in a cloud (0592's loop: the first that holds it).</summary>
    // orig: R014+0xAA22 (18BF:0592) draft (the cloud loop) calling R014+0x469E
    public bool InsideACloud(int combatant)
    {
        for (var k = 0; k < CloudCount; k++)
        {
            if (Inside(_clouds[k].X, _clouds[k].Y, _clouds[k].Size, combatant))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The strongest slowing area the walker stands in (0 none or it resists type 9).</summary>
    // orig: fighter-step R014+0x1624 (0E76:1624) draft (the DS:A395 test, spec D §2.2)
    public int SlowingStrength(int i)
    {
        var best = 0;
        for (var k = 0; k < AreaCount; k++)
        {
            // C: DS:C928[i][0] & 2 is bit 9 of the immunity word (type 9).
            if ((Battle[i].EffectImmunity & 0x200) == 0 && Inside(_areas[k].X, _areas[k].Y, _areas[k].Size, i) && best < _areas[k].Strength)
            {
                best = _areas[k].Strength;
            }
        }
        return best;
    }

    /// <summary>The modifiers whose time is up end (every tick the battle is not paused, for every combatant).</summary>
    // orig: close-combat-rounds R014+0x2A4A (0E76:2A4A) draft (the mods: tail)
    public void EndModifiersDue(int i)
    {
        if (Paused)
        {
            return;
        }
        var m = Battle[i].Modifiers;
        if (m.AttributesUntil != EffectModifiers.Off && m.AttributesUntil < Clock)
        {
            EndAttributeModifier(i);
        }
        if (m.SkillsUntil != EffectModifiers.Off && m.SkillsUntil < Clock)
        {
            EndSkillModifier(i);
        }
        if (m.HeldUntil != EffectModifiers.Off && m.HeldUntil < Clock)
        {
            EndHold(i);
        }
    }

    /// <summary>After the battle the party's running modifiers end (0694); a save in a battle ends those of the members present.</summary>
    /// <param name="presentOnly">The save's test of the member's status: empty slots are skipped.</param>
    public void EndPartyModifiers(bool presentOnly = false)
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (presentOnly && Battle[i].Status == CharacterStatus.Empty)
            {
                continue;
            }
            var m = Battle[i].Modifiers;
            if (m.AttributesUntil != EffectModifiers.Off)
            {
                EndAttributeModifier(i);
            }
            if (m.SkillsUntil != EffectModifiers.Off)
            {
                EndSkillModifier(i);
            }
            if (m.HeldUntil != EffectModifiers.Off)
            {
                EndHold(i);
            }
        }
    }

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
            if (_areas[k].Level != Battle.FloorShown || !AnyPuff(_areaPuffX, _areaPuffY, k, _areas[k].Size & 7))
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
            if (_clouds[k].Level == Battle.FloorShown && AnyPuff(_cloudPuffX, _cloudPuffY, k, _clouds[k].Size & 7))
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
            if (c.Status is not (CharacterStatus.Able or CharacterStatus.Dead or CharacterStatus.Unconscious) || c.Floor != Battle.FloorShown
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
            if (_clouds[k].Level == Battle.FloorShown)
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
            if (c.Status == CharacterStatus.Empty || c.Floor != Battle.FloorShown)
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
