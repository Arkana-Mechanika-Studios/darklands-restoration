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

    /// <summary>true: the cleaner drawing of 0.9.5 (GameOptions.CleanDrawing); false/null: the 1992 game's drawing, oddities included.</summary>
    public bool? CleanDrawing { get; set; }

    /// <summary>How the picture is drawn on the window: one of <see cref="PictureFilters.Names"/> (null: sharp).</summary>
    public string? Filter { get; set; }

    /// <summary>true: the CRT filter's picture slightly curved, as a tube's (null/false: flat).</summary>
    public bool? CrtCurvature { get; set; }

    /// <summary>One of <see cref="SoundCards"/>.</summary>
    public string? SoundCard { get; set; }

    public string? Mt32Roms { get; set; }

    /// <summary>A folder of replacement music and battle effects (an audio pack; not part of the game). Null: the game's own sound.</summary>
    public string? AudioPack { get; set; }

    /// <summary>The SoundFont (.sf2 file) that plays the audio pack's .mid files. Null: the first .sf2 in the SOUNDFONT folder next to the program.</summary>
    public string? SoundFont { get; set; }

    /// <summary>The mods to play with (docs/MODS.md): mod folders, or names of folders in a mods folder. Null or empty: none (the game's own cards).</summary>
    public List<string>? Mods { get; set; }

    /// <summary>Values that replace the ones of the game's CONFIG.DRK (null: CONFIG.DRK as it is).</summary>
    public ConfigDrkSettings? ConfigDrk { get; set; }

    public bool? Banner { get; set; }

    public bool? Intro { get; set; }


    /// <summary>The words of the game's DOS command line.</summary>
    public List<string>? CommandLine { get; set; }

    public string? TextFont { get; set; }

    public string? ScreenshotsFolder { get; set; }

    /// <summary>The SDL host's section: the SDL3 library to load.</summary>
    public string? SdlLibrary { get; set; }

    /// <summary>The mouse wheel gives ↑ / ↓ in the menus and lists (a host comfort, SDL host): on unless false.</summary>
    public bool? MouseWheel { get; set; }

    /// <summary>↑ / ↓ and the digits work at a card menu with the pointer off its lines (an interface convenience): on unless false.</summary>
    public bool? CardMenuKeys { get; set; }

    /// <summary>The developer console (a host feature, outside the game): on unless false.</summary>
    public bool? ConsoleEnabled { get; set; }

    /// <summary>The console's TrueType font file (default: the bundled DejaVu Sans Mono).</summary>
    public string? ConsoleFont { get; set; }

    /// <summary>The console's text height in pixels at the base window size (720x400); it scales with the window.</summary>
    public int? ConsoleFontSize { get; set; }

    /// <summary>A copy with its own lists (a change to the copy leaves this one as it is).</summary>
    public GameSettings Copy()
    {
        var copy = (GameSettings)MemberwiseClone();
        copy.Mods = Mods?.ToList();
        copy.CommandLine = CommandLine?.ToList();
        return copy;
    }
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
