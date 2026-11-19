using System.Text;

namespace Darklands.Localization;

/// <summary>
/// The pseudo-locale (<c>qps</c>): a deterministic transform that makes text easy to spot and stress-tests layout.
/// Letters become accented look-alikes, the text grows by about 35 percent (a tail of tildes), and the whole text is
/// wrapped in square brackets so that a clipped start or end is visible.
/// <para>
/// Left untouched: Fluent placeables (<c>{ $name }</c>, <c>{ OPTION(1) }</c>, nested selects, quoted strings), the game's
/// <c>$name</c> tokens, control characters (the game's escape codes, including 0x1F which stands for a with diaeresis),
/// the characters <c>|</c> and <c>{</c>/<c>}</c> when they are not placeables (the game uses <c>|</c> for u with diaeresis
/// and as a hotkey marker, and <c>{</c> for o with diaeresis), the letters right before and after a <c>|</c> (a hotkey), and a text of one
/// character (a hotkey letter).
/// </para>
/// <para>
/// Limitation: the transform cannot tell a hotkey marker from u with diaeresis, and cannot tell a placeable from an o with
/// diaeresis except by the rule that a placeable opens with a brace followed by a space. Texts that follow other conventions
/// are transformed as plain text.
/// </para>
/// </summary>
public static class PseudoLocale
{
    /// <summary>The locale tag of the pseudo-locale.</summary>
    public const string Tag = "qps";

    private const string Plain = "AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxYyZz";
    private const string Fancy = "ÅåƁƀÇçÐđÉéƑƒĜĝĤĥÍíĴĵĶķĻļṀṁÑñÓóÞþǪǫŔŕŠšŢţÚúṼṽŴŵẊẋÝýŽž";

    /// <summary>The pseudo form of <paramref name="text"/>.</summary>
    /// <param name="text">Text to transform.</param>
    /// <param name="brackets">Wrap the result in square brackets.</param>
    /// <param name="placeables">Treat a brace followed by white space as the start of a Fluent placeable and keep it intact.</param>
    public static string Transform(string text, bool brackets = true, bool placeables = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= 1)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + text.Length / 2 + 4);
        if (brackets)
        {
            sb.Append('[');
        }

        var letters = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (placeables && c == '{' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                var end = PlaceableEnd(text, i);
                sb.Append(text, i, end - i);
                i = end;
            }
            else if (c == '|')
            {
                sb.Append(c);
                i++;
                if (i < text.Length)
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            else if (c == '$' && i + 1 < text.Length && char.IsAsciiLetter(text[i + 1]))
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '-'))
                {
                    i++;
                }

                sb.Append(text, start, i - start);
            }
            else if (char.IsAsciiLetter(c) && i + 1 < text.Length && text[i + 1] == '|')
            {
                // the letter in front of a hotkey marker may be the hotkey itself
                sb.Append(c);
                i++;
            }
            else if (char.IsAsciiLetter(c))
            {
                sb.Append(Fancy[Plain.IndexOf(c, StringComparison.Ordinal)]);
                letters++;
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        sb.Append('~', (letters * 35 + 99) / 100);
        if (brackets)
        {
            sb.Append(']');
        }

        return sb.ToString();
    }

    // Index just past the brace that closes the placeable opened at 'start' (end of text when it never closes).
    private static int PlaceableEnd(string text, int start)
    {
        var depth = 0;
        var inQuote = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuote)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                }
                else if (c == '"')
                {
                    inQuote = false;
                }

                continue;
            }

            if (c == '"')
            {
                inQuote = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i + 1;
            }
        }

        return text.Length;
    }
}
