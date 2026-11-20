namespace Darklands.Core;

/// <summary>
/// Text as codes for the Unicode text path (L-6b): a translation's words drawn in a font with letters the game's bytes cannot hold.
/// A code below 0x20 is one of the game's control codes (0x0A a line, 0x14 a paragraph, the option markers, 0x1D the indent, 0x01 /
/// 0x13 a passage); a code from 0x20 is a Unicode code point; a colour code is <see cref="ColourBase"/> + its byte's low seven bits
/// (<see cref="ColourBack"/>: back to the call's colour, the original's 0xFF). The game's own text (bytes) gives the same codes for
/// the same meanings, so either path draws it alike; Classic never leaves the byte path.
/// </summary>
// Not in the original: the 1.1 translations (docs/LOCALISATION.md, L-6b).
public static class TextCodes
{
    /// <summary>The first colour code (past Unicode's last code point).</summary>
    public const int ColourBase = 0x110000;

    /// <summary>The colour code that goes back to the call's colour (the original's byte 0xFF).</summary>
    public const int ColourBack = ColourBase + 0x7F;

    /// <summary>Whether <paramref name="code"/> is a colour code.</summary>
    public static bool IsColour(int code) => code >= ColourBase;

    /// <summary>The code of a game byte: a control code as itself, a colour byte as a colour code, a letter as its code point.</summary>
    public static int OfByte(byte b) => b >= 0x80 ? ColourBase + (b & 0x7F) : b < 0x20 && b != 0x1F ? b : GameTextCodec.ToChar(b);

    /// <summary>The codes of game bytes (<see cref="OfByte"/>, up to a zero).</summary>
    public static int[] OfBytes(ReadOnlySpan<byte> bytes)
    {
        var length = bytes.IndexOf((byte)0);
        var codes = new int[length < 0 ? bytes.Length : length];
        for (var i = 0; i < codes.Length; i++)
        {
            codes[i] = OfByte(bytes[i]);
        }
        return codes;
    }

    /// <summary>
    /// The codes of a text: a character that stands for a game byte (a C1 control, the codec's private-use characters) as that
    /// byte's code, any other as its code point (a surrogate pair as one code).
    /// </summary>
    public static int[] OfText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var codes = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codes.Add(char.ConvertToUtf32(c, text[++i]));
            }
            else if (c is >= '\u0080' and <= '\u009F' || c is >= (char)(GameTextCodec.ByteAsIs + 0x80) and <= (char)(GameTextCodec.ByteAsIs + 0xFF))
            {
                codes.Add(OfByte(GameTextCodec.ToByte(c)));
            }
            else
            {
                codes.Add(c);
            }
        }
        return [.. codes];
    }

    /// <summary>
    /// Whether every character of <paramref name="text"/> has a game byte of the same meaning (ASCII, the seven letters, the control
    /// and colour codes): such a text keeps the byte path. A translation's é, ł or Cyrillic needs the Unicode path.
    /// </summary>
    public static bool FitsBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var c in text)
        {
            var fits = c < '\u0080'
                || c is >= '\u0080' and <= '\u009F'
                || c is 'ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü' or 'ß'
                || c is >= (char)(GameTextCodec.ByteAsIs + 0x80) and <= (char)(GameTextCodec.ByteAsIs + 0xFF);
            if (!fits)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The character a colour code is written as in the program's strings (the codec's character of its byte).</summary>
    public static char ColourCharacter(int colour) => GameTextCodec.ToChar((byte)(0x80 | (colour & 0x7F)));

    /// <summary>The colour (0..0x7F) a program string's character stands for, or -1 for a character that is not a colour code.</summary>
    public static int ColourOf(char c) =>
        c is >= '\u0080' and <= '\u009F' || c is >= (char)(GameTextCodec.ByteAsIs + 0xA0) and <= (char)(GameTextCodec.ByteAsIs + 0xFF)
            ? GameTextCodec.ToByte(c) & 0x7F
            : -1;

    /// <summary>
    /// A translation's colour tags (<c>&lt;c12&gt;</c> colour 12, <c>&lt;/c&gt;</c> back to the line's colour) as the program's colour
    /// characters; any other text as it is (docs/LOCALISATION.md: the menus' hot-key letters).
    /// </summary>
    public static string FromTags(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOf('<', StringComparison.Ordinal) < 0)
        {
            return text;
        }
        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' && TagAt(text, i, out var colour, out var length))
            {
                result.Append(ColourCharacter(colour));
                i += length - 1;
                continue;
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    /// <summary>A program string's colour characters as tags (the template's form; <c>&lt;</c> and <c>&gt;</c> left to the caller).</summary>
    public static string ToTags(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            var colour = ColourOf(c);
            result.Append(colour < 0 ? c.ToString() : colour == 0x7F ? "</c>" : "<c" + colour.ToString(System.Globalization.CultureInfo.InvariantCulture) + ">");
        }
        return result.ToString();
    }

    /// <summary>
    /// The coloured parts of a program string, in order (each from a colour character other than "back" to the next colour character,
    /// spaces trimmed): the menus' hot-key letters.
    /// </summary>
    public static IReadOnlyList<string> ColouredParts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string>();
        System.Text.StringBuilder? part = null;
        foreach (var c in text)
        {
            var colour = ColourOf(c);
            if (colour >= 0)
            {
                if (part is not null)
                {
                    parts.Add(part.ToString().Trim());
                }
                part = colour == 0x7F ? null : new System.Text.StringBuilder();
                continue;
            }
            part?.Append(c);
        }
        if (part is not null)
        {
            parts.Add(part.ToString().Trim());
        }
        return parts;
    }

    /// <summary>
    /// The menus' hot keys of a program string: its coloured parts that are a single letter (a menu marks its key's letter so); a coloured
    /// word or phrase (an error message's) is no key.
    /// </summary>
    public static IReadOnlyList<string> HotKeys(string text) => [.. ColouredParts(text).Where(p => p.Length == 1)];

    /// <summary>Whether a colour tag starts at <paramref name="at"/>: its colour (0x7F for <c>&lt;/c&gt;</c>) and length.</summary>
    public static bool TagAt(string text, int at, out int colour, out int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        colour = -1;
        length = 0;
        if (string.CompareOrdinal(text, at, "</c>", 0, 4) == 0)
        {
            (colour, length) = (0x7F, 4);
            return true;
        }
        if (at + 3 >= text.Length || text[at + 1] != 'c' || !char.IsAsciiDigit(text[at + 2]))
        {
            return false;
        }
        var end = at + 2;
        var value = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end]) && end - at < 6)
        {
            value = value * 10 + (text[end] - '0');
            end++;
        }
        if (end >= text.Length || text[end] != '>' || value > 0x7E)
        {
            return false;
        }
        (colour, length) = (value, end - at + 1);
        return true;
    }
}
