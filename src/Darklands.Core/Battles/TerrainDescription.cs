using System.Text;

namespace Darklands.Core.Battles;

/// <summary>The seven kinds of battlefield tiles, in the order of the terrain description.</summary>
public enum TileKind
{
    Floor = 0,
    FloorDecoration = 1,
    NorthWall = 2,
    NorthWallDecoration = 3,
    WestWall = 4,
    WestWallDecoration = 5,
    Furniture = 6,
}

/// <summary>
/// What a battlefield looks like (<c>terrain.fil</c>, the generator's tile description, 0xC4
/// bytes): the background palette and, for each tile kind, the tile set in BC (".flc", ".nwc" …)
/// and which of its tiles fill the battlefield's tile numbers 1..15. The decoration and furniture
/// kinds also give the number below which tiles are animated.
/// </summary>
// orig: tile-description (R004 enemy buffer +0xF504), written by the generators, saved as terrain.fil by battlefield-builder-entry R004+0x0000
// and read by battle-tileset-load R002+0x0C8C; defaults from R004+0x1134 (byte-matched per the draft notes)
public sealed class TerrainDescription
{
    public const int Size = 0xC4;

    private static readonly int[] NameAt = [0x01, 0x1A, 0x34, 0x4D, 0x67, 0x80, 0x9A];
    private static readonly int[] TilesAt = [0x0A, 0x24, 0x3D, 0x57, 0x70, 0x8A, 0xA4];
    private static readonly int[] AnimatedAt = [-1, 0x23, -1, 0x56, -1, 0x89, 0xA3];
    private const int VaryingCountAt = 0xB4;

    /// <summary>The defaults the loader sets before a generator runs: no sets, every tile −1, animation limits 2/8/8/8.</summary>
    // orig: R004+0x1134 (0E76:1134) draft (byte-matched per the draft notes)
    // C: the name tails and +0xB5..+0xC3 are never written: they hold allocator memory in the original; 0 here.
    public TerrainDescription()
    {
        foreach (var at in TilesAt)
        {
            Array.Fill(Bytes, (byte)0xFF, at, 16);
        }
        Bytes[0x23] = 2;
        Bytes[0x56] = Bytes[0x89] = Bytes[0xA3] = 8;
    }

    public TerrainDescription(byte[] bytes)
    {
        if (bytes.Length < Size - 1)
        {
            throw new ArgumentException("A terrain description is 0xC4 bytes.", nameof(bytes));
        }
        bytes.AsSpan(0, Math.Min(bytes.Length, Size)).CopyTo(Bytes);
    }

    /// <summary>The record (terrain.fil).</summary>
    public byte[] Bytes { get; } = new byte[Size];

    /// <summary>+0x00: which of BKGNDPAL.DAT's palettes colours the tiles (also the terrain class: 0 keep, 1 outdoor, 3 city, 5 cave).</summary>
    public int BackgroundPalette
    {
        get => Bytes[0];
        set => Bytes[0] = (byte)value;
    }

    /// <summary>The tile set file of a kind ("city", "citynw15"; "" none).</summary>
    public string SetName(TileKind kind)
    {
        var span = Bytes.AsSpan(NameAt[(int)kind], 9);
        var end = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
    }

    /// <summary>Sets the tile set name (as <c>_fstrcpy</c>/<c>sprintf</c>: the text and its zero; the tail is left as it was).</summary>
    public void SetSetName(TileKind kind, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        var at = NameAt[(int)kind];
        bytes.AsSpan(0, Math.Min(bytes.Length, 8)).CopyTo(Bytes.AsSpan(at));
        Bytes[at + Math.Min(bytes.Length, 8)] = 0;
    }

    /// <summary>The set's tile used for battlefield tile number <paramref name="number"/> (−1 none).</summary>
    public int Tile(TileKind kind, int number) => (sbyte)Bytes[TilesAt[(int)kind] + number];

    public void SetTile(TileKind kind, int number, int setTile) => Bytes[TilesAt[(int)kind] + number] = (byte)setTile;

    /// <summary>The tile numbers below which tiles of a decoration or furniture kind are animated (−1 for kinds without it).</summary>
    /// <summary>+0xA3: furniture tiles from this number on block sight (DS:C1CF once the tile set is loaded).</summary>
    // C: the furniture section's "animated below" byte doubles as the sight limit (furniture-sprites-load R024 07A2 sets DS:C1CF).
    public int FirstSightBlockingFurniture => Bytes[0xA3];

    public int AnimatedBelow(TileKind kind) => AnimatedAt[(int)kind] < 0 ? -1 : Bytes[AnimatedAt[(int)kind]];

    public void SetAnimatedBelow(TileKind kind, int value) => Bytes[AnimatedAt[(int)kind]] = (byte)value;

    /// <summary>+0xB4/+0xB5: furniture set tiles that may be swapped in while fighting.</summary>
    public IReadOnlyList<int> VaryingFurniture =>
        [.. Enumerable.Range(0, Math.Max(0, Math.Min((int)(sbyte)Bytes[VaryingCountAt], Size - VaryingCountAt - 1))).Select(i => (int)(sbyte)Bytes[VaryingCountAt + 1 + i])];

    /// <summary>Sets the furniture tiles that may be swapped in while fighting (+0xB4 their number, +0xB5 the tiles).</summary>
    public void SetVaryingFurniture(params int[] tiles)
    {
        Bytes[VaryingCountAt] = (byte)tiles.Length;
        for (var i = 0; i < tiles.Length; i++)
        {
            Bytes[VaryingCountAt + 1 + i] = (byte)tiles[i];
        }
    }

    /// <summary>Writes a byte of the record directly (the generators' tables of tile numbers).</summary>
    public byte this[int offset]
    {
        get => Bytes[offset];
        set => Bytes[offset] = value;
    }
}
