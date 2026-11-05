using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Darklands.Platform.Reporting;

/// <summary>What a report is made from.</summary>
/// <param name="ReportsFolder">Where the zip goes.</param>
/// <param name="LogsFolder">The session logs.</param>
/// <param name="Crash">True: a crash; false: the player asked.</param>
/// <param name="Source">How the player asked (console, launcher, key); null for a crash.</param>
/// <param name="Facts">Settings and packs as key and value lines, for report.txt (screen, sound, packs, mods ...).</param>
/// <param name="SavesFolders">Folders searched for saved games (the settings' and the game's, the user's fallback).</param>
/// <param name="ScreenshotFile">A PNG of the last frame, or null.</param>
/// <param name="LogCount">How many of the newest logs go in (the current and the previous run).</param>
public sealed record ReportInput(
    string ReportsFolder,
    string LogsFolder,
    string Version,
    string Commit,
    bool Crash,
    string? Source,
    string? PlayerText,
    Exception? Exception,
    IReadOnlyList<(string Key, string Value)> Facts,
    string? SettingsFile,
    string? GameFolder,
    IReadOnlyList<string> SavesFolders,
    SaveTracker? Saves,
    string? ScreenshotFile,
    Scrubber Scrubber,
    int LogCount = 2,
    DateTime? Now = null);

/// <summary>The finished report: the zip, what is in it, what was left out, and the text of report.txt (scrubbed).</summary>
public sealed record ReportResult(string Zip, IReadOnlyList<string> Entries, IReadOnlyList<string> Notes, string ReportText);

/// <summary>
/// Makes the report package <c>reports/darklands-report-yyyyMMdd-HHmmss.zip</c>: report.txt, the newest logs, the settings file,
/// the last frame, the saved game last used (as it is, and as it was when loaded), the newest save, CACHE.TMP and CHARACTR.TMP.
/// Text files are scrubbed of the player's name; saves and the picture are copied as they are. Nothing is sent anywhere. The
/// newest <see cref="Keep"/> reports are kept. New code (host feature); docs/REPORTING.md describes it.
/// </summary>
public static class ReportBuilder
{
    public const int Keep = 10;

    /// <summary>A file larger than this is left out (and said so).</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>The files together may not be larger than this; the rest is left out.</summary>
    public const long MaxTotalBytes = 80L * 1024 * 1024;

