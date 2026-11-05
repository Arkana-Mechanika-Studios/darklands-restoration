using System.Reflection;
using System.Runtime.InteropServices;
using Darklands.Content.Formats.Exe;
using Darklands.Platform;
using Darklands.Platform.Reporting;

namespace Darklands.Host.Sdl;

/// <summary>
/// The problem report in this program (docs/REPORTING.md): the session log, the commit the build was made from, what a report
/// says of the settings, and the manual report (the console's <c>report</c>, Ctrl-F12, the launcher's link). Nothing is sent.
/// </summary>
internal static class ReportHost
{
    /// <summary>The commit this build was made from (AssemblyMetadata "Commit", set by the build), or "unknown".</summary>
    public static string Commit(Assembly? assembly = null) =>
        (assembly ?? typeof(ReportHost).Assembly).GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Commit")?.Value is { Length: > 0 } c ? c : "unknown";

    /// <summary>
    /// Starts the session log beside the settings file (else the per-user folder) and makes the console's output and error go to it
    /// too; the service that makes reports. <paramref name="logIt"/> false: no log file (a tool run that should not push the
    /// logs of real sessions out).
    /// </summary>
    public static ReportService Start(string? settingsPath, string version, IReadOnlyList<string> args, bool logIt = true)
    {
        var locations = ReportLocations.Choose(settingsPath);
        var commit = Commit();
        SessionLog? log = null;
        if (logIt)
        {
            log = SessionLog.Start(locations.Logs,
            [
                "Darklands Restoration Project " + version + " (commit " + commit + ")",
                "Operating system: " + RuntimeInformation.OSDescription + ", " + RuntimeInformation.OSArchitecture,
                ".NET: " + RuntimeInformation.FrameworkDescription + ", process " + RuntimeInformation.ProcessArchitecture,
                "Settings file: " + (settingsPath ?? "none"),
                "Command line: " + (args.Count == 0 ? "(none)" : string.Join(' ', args)),
            ]);
            log?.CaptureConsole();
        }
        var service = new ReportService(locations, version, commit, log, new SaveTracker(locations.Session)) { SettingsFile = settingsPath };
        return service;
    }

    /// <summary>The settings and packs of a run as report lines.</summary>
    public static IReadOnlyList<(string Key, string Value)> Facts(ResolvedSettings s, string? gameFolder)
    {
        var window = s.Window is { } w ? $"{w.Width}x{w.Height}" : "default";
        return
        [
            ("Window", window + (s.Fullscreen ? ", fullscreen" : "")),
            ("Clean drawing", s.CleanDrawing ? "on" : "off"),
            ("Sound card", s.NoAudio ? "no audio" : s.SoundChoice ?? "config"),
            ("MT-32 ROM folder", s.Mt32Roms ?? "default"),
            ("Audio pack", s.AudioPack ?? "none"),
            ("SoundFont", s.SoundFont ?? "default"),
            ("HD pack", s.HdPack ?? "none"),
            ("Mods", s.Mods.Count == 0 ? "none (Classic)" : string.Join(", ", s.Mods)),
            ("Game command line", s.CommandLine.Count == 0 ? "none" : string.Join(' ', s.CommandLine)),
            ("Saves folder setting", s.SavesFolder ?? "default"),
            ("Intro / banner", (s.PlayIntro ? "intro" : "no intro") + " / " + (s.ShowBanner ? "banner" : "no banner")),
            ("Game program", ExeText(gameFolder)),
        ];
    }

    /// <summary>The saves folders worth looking in: the settings', the game folder's, the per-user one.</summary>
    public static IEnumerable<string> SavesFolders(ResolvedSettings? s, string? gameFolder)
    {
        if (s?.SavesFolder is { } given)
        {
            yield return given;
        }
        if (gameFolder is not null)
        {
            yield return Darklands.Presentation.DarklandsGame.DefaultSaveDirectory(gameFolder);
        }
        yield return SaveLocation.UserFolder();
    }

    private static string ExeText(string? gameFolder)
    {
        try
        {
            var path = gameFolder is null ? null : Path.Combine(gameFolder, "DARKLAND.EXE");
            if (path is null || !File.Exists(path))
            {
                return "not found";
            }
            return ExeVersion.Problem(File.ReadAllBytes(path)) ?? "English version 483.07 (the one supported)";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "unreadable (" + e.Message + ")";
        }
    }

    /// <summary>
    /// A report the player asked for: makes the zip, opens the folder with it and the issue form, and returns the lines to tell
    /// the player. Never throws.
    /// </summary>
    public static IReadOnlyList<string> Manual(ReportService service, string source, string? text, int logCount = 2)
    {
        try
        {
            var result = service.Build(crash: false, source, text, exception: null, logCount);
            var url = service.IssueUrl(crash: false, source, text, exception: null);
            var notes = new List<string>();
            service.Present(result, url, notes.Add);
            var lines = new List<string>
            {
                "Report saved: " + result.Zip,
                "It holds " + result.Entries.Count + " files. Nothing was sent: attach the zip to the issue form (it is open in your browser).",
            };
            lines.AddRange(result.Notes.Select(n => "Left out: " + n));
            lines.AddRange(notes);
            return lines;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return ["Report failed: " + e.Message];
        }
    }
}
