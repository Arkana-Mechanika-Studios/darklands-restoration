using Darklands.Content;

namespace Darklands.Battles.Tests;

/// <summary>The player's Darklands folder (DARKLANDS_DIR, or the "game" key of local/paths.json); tests that need it pass vacuously without it.</summary>
internal static class GameFolder
{
    public static GameFiles? Files { get; } = Find();

    private static GameFiles? Find()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? new GameFiles(folder) : null;
    }
}
