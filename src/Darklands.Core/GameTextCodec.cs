namespace Darklands.Core;

/// <summary>
/// The game's text bytes ↔ Unicode. The original's fonts draw bytes 0x00..0x7F; seven of them are German letters on ASCII's places
/// (0x1F ä, 0x7B ö, 0x7C ü, 0x5B Ä, 0x5C Ö, 0x5D Ü, 0x5F ß), and the bytes from 0x80 are colour codes. Inside the program text is
/// Unicode ("Schöff", not "Sch{ff"); the bytes are made only where text is drawn or written to the game's files.
/// <see cref="Encode(string)"/> of <see cref="Decode(ReadOnlySpan{byte})"/> gives every byte back, so drawn pixels and saved games
/// do not change.
/// </summary>
// Not in the original: the original keeps its text as bytes; this map is the meaning its fonts give them (local design note L-3).
public static class GameTextCodec
{
    /// <summary>The byte for a character the game cannot show.</summary>
    public const byte Unshowable = (byte)'?';

    /// <summary>Private-use characters that stand for a byte whose Latin-1 character is one of the seven letters (a colour code).</summary>
    private const char ByteAsIs = '';

    private static readonly (byte Byte, char Letter)[] Letters =
    [
        (0x1F, 'ä'), (0x7B, 'ö'), (0x7C, 'ü'), (0x5B, 'Ä'), (0x5C, 'Ö'), (0x5D, 'Ü'), (0x5F, 'ß'),
    ];

    private static readonly char[] ToCharTable = BuildToChar();

    private static char[] BuildToChar()
    {
        var table = new char[256];
        for (var b = 0; b < 256; b++)
        {
            table[b] = (char)b;
        }
        foreach (var (code, letter) in Letters)
        {
            table[code] = letter;
            // the byte whose Latin-1 character is the letter (a colour code) keeps a character of its own
            table[letter] = (char)(ByteAsIs + letter);
        }
        return table;
    }

    /// <summary>The character a game byte stands for.</summary>
    public static char ToChar(byte b) => ToCharTable[b];

    /// <summary>The game byte of a character: the letter's byte, a byte given as itself, or <see cref="Unshowable"/>.</summary>
    public static byte ToByte(char c)
    {
        foreach (var (code, letter) in Letters)
        {
            if (c == letter)
            {
                return code;
            }
        }
        if (c is >= ByteAsIs and <= (char)(ByteAsIs + 0xFF))
        {
            return (byte)(c - ByteAsIs);
        }
        // ASCII and Latin-1 (control and colour codes) stay their byte, the font's lost ASCII characters ([ \ ] _ { |) too
        return c <= 0xFF ? (byte)c : Unshowable;
    }

    /// <summary>Game bytes as text.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = ToCharTable[bytes[i]];
        }
        return new string(chars);
    }

    /// <summary>Text as game bytes, one byte per character.</summary>
    public static byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = ToByte(text[i]);
        }
        return bytes;
    }
}
