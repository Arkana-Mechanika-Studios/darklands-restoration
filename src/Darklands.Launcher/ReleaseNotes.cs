using System.Text.RegularExpressions;

namespace Darklands.Launcher;

/// <summary>
/// The release notes as the launcher shows them: the items ("- " lines) of the notes' sections up to "Getting started",
/// each reduced to its bold lead when it has one ("- **Crash when the map opens** (...). The seasonal ..." gives
/// "Crash when the map opens"), else to its first sentence.
/// </summary>
internal static partial class ReleaseNotes
{
    private static readonly string[] EndSections = ["getting started", "files", "install"];

    public static IReadOnlyList<string> Items(string markdown)
    {
        var items = new List<string>();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') && EndSections.Any(e => line.TrimStart('#').Trim().StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                items.Add(Short(line[2..].Trim()));
            }
        }
        return items;
    }

    /// <summary>
    /// The items with a line of detail each: the bold lead and what follows it up to its first "(", ";", ":" or the end of its
    /// sentence ("- **A new launcher.** A painted window without a title bar (drag ...)" gives ("A new launcher", "A painted window
    /// without a title bar")); an item without a bold lead has no detail.
    /// </summary>
    public static IReadOnlyList<(string Title, string Detail)> Detailed(string markdown)
    {
        var items = new List<(string, string)>();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') && EndSections.Any(e => line.TrimStart('#').Trim().StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
            if (!(line.StartsWith("- ") || line.StartsWith("* ")))
            {
                continue;
            }
            var item = line[2..].Trim();
            var bold = Bold().Match(item);
            if (!bold.Success || bold.Index != 0)
            {
                items.Add((Short(item), ""));
                continue;
            }
            var rest = Plain(item[bold.Length..]).Trim();
            var end = Sentence().Match(rest);
            if (end.Success)
            {
                rest = rest[..end.Index];
            }
            // (an item whose lead is followed at once by "(" has no detail line: its parenthesis is no sentence)
            var cut = rest.IndexOfAny(['(', ';', ':']);
            if (cut >= 0)
            {
                rest = rest[..cut];
            }
            items.Add((Short(item), rest.Trim().TrimEnd('.', ',')));
        }
        return items;
    }

    private static string Short(string item)
    {
        var bold = Bold().Match(item);
        if (bold.Success && bold.Index == 0)
        {
            return Plain(bold.Groups[1].Value).TrimEnd('.', ':');
        }
        var text = Plain(item);
        var end = Sentence().Match(text);
        return end.Success ? text[..(end.Index + 1)] : text;
    }

    /// <summary>Markdown emphasis, code and links reduced to their text.</summary>
    private static string Plain(string text) => Link().Replace(text, "$1").Replace("**", "").Replace("`", "").Replace("__", "").Replace("*", "");

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    // the end of the first sentence: a full stop followed by a space and a capital
    [GeneratedRegex(@"[.!?](?=\s+[A-Z])")]
    private static partial Regex Sentence();
}
