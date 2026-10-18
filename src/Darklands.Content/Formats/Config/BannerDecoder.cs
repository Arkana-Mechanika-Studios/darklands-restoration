namespace Darklands.Content.Formats.Config;

/// <summary>
/// Decoder for BANNER.DAT offset table entries and inline color tokens.
/// </summary>
public sealed class BannerDecoder
{
    public const string DefaultResourceName = "BANNER.DAT";

    /// <summary>
    /// Decodes a BANNER.DAT stream into the current banner table model.
    /// </summary>
    /// <remarks>
    /// This is format provenance. Engine startup code records where BANNER.DAT is opened.
    /// </remarks>
    public Banner Decode(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Decode(copy.ToArray());
    }

    /// <summary>
    /// Decodes BANNER.DAT offset-table entries and inline attribute/text tokens.
    /// </summary>
    /// <remarks>
    /// This method models the file format contract; it is not itself an original engine routine.
    /// </remarks>
    public Banner Decode(ReadOnlySpan<byte> bytes)
    {
        var entryCount = Banner.Current48307EntryCount;
        var headerLength = entryCount * 2;
        if (bytes.Length < headerLength)
        {
            throw new BannerDecodeException($"BANNER.DAT is too short for the {entryCount}-entry offset table.");
        }

        var offsets = new int[entryCount];
        for (var i = 0; i < offsets.Length; i++)
        {
            // The first 20 words are offsets to NUL-terminated strings.
            offsets[i] = bytes[i * 2] | (bytes[(i * 2) + 1] << 8);
            if (offsets[i] < headerLength || offsets[i] >= bytes.Length)
            {
                throw new BannerDecodeException($"BANNER.DAT entry {i} points outside the file: 0x{offsets[i]:X4}.");
            }

            if (i > 0 && offsets[i] <= offsets[i - 1])
            {
                throw new BannerDecodeException("BANNER.DAT offsets must be strictly increasing.");
            }
        }

        var entries = new BannerEntry[entryCount];
        for (var i = 0; i < entries.Length; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Length ? offsets[i + 1] : bytes.Length;
            entries[i] = DecodeEntry(i, start, bytes[start..end]);
        }

        return new Banner(offsets, entries);
    }

    private static BannerEntry DecodeEntry(int index, int offset, ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        var hasNullTerminator = terminator >= 0;
        var payload = hasNullTerminator ? bytes[..terminator] : bytes;
        var tokens = DecodeTokens(payload);
        var kind = index <= 13 ? BannerEntryKind.DisplayRow : BannerEntryKind.DiagnosticString;
        return new BannerEntry(index, offset, kind, tokens, hasNullTerminator, payload.ToArray());
    }

    private static IReadOnlyList<BannerToken> DecodeTokens(ReadOnlySpan<byte> bytes)
    {
        var tokens = new List<BannerToken>();
        var text = new List<char>();

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0xFF)
            {
                // FF changes the following text attribute instead of producing text.
                FlushText(tokens, text);
                if (i + 1 >= bytes.Length)
                {
                    throw new BannerDecodeException("BANNER.DAT attribute escape FF is missing its attribute byte.");
                }

                tokens.Add(new BannerAttributeToken(bytes[++i]));
                continue;
            }

            text.Add(DecodeCp437(bytes[i]));
        }

        FlushText(tokens, text);
        return tokens;
    }

    private static void FlushText(List<BannerToken> tokens, List<char> text)
    {
        if (text.Count == 0)
        {
            return;
        }

        tokens.Add(new BannerTextToken(new string(text.ToArray())));
        text.Clear();
    }

    private static char DecodeCp437(byte value)
    {
        if (value is >= 0x20 and <= 0x7E)
        {
            return (char)value;
        }

        return value switch
        {
            0x09 => '\t',
            0x0A => '\n',
            0x0D => '\r',
            0xB3 => '\u2502',
            0xBA => '\u2551',
            0xBB => '\u2557',
            0xBC => '\u255D',
            0xC8 => '\u255A',
            0xC9 => '\u2554',
            0xCD => '\u2550',
            0xD9 => '\u2518',
            0xDA => '\u250C',
            0xBF => '\u2510',
            0xC0 => '\u2514',
            0xC4 => '\u2500',
            0xB0 => '\u2591',
            0xB1 => '\u2592',
            0xB2 => '\u2593',
            _ => '\uFFFD'
        };
    }
}

/// <summary>
/// Raised when BANNER.DAT violates the current decoded table contract.
/// </summary>
public sealed class BannerDecodeException : Exception
{
    public BannerDecodeException(string message)
        : base(message)
    {
    }
}

