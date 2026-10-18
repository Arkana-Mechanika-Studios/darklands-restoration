using System.Text.Json;
using System.Text.RegularExpressions;

namespace Darklands.Platform;

/// <summary>Small edits of an existing settings file that keep its comments and layout (the file is JSON with comments).</summary>
public static partial class SettingsFileEdits
{
    /// <summary>
    /// Sets "gameFolder" in the file at <paramref name="path"/> to <paramref name="folder"/> (written with forward slashes).
    /// Only an empty or null value is replaced unless <paramref name="replace"/>; false (and why) when the file has no such
    /// key, already names a folder, or cannot be written.
    /// </summary>
    public static bool TrySetGameFolder(string path, string folder, bool replace, out string message)
    {
        try
        {
            var text = File.ReadAllText(path);
            var match = GameFolderKey().Match(text);
            if (!match.Success)
            {
                message = $"{path} has no \"gameFolder\" key";
                return false;
            }
            var current = match.Groups["value"].Value;
            if (!replace && current is not ("\"\"" or "null"))
            {
                message = $"{path} already names a game folder";
                return false;
            }
            var value = JsonSerializer.Serialize(folder.Replace('\\', '/'));
            text = text[..match.Groups["value"].Index] + value + text[(match.Groups["value"].Index + match.Groups["value"].Length)..];
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            message = $"wrote \"gameFolder\": {value} into {path}";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"cannot update {path}: {e.Message}";
            return false;
        }
    }

    // the first "gameFolder" key not inside a // comment line, with its string or null value
    [GeneratedRegex("""^(?![ \t]*//)[^\r\n]*?"gameFolder"\s*:\s*(?<value>"(?:[^"\\]|\\.)*"|null)""", RegexOptions.Multiline)]
    private static partial Regex GameFolderKey();
}
