using Darklands.Audio;
using Darklands.Audio.Mt32;
using Darklands.Content;
using Darklands.Launcher;
using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// The launcher (Darklands.Launcher) in the SDL window: what it is given from this program (the game-folder checks, the ROM
/// status, the release notes, the browser) and the saving of what the player changed. Any <see cref="ILauncher"/> fits here.
/// </summary>
internal static class LauncherHost
{
    /// <summary>The launcher's window sizes, largest first: the first that fits the screen is taken.</summary>
    private static readonly (int Width, int Height)[] Sizes = [(1080, 600), (900, 500), (720, 400)];

    /// <summary>Opens the window at the launcher's size.</summary>
    public static SdlHost Open(string title)
    {
        var (width, height) = SizeFor(SdlHost.MainScreen());
        var host = new SdlHost(title, width, height);
        host.Clear();
        return host;
    }

    /// <summary>Gives the window the launcher's size again (after the game's).</summary>
    public static void Fit(SdlHost host)
    {
        var (width, height) = SizeFor(host.UsableScreen());
        host.Resize(width, height, fullscreen: false);
    }

    /// <summary>The largest of <see cref="Sizes"/> that fits the screen with room to spare.</summary>
    private static (int Width, int Height) SizeFor((int Width, int Height)? screen) =>
        Sizes.FirstOrDefault(s => screen is not { } u || (s.Width * 1.1 <= u.Width && s.Height * 1.15 <= u.Height), Sizes[^1]);

    /// <summary>
    /// Shows the launcher; saves the changes into <paramref name="settingsPath"/> (its template is written when the file is
    /// missing). Returns the settings to play with, or null when the player left; <paramref name="saved"/>: changes were saved.
    /// </summary>
    public static GameSettings? Run(SdlHost host, LauncherReason reason, string? problem, GameSettings settings,
        CommandLineOptions cli, string? settingsPath, string version, out bool saved)
    {
        using var soundTest = new SoundTest(cli.GameCommandLine ?? settings.CommandLine ?? [], SdlLauncherSurface.Wake);
        var (given, fixedKeys) = GivenGameFolder(cli, Environment.GetEnvironmentVariable,
            folder => GameFolderSearch.GameFolderIn(folder, GameFiles.IsGameFolder) is not null);
        var context = new LauncherContext(
            settings,
            fixedKeys,
            version,
            reason,
            problem,
            folder => GameFolderSearch.GameFolderIn(folder, GameFiles.IsGameFolder),
            FoundGameFolders,
            DescribeRoms,
            soundTest,
            ReleaseNotes(),
            url => SdlNative.SDL_OpenURL(url),
            ReleasesPage + "/tag/v" + version,
            PackFolders,
            DescribePack,
            PackGuide,
            given);
        LauncherResult result;
        using (var surface = new SdlLauncherSurface(host))
        {
            result = new Launcher.Launcher().Run(context, surface);
        }
        Save(settingsPath, result);
        saved = result.Changes.Count > 0;
        return result.Choice == LauncherChoice.Play ? result.Settings : null;
    }

    /// <summary>
    /// The game folder the command line (else DARKLANDS_DIR) names, and the settings the launcher cannot change. A named folder
    /// that holds no game is not fixed: the player chooses another on the launcher (else the launcher could never start the game).
    /// </summary>
    internal static (string? Given, IReadOnlyDictionary<string, string> FixedKeys) GivenGameFolder(CommandLineOptions cli,
        Func<string, string?> environment, Func<string, bool> holdsGame)
    {
        var fixedKeys = new Dictionary<string, string>(cli.Overrides(environment), StringComparer.Ordinal);
        var given = !string.IsNullOrWhiteSpace(cli.GameFolder) ? cli.GameFolder
            : environment("DARKLANDS_DIR") is { } dir && !string.IsNullOrWhiteSpace(dir) ? dir : null;
        if (given is not null && !holdsGame(given))
        {
            fixedKeys.Remove("gameFolder");
        }
        return (given, fixedKeys);
    }

    private static void Save(string? settingsPath, LauncherResult result)
    {
        if (result.Changes.Count == 0)
        {
            return;
        }
        if (settingsPath is null)
        {
            Console.Error.WriteLine("Settings: no settings file could be written; the changes last until the program ends.");
            return;
        }
        if (!File.Exists(settingsPath) && !SettingsFile.TryWriteTemplate(settingsPath, out var written))
        {
            Console.Error.WriteLine("Settings: " + written);
            return;
        }
        var saved = SettingsFileEdits.TrySet(settingsPath, result.Changes, out var message);
        (saved ? Console.Out : Console.Error).WriteLine("Settings: " + message);
    }

    /// <summary>The game folders of the usual places (the program's folder, GOG, Steam, installed programs ...), each once.</summary>
    private static IReadOnlyList<(string Folder, string Source)> FoundGameFolders() =>
        [.. GameFolderSearch.Candidates(AppContext.BaseDirectory)
            .Select(c => (Folder: GameFolderSearch.GameFolderIn(c.Folder, GameFiles.IsGameFolder), c.Source))
            .Where(c => c.Folder is not null)
            .Select(c => (c.Folder!, c.Source))
            .DistinctBy(c => Path.GetFullPath(c.Item1).TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase)];

    private static RomStatus DescribeRoms(string? chosen)
    {
        var folder = SoundSelection.RomFolder(chosen);
        var mt32 = Mt32Roms.Find(folder, RolandModule.Mt32, out _) is not null;
        var cm32l = Mt32Roms.Find(folder, RolandModule.Cm32l, out _) is not null;
        return new RomStatus(folder, mt32, cm32l);
    }

    /// <summary>The guide for pack makers on the web (a copy is docs/PACKS.md beside the program).</summary>
    private const string PackGuide = "https://github.com/Arkana-Mechanika-Studios/darklands-restoration/blob/master/docs/PACKS.md";

    /// <summary>The subfolders of the packs folders: next to the program (the release's example pack is there) and next to the game.</summary>
    internal static IReadOnlyList<string> PackFolders(string? gameFolder)
    {
        var roots = new List<string> { Path.Combine(AppContext.BaseDirectory, "packs") };
        if (gameFolder is not null)
        {
            roots.Add(Path.Combine(gameFolder, "packs"));
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gameFolder)) is { } above)
            {
                roots.Add(Path.Combine(above, "packs"));
            }
        }
        var folders = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(root))
                {
                    folders.AddRange(Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return [.. folders.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A folder as the game would read it: its pictures (an HD pack) and sounds (an audio pack, with audio.json's problems).</summary>
    internal static PackStatus DescribePack(string folder)
    {
        var pictures = Darklands.Rendering.HdPack.Load(folder);
        var sounds = Darklands.Audio.Packs.AudioPack.Load(folder);
        var problem = sounds.Problems.Concat(pictures.Problems).FirstOrDefault(p => p.Contains(".json", StringComparison.Ordinal))
            ?? sounds.Problems.FirstOrDefault();
        return new PackStatus(pictures.Count, sounds.Count, problem);
    }

    /// <summary>The project's releases on GitHub (each version's page is /tag/v&lt;version&gt;).</summary>
    private const string ReleasesPage = "https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases";

    /// <summary>RELEASE_NOTES.md next to the program (the release's notes, copied there by the build), or nothing.</summary>
    private static string ReleaseNotes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RELEASE_NOTES.md");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
