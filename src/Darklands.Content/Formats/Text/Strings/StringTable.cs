using System.Globalization;
using System.Text;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>Where a string of the original-text table came from.</summary>
public enum StringOrigin
{
    /// <summary>A table or record the program reads today (a message block, a record field, a pointer table).</summary>
    Known,

    /// <summary>A printable run found by the conservative scan of the EXE data; its use is not established.</summary>
    Scanned,
}

/// <summary>One string of the original game: its key, where it came from, its bytes and the text they decode to.</summary>
/// <param name="Key">The stable id (see <see cref="OriginalTextExtractor"/>); never depends on the wording.</param>
/// <param name="Origin">Known (read by the program) or scanned (found by the EXE scan).</param>
/// <param name="Raw">The game's own bytes, control and colour codes included, without the terminator.</param>
/// <param name="Text">The bytes as characters, one byte per character (Latin-1), as the rest of Content decodes game text.</param>
public sealed record StringEntry(string Key, StringOrigin Origin, byte[] Raw, string Text);

/// <summary>
/// Key to original text. Insertion order is kept (so two runs over the same files give the same sequence); a key is
/// added once, and adding it again is an error.
/// </summary>
public sealed class StringTable
{
    private readonly List<StringEntry> _entries = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<StringEntry> Entries => _entries;

    public int Count => _entries.Count;

    public bool Contains(string key) => _index.ContainsKey(key);

    public StringEntry? Find(string key) => _index.TryGetValue(key, out var at) ? _entries[at] : null;

    /// <summary>Adds an entry from the game's bytes.</summary>
    /// <exception cref="ArgumentException">The key is already present.</exception>
    public StringEntry Add(string key, StringOrigin origin, ReadOnlySpan<byte> raw)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A string key is required.", nameof(key));
        }
        if (_index.ContainsKey(key))
        {
            throw new ArgumentException($"Duplicate string key {key}.", nameof(key));
        }
        var bytes = raw.ToArray();
        var entry = new StringEntry(key, origin, bytes, Encoding.Latin1.GetString(bytes));
        _index[key] = _entries.Count;
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Adds an entry from text already decoded one byte per character (Latin-1).</summary>
    public StringEntry Add(string key, StringOrigin origin, string text) => Add(key, origin, Encoding.Latin1.GetBytes(text));

    /// <summary>The number of entries per key prefix (the part before the first ':'), in prefix order.</summary>
    public IReadOnlyDictionary<string, int> CountsByPrefix() =>
        _entries.GroupBy(e => e.Key[..e.Key.IndexOf(':')], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    /// <summary>
    /// Tab-separated dump for translators and developers: key, origin, text (tab, CR, LF and backslash escaped as
    /// \t \r \n \\, every other byte outside 0x20..0x7E as \xNN, so the file is plain ASCII).
    /// </summary>
    public void WriteTsv(TextWriter writer)
    {
        foreach (var e in _entries)
        {
            writer.Write(e.Key);
            writer.Write('\t');
            writer.Write(e.Origin == StringOrigin.Known ? "known" : "scanned");
            writer.Write('\t');
            foreach (var b in e.Raw)
            {
                switch (b)
                {
                    case (byte)'\t': writer.Write("\\t"); break;
                    case (byte)'\r': writer.Write("\\r"); break;
                    case (byte)'\n': writer.Write("\\n"); break;
                    case (byte)'\\': writer.Write("\\\\"); break;
                    case < 0x20 or > 0x7E: writer.Write("\\x" + b.ToString("X2", CultureInfo.InvariantCulture)); break;
                    default: writer.Write((char)b); break;
                }
            }
            writer.Write('\n');
        }
    }
}
