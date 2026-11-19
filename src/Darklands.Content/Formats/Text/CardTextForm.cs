using System.Globalization;
using System.Text;

namespace Darklands.Content.Formats.Text;

/// <summary>
/// The written form of a card text that translators type (docs/LOCALISATION.md): the block's control codes as tags,
/// <c>&lt;br&gt;</c> line end (0x0A), <c>&lt;p&gt;</c> paragraph (0x14), <c>&lt;opt&gt;</c> / <c>&lt;opt-items&gt;</c> /
/// <c>&lt;opt-saints&gt;</c> / <c>&lt;opt-battle&gt;</c> the option lines (0x15 / 0x10 / 0x16 / 0x06), <c>&lt;indent&gt;</c> the
/// hanging indent (0x1D), <c>&lt;if-N&gt;</c> … <c>&lt;endif&gt;</c> a conditional passage (0x01 … 0x13) that tests the card's
/// condition N, <c>&lt;xNN&gt;</c> any other byte below 0x20; letters through GameTextCodec (the game's own ä ö ü ß ...),
/// <c>$Names</c> as written. A line break of the file and the spaces round it count as one space, or as nothing next to a tag (so
/// a long text may be wrapped freely). <c>{</c> and <c>}</c> are Fluent's: the template writes them as <c>{"{"}</c>, <c>{"}"}</c>.
/// </summary>
// C: the passage's condition is not in its bytes: text-paint-wrapped counts the 0x01 markers of the block and the k-th tests
//   text-slot condition k (CardTextLayout, CardVariables.Conditions, ten of them); N is that count from 1, so a translation keeps the
//   passages in the English order (Check).
// Not in the original: the 1.1 translation packs. No '<' or '>' occurs in the 3,756 blocks of the English MSGFILES, so a tag never
//   meets text.
public static class CardTextForm
{
    private static readonly (string Tag, byte Code)[] Tags =
    [
        ("br", 0x0A), ("p", 0x14), ("opt", 0x15), ("opt-items", 0x10), ("opt-saints", 0x16), ("opt-battle", 0x06), ("indent", 0x1D),
        ("endif", 0x13),
    ];

    private const string IfTag = "if-";

    /// <summary>The option markers, in the order of the option kinds the validator compares.</summary>
    public static readonly IReadOnlyList<byte> OptionCodes = [0x15, 0x10, 0x16, 0x06];

    /// <summary>The block's bytes for a written card text; null with the problem in <paramref name="problem"/>.</summary>
    // C: text-paint-wrapped skips the byte after 0x01 and 0x13 (CardTextLayout); a translation's tag is followed by a space there, so
    //   the skipped byte is never one of the translator's letters.
    public static byte[]? ToBytes(string written, out string? problem)
    {
        var bytes = new List<byte>(written?.Length + 16 ?? 0);
        return Parse(written!, text => bytes.AddRange(Darklands.Core.GameTextCodec.Encode(text)), code => bytes.Add(code), out problem)
            ? [.. bytes]
            : null;
    }

    /// <summary>
    /// The block's codes for a written card text (the Unicode text path, <see cref="Darklands.Core.TextCodes"/>): the same codes as
    /// <see cref="ToBytes"/> gives for the same text, with the letters as code points (a letter the game's bytes cannot hold stays itself).
    /// </summary>
    // Not in the original: the 1.1 translations with letters of their own (L-6b).
    public static int[]? ToCodes(string written, out string? problem)
    {
        var codes = new List<int>(written?.Length + 16 ?? 0);
        return Parse(written!, text => codes.AddRange(Darklands.Core.TextCodes.OfText(text)), code => codes.Add(Darklands.Core.TextCodes.OfByte(code)), out problem)
            ? [.. codes]
            : null;
    }

