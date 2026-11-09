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
    Even,

    /// <summary>A gentle beam-shaped blur, wider across than down, as a 1992 VGA monitor drew: the dithering blends into its colours; text, outlines and figures keep their edges.</summary>
    Soft,

    /// <summary>Soft with the CRT's scanlines and aperture mask: the softer CRT look.</summary>
    SoftCrt,

    /// <summary>Pixel-art smoothing: staircase diagonals and outlines rounded; dithering, straight edges and lone pixels kept.</summary>
    Hq,

    /// <summary>Scanlines and a light aperture mask over the picture, as on a VGA monitor, with a little brightness given back.</summary>
    Crt,

    /// <summary>A richer CRT: beams that widen with brightness, a stronger aperture mask and a slight glow.</summary>
    CrtPlus,
}

/// <summary>The names of <see cref="PictureFilter"/> in the settings file and on the command line.</summary>
public static class PictureFilters
{
    /// <summary>The names, in the order a host cycles through them.</summary>
    public static readonly IReadOnlyList<string> Names = ["sharp", "even", "soft", "softcrt", "hq", "crt", "crtplus"];

    /// <summary>The filter of a name (any case), or null when it names none. "smooth" (the name before 1.0's last round) is Even.</summary>
    public static PictureFilter? Parse(string? name)
    {
        var key = name?.Trim().ToLowerInvariant();
        if (key == "smooth")
        {
            return PictureFilter.Even;
        }
        var index = key is null ? -1 : Names.ToList().IndexOf(key);
        return index < 0 ? null : (PictureFilter)index;
    }

    /// <summary>The settings-file name of a filter.</summary>
    public static string Name(PictureFilter filter) => Names[(int)filter];

    /// <summary>The filter after this one (the last one goes back to the first).</summary>
    public static PictureFilter Next(PictureFilter filter) => (PictureFilter)(((int)filter + 1) % Names.Count);

    /// <summary>What a player reads for a filter (the launcher, the window's notice): short, to fit the launcher's row.</summary>
    public static string Label(PictureFilter filter) => filter switch
    {
        PictureFilter.Even => "Even",
        PictureFilter.Soft => "Soft",
        PictureFilter.SoftCrt => "Soft CRT",
        PictureFilter.Hq => "HQ",
        PictureFilter.Crt => "CRT",
        PictureFilter.CrtPlus => "CRT+",
        _ => "Sharp",
    };

    /// <summary>One line on what a filter looks like (the launcher's tooltip).</summary>
    public static string Description(PictureFilter filter) => filter switch
    {
        PictureFilter.Even => "Even: every pixel the same size at any window size.",
        PictureFilter.Soft => "Soft: as a 1992 VGA monitor drew it, the dithering blended.",
        PictureFilter.SoftCrt => "Soft CRT: Soft with scanlines and a light mask.",
        PictureFilter.Hq => "HQ: diagonals and outlines rounded.",
        PictureFilter.Crt => "CRT: scanlines and a light mask over sharp pixels.",
        PictureFilter.CrtPlus => "CRT+: a richer CRT, with glow and a stronger mask.",
        _ => "Sharp: hard pixels, as the game drew them.",
    };

    /// <summary>The filters with scanlines and a mask (the curvature applies to them).</summary>
    public static bool IsCrt(PictureFilter filter) => filter is PictureFilter.Crt or PictureFilter.SoftCrt or PictureFilter.CrtPlus;

    /// <summary>The filters only the GPU renderer's shader draws.</summary>
    public static bool NeedsShader(PictureFilter filter) => filter is PictureFilter.Soft or PictureFilter.SoftCrt or PictureFilter.Hq or PictureFilter.CrtPlus;

    /// <summary>What a renderer without the shader draws instead (render targets and blend modes): Even or CRT.</summary>
    public static PictureFilter WithoutShader(PictureFilter filter) => filter switch
    {
        PictureFilter.Soft or PictureFilter.Hq => PictureFilter.Even,
        PictureFilter.SoftCrt or PictureFilter.CrtPlus => PictureFilter.Crt,
        _ => filter,
    };
}
