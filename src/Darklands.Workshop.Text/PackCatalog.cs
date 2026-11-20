using Darklands.Localization;

namespace Darklands.Workshop.Text;

/// <summary>A translation pack found on this computer: where it is, what it is and how far it is.</summary>
/// <param name="Folder">Its folder (holds pack.json).</param>
/// <param name="Language">Its language tag.</param>
/// <param name="Name">Its name (pack.json's, else the folder's).</param>
/// <param name="Author">Its author, if pack.json names one.</param>
/// <param name="Translated">How many texts it translates (its messages, its own variables left out).</param>
/// <param name="Changed">When its newest file changed.</param>
public sealed record PackSummary(string Folder, string Language, string Name, string? Author, int Translated, DateTime Changed);

/// <summary>
/// The packs the Workshop's start page lists (W-0 polish): every folder holding a pack.json in the program's packs folder, the game
/// folder's packs folder, and the packs opened lately, each once, the newest first.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class PackCatalog
{
    /// <summary>The folders searched for packs (each of their subfolders that holds a pack.json).</summary>
    public static IReadOnlyList<string> PackFolders(string programFolder, string? gameFolder) =>
        [.. new[] { Path.Combine(programFolder, "packs"), gameFolder is null ? null : Path.Combine(gameFolder, "packs") }
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The packs in <paramref name="folders"/> (one level down) and <paramref name="recent"/>, newest first.</summary>
    public static IReadOnlyList<PackSummary> Find(IEnumerable<string> folders, IEnumerable<string> recent)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(recent);
        var candidates = new List<string>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                candidates.AddRange(Directory.GetDirectories(folder));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a folder that cannot be listed has no pack to show
            }
        }
        candidates.AddRange(recent);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<PackSummary>();
        foreach (var candidate in candidates)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (!seen.Add(full) || !File.Exists(Path.Combine(full, LanguagePack.ManifestName)) || Summary(full) is not { } summary)
            {
                continue;
            }
            found.Add(summary);
        }
        return [.. found.OrderByDescending(p => p.Changed)];
    }

    /// <summary>The summary of the pack in <paramref name="folder"/>, or null when it cannot be read.</summary>
    public static PackSummary? Summary(string folder)
    {
        if (LanguagePack.Load(folder, out _) is not { } pack)
        {
            return null;
        }
        var translated = pack.Messages.Count(m => !m.Message.Id.StartsWith(LanguagePack.VariablePrefix, StringComparison.Ordinal));
        var changed = File.GetLastWriteTime(Path.Combine(folder, LanguagePack.ManifestName));
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var at = File.GetLastWriteTime(file);
                changed = at > changed ? at : changed;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the manifest's time
        }
        return new PackSummary(folder, pack.Language, pack.Name ?? Path.GetFileName(folder), pack.Author, translated, changed);
    }
}
