namespace Darklands.Content;

/// <summary>A mod folder's manifest as read from disk: its text, or why it could not be read (the parsing is Simulation's ModFolder).</summary>
/// <param name="Folder">The mod's folder, full path.</param>
/// <param name="Id">The mod's id: its folder's name.</param>
/// <param name="Json">The text of its mod.json, null when it could not be read.</param>
/// <param name="Problem">Why it could not be read, else null.</param>
public sealed record ModManifest(string Folder, string Id, string? Json, string? Problem);

/// <summary>
/// The file side of mods (docs/MODS.md): finding a mod's folder and reading its mod.json. The rules never touch the disk;
/// they get the text from here.
/// </summary>
public static class ModFolders
{
    /// <summary>The file that makes a folder a mod.</summary>
    public const string ManifestName = "mod.json";

    /// <summary>True when a mod.json is in the folder.</summary>
    public static bool IsMod(string folder) => File.Exists(Path.Combine(folder, ManifestName));

    /// <summary>Reads a mod folder's mod.json. A missing folder or file, or one that cannot be read, gives the problem instead of the text.</summary>
    public static ModManifest Read(string folder)
    {
        var full = Path.GetFullPath(folder);
        var id = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var manifest = Path.Combine(full, ManifestName);
        if (!File.Exists(manifest))
        {
            return new ModManifest(full, id, null, $"{full}: no {ManifestName} (not a mod folder)");
        }
        try
        {
            return new ModManifest(full, id, File.ReadAllText(manifest), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ModManifest(full, id, null, $"{manifest}: cannot be read: {e.Message}");
        }
    }

    /// <summary>
    /// The folder of a mod named on the command line or in the settings: an existing folder as given (relative to
    /// <paramref name="baseFolder"/> when not rooted), else a folder of that name in a <c>mods</c> folder next to the
    /// program, in the game folder or next to the game folder. Null when none holds a mod.json.
    /// </summary>
    public static string? Find(string nameOrFolder, string? gameFolder, string? baseFolder = null)
    {
        var given = Path.IsPathRooted(nameOrFolder) || baseFolder is null ? nameOrFolder : Path.Combine(baseFolder, nameOrFolder);
        if (IsMod(given))
        {
            return Path.GetFullPath(given);
        }
        foreach (var root in SearchFolders(gameFolder))
        {
            var candidate = Path.Combine(root, nameOrFolder);
            if (IsMod(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    /// <summary>The <c>mods</c> folders looked in, in order: next to the program, in the game folder, next to the game folder.</summary>
    public static IReadOnlyList<string> SearchFolders(string? gameFolder)
    {
        var roots = new List<string> { Path.Combine(AppContext.BaseDirectory, "mods") };
        if (!string.IsNullOrEmpty(gameFolder))
        {
            roots.Add(Path.Combine(gameFolder, "mods"));
            if (Path.GetDirectoryName(Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { } above)
            {
                roots.Add(Path.Combine(above, "mods"));
            }
        }
        return roots;
    }
}
