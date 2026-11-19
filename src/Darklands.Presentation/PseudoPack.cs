using System.Text;
using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>
/// The pseudo-locale pack for translators and developers (docs/LOCALISATION.md): the translator's template with every text made
/// longer by about a third and its vowels marked (a → ä, o → ö, u → ü), in letters the game's fonts draw, so a screen shows at once
/// which of its texts come from a pack and where a longer language will not fit; the pack check lists those places.
/// </summary>
// Not in the original: the 1.1 translation packs (step 5 of the plan).
public static class PseudoPack
{
    /// <summary>The pseudo-locale's language tag.</summary>
    public const string Language = PseudoLocale.Tag;

    /// <summary>The template's messages in the pseudo-locale.</summary>
    public static IReadOnlyList<TranslatorTemplate.Message> From(IReadOnlyList<TranslatorTemplate.Message> template) =>
        [.. template.Select(m => m with { Value = Transform(m.Value) })];

    /// <summary>
    /// A template value in the pseudo-locale: only its words change; placeables (<c>{ $arg0 }</c>, <c>{" "}</c>), tags (<c>&lt;br&gt;</c>,
    /// <c>&lt;c12&gt;</c>), <c>$Names</c> and the line breaks stay, and so does a hot key's coloured letter (the menus test it in the code).
    /// </summary>
    public static string Transform(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = new StringBuilder(value.Length + value.Length / 2);
        var run = 0;
        var coloured = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '<' && Darklands.Core.TextCodes.TagAt(value, i, out var colour, out var length))
            {
                coloured = colour != 0x7F;
                text.Append(value, i, length);
                i += length - 1;
                run = 0;
                continue;
            }
            var skip = c switch
            {
                '{' => value.IndexOf('}', i),
                '<' => value.IndexOf('>', i),
                '$' when i + 1 < value.Length && char.IsAsciiLetter(value[i + 1]) => Name(value, i),
                _ => -1,
            };
            if (skip > i)
            {
                text.Append(value, i, skip - i + 1);
                i = skip;
                run = 0;
                continue;
            }
            if (!char.IsAsciiLetter(c) || coloured)
            {
                text.Append(c);
                run = 0;
                continue;
            }
            var marked = c switch { 'a' => 'ä', 'o' => 'ö', 'u' => 'ü', 'A' => 'Ä', 'O' => 'Ö', 'U' => 'Ü', _ => c };
            text.Append(marked);
            // a third longer: every third letter of a word twice
            if (++run % 3 == 0)
            {
                text.Append(marked);
            }
        }
        return text.ToString();
    }

    private static int Name(string value, int start)
    {
        var i = start + 1;
        while (i + 1 < value.Length && char.IsAsciiLetterOrDigit(value[i + 1]))
        {
            i++;
        }
        return i;
    }
}
