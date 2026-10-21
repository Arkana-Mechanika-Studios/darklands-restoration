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
    /// missing). Returns the settings to play with, or null when the player left.
    /// </summary>
    public static GameSettings? Run(SdlHost host, LauncherReason reason, string? problem, GameSettings settings,
        CommandLineOptions cli, string? settingsPath, string version)
    {
        using var soundTest = new SoundTest(cli.GameCommandLine ?? settings.CommandLine ?? [], SdlLauncherSurface.Wake);
        var context = new LauncherContext(
            settings,
            cli.Overrides(Environment.GetEnvironmentVariable),
            version,
            reason,
            problem,
            folder => GameFolderSearch.GameFolderIn(folder, GameFiles.IsGameFolder),
            FoundGameFolders,
            DescribeRoms,
            soundTest,
            ReleaseNotes(),
            url => SdlNative.SDL_OpenURL(url),
            ReleasesPage + "/tag/v" + version);
        LauncherResult result;
        using (var surface = new SdlLauncherSurface(host))
        {
            result = new Launcher.Launcher().Run(context, surface);
        }
        Save(settingsPath, result);
        return result.Choice == LauncherChoice.Play ? result.Settings : null;
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
