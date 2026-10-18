namespace Darklands.Content.Formats.Config;

/// <summary>
/// Decoded BANNER.DAT table.
/// </summary>
public sealed record Banner(IReadOnlyList<int> Offsets, IReadOnlyList<BannerEntry> Entries)
{
    public const int Current48307EntryCount = 20;

    public IReadOnlyList<BannerEntry> DisplayRows =>
        Entries.Where(entry => entry.Kind == BannerEntryKind.DisplayRow).ToArray();

    public IReadOnlyList<BannerEntry> Current48307StartupBannerRows =>
        Entries.Where(entry => entry.Index is >= 1 and <= 10).ToArray();

    public IReadOnlyList<BannerEntry> Diagnostics =>
        Entries.Where(entry => entry.Kind == BannerEntryKind.DiagnosticString).ToArray();
}

/// <summary>
/// One BANNER.DAT string entry with text and inline attribute tokens preserved; <see cref="Bytes"/> is the
/// entry as the game hands it to its text routines (code page 437, FF xx attribute escapes, up to the NUL).
/// </summary>
public sealed record BannerEntry(
    int Index,
    int Offset,
    BannerEntryKind Kind,
    IReadOnlyList<BannerToken> Tokens,
    bool HasNullTerminator,
    IReadOnlyList<byte> Bytes)
{
    public string Text => string.Concat(Tokens.OfType<BannerTextToken>().Select(token => token.Text));

    public string DisplayText => StripDosTerminators(Text);

    public IReadOnlyList<byte> AttributeChanges =>
        Tokens.OfType<BannerAttributeToken>().Select(token => token.Attribute).ToArray();

    public byte? InitialAttribute => Tokens.FirstOrDefault() is BannerAttributeToken attribute
        ? attribute.Attribute
        : null;

    private static string StripDosTerminators(string text)
    {
        return text.EndsWith("\r\n$", StringComparison.Ordinal)
            ? text[..^3]
            : text.EndsWith('$') ? text[..^1] : text;
    }
}

/// <summary>
/// Current interpretation of a BANNER.DAT entry.
/// </summary>
public enum BannerEntryKind
{
    DisplayRow,
    DiagnosticString
}

/// <summary>
/// Token emitted by the BANNER.DAT parser.
/// </summary>
public abstract record BannerToken;

public sealed record BannerTextToken(string Text) : BannerToken;

public sealed record BannerAttributeToken(byte Attribute) : BannerToken;

