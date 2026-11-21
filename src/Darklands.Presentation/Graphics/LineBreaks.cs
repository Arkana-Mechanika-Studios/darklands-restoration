namespace Darklands.Presentation.Graphics;

/// <summary>
/// Where a line may break besides the game's own places (a space, a hyphen, the line's end), by the translation pack's language:
/// Chinese and Japanese, written without spaces, between any two of their characters, except before a closing mark (、。，」…) or
/// after an opening one (「（…), and never inside a run of Latin letters or digits. Korean breaks at its spaces, as the game does.
/// Every other language: none (null), so the original's wrapping is untouched.
/// </summary>
// Not in the original: the 1.1 translations (Chinese, Japanese, Korean).
public static class LineBreaks
{
    // marks a line may not start with (closing punctuation, small kana, the prolonged sound mark, iteration marks)
    private const string NoStart = "、。，．・：；？！）」』】〕〉》〙〗”’ゝゞヽヾーァィゥェォッャュョヮヵヶぁぃぅぇぉっゃゅょゎゕゖ々〻‐゠–〜～!),.:;?]}」";

    // marks a line may not end with (opening brackets and quotes)
    private const string NoEnd = "（「『【〔〈《〘〖“‘([{";

    /// <summary>The rule for <paramref name="language"/> (a BCP 47 tag: zh, zh-Hans, ja ...), null where the game's own places suffice.</summary>
    public static Func<int, int, bool>? For(string? language)
    {
        if (language is null)
        {
            return null;
        }
        var primary = language.Split('-')[0].ToLowerInvariant();
        return primary is "zh" or "ja" or "yue" ? BetweenCharacters : null;
    }

    /// <summary>Whether a line may break after <paramref name="before"/> when <paramref name="after"/> follows (Chinese and Japanese).</summary>
    public static bool BetweenCharacters(int before, int after)
    {
        if (before <= 0 || after <= 0 || before == '\n' || after == '\n')
        {
            return false;
        }
        if (!IsWide(before) && !IsWide(after))
        {
            // Latin letters, digits and their punctuation keep together (they break at their spaces)
            return false;
        }
        return !Contains(NoStart, after) && !Contains(NoEnd, before);
    }

    private static bool Contains(string marks, int code) => code <= 0xFFFF && marks.Contains((char)code, StringComparison.Ordinal);

    /// <summary>A character of the scripts written without spaces: CJK ideographs, kana, their punctuation and full-width forms, Hangul.</summary>
    public static bool IsWide(int c) =>
        c is >= 0x1100 and <= 0x11FF          // Hangul Jamo
            or >= 0x2E80 and <= 0x303F        // CJK radicals, symbols and punctuation
            or >= 0x3040 and <= 0x30FF        // hiragana, katakana
            or >= 0x3100 and <= 0x31FF        // bopomofo, katakana extensions
            or >= 0x3400 and <= 0x4DBF        // CJK extension A
            or >= 0x4E00 and <= 0x9FFF        // CJK unified ideographs
            or >= 0xAC00 and <= 0xD7AF        // Hangul syllables
            or >= 0xF900 and <= 0xFAFF        // CJK compatibility ideographs
            or >= 0xFF00 and <= 0xFFEF        // full-width forms
            or >= 0x20000 and <= 0x2FA1F;     // CJK extensions B..
}
