using System.Globalization;

namespace Darklands.Platform;

/// <summary>What the command line gave (null / false: not given). Hosts fill it from their own options.</summary>
public sealed class CommandLineOptions
{
    public string? GameFolder { get; set; }

    public string? SavesFolder { get; set; }

    public string? Scale { get; set; }

    public string? Resolution { get; set; }

    public bool Fullscreen { get; set; }

    public string? HdPack { get; set; }

    public string? Sound { get; set; }

    public bool NoAudio { get; set; }

    public string? Mt32Roms { get; set; }

    public bool NoBanner { get; set; }

    public bool NoIntro { get; set; }

    /// <summary>The words after "--" (null without the "--").</summary>
    public IReadOnlyList<string>? GameCommandLine { get; set; }

    public string? TextFont { get; set; }

    public string? ScreenshotsFolder { get; set; }

    public string? SdlLibrary { get; set; }

    public bool NoConsole { get; set; }

    /// <summary>
    /// The settings-file keys this command line (or an environment variable) overrides, each with what overrides it
    /// ("--fullscreen", "DARKLANDS_DIR"): a settings screen shows them as fixed, since changing the file would not change them.
    /// </summary>
    public IReadOnlyDictionary<string, string> Overrides(Func<string, string?> environment)
    {
        var fixedKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        void By(string key, bool overridden, string by)
        {
            if (overridden)
            {
                fixedKeys.TryAdd(key, by);
            }
        }
        static bool Given(string? text) => !string.IsNullOrWhiteSpace(text);
        By("gameFolder", Given(GameFolder), "the command line");
        By("gameFolder", Given(environment("DARKLANDS_DIR")), "DARKLANDS_DIR");
        By("savesFolder", Given(SavesFolder), "--saves");
        By("savesFolder", Given(environment("DARKLANDS_SAVES")), "DARKLANDS_SAVES");
        var size = Scale is not null ? "--scale" : Resolution is not null ? "--resolution" : null;
        By("display.scale", size is not null, size ?? "");
        By("display.resolution", size is not null, size ?? "");
        By("display.fullscreen", Fullscreen, "--fullscreen");
        By("display.hdPack", Given(HdPack), "--hd-pack");
        By("sound.card", NoAudio, "--no-audio");
        By("sound.card", Given(Sound), "--sound");
        By("sound.mt32Roms", Given(Mt32Roms), "--mt32-roms");
        By("sound.mt32Roms", Given(environment("DARKLANDS_MT32_ROMS")), "DARKLANDS_MT32_ROMS");
        By("startup.banner", NoBanner, "--no-banner");
        By("startup.intro", NoIntro, "--no-intro");
        By("startup.commandLine", GameCommandLine is not null, "--");
        return fixedKeys;
    }
}

/// <summary>
/// The settings a host runs with: command line, else environment variables, else the settings file, else the defaults.
/// Values the player did not set stay null where the host has its own fallback (usual game folders, the game folder's SAVES ...).
/// </summary>
public sealed class ResolvedSettings
{
    public string? GameFolder { get; private init; }

    public string? SavesFolder { get; private init; }

    /// <summary>The window size, null when neither a scale nor a resolution was set (the host's own default).</summary>
    public (int Width, int Height)? Window { get; private init; }

    public bool Fullscreen { get; private init; }

    /// <summary>The HD pack's folder; null: none (the original pictures).</summary>
    public string? HdPack { get; private init; }

    /// <summary>adlib, sb, sbpro2, mt32, cm32l or speaker; null: the game's own choice from CONFIG.DRK.</summary>
    public string? SoundChoice { get; private init; }

    /// <summary>The sound card "none" or --no-audio.</summary>
    public bool NoAudio { get; private init; }

    public string? Mt32Roms { get; private init; }

    public ConfigDrkSettings? ConfigDrk { get; private init; }

    public bool ShowBanner { get; private init; }

    public bool PlayIntro { get; private init; }

    /// <summary>The launcher at every start (the settings file's startup.launcher).</summary>
    public bool AlwaysLaunch { get; private init; }

    public IReadOnlyList<string> CommandLine { get; private init; } = [];

    public string? TextFont { get; private init; }

    public string? ScreenshotsFolder { get; private init; }

    public string? SdlLibrary { get; private init; }

    /// <summary>The developer console is available (a host feature).</summary>
    public bool ConsoleEnabled { get; private init; }

    public string? ConsoleFont { get; private init; }

    /// <summary>Text height in pixels at the base window size.</summary>
    public int ConsoleFontSize { get; private init; }

    /// <summary>
    /// Combines the sources. A scale or resolution on the command line replaces the file's scale and resolution.
    /// Throws <see cref="ArgumentException"/> (or <see cref="FormatException"/>) with a message for the player when the
    /// command line's own values are invalid.
    /// </summary>
    public static ResolvedSettings Resolve(CommandLineOptions cli, Func<string, string?> environment, GameSettings file)
    {
        static string? Set(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
        string? Pick(string? fromCli, string? variable, string? fromFile) =>
            Set(fromCli) ?? (variable is null ? null : Set(environment(variable))) ?? Set(fromFile);

        int? scale;
        string? resolution;
        if (cli.Scale is not null || cli.Resolution is not null)
        {
            scale = cli.Scale is null ? null : int.Parse(cli.Scale, NumberStyles.None, CultureInfo.InvariantCulture);
            resolution = cli.Resolution;
        }
        else
        {
            scale = file.Scale;
            resolution = file.Resolution;
        }
        (int, int)? window = scale is null && resolution is null
            ? null
            : DisplayLayout.WindowSize(scale, resolution is null ? null : DisplayLayout.ParseResolution(resolution));

        var sound = Set(cli.Sound) ?? Set(file.SoundCard);
        if (sound is not null && !GameSettings.SoundCards.Contains(sound))
        {
            throw new ArgumentException($"unknown sound card \"{sound}\" ({string.Join(", ", GameSettings.SoundCards)})");
        }
        return new ResolvedSettings
        {
            GameFolder = Pick(cli.GameFolder, "DARKLANDS_DIR", file.GameFolder),
            SavesFolder = Pick(cli.SavesFolder, "DARKLANDS_SAVES", file.SavesFolder),
            Window = window,
            Fullscreen = cli.Fullscreen || (file.Fullscreen ?? false),
            HdPack = Pick(cli.HdPack, null, file.HdPack),
            SoundChoice = sound is null or "config" or "none" ? null : sound,
            NoAudio = cli.NoAudio || (cli.Sound is null && sound == "none") || cli.Sound == "none",
            Mt32Roms = Pick(cli.Mt32Roms, "DARKLANDS_MT32_ROMS", file.Mt32Roms),
            ConfigDrk = file.ConfigDrk,
            ShowBanner = !cli.NoBanner && (file.Banner ?? true),
            PlayIntro = !cli.NoIntro && (file.Intro ?? true),
            AlwaysLaunch = file.Launcher ?? false,
            CommandLine = cli.GameCommandLine ?? file.CommandLine ?? [],
            TextFont = Pick(cli.TextFont, "DARKLANDS_TEXT_FONT", file.TextFont),
            ScreenshotsFolder = Pick(cli.ScreenshotsFolder, null, file.ScreenshotsFolder),
            SdlLibrary = Pick(cli.SdlLibrary, null, file.SdlLibrary),
            ConsoleEnabled = !cli.NoConsole && (file.ConsoleEnabled ?? true),
            ConsoleFont = file.ConsoleFont,
            ConsoleFontSize = file.ConsoleFontSize ?? 14,
        };
    }
}
