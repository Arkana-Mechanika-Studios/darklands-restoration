using Darklands.Platform.Reporting;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's "Report a problem…" (docs/REPORTING.md, docs/LOCALISATION.md): the game's report package, made beside the program
/// (its logs, the game folder's saves), with the Workshop's version and the open pack's (its language, the Workshop that last
/// wrote it); its folder and the issue form are opened. Nothing is sent: the translator attaches the zip.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal static class WorkshopReport
{
    /// <summary>What report.txt says of this Workshop and its pack.</summary>
    public static IReadOnlyList<(string Key, string Value)> Facts(string? game, TextProject? project) =>
    [
        ("Program", WorkshopVersion.Label + (WorkshopVersion.IsBeta ? " (beta)" : "")),
        ("Game folder", game ?? "not found"),
        ("Pack", project?.Folder ?? "none open"),
        ("Pack language", project?.Language ?? "none"),
        ("Pack written by", project is null ? "none" : Darklands.Localization.LanguagePack.WorkshopOf(project.Folder) is { } w ? "Darklands Workshop " + w : "not said"),
        ("Pack texts translated", project is null ? "none" : $"{project.Progress().Translated} of {project.Progress().All}"),
    ];

    /// <summary>Makes the report; returns the zip's path and the issue form's address (null when it could not be made) and what to say.</summary>
    public static (ReportResult? Result, string? Url, string Message) Make(string? game, TextProject? project)
    {
        try
        {
            var locations = ReportLocations.Choose(null, AppContext.BaseDirectory);
            var service = new ReportService(locations, "Workshop " + WorkshopVersion.Current, "unknown", null, new SaveTracker(locations.Session))
            {
                GameFolder = game,
                Facts = () => Facts(game, project),
            };
            if (game is not null)
            {
                service.SavesFolders.Add(Darklands.Presentation.DarklandsGame.DefaultSaveDirectory(game));
            }
            var result = service.Build(crash: false, "workshop", null, null);
            var url = service.IssueUrl(crash: false, "workshop", null, null);
            return (result, url, $"Report saved: {result.Zip}. Attach it to the report form; nothing was sent.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (null, null, "The report could not be made: " + e.Message);
        }
    }

    /// <summary>Opens the report's folder and the issue form (the translator's own browser).</summary>
    public static void Present(ReportResult result, string url, Action<string> say)
    {
        var locations = ReportLocations.Choose(null, AppContext.BaseDirectory);
        new ReportService(locations, "Workshop " + WorkshopVersion.Current, "unknown", null, new SaveTracker(locations.Session)).Present(result, url, say);
    }
}
