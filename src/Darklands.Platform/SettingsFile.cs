using System.Globalization;
using System.Text.Json;

namespace Darklands.Platform;

/// <summary>The outcome of reading a settings file: what it says and what was wrong with it (already ignored).</summary>
public sealed record SettingsLoad(GameSettings Settings, string? Path, IReadOnlyList<string> Problems);

/// <summary>
/// darklands.settings.json: where it is looked for, how it is read (comments and trailing commas allowed; unknown keys and
/// invalid values are reported and ignored, never fatal) and the commented template written on the first run.
/// New code (host configuration, not a reconstruction); docs/SETTINGS.md describes the file.
/// </summary>
public static class SettingsFile
{
    public const string FileName = "darklands.settings.json";

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The places searched after an explicit --settings file, in order: next to the executable, then %APPDATA%\DarklandsRestoration.</summary>
    public static IReadOnlyList<string> SearchPaths(string? programFolder = null, string? appData = null) =>
    [
        Path.Combine(programFolder ?? AppContext.BaseDirectory, FileName),
        Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarklandsRestoration", FileName),
    ];

    /// <summary>The settings file to use: the explicit one (even when missing, so it can be reported), else the first existing of <see cref="SearchPaths"/>.</summary>
    public static string? Locate(string? explicitFile, string? programFolder = null, string? appData = null) =>
        explicitFile ?? SearchPaths(programFolder, appData).FirstOrDefault(File.Exists);

