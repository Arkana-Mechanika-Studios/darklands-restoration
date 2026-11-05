using System.IO.Compression;
using Darklands.Platform;
using Darklands.Platform.Reporting;

namespace Darklands.Host.Sdl.Tests;

/// <summary>A report made from a copy of the real game folder (DARKLANDS_DIR or local/paths.json; nothing is checked without it).</summary>
public sealed class RealReportTests
{
    [Fact]
    public void A_report_from_a_copy_of_the_real_game_folder_lists_what_a_tester_needs()
    {
        var real = LocalPaths.Get("game", "DARKLANDS_DIR");
        if (real is null || !File.Exists(Path.Combine(real, "DARKLAND.EXE")))
        {
            return;
        }
        var scratch = Path.Combine(Path.GetTempPath(), "darklands-real-report");
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
        var game = Path.Combine(scratch, "game");
        Directory.CreateDirectory(Path.Combine(game, "SAVES"));
        // the files a report reads (the rest of the game is not needed)
        foreach (var name in new[] { "DARKLAND.EXE", "CACHE.TMP", "CHARACTR.TMP" }.Where(n => File.Exists(Path.Combine(real, n))))
        {
            File.Copy(Path.Combine(real, name), Path.Combine(game, name));
        }
        var savesAt = Directory.EnumerateDirectories(real).FirstOrDefault(d => Path.GetFileName(d).Equals("SAVES", StringComparison.OrdinalIgnoreCase));
        foreach (var file in savesAt is null ? [] : Directory.EnumerateFiles(savesAt).Where(f => f.EndsWith(".SAV", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".BSV", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(file, Path.Combine(game, "SAVES", Path.GetFileName(file)));
        }
        var settingsFile = Path.Combine(scratch, "program", "darklands.settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        File.WriteAllText(settingsFile, "{ \"gameFolder\": " + System.Text.Json.JsonSerializer.Serialize(game) + " }");

        var locations = ReportLocations.Choose(settingsFile, canWrite: _ => true);
        using var log = SessionLog.Start(locations.Logs, ["Darklands Restoration Project (real check)"]);
        var tracker = new SaveTracker(locations.Session);
        var saves = Path.Combine(game, "SAVES");
        var first = Directory.EnumerateFiles(saves, "*.SAV").Order().FirstOrDefault();
        if (first is not null)
        {
            tracker.Used(saves, Path.GetFileName(first), loading: true);
        }
        var settings = ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, SettingsFile.Load(settingsFile).Settings);
        var service = new ReportService(locations, "1.0.0-rc1", ReportHost.Commit(), log, tracker)
        {
            SettingsFile = settingsFile,
            GameFolder = game,
            Facts = () => ReportHost.Facts(settings, game),
        };
        service.SavesFolders.AddRange(ReportHost.SavesFolders(settings, game));

        var result = service.Build(crash: false, "real check", "listing the entries", null);

        using var archive = ZipFile.OpenRead(result.Zip);
        var entries = archive.Entries.Select(e => $"{e.FullName} ({e.Length} bytes)").ToList();
        File.WriteAllLines(Path.Combine(scratch, "entries.txt"), entries);
        Assert.Contains(entries, e => e.StartsWith("report.txt", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.StartsWith("game/CACHE.TMP", StringComparison.Ordinal) || !File.Exists(Path.Combine(game, "CACHE.TMP")));
        Assert.Contains("English version 483.07", result.ReportText);
        Assert.Contains("SHA-256 ", result.ReportText);
    }
}
