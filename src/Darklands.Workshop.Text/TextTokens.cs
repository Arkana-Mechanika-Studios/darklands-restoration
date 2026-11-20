using System.Text.RegularExpressions;

namespace Darklands.Workshop.Text;

/// <summary>What a part of a text is: words the translator types, or a chip they move but cannot break.</summary>
public enum TokenKind
{
    /// <summary>Words.</summary>
    Words,

    /// <summary>A tag of a card text or a colour (<c>&lt;br&gt;</c>, <c>&lt;opt&gt;</c>, <c>&lt;if-1&gt;</c>, <c>&lt;c12&gt;</c>).</summary>
    Tag,

    /// <summary>A name the game puts words in (<c>$ChosenOneName</c>, a pack's own <c>$him1</c>).</summary>
    Name,

    /// <summary>A Fluent placeable (<c>{ $arg0 }</c>, <c>{" "}</c>, a choice).</summary>
    Placeable,
}

/// <summary>A part of a text: its kind and its text as written.</summary>
public readonly record struct TextToken(TokenKind Kind, string Text);

/// <summary>
/// A text's parts for the editor's chips (W-0 + E-text): tags, $Names and Fluent placeables as whole chips, the words between them;
/// the parts written one after another give the text back exactly.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static partial class TextTokens
{
    [GeneratedRegex(@"<(/?[a-z][a-z0-9-]*)>|\$[A-Za-z][A-Za-z0-9]*")]
    private static partial Regex Chip();

    /// <summary>The parts of <paramref name="value"/> (a Fluent value as written).</summary>
    public static IReadOnlyList<TextToken> Split(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tokens = new List<TextToken>();
        var words = new System.Text.StringBuilder();
        void Words()
        {
            if (words.Length > 0)
            {
                tokens.Add(new TextToken(TokenKind.Words, words.ToString()));
                words.Clear();
            }
        }
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '{' && Close(value, i) is var end and > 0)
            {
                Words();
                tokens.Add(new TextToken(TokenKind.Placeable, value[i..(end + 1)]));
                i = end;
                continue;
            }
            if (value[i] is '<' or '$' && Chip().Match(value, i) is { Success: true } m && m.Index == i)
            {
                Words();
                tokens.Add(new TextToken(m.Value[0] == '$' ? TokenKind.Name : TokenKind.Tag, m.Value));
                i += m.Length - 1;
                continue;
            }
            words.Append(value[i]);
        }
        Words();
        return tokens;
    }

    /// <summary>The text of <paramref name="tokens"/> (their texts one after another).</summary>
    public static string Join(IEnumerable<TextToken> tokens) => string.Concat(tokens.Select(t => t.Text));

    // the brace that closes the placeable opened at start (nested braces and quoted strings counted), or -1
    private static int Close(string value, int start)
    {
        var depth = 0;
        var quoted = false;
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (quoted)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}' when --depth == 0:
                    return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// A choice of words as plain fields (W-0 + E-text): a value that is one Fluent select, such as a pack variable's
/// <c>{ $sex -&gt; [female] ihr *[male] ihm }</c> or a plural <c>{ $count -&gt; [one] ... *[other] ... }</c>, read as its selector and
/// variants, and written back in Fluent's multi-line form; the translator never types the syntax.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static partial class FluentChoice
{
    /// <summary>A variant: its key, its words, whether it is the default (<c>*</c>).</summary>
    public readonly record struct Variant(string Key, string Words, bool Default);

    [GeneratedRegex(@"^\s*\{\s*\$([A-Za-z][A-Za-z0-9_-]*)\s*->\s*(.*)\}\s*$", RegexOptions.Singleline)]
    private static partial Regex Select();

    [GeneratedRegex(@"^\s*(\*?)\[([A-Za-z0-9_-]+)\]\s*(.*?)\s*$")]
    private static partial Regex VariantLine();

    /// <summary>The selector and variants of a value that is one select (null when it is not).</summary>
    public static (string Selector, IReadOnlyList<Variant> Variants)? Read(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Select().Match(value) is not { Success: true } m)
        {
            return null;
        }
        var variants = new List<Variant>();
        foreach (var line in m.Groups[2].Value.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            if (VariantLine().Match(line) is not { Success: true } v)
            {
                return null;
            }
            variants.Add(new Variant(v.Groups[2].Value, v.Groups[3].Value, v.Groups[1].Value == "*"));
        }
        return variants.Count(v => v.Default) == 1 ? (m.Groups[1].Value, variants) : null;
    }

    /// <summary>A select of <paramref name="selector"/> over <paramref name="variants"/> as a Fluent value (multi-line, as Fluent requires).</summary>
    public static string Write(string selector, IEnumerable<Variant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        var text = new System.Text.StringBuilder("\n    { $").Append(selector).Append(" ->");
        foreach (var v in variants)
        {
            text.Append("\n       ").Append(v.Default ? '*' : ' ').Append('[').Append(v.Key).Append("] ").Append(v.Words);
        }
        return text.Append("\n    }").ToString();
    }
}
