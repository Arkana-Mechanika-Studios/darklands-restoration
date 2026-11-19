using System.Collections.Concurrent;
using System.Text;

namespace Darklands.Localization;

/// <summary>Why a key was reported.</summary>
public enum MissingKind
{
    /// <summary>The active locale has no entry, but the original table does: the original text was shown.</summary>
    Untranslated,

    /// <summary>Neither the active locale nor the original table knows the key: the key itself was shown.</summary>
    Unknown,
}

/// <summary>One reported key and how often it was asked for.</summary>
public readonly record struct MissingKeyEntry(string Key, MissingKind Kind, int Count);

/// <summary>Thread-safe record of keys that fell back to the original text or were not found at all.</summary>
public sealed class MissingKeyLog
{
    private readonly ConcurrentDictionary<string, (MissingKind Kind, int Count)> _entries = new(StringComparer.Ordinal);

    /// <summary>Number of distinct keys reported.</summary>
    public int Count => _entries.Count;

    /// <summary>Notes one request that missed.</summary>
    public void Record(string key, MissingKind kind) =>
        _entries.AddOrUpdate(key, _ => (kind, 1), (_, old) => (kind, old.Count + 1));

    /// <summary>The reported keys, ordered by key.</summary>
    public IReadOnlyList<MissingKeyEntry> Snapshot()
    {
        var list = new List<MissingKeyEntry>(_entries.Count);
        foreach (var pair in _entries)
        {
            list.Add(new MissingKeyEntry(pair.Key, pair.Value.Kind, pair.Value.Count));
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }

    /// <summary>Forgets everything reported so far.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>A plain-text report: one line per key, kind, count and key separated by tabs.</summary>
    public string ToReport()
    {
        var sb = new StringBuilder();
        foreach (var e in Snapshot())
        {
            sb.Append(e.Kind).Append('\t').Append(e.Count).Append('\t').Append(e.Key).Append('\n');
        }

        return sb.ToString();
    }
}