    public static ReportResult Build(ReportInput input)
    {
        var now = input.Now ?? DateTime.Now;
        Directory.CreateDirectory(input.ReportsFolder);
        var entries = new List<string>();
        var notes = new List<string>();
        var total = 0L;
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var zipPath = Path.Combine(input.ReportsFolder, "darklands-report-" + stamp + ".zip");
        for (var n = 2; File.Exists(zipPath); n++)
        {
            zipPath = Path.Combine(input.ReportsFolder, "darklands-report-" + stamp + "-" + n + ".zip");
        }
        var temp = zipPath + ".part";
        var pending = new List<(string Entry, string Path, bool Text)>();

        void Add(string entry, string? path, bool text)
        {
            if (path is null || entries.Contains(entry, StringComparer.OrdinalIgnoreCase) || pending.Any(p => string.Equals(p.Entry, entry, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            try
            {
                var size = new FileInfo(path).Length;
                if (size > MaxFileBytes)
                {
                    notes.Add($"left out {entry}: {size / (1024 * 1024)} MB is over the {MaxFileBytes / (1024 * 1024)} MB limit");
                }
                else if (total + size > MaxTotalBytes)
                {
                    notes.Add($"left out {entry}: the package would pass {MaxTotalBytes / (1024 * 1024)} MB");
                }
                else
                {
                    total += size;
                    pending.Add((entry, path, text));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                notes.Add($"left out {entry}: {e.Message}");
            }
        }

        // the logs: the newest ones (this run, the one before)
        foreach (var log in SessionLog.Logs(input.LogsFolder).Take(input.LogCount))
        {
            Add("logs/" + Path.GetFileName(log), log, text: true);
        }
        if (input.SettingsFile is { } settings && File.Exists(settings))
        {
            Add("settings/" + Path.GetFileName(settings), settings, text: true);
        }
        Add("screenshot.png", input.ScreenshotFile is { } shot && File.Exists(shot) ? shot : null, text: false);

        // the saved game last used (as it is now), its copy from the time it was loaded, the newest save
        var lastUse = input.Saves?.Last;
        string? lastFolder = null;
        string? lastName = null;
        if (lastUse is not null && Directory.Exists(lastUse.Folder))
        {
            lastFolder = lastUse.Folder;
            lastName = lastUse.Name;
            foreach (var file in SaveTracker.FilesOf(lastUse.Folder, lastUse.Name))
            {
                Add("saves/last-used/" + Path.GetFileName(file), file, text: false);
            }
        }
        if (input.Saves is { AtLoadFolder: var copy } && Directory.Exists(copy))
        {
            foreach (var file in Directory.EnumerateFiles(copy).Order(StringComparer.OrdinalIgnoreCase))
            {
                Add("saves/at-load/" + Path.GetFileName(file), file, text: false);
            }
        }
        var newest = NewestSave(input.SavesFolders);
        if (newest is { } n1 && !(string.Equals(n1.Name, lastName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFullPath(n1.Folder), lastFolder is null ? null : Path.GetFullPath(lastFolder), StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var file in SaveTracker.FilesOf(n1.Folder, n1.Name))
            {
                Add("saves/newest/" + Path.GetFileName(file), file, text: false);
            }
        }
        // the game's working files: next to the game or in the saves folder
        foreach (var tmp in new[] { "CACHE.TMP", "CHARACTR.TMP" })
        {
            var found = new[] { input.GameFolder }.Concat(input.SavesFolders).Where(f => f is not null)
                .Select(f => FindFile(f!, tmp)).FirstOrDefault(f => f is not null);
            Add("game/" + tmp, found, text: false);
        }

        var exe = input.GameFolder is null ? null : FindFile(input.GameFolder, "DARKLAND.EXE");
        // report.txt last, so that it can say what was left out
        var files = pending.Select(p => p.Entry).Prepend("report.txt").ToList();
        var report = input.Scrubber.Scrub(ReportText(input, now, exe, files, notes, lastUse));
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteText(zip, "report.txt", report);
                entries.Add("report.txt");
                foreach (var (entry, path, text) in pending)
                {
                    try
                    {
                        if (text)
                        {
                            WriteText(zip, entry, input.Scrubber.Scrub(ReadText(path)));
                        }
                        else
                        {
                            var item = zip.CreateEntry(entry, CompressionLevel.Optimal);
                            using var target = item.Open();
                            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            source.CopyTo(target);
                        }
                        entries.Add(entry);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        notes.Add($"left out {entry}: {e.Message}");
                    }
                }
            }
            File.Move(temp, zipPath);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        PruneReports(input.ReportsFolder, Keep);
        return new ReportResult(zipPath, entries, notes, report);
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> reports of the folder.</summary>
    public static void PruneReports(string folder, int keep)
    {
        try
        {
            foreach (var old in Directory.EnumerateFiles(folder, "darklands-report-*.zip")
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(keep))
            {
                TryDelete(old);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The newest .SAV (by its write time) in the folders, or null.</summary>
    public static (string Folder, string Name)? NewestSave(IEnumerable<string> folders)
    {
        (string Folder, string Name, DateTime Time)? best = null;
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }
                foreach (var file in Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)))
                {
                    var time = File.GetLastWriteTimeUtc(file);
                    if (best is null || time > best.Value.Time)
                    {
                        best = (folder, Path.GetFileName(file), time);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return best is { } b ? (b.Folder, b.Name) : null;
    }

    private static string ReportText(ReportInput input, DateTime now, string? exe, List<string> files, List<string> notes, SaveUse? lastUse)
    {
        var b = new StringBuilder();
        void Line(string key, string? value) => b.Append(key).Append(": ").AppendLine(value);
        b.AppendLine("Darklands Restoration Project problem report");
        Line("Made", now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        Line("Trigger", input.Crash ? "crash" : "manual" + (input.Source is null ? "" : " (" + input.Source + ")"));
        Line("Version", input.Version);
        Line("Commit", input.Commit);
        Line("Operating system", RuntimeInformation.OSDescription);
        Line(".NET", RuntimeInformation.FrameworkDescription);
        Line("CPU architecture", RuntimeInformation.ProcessArchitecture + " (system " + RuntimeInformation.OSArchitecture + ")");
        foreach (var (key, value) in input.Facts)
        {
            Line(key, value);
        }
        Line("Game folder", input.GameFolder ?? "none");
        Line("DARKLAND.EXE", exe is null ? "not found" : ExeLine(exe));
        Line("Settings file", input.SettingsFile ?? "none");
        Line("Saved game last used", lastUse is null ? "none noted"
            : $"{lastUse.Name} in {lastUse.Folder}, {(lastUse.Loaded ? "loaded" : "saved")} at {lastUse.When:yyyy-MM-dd HH:mm:ss}"
                + (input.Saves?.AtLoadWhen is { } at ? $"; the copy from load time was taken at {at:yyyy-MM-dd HH:mm:ss}" : ""));
        b.AppendLine();
        b.AppendLine("What the player wrote:");
        b.AppendLine(string.IsNullOrWhiteSpace(input.PlayerText) ? "(nothing)" : input.PlayerText.Trim());
        if (input.Exception is not null)
        {
            b.AppendLine();
            b.AppendLine("Exception:");
            b.AppendLine(input.Exception.ToString());
        }
        b.AppendLine();
        b.AppendLine("Files in this package:");
        foreach (var file in files)
        {
            b.Append("  ").AppendLine(file);
        }
        if (notes.Count > 0)
        {
            b.AppendLine();
            b.AppendLine("Left out:");
            foreach (var note in notes)
            {
                b.Append("  ").AppendLine(note);
            }
        }
        return b.ToString();
    }

    private static string ExeLine(string exe)
    {
        try
        {
            using var stream = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return $"{stream.Length} bytes, SHA-256 {hash}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "unreadable (" + e.Message + ")";
        }
    }

    private static string? FindFile(string folder, string name)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteText(ZipArchive zip, string entry, string text)
    {
        var item = zip.CreateEntry(entry, CompressionLevel.Optimal);
        using var writer = new StreamWriter(item.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
