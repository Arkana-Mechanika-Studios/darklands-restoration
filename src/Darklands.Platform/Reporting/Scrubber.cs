using System.Text.RegularExpressions;

namespace Darklands.Platform.Reporting;

/// <summary>
/// Takes the player's name out of the text of a report: the user-profile folder and the user name become <c>&lt;user&gt;</c>
/// (also in the forms a JSON file and a Unix path give them). Saved games and pictures are not changed.
/// </summary>
public sealed partial class Scrubber
{
    public const string Placeholder = "<user>";

    private readonly List<string> _literals = [];
    private readonly string? _name;

    public Scrubber(string? profileFolder, string? userName)
    {
        if (!string.IsNullOrWhiteSpace(profileFolder))
        {
            var trimmed = profileFolder.TrimEnd('\\', '/');
            foreach (var form in new[] { trimmed, trimmed.Replace('\\', '/'), trimmed.Replace('/', '\\'), trimmed.Replace("\\", "\\\\", StringComparison.Ordinal) })
            {
                if (form.Length > 0 && !_literals.Contains(form, StringComparer.OrdinalIgnoreCase))
                {
                    _literals.Add(form);
                }
            }
            _literals.Sort((a, b) => b.Length.CompareTo(a.Length));
        }
        // a name of one or two letters would damage ordinary words
        _name = userName is { Length: >= 3 } ? userName : null;
    }

    /// <summary>The scrubber for the user running the program.</summary>
    public static Scrubber ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    public string Scrub(string text)
    {
        foreach (var literal in _literals)
        {
            text = text.Replace(literal, Placeholder, StringComparison.OrdinalIgnoreCase);
        }
        text = ProfilePath().Replace(text, Placeholder);
        if (_name is not null)
        {
            text = text.Replace(_name, Placeholder, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    // C:\Users\Name, C:/Users/Name, C:\\Users\\Name (JSON), /home/name, /Users/name: any user's profile folder
    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]+Users[\\/]+|(?<![\w.-])/(?:home|Users)/)(?!Public\b|Default\b|<user>)[^\\/\s""'<>|:*?]+", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilePath();
}
