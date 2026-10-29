using System.Text.Json;

namespace Darklands.Platform;

/// <summary>
/// The Linux half of the search (SteamOS / Steam Deck included): Steam's libraries (native, Flatpak, the Deck's SD card),
/// GOG installs (Heroic's list of installed games, the GOG installer's and Lutris' usual folders) and Wine prefixes (the
/// default one, WINEPREFIX, Lutris, Heroic, Bottles, Steam's Proton). Only known folders are opened; no disk is scanned.
/// </summary>
public static partial class GameFolderSearch
{
    /// <summary>Where a Windows installation of Darklands lies inside a Wine prefix's drive_c.</summary>
    private static readonly string[] WineInstallFolders =
    [
        "GOG Games/Darklands",
        "Program Files (x86)/GOG Galaxy/Games/Darklands",
        "Program Files/GOG Galaxy/Games/Darklands",
        "Program Files (x86)/Steam/steamapps/common/Darklands",
        "Games/Darklands",
        "DARKLAND",
        "Darklands",
    ];

    /// <summary>
    /// The places to look on Linux, in order (lazily). <paramref name="home"/> is the user's home folder, <paramref name="environment"/>
    /// reads an environment variable (XDG_DATA_HOME, XDG_CONFIG_HOME, WINEPREFIX, USER); folders that do not exist are skipped.
    /// <paramref name="mountRoots"/> are the folders external disks appear under (/run/media, /media, /mnt).
    /// </summary>
    public static IEnumerable<(string Folder, string Source)> LinuxCandidates(string home, Func<string, string?> environment, IReadOnlyList<string>? mountRoots = null)
    {
        mountRoots ??= ["/run/media", "/media", "/mnt"];
        var data = Given(environment("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
        var config = Given(environment("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
        var flatpak = Path.Combine(home, ".var", "app");

        // Steam: the client's folders, then every library its libraryfolders.vdf lists (an SD card or a second disk)
        var steamRoots = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(data, "Steam"),
            Path.Combine(flatpak, "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(flatpak, "com.valvesoftware.Steam", "data", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
        };
        var libraries = new List<string>();
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(root);
            foreach (var vdf in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
            {
                if (TryRead(vdf) is { } text)
                {
                    libraries.AddRange(SteamLibraries(text));
                }
            }
        }
        foreach (var mounts in mountRoots)
        {
            // the Steam Deck's SD card (listed in the vdf too), and disks mounted by name
            libraries.AddRange(SubFolders(mounts).SelectMany(m => SubFolders(m).Prepend(m)));
        }
        var libraryList = libraries.Distinct().ToList();
        foreach (var library in libraryList)
        {
            yield return (Path.Combine(library, "steamapps", "common", "Darklands"), "Steam's library");
        }

        // GOG: Heroic's list of installed games names the folder exactly; then the usual places of GOG's installer, Heroic and Lutris
        foreach (var installed in new[]
        {
            Path.Combine(config, "heroic", "gog_store", "installed.json"),
            Path.Combine(flatpak, "com.heroicgameslauncher.hgl", "config", "heroic", "gog_store", "installed.json"),
        })
        {
            if (TryRead(installed) is { } text && HeroicInstallPath(text, GogId) is { } path)
            {
                yield return (path, "Heroic's installed games");
            }
        }
        foreach (var usual in new[]
        {
            Path.Combine(home, "GOG Games", "Darklands"),
            Path.Combine(home, "Games", "Darklands"),
            Path.Combine(home, "Games", "GOG", "Darklands"),
            Path.Combine(home, "Games", "Heroic", "Darklands"),
        })
        {
            yield return (usual, "a GOG / Heroic / Lutris folder");
        }

        // Wine prefixes: the named one, the default one, then the managers' folders (each child holding a drive_c is a prefix)
        var prefixes = new List<string>();
        if (Given(environment("WINEPREFIX")) is { } named)
        {
            prefixes.Add(named);
        }
        prefixes.Add(Path.Combine(home, ".wine"));
        foreach (var parent in new[]
        {
            Path.Combine(home, "Games"),
            Path.Combine(home, "Games", "Heroic", "Prefixes"),
            Path.Combine(home, "Games", "Heroic", "Prefixes", "default"),
            Path.Combine(data, "bottles", "bottles"),
            Path.Combine(flatpak, "com.usebottles.bottles", "data", "bottles", "bottles"),
        })
        {
            prefixes.AddRange(SubFolders(parent));
        }
        foreach (var library in libraryList)
        {
            // Proton keeps one prefix per Steam app, the Windows installation inside pfx
            prefixes.AddRange(SubFolders(Path.Combine(library, "steamapps", "compatdata")).Select(p => Path.Combine(p, "pfx")));
        }
        foreach (var prefix in prefixes.Distinct())
        {
            var driveC = Path.Combine(prefix, "drive_c");
            if (!Directory.Exists(driveC))
            {
                continue;
            }
            foreach (var inside in WineInstallFolders)
            {
                yield return (Path.Combine(driveC, inside.Replace('/', Path.DirectorySeparatorChar)), "a Wine prefix (" + prefix + ")");
            }
        }

        // the usual folders of a copy kept by hand, and of external disks
        foreach (var usual in new[] { Path.Combine(home, "Darklands"), Path.Combine(home, "DARKLAND"), "/opt/Darklands", "/opt/darklands" })
        {
            yield return (usual, "a usual folder");
        }
        foreach (var mounts in mountRoots)
        {
            foreach (var volume in SubFolders(mounts).SelectMany(m => SubFolders(m).Prepend(m)))
            {
                foreach (var inside in new[] { "Darklands", "DARKLAND", "Games/Darklands", "GOG Games/Darklands" })
                {
                    yield return (Path.Combine(volume, inside.Replace('/', Path.DirectorySeparatorChar)), "a usual folder");
                }
            }
        }
    }

    /// <summary>The install path of the GOG game <paramref name="gogId"/> in Heroic's gog_store/installed.json; null when not listed or unreadable.</summary>
    public static string? HeroicInstallPath(string json, string gogId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("installed", out var installed)
                ? installed
                : doc.RootElement;
            if (list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (var game in list.EnumerateArray())
            {
                if (game.ValueKind == JsonValueKind.Object
                    && game.TryGetProperty("appName", out var name) && name.GetString() == gogId
                    && game.TryGetProperty("install_path", out var path) && path.GetString() is { Length: > 0 } found)
                {
                    return found;
                }
            }
        }
        catch (JsonException)
        {
            // not Heroic's file after all
        }
        return null;
    }

    private static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? TryRead(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SubFolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
