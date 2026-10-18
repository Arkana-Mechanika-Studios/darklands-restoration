using System.Text.Json;

namespace Darklands.Platform;

/// <summary>
/// Machine-local paths: the environment variable, else the key in local/paths.json at the root
/// of the repository the program runs from (git-ignored; the same file tools/local_paths.py reads).
///   game       DARKLANDS_DIR         the original game's folder (tests; the SDL host when nothing else names one)
///   exe        DARKLANDS_EXE         its DARKLAND.EXE, when not in the game folder (tests)
///   sdl        DARKLANDS_SDL2_PATH   the SDL2 library (SDL host)
///   mt32roms   DARKLANDS_MT32_ROMS   the MT-32 / CM-32L ROM folder
///   textfont   DARKLANDS_TEXT_FONT   the 8x16 font of the text screen
/// A relative path in local/paths.json is relative to the project root: DARKLANDS_ROOT, else the folder
/// that holds the repository. Moving the whole project folder then needs no change.
/// </summary>
public static class LocalPaths
{
    /// <summary>The environment variable naming the project root (the folder that holds the repository, the reference, the game copy …).</summary>
    public const string RootVariable = "DARKLANDS_ROOT";

    public static string? Get(string key, string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var file = Path.Combine(dir.FullName, "local", "paths.json");
                if (File.Exists(file))
                {
                    try
                    {
                        using var json = JsonDocument.Parse(File.ReadAllText(file));
                        if (json.RootElement.TryGetProperty(key, out var entry) && entry.GetString() is { Length: > 0 } path)
                        {
                            return Resolve(path, dir.FullName, Environment.GetEnvironmentVariable(RootVariable));
                        }
                    }
                    catch (JsonException)
                    {
                        Console.Error.WriteLine($"cannot read {file}");
                    }
                    break;
                }
                if (File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
                {
                    break;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// A path of local/paths.json as given when absolute; else relative to <paramref name="root"/> (DARKLANDS_ROOT)
    /// when set, else to the folder holding the repository at <paramref name="repository"/>.
    /// </summary>
    public static string Resolve(string path, string repository, string? root)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }
        var basis = !string.IsNullOrWhiteSpace(root) ? root : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(repository)) ?? repository;
        return Path.GetFullPath(Path.Combine(basis, path));
    }
}
