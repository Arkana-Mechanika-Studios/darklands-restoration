using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Darklands.Platform;

/// <summary>
/// Finds the player's copy of Darklands when nothing names it: the program's own folder and the one above it, GOG's and
/// Steam's registrations, Windows' list of installed programs, then the usual folders. GOG and Steam keep the game in a
/// DARKLAND subfolder of the install, so a folder counts when it, or its DARKLAND subfolder, is the game folder. No disk is
/// scanned.
/// </summary>
public static partial class GameFolderSearch
{
    /// <summary>GOG's product id of Darklands.</summary>
    public const string GogId = "1207658972";

    /// <summary>Steam's app id of Darklands.</summary>
    public const string SteamAppId = "327930";

    /// <summary>The folder itself or its DARKLAND subfolder when either is the game folder (by <paramref name="isGameFolder"/>); else null.</summary>
    public static string? GameFolderIn(string? folder, Func<string, bool> isGameFolder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }
        if (isGameFolder(folder))
        {
            return folder;
        }
        var sub = Path.Combine(folder, "DARKLAND");
        if (isGameFolder(sub))
        {
            return sub;
        }
        // a case-sensitive file system (Linux): the subfolder may be spelled darkland
        if (Directory.Exists(folder))
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                if (string.Equals(Path.GetFileName(dir), "DARKLAND", StringComparison.OrdinalIgnoreCase) && isGameFolder(dir))
                {
                    return dir;
                }
            }
        }
        return null;
    }

    /// <summary>The first candidate that holds the game (itself or its DARKLAND subfolder), with where it came from; null when none does.</summary>
    public static (string Folder, string Source)? Find(IEnumerable<(string Folder, string Source)> candidates, Func<string, bool> isGameFolder)
    {
        foreach (var (folder, source) in candidates)
        {
            try
            {
                if (GameFolderIn(folder, isGameFolder) is { } found)
                {
                    return (found, source);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // an unreadable or malformed candidate is skipped
            }
        }
        return null;
    }

    /// <summary>The places to look, in order (lazily: the registry is read only when the folders before it fail).</summary>
    public static IEnumerable<(string Folder, string Source)> Candidates(string programFolder)
    {
        yield return (programFolder, "the program's folder");
        if (Directory.GetParent(Path.TrimEndingDirectorySeparator(programFolder)) is { } parent)
        {
            yield return (parent.FullName, "the folder above the program");
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var gog in GogFolders())
            {
                yield return (gog, "GOG's registration");
            }
            foreach (var steam in SteamFolders())
            {
                yield return (steam, "Steam's library");
            }
            foreach (var installed in InstalledProgramFolders())
            {
                yield return (installed, "Windows' installed programs");
            }
        }
        if (OperatingSystem.IsLinux())
        {
            foreach (var candidate in LinuxCandidates(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable))
            {
                yield return candidate;
            }
            yield break;
        }
        foreach (var usual in UsualFolders())
        {
            yield return (usual, "a usual folder");
        }
    }

    private static IEnumerable<string> GogFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games\" + GogId, @"SOFTWARE\GOG.com\Games\" + GogId })
        {
            if (Value(Registry.LocalMachine, key, "path") is { } path)
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> SteamFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }
        var steam = Value(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
            ?? Value(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (steam is null)
        {
            yield break;
        }
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            libraries.AddRange(SteamLibraries(File.ReadAllText(vdf)));
        }
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(library, "steamapps", "common", "Darklands");
        }
    }

    /// <summary>The library folders listed in Steam's libraryfolders.vdf ("path" entries, backslashes doubled).</summary>
    public static IEnumerable<string> SteamLibraries(string vdf) =>
        SteamPath().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex SteamPath();

    private static IEnumerable<string> InstalledProgramFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }
        var places = new (RegistryKey Root, string Key)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (root, key) in places)
        {
            using var uninstall = root.OpenSubKey(key);
            if (uninstall is null)
            {
                continue;
            }
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var program = uninstall.OpenSubKey(name);
                if (program?.GetValue("DisplayName") is string display && display.Contains("Darklands", StringComparison.OrdinalIgnoreCase)
                    && program.GetValue("InstallLocation") is string location && location.Length > 0)
                {
                    yield return location;
                }
            }
        }
    }

    private static IEnumerable<string> UsualFolders()
    {
        yield return @"C:\GOG Games\Darklands";
        yield return @"C:\Program Files (x86)\GOG Galaxy\Games\Darklands";
        yield return @"C:\Program Files (x86)\Steam\steamapps\common\Darklands";
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }
        foreach (var drive in drives.Where(d => d.DriveType == DriveType.Fixed))
        {
            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "DARKLAND");
            yield return Path.Combine(root, "Darklands");
            yield return Path.Combine(root, "Games", "Darklands");
            yield return Path.Combine(root, "GOG Games", "Darklands");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? Value(RegistryKey root, string key, string name)
    {
        try
        {
            using var k = root.OpenSubKey(key);
            return k?.GetValue(name) as string is { Length: > 0 } value ? value : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
