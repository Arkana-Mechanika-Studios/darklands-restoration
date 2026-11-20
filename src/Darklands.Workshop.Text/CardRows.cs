namespace Darklands.Workshop.Text;

/// <summary>What a row of a card text is.</summary>
public enum CardRowKind
{
    /// <summary>The words before the first option (a part of them, between conditional passages).</summary>
    Intro,

    /// <summary>A conditional passage (<c>&lt;if-N&gt; … &lt;endif&gt;</c>): shown only when its condition holds.</summary>
    Passage,

    /// <summary>An option line (<c>&lt;opt&gt;</c>, <c>&lt;opt-items&gt;</c>, <c>&lt;opt-saints&gt;</c>, <c>&lt;opt-battle&gt;</c>).</summary>
    Option,
}

/// <summary>
/// One row of a card text: what the translator writes (<see cref="Content"/>) between the row's fixed parts (<see cref="Prefix"/>:
/// an option's marker, lead-in and indent, a passage's opening tag; <see cref="Suffix"/>: its line breaks, a passage's end).
/// </summary>
public sealed record CardRow(CardRowKind Kind, string Label, string Prefix, string Content, string Suffix)
{
    /// <summary>The row as written (its parts joined).</summary>
    public string Written => Prefix + Content + Suffix;

    /// <summary>
    /// The row's words as the translator sees them in a field: line breaks as lines (<c>&lt;br&gt;&lt;p&gt;</c> a blank line,
    /// <c>&lt;br&gt;</c> a line), the file's own line breaks left out; null when that would not give the row back as written
    /// (then the row is shown as written, its tags included).
    /// </summary>
    public string? Field
    {
        get
        {
            var text = ToField(Content);
            return FromField(text) == Unwrapped(Content) ? text : null;
        }
    }

    /// <summary>The row with <paramref name="field"/> as its words (a field's lines back into line-break tags).</summary>
    public CardRow WithField(string field) => this with { Content = FromField(field) };

    /// <summary>The written content without the file's line breaks (a value's lines in a .ftl file).</summary>
    public static string Unwrapped(string content) => content.Replace("\r", "", StringComparison.Ordinal).Replace("\n    ", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);

    /// <summary>Written content to a field's text.</summary>
    public static string ToField(string content) =>
        Unwrapped(content).Replace("<br><p>", "\n\n", StringComparison.Ordinal).Replace("<br>", "\n", StringComparison.Ordinal);

    /// <summary>A field's text to written content.</summary>
    public static string FromField(string field) =>
        field.Replace("\r", "", StringComparison.Ordinal).Replace("\n\n", "<br><p>", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}

/// <summary>
/// A card text as rows (W-0 polish, the Workshop's Rows view): the intro, each conditional passage at the top level and each option
/// line, each with its fixed parts apart from the words the translator writes, so options are typed one per field and the tags of
/// line breaks, option markers and indents are written for the translator. <see cref="Write"/> joins the rows back: for every
/// card text of the game the rows give the written text back exactly (the tests), and the pack check stays the judge.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class CardRows
{
    private static readonly string[] OptionTags = ["<opt>", "<opt-items>", "<opt-saints>", "<opt-battle>"];

    /// <summary>The rows of a written card text (a Fluent value as written).</summary>
    public static IReadOnlyList<CardRow> Parse(string written)
    {
        ArgumentNullException.ThrowIfNull(written);
        var tokens = TextTokens.Split(written).Select(t => t.Text).ToList();
        var rows = new List<CardRow>();
        var at = 0;
        var options = 0;
        var passages = 0;
        bool IsOption(int i) => i < tokens.Count && OptionTags.Contains(tokens[i]);
        bool IsBreak(int i) => i < tokens.Count && (tokens[i] is "<br>" or "<p>" || tokens[i].Trim().Length == 0 && tokens[i].Contains('\n', StringComparison.Ordinal));
        string Join(int from, int to) => string.Concat(tokens.Skip(from).Take(to - from));

        // the intro and the passages among it, up to the first option
        var start = at;
        while (at < tokens.Count && !IsOption(at))
        {
            if (tokens[at].StartsWith("<if-", StringComparison.Ordinal))
            {
                if (at > start)
                {
                    rows.Add(new CardRow(CardRowKind.Intro, "", "", Join(start, at), ""));
                }
                var end = tokens.FindIndex(at, t => t == "<endif>");
                if (end < 0)
                {
                    end = tokens.Count - 1;
                }
                passages++;
                var n = tokens[at][4..^1];
                rows.Add(new CardRow(CardRowKind.Passage, "passage " + n, tokens[at], Join(at + 1, end), end < tokens.Count && tokens[end] == "<endif>" ? "<endif>" : ""));
                at = end + 1;
                start = at;
                continue;
            }
            at++;
        }
        if (at > start)
        {
            // the intro's last line breaks (before the first option) are fixed, as an option's are
            var end = at;
            while (at < tokens.Count && end > start && IsBreak(end - 1))
            {
                end--;
            }
            rows.Add(new CardRow(CardRowKind.Intro, "", "", Join(start, end), Join(end, at)));
        }

        // the options: marker, lead-in ("..."), indent | words | the line breaks before the next option
        while (at < tokens.Count)
        {
            var marker = at;
            at++;
            var prefixEnd = at;
            // the lead-in and the indent: "..." words and <indent> right after the marker
            if (prefixEnd < tokens.Count && tokens[prefixEnd] == "...")
            {
                prefixEnd++;
            }
            if (prefixEnd < tokens.Count && tokens[prefixEnd] == "<indent>")
            {
                prefixEnd++;
            }
            else if (prefixEnd != at && (prefixEnd >= tokens.Count || tokens[prefixEnd] != "<indent>"))
            {
                prefixEnd = at;   // "..." without an indent: part of the words
            }
            var next = at;
            while (next < tokens.Count && !IsOption(next))
            {
                next++;
            }
            var contentEnd = next;
            while (contentEnd > prefixEnd && IsBreak(contentEnd - 1))
            {
                contentEnd--;
            }
            options++;
            var kind = tokens[marker] switch
            {
                "<opt-items>" => "items",
                "<opt-saints>" => "saints",
                "<opt-battle>" => "battle",
                _ => "",
            };
            rows.Add(new CardRow(CardRowKind.Option, kind, Join(marker, prefixEnd), Join(prefixEnd, contentEnd), Join(contentEnd, next)));
            at = next;
        }
        return rows;
    }

    /// <summary>The rows joined back into the written text.</summary>
    public static string Write(IEnumerable<CardRow> rows) => string.Concat(rows.Select(r => r.Written));

    /// <summary>
    /// The rows of <paramref name="translation"/> laid against <paramref name="english"/>'s: the same kinds in the same order (null
    /// when they differ: the Raw view shows such a text); an untranslated text gives the English's rows with no words.
    /// </summary>
    public static IReadOnlyList<CardRow>? Against(IReadOnlyList<CardRow> english, string? translation)
    {
        ArgumentNullException.ThrowIfNull(english);
        if (translation is null)
        {
            return [.. english.Select(r => r with { Content = "" })];
        }
        var rows = Parse(translation);
        if (rows.Count != english.Count || rows.Zip(english).Any(p => p.First.Kind != p.Second.Kind || p.First.Label != p.Second.Label))
        {
            return null;
        }
        return rows;
    }
}
