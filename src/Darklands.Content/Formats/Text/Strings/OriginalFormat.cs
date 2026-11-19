using System.Globalization;
using System.Text;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>
/// The original's <c>sprintf</c> on one of its own format strings (the MSC 6 run-time's conversions the game's formats use): the
/// screens' sentences keep the original's words, spacing and widths. A number argument is an int (a 16-bit <c>%d</c> / <c>%u</c> /
/// <c>%x</c> takes its low 16 bits, as the 16-bit program passes it), <c>%ld</c> / <c>%lu</c> take 32 bits; a <c>%s</c> argument is a
/// string (Unicode inside: GameTextCodec), <c>%c</c> a char or a number.
/// </summary>
// orig: _sprintf (MSC 6 run-time library, root 06A1) asm: flags - + space 0 #, width (or *), .precision, size l / h / F / N, conversions d i u x X o c s %
public static class OriginalFormat
{
    /// <summary>The text of <paramref name="format"/> with <paramref name="args"/> put in.</summary>
    public static string Format(string format, IReadOnlyList<object> args) => Run(format, args, null);

    /// <summary>
    /// What each conversion of <paramref name="format"/> prints with <paramref name="args"/> (its width and padding included): the
    /// arguments <c>$arg0</c>, <c>$arg1</c> ... of a translation of the format, one per conversion (a <c>*</c> width takes an argument
    /// but makes no piece).
    /// </summary>
    // Not in the original: the 1.1 translation packs (docs/LOCALISATION.md): a translation places the original's printed pieces.
    public static IReadOnlyList<string> Pieces(string format, IReadOnlyList<object> args)
    {
        var pieces = new List<string>();
        Run(format, args, pieces);
        return pieces;
    }

    /// <summary>The conversions of <paramref name="format"/> in order, as (start, length) in it (<c>%%</c> is none).</summary>
    // Not in the original: the translator's template writes each one as { $argN } (docs/LOCALISATION.md).
    public static IReadOnlyList<(int Start, int Length)> Conversions(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var found = new List<(int, int)>();
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                continue;
            }
            var start = i++;
            if (i < format.Length && format[i] == '%')
            {
                continue;
            }
            while (i < format.Length && "-+ 0#*.0123456789lhFN".Contains(format[i], StringComparison.Ordinal))
            {
                i++;
            }
            if (i < format.Length && "diuxXocs".Contains(format[i], StringComparison.Ordinal))
            {
                found.Add((start, i - start + 1));
            }
        }
        return found;
    }

    private static string Run(string format, IReadOnlyList<object> args, List<string>? pieces)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(args);
        var text = new StringBuilder();
        var next = 0;
        object Next() => next < args.Count ? args[next++] : throw new FormatException($"too few arguments for \"{format}\"");
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c != '%')
            {
                text.Append(c);
                continue;
            }
            if (++i >= format.Length)
            {
                break;
            }
            if (format[i] == '%')
            {
                text.Append('%');
                continue;
            }
            bool left = false, plus = false, space = false, zero = false, alternate = false;
            for (; i < format.Length && "-+ 0#".Contains(format[i], StringComparison.Ordinal); i++)
            {
                switch (format[i])
                {
                    case '-': left = true; break;
                    case '+': plus = true; break;
                    case ' ': space = true; break;
                    case '0': zero = true; break;
                    default: alternate = true; break;
                }
            }
            var width = 0;
            if (i < format.Length && format[i] == '*')
            {
                width = (int)Number(Next());
                if (width < 0)
                {
                    left = true;
                    width = -width;
                }
                i++;
            }
            for (; i < format.Length && char.IsAsciiDigit(format[i]); i++)
            {
                width = width * 10 + (format[i] - '0');
            }
            var precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                precision = 0;
                i++;
                if (i < format.Length && format[i] == '*')
                {
                    precision = (int)Number(Next());
                    i++;
                }
                for (; i < format.Length && char.IsAsciiDigit(format[i]); i++)
                {
                    precision = precision * 10 + (format[i] - '0');
                }
            }
            var longSize = false;
            for (; i < format.Length && "lhFN".Contains(format[i], StringComparison.Ordinal); i++)
            {
                longSize |= format[i] == 'l';
            }
            if (i >= format.Length)
            {
                break;
            }
            var conversion = format[i];
            var printedFrom = text.Length;
            string body;
            var sign = "";
            switch (conversion)
            {
                case 'd' or 'i':
                {
                    var v = longSize ? (int)Number(Next()) : (long)(short)Number(Next());
                    sign = v < 0 ? "-" : plus ? "+" : space ? " " : "";
                    body = Digits(Math.Abs(v).ToString(CultureInfo.InvariantCulture), precision);
                    break;
                }
                case 'u' or 'x' or 'X' or 'o':
                {
                    var raw = Number(Next());
                    ulong v = longSize ? (uint)raw : (ushort)raw;
                    body = conversion switch
                    {
                        'u' => v.ToString(CultureInfo.InvariantCulture),
                        'x' => v.ToString("x", CultureInfo.InvariantCulture),
                        'X' => v.ToString("X", CultureInfo.InvariantCulture),
                        _ => Convert.ToString((long)v, 8),
                    };
                    body = Digits(body, precision);
                    if (alternate && v != 0)
                    {
                        sign = conversion switch { 'x' => "0x", 'X' => "0X", _ => "" };
                        if (conversion == 'o' && !body.StartsWith('0'))
                        {
                            body = "0" + body;
                        }
                    }
                    break;
                }
                case 'c':
                    body = Next() is char ch ? ch.ToString() : ((char)(byte)Number(args[next - 1])).ToString();
                    zero = false;
                    break;
                case 's':
                {
                    var s = Convert.ToString(Next(), CultureInfo.InvariantCulture) ?? "";
                    body = precision >= 0 && precision < s.Length ? s[..precision] : s;
                    zero = false;
                    break;
                }
                default:
                    // an unknown conversion is printed as it is (the run-time prints the character)
                    text.Append(conversion);
                    continue;
            }
            var pad = width - sign.Length - body.Length;
            if (pad <= 0)
            {
                text.Append(sign).Append(body);
            }
            else if (left)
            {
                text.Append(sign).Append(body).Append(' ', pad);
            }
            else if (zero && precision < 0)
            {
                text.Append(sign).Append('0', pad).Append(body);
            }
            else
            {
                text.Append(' ', pad).Append(sign).Append(body);
            }
            pieces?.Add(text.ToString(printedFrom, text.Length - printedFrom));
        }
        return text.ToString();
    }

    private static long Number(object value) => value switch
    {
        char c => c,
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };

    // a precision is the least number of digits; precision 0 and value 0 print nothing
    private static string Digits(string digits, int precision) =>
        precision < 0 ? digits : precision == 0 && digits == "0" ? "" : digits.PadLeft(precision, '0');
}
