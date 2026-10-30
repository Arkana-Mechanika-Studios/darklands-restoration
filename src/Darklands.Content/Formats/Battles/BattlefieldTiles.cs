using System.Buffers.Binary;
using Darklands.Content.Formats.Graphics;
using Darklands.Core.Battles;

namespace Darklands.Content.Formats.Battles;

/// <summary>A decoration drawn on a wall or the floor: one or two animation frames and its offset.</summary>
public sealed record TileDecoration(RowRunSprite Sprite, RowRunSprite? SecondFrame, int OffsetX, int OffsetY);

/// <summary>A wall tile: the plain piece and its three corner variants.</summary>
/// <param name="Plain">Neither end joins another wall.</param>
/// <param name="JoinedAtEnd">Joined at the far end (east for north walls, near end for west walls).</param>
/// <param name="JoinedAtStart">Joined at the start (west for north walls, far end for west walls).</param>
/// <param name="JoinedBothEnds">Joined at both ends; also the piece used for the cross walls (east and south sides).</param>
public sealed record WallTile(RowRunSprite Plain, RowRunSprite JoinedAtEnd, RowRunSprite JoinedAtStart, RowRunSprite JoinedBothEnds);

/// <summary>A piece of furniture: its sprite, its offsets for the four orientations and its height.</summary>
public sealed record FurnitureTile(RowRunSprite Sprite, int[] BoxX, int[] BoxY, int[] SpriteX, int[] SpriteY, int Footprint, int Height, bool Varies);

/// <summary>
/// The tiles of one battlefield, loaded from the BC catalog as its terrain description says
/// (<c>battle-tileset-load</c>): battlefield tile numbers 1..15 of each kind. Index 0 and unused
/// numbers are null.
/// </summary>
// orig: battle-tileset-load R002+0x0C8C (0E76:0C8C) draft; tests: BattlefieldTilesTests
// Record layouts (.flc 4, .ffc 6, .nwc 10, .nfc 10, .wwc 10, .wfc 10, .frc 0x28 bytes) checked against the real BC members.
// DIVERGENCE: the original copies the used tiles into one buffer and keeps paragraph segments; it also sets
// DS:C8B7 ("keep" floor set) and DS:C907 (some furniture varies) and reserves a buffer for the varying
// furniture (tile description +0xB4). Here each tile is a decoded sprite and Varies is kept per tile.
public sealed class BattlefieldTiles
{
    public RowRunSprite?[] Floors { get; } = new RowRunSprite?[16];

    public TileDecoration?[] FloorDecorations { get; } = new TileDecoration?[16];

    public WallTile?[] NorthWalls { get; } = new WallTile?[16];

    public TileDecoration?[] NorthWallDecorations { get; } = new TileDecoration?[16];

    public WallTile?[] WestWalls { get; } = new WallTile?[16];

    public TileDecoration?[] WestWallDecorations { get; } = new TileDecoration?[16];

    public FurnitureTile?[] Furniture { get; } = new FurnitureTile?[16];

    public static BattlefieldTiles Load(CatArchive tileSets, TerrainDescription terrain)
    {
        var tiles = new BattlefieldTiles();
        LoadKind(tileSets, terrain, TileKind.Floor, ".flc", 4, (data, at, i, member) =>
            tiles.Floors[i] = Sprite(data, Word(data, at + 2), member));
        LoadKind(tileSets, terrain, TileKind.FloorDecoration, ".ffc", 6, (data, at, i, member) =>
            tiles.FloorDecorations[i] = new TileDecoration(Sprite(data, Word(data, at + 2), member), Second(data, at + 4, member), 0, 0));
        LoadKind(tileSets, terrain, TileKind.NorthWall, ".nwc", 10, (data, at, i, member) => tiles.NorthWalls[i] = Wall(data, at, member));
        LoadKind(tileSets, terrain, TileKind.NorthWallDecoration, ".nfc", 10, (data, at, i, member) =>
            tiles.NorthWallDecorations[i] = new TileDecoration(Sprite(data, Word(data, at + 2), member), Second(data, at + 4, member), (sbyte)data[at + 8], (sbyte)data[at + 9]));
        LoadKind(tileSets, terrain, TileKind.WestWall, ".wwc", 10, (data, at, i, member) => tiles.WestWalls[i] = Wall(data, at, member));
        LoadKind(tileSets, terrain, TileKind.WestWallDecoration, ".wfc", 10, (data, at, i, member) =>
            tiles.WestWallDecorations[i] = new TileDecoration(Sprite(data, Word(data, at + 2), member), Second(data, at + 4, member), (sbyte)data[at + 8], (sbyte)data[at + 9]));
        LoadKind(tileSets, terrain, TileKind.Furniture, ".frc", 0x28, (data, at, i, member) => tiles.Furniture[i] = FurnitureRecord(data, at, member));
        return tiles;
    }

