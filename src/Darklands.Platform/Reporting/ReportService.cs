using System.Runtime.InteropServices;

namespace Darklands.Platform.Reporting;

/// <summary>
/// What a host needs to report a problem: the folders, the version, the log and the tracker of saved games, and what is known
/// of this run (game folder, settings, a picture of the last frame). Makes the report package and the prefilled issue address,
/// and shows both to the player. Nothing is sent: the player attaches the zip to the issue. Never throws from the methods a
/// crash handler calls.
/// </summary>
public sealed class ReportService(ReportLocations locations, string version, string commit, SessionLog? log, SaveTracker saves, Scrubber? scrubber = null)
{
    private readonly Scrubber _scrubber = scrubber ?? Scrubber.ForCurrentUser();

    public ReportLocations Locations => locations;

    public SessionLog? Log => log;

    public string Version => version;

    public string Commit => commit;

    public SaveTracker Saves => saves;

    public string? SettingsFile { get; set; }

    public string? GameFolder { get; set; }

    /// <summary>Folders where saved games may be (the settings', the game's, the user's fallback).</summary>
    public List<string> SavesFolders { get; } = [];

    /// <summary>Settings and packs of this run as key and value lines for report.txt.</summary>
    public Func<IReadOnlyList<(string Key, string Value)>> Facts { get; set; } = () => [];

    /// <summary>Writes a PNG of the last frame and gives its path (null: none to give).</summary>
    public Func<string?>? Screenshot { get; set; }

    /// <summary>The version with its commit, as the issue form wants it.</summary>
    public string VersionText => version + " (commit " + commit + ")";

    public static string SystemText => RuntimeInformation.OSDescription + ", " + RuntimeInformation.FrameworkDescription + ", " + RuntimeInformation.ProcessArchitecture;

    public ReportResult Build(bool crash, string? source, string? playerText, Exception? exception, int logCount = 2)
    {
        string? picture = null;
        try
        {
            picture = Screenshot?.Invoke();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log?.WriteLine("Report: no screenshot (" + e.GetType().Name + ": " + e.Message + ")", error: true);
        }
        IReadOnlyList<(string, string)> facts;
        try
        {
            facts = Facts();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            facts = [("Settings", "unavailable: " + e.Message)];
        }
        var result = ReportBuilder.Build(new ReportInput(locations.Reports, locations.Logs, version, commit, crash, source, playerText, exception, facts,
            SettingsFile, GameFolder, SavesFolders, saves, picture, _scrubber, logCount));
        log?.WriteLine("Report: made " + result.Zip + " (" + result.Entries.Count + " files" + (result.Notes.Count > 0 ? ", " + result.Notes.Count + " left out" : "") + ")");
        return result;
    }

    /// <summary>The issue form's address, prefilled from the report (version, commit, system, trigger, the first lines of the exception).</summary>
    public string IssueUrl(bool crash, string? source, string? playerText, Exception? exception)
    {
        var happened = crash ? "The program crashed." : "Reported by the player" + (source is null ? "." : " (" + source + ").");
        if (exception is not null)
        {
            happened += "\n\n" + _scrubber.Scrub(IssueForm.FirstLines(exception.ToString()));
        }
        if (!string.IsNullOrWhiteSpace(playerText))
        {
            happened += "\n\n" + playerText.Trim();
        }
        var title = crash ? "Crash: " + (exception?.GetType().Name ?? "unhandled error") : null;
        return IssueForm.Url(crash ? IssueForm.KindCrash : IssueForm.KindOther, VersionText, SystemText, happened, title);
    }

    /// <summary>Opens the folder with the zip and the issue form in the browser; a failure only says where things are.</summary>
    public void Present(ReportResult result, string url, Action<string> say, bool folder = true, bool form = true)
    {
        if (folder)
        {
            ReportOpener.ShowFile(result.Zip, say);
        }
        if (form)
        {
            ReportOpener.OpenUrl(url, say);
        }
    }
}
