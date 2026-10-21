namespace Darklands.Platform;

/// <summary>
/// The player's settings file (darklands.settings.json), host-independent. Every value is optional: null
/// means "not set", so a host can tell what the player chose from what a default fills in.
/// New code (host configuration, not a reconstruction); docs/SETTINGS.md describes the file.
/// </summary>
public sealed class GameSettings
{
    /// <summary>The sound choices of the file and of --sound ("config": as CONFIG.DRK says).</summary>
    public static readonly IReadOnlyList<string> SoundCards = ["config", "adlib", "sb", "sbpro2", "mt32", "cm32l", "speaker", "none"];

    public string? GameFolder { get; set; }

    public string? SavesFolder { get; set; }

    public int? Scale { get; set; }

    /// <summary>The window size, "WIDTHxHEIGHT".</summary>
    public string? Resolution { get; set; }

    public bool? Fullscreen { get; set; }

    /// <summary>A folder of replacement pictures (an HD pack; not part of the game). Null: the original pictures.</summary>
    public string? HdPack { get; set; }

    /// <summary>One of <see cref="SoundCards"/>.</summary>
    public string? SoundCard { get; set; }

    public string? Mt32Roms { get; set; }

    /// <summary>Values that replace the ones of the game's CONFIG.DRK (null: CONFIG.DRK as it is).</summary>
    public ConfigDrkSettings? ConfigDrk { get; set; }

    public bool? Banner { get; set; }

    public bool? Intro { get; set; }

    /// <summary>The launcher (the settings screen) at every start; else only when needed.</summary>
    public bool? Launcher { get; set; }

    /// <summary>The words of the game's DOS command line.</summary>
    public List<string>? CommandLine { get; set; }

    public string? TextFont { get; set; }

    public string? ScreenshotsFolder { get; set; }

    /// <summary>The SDL host's section: the SDL2 library to load.</summary>
    public string? SdlLibrary { get; set; }

    /// <summary>The developer console (a host feature, outside the game): on unless false.</summary>
    public bool? ConsoleEnabled { get; set; }

    /// <summary>The console's TrueType font file (default: the bundled DejaVu Sans Mono).</summary>
    public string? ConsoleFont { get; set; }

    /// <summary>The console's text height in pixels at the base window size (720x400); it scales with the window.</summary>
    public int? ConsoleFontSize { get; set; }
}

/// <summary>
/// The four words of CONFIG.DRK (the sound card's number, port, IRQ and DMA channel); a field left null keeps the
/// CONFIG.DRK's own value (or the usual 220h, IRQ 7, DMA 1 without the file).
/// </summary>
public sealed record ConfigDrkSettings(int? Card, int? Port, int? Irq, int? Dma)
{
    /// <summary>Names accepted for the card, with the number the install program writes for it.</summary>
    public static readonly IReadOnlyDictionary<string, int> CardNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["adlib"] = 0,
        ["covox"] = 1,
        ["sb"] = 2,
        ["sbpro"] = 3,
        ["sbpro2"] = 4,
        ["pas"] = 5,
        ["pas16"] = 6,
        ["roland"] = 7,
        ["mt32"] = 7,
        ["cm32l"] = 7,
        ["speaker"] = 0x7F,
        ["none"] = 0xFFFF,
    };
}
