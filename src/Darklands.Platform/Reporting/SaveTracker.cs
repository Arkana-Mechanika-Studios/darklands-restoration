using System.Globalization;

namespace Darklands.Platform.Reporting;

/// <summary>The saved game last loaded or saved: its folder, its name (DKSAVEn.SAV), whether it was loaded, and when.</summary>
public sealed record SaveUse(string Folder, string Name, bool Loaded, DateTime When);

/// <summary>
/// Remembers which saved game the player last loaded or saved, and copies a game being loaded (the state before whatever goes
/// wrong afterwards) into a helper folder, so a report can carry both. The note is kept in a file, so a report made after a
/// restart (from the launcher) still has it. Never throws: a report helper must not hurt the game.
/// </summary>
public sealed class SaveTracker
{
    private const string NoteFile = "last-save.txt";

    private const string CopyFolder = "at-load";

    private readonly string _folder;

    public SaveTracker(string sessionFolder)
    {
        _folder = sessionFolder;
        Last = ReadNote();
    }

    public SaveUse? Last { get; private set; }

    /// <summary>The folder with the copy taken when the last game was loaded (it may not exist).</summary>
    public string AtLoadFolder => Path.Combine(_folder, CopyFolder);

    /// <summary>When the copy in <see cref="AtLoadFolder"/> was taken, or null.</summary>
    public DateTime? AtLoadWhen { get; private set; }

    /// <summary>The player loaded (before the file is read) or saved (after it is written) <paramref name="fileName"/> in <paramref name="folder"/>.</summary>
    public void Used(string folder, string fileName, bool loading)
    {
        try
        {
            Last = new SaveUse(Path.GetFullPath(folder), fileName.ToUpperInvariant(), loading, DateTime.Now);
            Directory.CreateDirectory(_folder);
            File.WriteAllLines(Path.Combine(_folder, NoteFile),
                [Last.Folder, Last.Name, Last.Loaded ? "loaded" : "saved", Last.When.ToString("o", CultureInfo.InvariantCulture)]);
            if (loading)
            {
                if (Directory.Exists(AtLoadFolder))
                {
                    Directory.Delete(AtLoadFolder, recursive: true);
                }
                Directory.CreateDirectory(AtLoadFolder);
                foreach (var file in FilesOf(folder, fileName))
                {
                    File.Copy(file, Path.Combine(AtLoadFolder, Path.GetFileName(file)), overwrite: true);
                }
                AtLoadWhen = Last.When;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    /// <summary>The files that make up a saved game: its .SAV, .BSV and .MODS.JSON, whatever the case of the letters.</summary>
    public static IReadOnlyList<string> FilesOf(string folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        try
        {
            return Directory.Exists(folder)
                ? [.. Directory.EnumerateFiles(folder)
                    .Where(f =>
                    {
                        var n = Path.GetFileName(f);
                        return n.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)
                            && (n.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".bsv", StringComparison.OrdinalIgnoreCase)
                                || n.EndsWith(".mods.json", StringComparison.OrdinalIgnoreCase));
                    })
                    .Order(StringComparer.OrdinalIgnoreCase)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private SaveUse? ReadNote()
    {
        try
        {
            var path = Path.Combine(_folder, NoteFile);
            if (File.Exists(path) && File.ReadAllLines(path) is { Length: >= 4 } lines
                && DateTime.TryParse(lines[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
            {
                if (Directory.Exists(AtLoadFolder))
                {
                    AtLoadWhen = Directory.GetLastWriteTime(AtLoadFolder);
                }
                return new SaveUse(lines[0], lines[1], lines[2] == "loaded", when);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}
