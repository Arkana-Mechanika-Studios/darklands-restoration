namespace Darklands.Platform;

/// <summary>
/// The game's 320×200 indexed-colour screen and its 256-colour palette. Presentation draws
/// into it; a host shows it.
/// </summary>
public sealed class Screen
{
    public const int Width = 320;
    public const int Height = 200;

    /// <summary>One palette index per pixel, row by row.</summary>
    public byte[] Pixels { get; } = new byte[Width * Height];

    /// <summary>The 256 colours as 0xAARRGGBB.</summary>
    public uint[] Palette { get; } = new uint[256];

    /// <summary>
    /// Per pixel, the <see cref="ScreenOwners"/> id of the asset that drew it (0: none, or not known).
    /// </summary>
    // Not in the original: a record kept beside the pixels for replacement graphics (an HD backend draws an asset's
    // own texture over the pixels it still owns and keeps the original pixels everywhere else). It never changes Pixels.
    public int[] Owners { get; } = new int[Width * Height];

    /// <summary>
    /// Per owned pixel, which pixel of its asset it shows (<see cref="ScreenTexel"/>): with the asset's size this places
    /// the asset's drawing on the screen. Meaningless where <see cref="Owners"/> is 0.
    /// </summary>
    // Not in the original, like Owners: the owner names the asset, the texel says where it was drawn (an asset may be
    // drawn several times, clipped, mirrored or partly covered, so its rectangle cannot be told from the owners alone).
    public int[] Texels { get; } = new int[Width * Height];

    /// <summary>Copies <see cref="Owners"/> and <see cref="Texels"/> to another screen.</summary>
    public void CopyOwnershipTo(Screen other)
    {
        Owners.CopyTo(other.Owners, 0);
        Texels.CopyTo(other.Texels, 0);
    }
}

/// <summary>
/// The entries of <see cref="Screen.Texels"/>: the column and row of the asset's own pixel (before mirroring), and
/// whether the asset was drawn turned over left to right or top to bottom.
/// </summary>
// Not in the original (see Screen.Owners).
public static class ScreenTexel
{
    private const int FlipXBit = 1 << 24;
    private const int FlipYBit = 1 << 25;

    /// <summary>An owned pixel whose place in its asset is not known (a replacement leaves it alone).</summary>
    public const int Unknown = -1;

    /// <summary>Column and row 0..4095 of the asset's pixel.</summary>
    public static int Pack(int column, int row, bool flipX = false, bool flipY = false) =>
        (column & 0xFFF) | (row & 0xFFF) << 12 | (flipX ? FlipXBit : 0) | (flipY ? FlipYBit : 0);

    public static (int Column, int Row, bool FlipX, bool FlipY) Unpack(int texel) =>
        (texel & 0xFFF, texel >> 12 & 0xFFF, (texel & FlipXBit) != 0, (texel & FlipYBit) != 0);
}

/// <summary>
/// The asset names behind <see cref="Screen.Owners"/>: one id per name, interned for the whole program, so the ids
/// mean the same in every screen, saved region and copy of a frame. Id 0 is no owner.
/// </summary>
// Not in the original (see Screen.Owners). Interned rather than per frame: frames are copied (the shown screen, the
// back pages, saved regions) and an id must stay valid in every copy; the table only grows by the number of distinct
// assets drawn, a few thousand at most.
public static class ScreenOwners
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly List<string?> Names = [null];
    private static readonly List<(int Width, int Height)> Sizes = [(0, 0)];
    private static readonly Dictionary<int, (byte[] Pixels, uint[] Palette)> OwnColors = [];

    /// <summary>The id of an asset name; 0 for none.</summary>
    public static int IdOf(string? asset)
    {
        if (asset is null)
        {
            return 0;
        }
        lock (Gate)
        {
            if (!Ids.TryGetValue(asset, out var id))
            {
                id = Names.Count;
                Names.Add(asset);
                Sizes.Add((0, 0));
                Ids[asset] = id;
            }
            return id;
        }
    }

    /// <summary>The id of an asset name, recording the asset's size (the largest given) for <see cref="SizeOf"/>.</summary>
    public static int IdOf(string? asset, int width, int height)
    {
        var id = IdOf(asset);
        if (id != 0)
        {
            lock (Gate)
            {
                var (w, h) = Sizes[id];
                Sizes[id] = (Math.Max(w, width), Math.Max(h, height));
            }
        }
        return id;
    }

    /// <summary>The size of the asset in its own pixels, (0, 0) when none was recorded.</summary>
    public static (int Width, int Height) SizeOf(int id)
    {
        lock (Gate)
        {
            return id > 0 && id < Sizes.Count ? Sizes[id] : (0, 0);
        }
    }

    /// <summary>
    /// Records an asset's own pixels (row by row, its size) and the colours it is meant to be seen in (the palette it was
    /// drawn with): a replacement then follows what the palette does to it since (a fade, a remap).
    /// </summary>
    public static void SetOwnColors(int id, ReadOnlySpan<byte> pixels, uint[] palette)
    {
        if (id == 0)
        {
            return;
        }
        lock (Gate)
        {
            if (OwnColors.TryGetValue(id, out var known) && known.Pixels.AsSpan().SequenceEqual(pixels) && known.Palette.AsSpan().SequenceEqual(palette))
            {
                return;
            }
            OwnColors[id] = (pixels.ToArray(), (uint[])palette.Clone());
        }
    }

    /// <summary>The asset's own pixels and colours (<see cref="SetOwnColors"/>), null when none were recorded.</summary>
    public static (byte[] Pixels, uint[] Palette)? OwnColorsOf(int id)
    {
        lock (Gate)
        {
            return OwnColors.TryGetValue(id, out var known) ? known : null;
        }
    }

    /// <summary>The asset name of an id; null for 0 or an unknown id.</summary>
    public static string? NameOf(int id)
    {
        lock (Gate)
        {
            return id > 0 && id < Names.Count ? Names[id] : null;
        }
    }
}
