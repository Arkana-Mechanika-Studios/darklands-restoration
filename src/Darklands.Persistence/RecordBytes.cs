using System.Text;

namespace Darklands.Persistence;

/// <summary>Little-endian fields and zero-ended texts of the original's records.</summary>
internal static class RecordBytes
{
    public static int Word(ReadOnlySpan<byte> r, int at) => (short)(r[at] | r[at + 1] << 8);

    public static int UWord(ReadOnlySpan<byte> r, int at) => r[at] | r[at + 1] << 8;

    public static void PutWord(Span<byte> r, int at, int value)
    {
        r[at] = (byte)value;
        r[at + 1] = (byte)(value >> 8);
    }

    /// <summary>The text up to the first zero byte, at most <paramref name="length"/> bytes.</summary>
    public static string Text(ReadOnlySpan<byte> r, int at, int length)
    {
        var field = r.Slice(at, length);
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }

    /// <summary>
    /// Writes a text into a fixed field: kept as it is when it already reads as
    /// <paramref name="text"/> (the bytes after the zero are whatever the original left there),
    /// otherwise the text (cut to leave room for its zero) and zeros to the end of the field.
    /// </summary>
    public static void PutText(Span<byte> r, int at, int length, string text)
    {
        if (Text(r, at, length) == text)
        {
            return;
        }
        var field = r.Slice(at, length);
        field.Clear();
        var bytes = Encoding.Latin1.GetBytes(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, length - 1)).CopyTo(field);
    }
}
