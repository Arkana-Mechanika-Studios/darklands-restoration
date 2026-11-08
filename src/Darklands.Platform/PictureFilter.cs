namespace Darklands.Platform;

/// <summary>
/// How a host draws the game's 320x200 picture on a larger window (the settings file's display.filter, --filter). Presentation
/// only: the game, its timing, its screenshots and the frames the oracles compare are the same with every filter.
/// New code (host configuration, not a reconstruction).
/// </summary>
public enum PictureFilter
{
    /// <summary>Each window pixel takes the nearest game pixel: hard pixels, a little uneven at sizes that are not whole multiples (the default).</summary>
    Sharp,

    /// <summary>"Sharp bilinear": the picture enlarged by whole numbers with hard pixels, then smoothly to the window: even, crisp pixels at any size.</summary>
    Smooth,

    /// <summary>Scanlines and a light aperture mask over the picture, as on a VGA monitor, with a little brightness given back. No curvature.</summary>
    Crt,
}

/// <summary>The names of <see cref="PictureFilter"/> in the settings file and on the command line.</summary>
public static class PictureFilters
{
    /// <summary>The names, in the order a host cycles through them.</summary>
    public static readonly IReadOnlyList<string> Names = ["sharp", "smooth", "crt"];

    /// <summary>The filter of a name (any case), or null when it names none.</summary>
    public static PictureFilter? Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "sharp" => PictureFilter.Sharp,
            "smooth" => PictureFilter.Smooth,
            "crt" => PictureFilter.Crt,
            _ => null,
        };

    /// <summary>The settings-file name of a filter.</summary>
    public static string Name(PictureFilter filter) => Names[(int)filter];

    /// <summary>The filter after this one (the last one goes back to the first).</summary>
    public static PictureFilter Next(PictureFilter filter) => (PictureFilter)(((int)filter + 1) % Names.Count);

    /// <summary>What a player reads for a filter (the launcher, the window's notice).</summary>
    public static string Label(PictureFilter filter) => filter switch
    {
        PictureFilter.Smooth => "Smooth pixels",
        PictureFilter.Crt => "CRT",
        _ => "Sharp",
    };
}