    /// <summary>
    /// The varying furniture: record <paramref name="record"/> of the set <paramref name="set"/>.frc becomes battlefield furniture tile
    /// <paramref name="tile"/> (each level of the fortress names its own). Null, and nothing changed, for a tile or record out of range.
    /// </summary>
    // orig: R024+0x07A2 (1EF8:07A2) draft (checked against the asm): "%Fs.frc" of the set named at level + 0x33C8 loaded from the catalog
    // "bc" (root 05D9:019A); its record at level + 0x33D2 copied to the tile at level + 0x33D1
    // C: as battle-tileset-load copies each tile of the terrain's furniture set
    // DIVERGENCE: a tile outside 0..15 or a record past the file's end writes or reads past the original's tables; here nothing changes.
    public FurnitureTile? Vary(CatArchive tileSets, string set, int tile, int record)
    {
        if (tile is < 0 or >= 16 || record < 0)
        {
            return null;
        }
        var data = CatMemberDecoder.Decode(tileSets.ReadStored(set + ".frc"), 0x10000);
        if (record * 0x28 + 0x28 > data.Length)
        {
            return null;
        }
        return Furniture[tile] = FurnitureRecord(data, record * 0x28, set + ".frc");
    }

    private static FurnitureTile FurnitureRecord(byte[] data, int at, string member)
    {
        int[] Offsets(int first) => [.. Enumerable.Range(0, 4).Select(k => (int)(short)Word(data, at + 2 * (first + k)))];
        return new FurnitureTile(Sprite(data, Word(data, at + 0x26), member),
            Offsets(0), Offsets(4), Offsets(8), Offsets(12), (sbyte)data[at + 0x20], (sbyte)data[at + 0x21], Word(data, at + 0x24) == 1);
    }

    private static void LoadKind(CatArchive tileSets, TerrainDescription terrain, TileKind kind, string extension, int recordSize,
        Action<byte[], int, int, string> load)
    {
        var name = terrain.SetName(kind);
        if (name.Length == 0)
        {
            return;
        }
        var data = CatMemberDecoder.Decode(tileSets.ReadStored(name + extension), 0x10000);
        for (var i = 1; i < 16; i++)
        {
            var tile = terrain.Tile(kind, i);
            if (tile >= 0)
            {
                load(data, tile * recordSize, i, name + extension);
            }
        }
    }

    // C: the four wall pieces are the record's offsets +2 (plain, DS:C220/DS:C29B), +4 (end joined, DS:C4DD/DS:C62C),
    // +6 (start joined, DS:C601/DS:C658) and +8 (both, DS:C24C/DS:C26F).
    private static WallTile Wall(byte[] data, int at, string member) => new(
        Sprite(data, Word(data, at + 2), member), Sprite(data, Word(data, at + 4), member),
        Sprite(data, Word(data, at + 6), member), Sprite(data, Word(data, at + 8), member));

    // C: a zero second-frame offset means the decoration has no second frame (the original stores segment 0).
    // UNCERTAIN: some sets (battlefield 0x38's floor decorations: record 23 of a 5680-byte .ffc, second frame 0x1B40) give a second
    // frame past the end of the decoded file; the original points into whatever follows the block. No second frame here.
    private static RowRunSprite? Second(byte[] data, int at, string member) =>
        Word(data, at) == 0 || Word(data, at) + 2 > data.Length ? null : Sprite(data, Word(data, at), member);

    // Not in the original: the sprite's asset name, "BC/CAVE.FLC@1A40" (the member and the offset; see IndexedImage.AssetName).
    private static RowRunSprite Sprite(byte[] data, int offset, string member) =>
        RowRunSprite.Decode(data, offset, $"BC/{member.ToUpperInvariant()}@{offset:X4}");

    private static int Word(byte[] data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
}
