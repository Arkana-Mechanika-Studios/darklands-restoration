namespace Darklands.Platform;

/// <summary>Where a host keeps the saved games: what <see cref="Choose"/> decided, and whether it fell back to the per-user folder.</summary>
public sealed record SaveLocationChoice(string Folder, bool FellBack);

/// <summary>
/// The saves folder when the settings name none: the game folder's SAVES (shared with the original) while it can be written,
/// else %APPDATA%\DarklandsRestoration\SAVES (a game under Program Files: a .NET program gets no VirtualStore there).
/// New code (host configuration, not a reconstruction); docs/SETTINGS.md and docs/SAVES.md describe it.
/// </summary>
public static class SaveLocation
{
    /// <summary>The per-user saves folder, beside the per-user settings file (<see cref="SettingsFile.SearchPaths"/>).</summary>
    public static string UserFolder(string? appData = null) =>
        Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarklandsRestoration", "SAVES");

    /// <summary>
    /// The configured folder when there is one (as given, never tested); else the game's folder when <paramref name="canWrite"/>
    /// says so; else <paramref name="userFolder"/>.
    /// </summary>
    public static SaveLocationChoice Choose(string? configured, string gameSaves, string userFolder, Func<string, bool> canWrite) =>
        configured is not null ? new(configured, false)
        : canWrite(gameSaves) ? new(gameSaves, false)
        : new(userFolder, true);

    /// <summary>
    /// True when a file can be created in the folder (or, for a folder not made yet, in its nearest existing parent, where saving
    /// would create it): a probe file is created and deleted, nothing else is left behind.
    /// </summary>
    public static bool CanWrite(string folder)
    {
        var probeIn = Path.GetFullPath(folder);
        while (!Directory.Exists(probeIn))
        {
            var parent = Path.GetDirectoryName(probeIn);
            if (parent is null)
            {
                return false;
            }
            probeIn = parent;
        }
        var probe = Path.Combine(probeIn, ".darklands-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
