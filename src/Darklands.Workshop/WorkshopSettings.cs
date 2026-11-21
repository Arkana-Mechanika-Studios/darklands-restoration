using System.Text.Json;
using Darklands.Content;
using Darklands.Platform;

namespace Darklands.Workshop;

/// <summary>
/// What the Workshop remembers between runs (in the user's application data, <c>DarklandsWorkshop/settings.json</c>): the game folder
/// and the packs opened lately. Never the game's files or a pack's texts.
/// </summary>
internal sealed class WorkshopSettings
{
    public string? GameFolder { get; set; }

    public List<string> RecentPacks { get; set; } = [];

    /// <summary>The author a new pack takes (the last one given).</summary>
    public string? Author { get; set; }

    private static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarklandsWorkshop", "settings.json");

    public static WorkshopSettings Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<WorkshopSettings>(File.ReadAllText(Path)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // remembering is a comfort: the Workshop works without it
        }
    }

    /// <summary>The game folder: the one remembered, else DARKLANDS_DIR or the developer's local paths, else the usual places.</summary>
    public string? FindGame()
    {
        if (GameFolderSearch.GameFolderIn(GameFolder, GameFiles.IsGameFolder) is { } remembered)
        {
            return remembered;
        }
        if (GameFolderSearch.GameFolderIn(LocalPaths.Get("game", "DARKLANDS_DIR"), GameFiles.IsGameFolder) is { } local)
        {
            return local;
        }
        return GameFolderSearch.Find(GameFolderSearch.Candidates(WorkshopApp.GameProgramFolder), GameFiles.IsGameFolder)?.Folder;
    }

    /// <summary>Puts <paramref name="pack"/> first in the recent packs (ten kept).</summary>
    public void Opened(string pack)
    {
        RecentPacks.RemoveAll(p => string.Equals(p, pack, StringComparison.OrdinalIgnoreCase));
        RecentPacks.Insert(0, pack);
        if (RecentPacks.Count > 10)
        {
            RecentPacks.RemoveRange(10, RecentPacks.Count - 10);
        }
    }
}