    /// <summary>Reads the file; a missing or unreadable one gives the defaults and a problem.</summary>
    public static SettingsLoad Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoad(new GameSettings(), path, [$"settings: cannot read {path}: {e.Message}"]);
        }
        var load = Parse(text, Path.GetDirectoryName(Path.GetFullPath(path)));
        return load with { Path = path, Problems = [.. load.Problems.Select(p => $"settings: {path}: {p}")] };
    }

    /// <summary>
    /// Reads settings from text. Relative folders and files are taken relative to <paramref name="baseFolder"/> (the settings
    /// file's folder), and %VARIABLES% in them are expanded.
    /// </summary>
    public static SettingsLoad Parse(string text, string? baseFolder = null)
    {
        var settings = new GameSettings();
        var problems = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ReadOptions);
        }
        catch (JsonException e)
        {
            problems.Add($"not valid JSON ({e.Message}); every setting ignored");
            return new SettingsLoad(settings, null, problems);
        }
        using (document)
        {
            new Reader(problems, baseFolder).Root(document.RootElement, settings);
        }
        return new SettingsLoad(settings, null, problems);
    }

    /// <summary>Writes the commented template to <paramref name="path"/> (creating its folder); false when the file already exists or cannot be written.</summary>
    public static bool TryWriteTemplate(string path, out string message)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(Template);
            message = $"wrote the settings template {path}";
            return true;
        }
        catch (IOException e) when (File.Exists(path))
        {
            message = $"{path} already exists ({e.Message}); not overwritten";
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            message = $"cannot write {path}: {e.Message}";
            return false;
        }
    }

    /// <summary>
    /// First run: with no settings file found anywhere, writes the template next to the executable, else in %APPDATA%.
    /// Returns the path written, or null (nothing could be written; the reason goes to <paramref name="report"/>).
    /// </summary>
    public static string? WriteFirstRunTemplate(Action<string> report, string? programFolder = null, string? appData = null)
    {
        foreach (var path in SearchPaths(programFolder, appData))
        {
            if (TryWriteTemplate(path, out var message))
            {
                report($"settings: no {FileName} found; {message}. Edit it to set the game folder, sound and display options.");
                return path;
            }
        }
        report($"settings: no {FileName} found and none could be written; using the defaults (--write-settings <file> writes one).");
        return null;
    }

    public static int? ParseWord(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var style = NumberStyles.None;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = NumberStyles.AllowHexSpecifier;
        }
        else if (text.EndsWith('h') || text.EndsWith('H'))
        {
            text = text[..^1];
            style = NumberStyles.AllowHexSpecifier;
        }
        return int.TryParse(text, style, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private sealed class Reader(List<string> problems, string? baseFolder)
    {
        public void Root(JsonElement root, GameSettings settings)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add("the top level must be an object; every setting ignored");
                return;
            }
            foreach (var property in root.EnumerateObject())
            {
                var name = property.Name;
                var value = property.Value;
                switch (name)
                {
                    case "gameFolder": settings.GameFolder = FilePath(name, value); break;
                    case "savesFolder": settings.SavesFolder = FilePath(name, value); break;
                    case "textFont": settings.TextFont = FilePath(name, value); break;
                    case "screenshotsFolder": settings.ScreenshotsFolder = FilePath(name, value); break;
                    case "display": Section(name, value, settings, Display); break;
                    case "sound": Section(name, value, settings, Sound); break;
                    case "startup": Section(name, value, settings, Startup); break;
                    case "sdl": Section(name, value, settings, Sdl); break;
                    case "console": Section(name, value, settings, Console); break;
                    default: Unknown(name); break;
                }
            }
            if (settings.Resolution is not null)
            {
                // a resolution replaces the scale (the template carries "scale": 1 beside an empty resolution)
                settings.Scale = null;
            }
        }

        private void Section(string name, JsonElement value, GameSettings settings, Action<string, JsonProperty, GameSettings> apply)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return;
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"\"{name}\" must be an object; ignored");
                return;
            }
            foreach (var property in value.EnumerateObject())
            {
                apply(name, property, settings);
            }
        }

        private void Display(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "scale":
                    if (Integer(key, property.Value) is { } scale)
                    {
                        if (scale <= 0)
                        {
                            problems.Add($"\"{key}\" must be a positive whole number; ignored");
                        }
                        else
                        {
                            settings.Scale = scale;
                        }
                    }
                    break;
                case "resolution":
                    if (Text(key, property.Value) is { } text)
                    {
                        try
                        {
                            DisplayLayout.ParseResolution(text);
                            settings.Resolution = text;
                        }
                        catch (FormatException)
                        {
                            problems.Add($"\"{key}\" must be WIDTHxHEIGHT with positive numbers, e.g. \"1440x800\" (not \"{text}\"); ignored");
                        }
                    }
                    break;
                case "fullscreen": settings.Fullscreen = Boolean(key, property.Value) ?? settings.Fullscreen; break;
                default: Unknown(key); break;
            }
        }

        private void Sound(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "card":
                    if (Text(key, property.Value) is { } card)
                    {
                        if (GameSettings.SoundCards.Contains(card, StringComparer.OrdinalIgnoreCase))
                        {
                            settings.SoundCard = card.ToLowerInvariant();
                        }
                        else
                        {
                            problems.Add($"\"{key}\" must be one of {string.Join(", ", GameSettings.SoundCards)} (not \"{card}\"); ignored");
                        }
                    }
                    break;
                case "mt32Roms": settings.Mt32Roms = FilePath(key, property.Value); break;
                case "configDrk": ConfigDrk(key, property.Value, settings); break;
                default: Unknown(key); break;
            }
        }

        private void ConfigDrk(string key, JsonElement value, GameSettings settings)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return;
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"\"{key}\" must be null or an object with card, port, irq, dma; ignored");
                return;
            }
            int? card = null, port = null, irq = null, dma = null;
            foreach (var property in value.EnumerateObject())
            {
                var field = $"{key}.{property.Name}";
                switch (property.Name)
                {
                    case "card": card = CardNumber(field, property.Value); break;
                    case "port": port = Word(field, property.Value); break;
                    case "irq": irq = Word(field, property.Value); break;
                    case "dma": dma = Word(field, property.Value); break;
                    default: Unknown(field); break;
                }
            }
            if (card is not null || port is not null || irq is not null || dma is not null)
            {
                settings.ConfigDrk = new ConfigDrkSettings(card, port, irq, dma);
            }
        }

        private void Startup(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "banner": settings.Banner = Boolean(key, property.Value) ?? settings.Banner; break;
                case "intro": settings.Intro = Boolean(key, property.Value) ?? settings.Intro; break;
                case "commandLine": settings.CommandLine = Words(key, property.Value) ?? settings.CommandLine; break;
                default: Unknown(key); break;
            }
        }

        private void Sdl(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            if (property.Name == "library")
            {
                settings.SdlLibrary = FilePath(key, property.Value);
            }
            else
            {
                Unknown(key);
            }
        }

        private void Console(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "enabled": settings.ConsoleEnabled = Boolean(key, property.Value) ?? settings.ConsoleEnabled; break;
                case "font": settings.ConsoleFont = FilePath(key, property.Value); break;
                case "fontSize":
                    if (Integer(key, property.Value) is { } size)
                    {
                        if (size is >= 6 and <= 64)
                        {
                            settings.ConsoleFontSize = size;
                        }
                        else
                        {
                            problems.Add($"\"{key}\" must be from 6 to 64; ignored");
                        }
                    }
                    break;
                default: Unknown(key); break;
            }
        }

        private void Unknown(string key) => problems.Add($"unknown key \"{key}\" ignored");

        private string? Text(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                problems.Add($"\"{key}\" must be a string; ignored");
                return null;
            }
            return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString();
        }

        private string? FilePath(string key, JsonElement value)
        {
            var text = Text(key, value);
            if (text is null)
            {
                return null;
            }
            text = Environment.ExpandEnvironmentVariables(text);
            return baseFolder is null || Path.IsPathRooted(text) ? text : Path.GetFullPath(Path.Combine(baseFolder, text));
        }

        private bool? Boolean(string key, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default:
                    problems.Add($"\"{key}\" must be true or false; ignored");
                    return null;
            }
        }

        private int? Integer(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }
            problems.Add($"\"{key}\" must be a whole number; ignored");
            return null;
        }

        /// <summary>A 16-bit word: a whole number, or text such as "0x220" or "220h" (hexadecimal) or "544".</summary>
        private int? Word(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            int? number = null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var plain))
            {
                number = plain;
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                number = ParseWord(value.GetString());
            }
            if (number is null or < 0 or > 0xFFFF)
            {
                problems.Add($"\"{key}\" must be a number from 0 to 65535 (or text like \"0x220\"); ignored");
                return null;
            }
            return number;
        }

        private int? CardNumber(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { } name && ConfigDrkSettings.CardNames.TryGetValue(name.Trim(), out var known))
            {
                return known;
            }
            var before = problems.Count;
            var number = Word(key, value);
            if (number is null && problems.Count > before)
            {
                problems[^1] = $"\"{key}\" must be the card's number (0 AdLib, 2 Sound Blaster, 4 Sound Blaster Pro 2, 7 Roland, 127 speaker ...) or a name ({string.Join(", ", ConfigDrkSettings.CardNames.Keys)}); ignored";
            }
            return number;
        }

        private List<string>? Words(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                return [.. (value.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)];
            }
            if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String))
            {
                return [.. value.EnumerateArray().Select(e => e.GetString() ?? "")];
            }
            problems.Add($"\"{key}\" must be a list of words (or one string); ignored");
            return null;
        }
    }

    /// <summary>The commented file written on the first run: every key, its default and the command-line option that does the same.</summary>
    public const string Template = """
        // Darklands settings. Comments (// and /* */) and trailing commas are allowed.
        // Each key shows its default; "" (empty text) means the default described above it, and a key left out takes its default.
        // Precedence: command line > environment variables > this file > defaults.
        // Relative paths are taken relative to this file's folder; %VARIABLES% are expanded.
        // File lookup: --settings <file>, else next to the program, else %APPDATA%\DarklandsRestoration\darklands.settings.json.
        {
          // The folder with the original game (DARKLAND.EXE and its data); the GOG or Steam install folder is fine.
          // "": searched (the program's folder and the one above, GOG, Steam, installed programs, usual folders) and written
          // here when found; if not found, Windows asks for it. Command line: the first argument; environment: DARKLANDS_DIR.
          "gameFolder": "",

          // Where the saved games (DKSAVEn.SAV) go. "": the game folder's SAVES, shared with the original.
          // Set a folder here to keep this program's saves elsewhere. Command line: --saves <folder>; environment: DARKLANDS_SAVES.
          "savesFolder": "",

          "display": {
            // The window is 720x400 times this whole number. Command line: --scale N.
            "scale": 1,
            // Or any window size, "WIDTHxHEIGHT" such as "1440x800"; when set it replaces scale. Command line: --resolution WxH.
            "resolution": "",
            // Command line: --fullscreen.
            "fullscreen": false
          },

          "sound": {
            // "config": the game's own choice from its CONFIG.DRK.
            // Or "adlib", "sb", "sbpro2", "mt32", "cm32l", "speaker" (not emulated: silent) or "none" (no sound).
            // Command line: --sound <card> (--no-audio: none).
            "card": "config",
            // Folder with the MT-32 / CM-32L ROM files (or MT32, CM32L subfolders). "": MT32_ROMS next to the program.
            // Without the ROMs the OPL3 FM drivers play (with a warning).
            // Command line: --mt32-roms <folder>; environment: DARKLANDS_MT32_ROMS.
            "mt32Roms": "",
            // To replace values of CONFIG.DRK as if the file held them, remove the // before "configDrk" (any field may be left out).
            // card is the install program's number (0 AdLib, 1 Covox, 2 Sound Blaster, 3 SB Pro, 4 SB Pro 2, 5 PAS, 6 PAS 16,
            // 7 Roland, 127 speaker) or a name (adlib, sb, sbpro2, roland, speaker, none); it also picks the driver files
            // (0-3 a, 4-6 p, 7 r, 127 i, others n + SOUND.DLC/.DLB). port, irq and dma are the Sound Blaster's resources.
            // No command-line option.
            // "configDrk": { "card": 2, "port": "0x220", "irq": 7, "dma": 1 },
          },

          "startup": {
            // The BANNER.DAT banner and the start-up checks. Command line: --no-banner.
            "banner": true,
            // The opening animation. Command line: --no-intro.
            "intro": true,
            // The game's own DOS command line, a list of words: ["-Q"] (no animations), ["-O"], ["-AR"] (driver letter).
            // Command line: -- <words>.
            "commandLine": []
          },

          // The 8x16 text-screen font: a 4096-byte VGA BIOS font dump or its base64. "": a built-in font.
          // Command line: --text-font <file>; environment: DARKLANDS_TEXT_FONT.
          "textFont": "",

          // Where screenshots go. "": the current folder. Command line: --screenshots <folder>.
          "screenshotsFolder": "",

          // Settings of the SDL2 host only.
          "sdl": {
            // The SDL2 library (SDL2.dll) to load. "": the one next to the program, else the system's.
            // Command line: --sdl <file>.
            "library": ""
          },

          // The developer console (a host feature, not part of the game): F11 opens and closes it (the original never reads F11).
          "console": {
            // Command line: --no-console.
            "enabled": true,
            // A TrueType font file (.ttf), e.g. "C:/Windows/Fonts/consola.ttf". "": the bundled DejaVu Sans Mono.
            "font": "",
            // Text height in pixels at the 720x400 window (it grows with the window); 6 to 64.
            "fontSize": 14
          }
        }
        """;
}
