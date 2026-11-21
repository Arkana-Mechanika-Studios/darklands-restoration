namespace Darklands.Platform;

/// <summary>
/// Where the program's packs are (docs/PACKS.md): a <c>packs</c> folder holds one subfolder per kind (<c>fonts</c>, <c>languages</c>,
/// <c>audio</c>) with the packs in them, as the release lays them out; a pack put straight into <c>packs</c> (a "loose" pack, the 1.0
/// layout) is found too. The launcher's pack rows and the Workshop's pack list read the packs from here.
/// </summary>
// Not in the original: the 1.1 release layout.
public static class PackLayout
{
    /// <summary>The name of a packs folder.</summary>
    public const string Packs = "packs";

    /// <summary>The kinds' subfolders of a packs folder, in the order they are listed.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["fonts", "languages", "audio"];

    /// <summary>The language packs' subfolder (where the Workshop makes a new pack).</summary>
    public const string Languages = "languages";

    /// <summary>The <paramref name="kind"/> subfolder of the packs folder beside <paramref name="programFolder"/>.</summary>
    public static string KindFolder(string programFolder, string kind) => Path.Combine(programFolder, Packs, kind);

    /// <summary>
    /// The packs in the packs folder <paramref name="root"/>: the packs in its kinds' subfolders, then the loose packs beside them (a
    /// kind's subfolder that is itself a pack, with a pack.json or an audio.json, counts as a loose pack). Nothing when it is absent or
    /// cannot be read.
    /// </summary>
    public static IReadOnlyList<string> PacksIn(string root)
    {
        var found = new List<string>();
        var subfolders = Subfolders(root).ToList();
        var kinds = subfolders.Where(IsKindFolder).ToList();
        foreach (var kind in Kinds)
        {
            foreach (var folder in kinds.Where(f => string.Equals(Path.GetFileName(f), kind, StringComparison.OrdinalIgnoreCase)))
            {
                found.AddRange(Subfolders(folder));
            }
        }
        return [.. found.Concat(subfolders.Except(kinds))];
    }

    private static bool IsKindFolder(string folder) =>
        Kinds.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(folder, "pack.json")) && !File.Exists(Path.Combine(folder, "audio.json"));

    private static IEnumerable<string> Subfolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The folder of the game's program (DarklandsRP) for a tool shipped in a folder of its own beside it (the Workshop, in
    /// <c>Workshop</c>): <paramref name="toolFolder"/> itself when the program is there (a developer's build, the tests), else the
    /// folder above when it is there, else <paramref name="toolFolder"/>.
    /// </summary>
    public static string GameProgramFolder(string toolFolder)
    {
        var name = OperatingSystem.IsWindows() ? "DarklandsRP.exe" : "DarklandsRP";
        if (File.Exists(Path.Combine(toolFolder, name)))
        {
            return toolFolder;
        }
        var above = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(toolFolder));
        return above is not null && File.Exists(Path.Combine(above, name)) ? above : toolFolder;
    }
}
