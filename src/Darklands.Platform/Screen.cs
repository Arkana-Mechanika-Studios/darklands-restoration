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
                Ids[asset] = id;
            }
            return id;
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