    // the written form read once for both: its text runs and its tags' bytes (a tag's byte; the space after 0x01 / 0x13 is a text run)
    private static bool Parse(string written, Action<string> addText, Action<byte> addCode, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(written);
        var text = new StringBuilder();
        var pendingBreak = false;
        var lastWasTag = true;
        void Flush()
        {
            if (text.Length > 0)
            {
                addText(text.ToString());
                text.Clear();
            }
        }
        for (var i = 0; i < written.Length; i++)
        {
            var c = written[i];
            if (c is '\n' or '\r')
            {
                // the line break and the spaces round it
                while (text.Length > 0 && text[^1] == ' ')
                {
                    text.Length--;
                }
                while (i + 1 < written.Length && written[i + 1] is ' ' or '\t' or '\n' or '\r')
                {
                    i++;
                }
                pendingBreak = true;
                continue;
            }
            if (c == '<')
            {
                var end = written.IndexOf('>', i);
                if (end < 0)
                {
                    problem = $"a '<' at {i} has no '>'";
                    return false;
                }
                var name = written[(i + 1)..end];
                byte code;
                if (name is "lt" or "gt")
                {
                    // a text's own angle bracket (the German edition writes some): text, not a code
                    pendingBreak = false;
                    text.Append(name == "lt" ? '<' : '>');
                    lastWasTag = false;
                    i = end;
                    continue;
                }
                if (Darklands.Core.TextCodes.TagAt(written, i, out var colour, out _))
                {
                    // a colour tag (<c12> ... </c>): the original's colour byte
                    code = (byte)(0x80 | colour);
                }
                else if (Tags.FirstOrDefault(t => t.Tag == name) is { Tag: not null } tag)
                {
                    code = tag.Code;
                }
                else if (name.StartsWith(IfTag, StringComparison.Ordinal) && int.TryParse(name.AsSpan(IfTag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1)
                {
                    code = 0x01;
                }
                else if (name.Length == 3 && name[0] == 'x' && byte.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) && b < 0x20)
                {
                    code = b;
                }
                else
                {
                    problem = $"unknown tag <{name}>";
                    return false;
                }
                pendingBreak = false;
                Flush();
                addCode(code);
                if (code is 0x01 or 0x13)
                {
                    addText(" ");
                }
                lastWasTag = true;
                i = end;
                continue;
            }
            if (pendingBreak && !lastWasTag)
            {
                text.Append(' ');
            }
            pendingBreak = false;
            lastWasTag = false;
            text.Append(c);
        }
        Flush();
        problem = null;
        return true;
    }

    /// <summary>The written form of an original block (the translator's template): its codes as tags.</summary>
    // C: the byte text-paint-wrapped skips after 0x01 and 0x13 is left out (the original never shows it).
    public static string ToWritten(ReadOnlySpan<byte> raw)
    {
        var written = new StringBuilder(raw.Length + 32);
        var run = new List<byte>();
        var conditions = 0;
        void Flush()
        {
            if (run.Count > 0)
            {
                // Fluent's own braces, should a text ever hold one, as string literals
                written.Append(Darklands.Core.TextCodes.ToTags(Darklands.Core.GameTextCodec.Decode([.. run]).Replace("<", "\u0002").Replace(">", "<gt>").Replace("\u0002", "<lt>")).Replace("{", "\u0001").Replace("}", "{\"}\"}").Replace("\u0001", "{\"{\"}"));
                run.Clear();
            }
        }
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            if (b == 0)
            {
                break;
            }
            if (b >= 0x20 || b == 0x1F)
            {
                run.Add(b);
                continue;
            }
            Flush();
            var tag = b == 0x01 ? IfTag + (++conditions).ToString(CultureInfo.InvariantCulture) : Tags.FirstOrDefault(t => t.Code == b).Tag;
            written.Append('<').Append(tag ?? "x" + b.ToString("X2", CultureInfo.InvariantCulture)).Append('>');
            if (b is 0x01 or 0x13)
            {
                i++;
            }
        }
        Flush();
        return written.ToString();
    }

    /// <summary>What a translated block must keep of the original's: the option lines in order, the conditional passages in order,
    /// and the same <c>$Names</c> and other code bytes (as sets).</summary>
    public sealed record Structure(IReadOnlyList<string> Options, IReadOnlyList<int> Conditions, IReadOnlySet<string> Names, IReadOnlySet<string> Codes);

    /// <summary>The structure of a written card text (its tags and <c>$Names</c>).</summary>
    public static Structure StructureOf(string written)
    {
        ArgumentNullException.ThrowIfNull(written);
        var options = new List<string>();
        var conditions = new List<int>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(written, @"<(/?[a-z0-9-]+)>|\$[A-Za-z][A-Za-z0-9]*"))
        {
            if (m.Value[0] == '$')
            {
                names.Add(m.Value);
                continue;
            }
            var tag = m.Groups[1].Value;
            if (tag.StartsWith("opt", StringComparison.Ordinal))
            {
                options.Add(tag);
            }
            else if (tag.StartsWith(IfTag, StringComparison.Ordinal) && int.TryParse(tag.AsSpan(IfTag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                conditions.Add(n);
            }
            else if ((tag.Length == 3 && tag[0] == 'x') || tag == "/c" || (tag.Length > 1 && tag[0] == 'c' && char.IsAsciiDigit(tag[1])))
            {
                codes.Add(tag);
            }
        }
        return new Structure(options, conditions, names, codes);
    }

    /// <summary>
    /// Why a translation of a block cannot stand for the original (<paramref name="original"/>, the block's bytes): null when it can.
    /// The option lines and the conditional passages must be the original's, in order (the menu chooses by line, the game counts the
    /// passages); the <c>$Names</c> and the other code bytes the same sets.
    /// </summary>
    public static string? Check(string translation, ReadOnlySpan<byte> original)
    {
        if (ToBytes(translation, out var problem) is null)
        {
            return problem;
        }
        var want = StructureOf(ToWritten(original));
        var got = StructureOf(translation);
        if (!got.Options.SequenceEqual(want.Options))
        {
            return $"its option lines ({string.Join(" ", got.Options.Select(o => "<" + o + ">"))}) are not the original's ({string.Join(" ", want.Options.Select(o => "<" + o + ">"))})";
        }
        if (!got.Conditions.SequenceEqual(want.Conditions))
        {
            return $"its conditional passages ({string.Join(" ", got.Conditions.Select(c => "<if-" + c + ">"))}) are not the original's ({string.Join(" ", want.Conditions.Select(c => "<if-" + c + ">"))}), in order";
        }
        if (!got.Names.SetEquals(want.Names))
        {
            return $"its $Names ({string.Join(" ", got.Names.Order(StringComparer.Ordinal))}) are not the original's ({string.Join(" ", want.Names.Order(StringComparer.Ordinal))})";
        }
        if (!got.Codes.SetEquals(want.Codes))
        {
            return $"its code bytes ({string.Join(" ", got.Codes.Order(StringComparer.Ordinal))}) are not the original's ({string.Join(" ", want.Codes.Order(StringComparer.Ordinal))})";
        }
        return null;
    }

    /// <summary>The option markers of a block in order (what the menu chooses by: their number and kinds must stay).</summary>
    public static IReadOnlyList<byte> Options(ReadOnlySpan<byte> raw)
    {
        var options = new List<byte>();
        for (var i = 0; i < raw.Length && raw[i] != 0; i++)
        {
            if (raw[i] is 0x01 or 0x13)
            {
                i++;
            }
            else if (OptionCodes.Contains(raw[i]))
            {
                options.Add(raw[i]);
            }
        }
        return options;
    }
}
