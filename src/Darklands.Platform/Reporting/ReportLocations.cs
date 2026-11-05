namespace Darklands.Platform.Reporting;

/// <summary>
/// Where the session logs, the report packages and the report helper files go: next to the settings file, or in
/// %APPDATA%\DarklandsRestoration when that folder cannot be written (a program under Program Files), as the saves fall back.
/// New code (host configuration); docs/REPORTING.md describes it.
/// </summary>
public sealed record ReportLocations(string Root, string Logs, string Reports, string Session)
{
    /// <summary>
    /// The folders for the settings file at <paramref name="settingsPath"/> (null: none found, the program's folder is taken).
    /// <paramref name="canWrite"/> tells whether a folder can be written (default: <see cref="SaveLocation.CanWrite"/>).
    /// </summary>
    public static ReportLocations Choose(string? settingsPath, string? programFolder = null, string? appData = null, Func<string, bool>? canWrite = null)
    {
        canWrite ??= SaveLocation.CanWrite;
        var beside = settingsPath is null
            ? programFolder ?? AppContext.BaseDirectory
            : Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? programFolder ?? AppContext.BaseDirectory;
        var user = Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarklandsRestoration");
        var root = canWrite(Path.Combine(beside, "logs")) ? beside : user;
        return new ReportLocations(root, Path.Combine(root, "logs"), Path.Combine(root, "reports"), Path.Combine(root, "logs", "session"));
    }
}
