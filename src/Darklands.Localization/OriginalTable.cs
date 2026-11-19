using System.Collections.Frozen;

namespace Darklands.Localization;

/// <summary>
/// The <c>en-original</c> fallback: key to original text, supplied by the caller (built at run time from the
/// player's own game files; the repository ships none of it). The texts are plain strings, not Fluent source,
/// so characters such as braces in them are never interpreted.
/// </summary>
public sealed class OriginalTable
{
    private readonly FrozenDictionary<string, string> _texts;

    /// <summary>An empty table.</summary>
    public static OriginalTable Empty { get; } = new(Array.Empty<KeyValuePair<string, string>>());

    /// <summary>Builds the table; a later entry for the same key replaces an earlier one.</summary>
    public OriginalTable(IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        _texts = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Number of keys.</summary>
    public int Count => _texts.Count;

    /// <summary>All keys.</summary>
    public IEnumerable<string> Keys => _texts.Keys;

    /// <summary>The original text for <paramref name="key"/>, if known.</summary>
    public bool TryGet(string key, out string text)
    {
        if (_texts.TryGetValue(key, out var found))
        {
            text = found;
            return true;
        }

        text = string.Empty;
        return false;
    }
}
