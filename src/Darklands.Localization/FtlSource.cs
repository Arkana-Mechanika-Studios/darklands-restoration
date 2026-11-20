using System.Text.RegularExpressions;

namespace Darklands.Localization;

/// <summary>
/// The messages of a Fluent file as written, for the pack's checks (L-7w): each message's id, its line and the variables its value
/// uses (<c>{ $arg0 }</c>, <c>{ $count }</c>). Not a Fluent parser: the localizer reads the files; this only lists what they define.
/// </summary>
// Not in the original: the 1.1 translation packs (docs/LOCALISATION.md).
public static partial class FtlSource
{
    /// <summary>One message of a file: its id, its line (from 1), and the variables its value names.</summary>
    public sealed record Message(string Id, int Line, IReadOnlySet<string> Variables)
    {
        /// <summary>
        /// The value as written: the first line's text after <c>=</c>, then each further line of the message (its indentation kept)
        /// after a line break; a value that starts on the next line starts with the line break. <c>id =</c> and this give the
        /// message back (<see cref="Write"/>).
        /// </summary>
        public string Value { get; init; } = "";
    }

    /// <summary>A message as a file holds it: <c>id = value</c>, or <c>id =</c> and the value's lines.</summary>
    public static string Write(string id, string value)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(value);
        return id + " =" + (value.StartsWith('\n') ? value : " " + value);
    }

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9_-]*)[ \t]*=")]
    private static partial Regex MessageStart();

    [GeneratedRegex(@"\{\s*\$([A-Za-z][A-Za-z0-9_-]*)")]
    private static partial Regex Variable();

    /// <summary>The messages of <paramref name="text"/> (a <c>.ftl</c> file's text), in file order.</summary>
    public static IReadOnlyList<Message> Messages(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<Message>();
        string? id = null;
        var line = 0;
        var value = new System.Text.StringBuilder();
        var raw = new System.Text.StringBuilder();
        var blanks = 0;
        void End()
        {
            if (id is not null)
            {
                found.Add(new Message(id, line, Variables(value.ToString())) { Value = raw.ToString() });
            }
            id = null;
            value.Clear();
            raw.Clear();
            blanks = 0;
        }
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Length > 0 && l[0] is ' ' or '\t')
            {
                // a continuation of the value (or of an attribute)
                value.Append(' ').Append(l);
                raw.Append('\n', blanks + 1).Append(l);
                blanks = 0;
                continue;
            }
            if (l.Length == 0)
            {
                // a blank line inside a value counts only when the value goes on after it
                blanks += id is null ? 0 : 1;
                continue;
            }
            End();
            if (MessageStart().Match(l) is { Success: true } m)
            {
                id = m.Groups[1].Value;
                line = i + 1;
                value.Append(l, m.Length, l.Length - m.Length);
                raw.Append(l.AsSpan(m.Length).TrimStart(' '));
            }
        }
        End();
        return found;
    }

    /// <summary>The variables a Fluent value names (<c>{ $arg0 }</c> → <c>arg0</c>).</summary>
    public static IReadOnlySet<string> Variables(string value) =>
        Variable().Matches(value ?? "").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
}
