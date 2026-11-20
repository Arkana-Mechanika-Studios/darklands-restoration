using System.Text.Json;

namespace Darklands.Workshop.Text;

/// <summary>
/// The texts a translator marked "needs review" (W-0 polish), kept in the pack's <c>workshop.json</c>: the Workshop's own file,
/// which the game does not read.
/// </summary>
// Not in the original: the 1.1 Workshop.
public sealed class ReviewMarks
{
    /// <summary>The file in a pack's folder.</summary>
    public const string FileName = "workshop.json";

    private readonly HashSet<string> _keys;

    private ReviewMarks(IEnumerable<string> keys) => _keys = new HashSet<string>(keys, StringComparer.Ordinal);

    /// <summary>The marks of the pack in <paramref name="folder"/> (none when it has no file, or one that cannot be read).</summary>
    public static ReviewMarks Load(string folder)
    {
        try
        {
            var path = Path.Combine(folder, FileName);
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { Review: { } review })
            {
                return new ReviewMarks(review);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // the marks are a help: a file that cannot be read gives none
        }
        return new ReviewMarks([]);
    }

    /// <summary>Whether <paramref name="key"/> is marked.</summary>
    public bool Has(string key) => _keys.Contains(key);

    /// <summary>How many texts are marked.</summary>
    public int Count => _keys.Count;

    /// <summary>Marks or unmarks <paramref name="key"/>.</summary>
    public void Set(string key, bool marked)
    {
        if (marked ? _keys.Add(key) : _keys.Remove(key))
        {
            Dirty = true;
        }
    }

    /// <summary>Whether the marks changed since they were read or written.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Writes the marks into <paramref name="folder"/> (only when they changed).</summary>
    public void Save(string folder)
    {
        if (!Dirty)
        {
            return;
        }
        File.WriteAllText(Path.Combine(folder, FileName),
            JsonSerializer.Serialize(new Stored { Review = [.. _keys.Order(StringComparer.Ordinal)] }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Dirty = false;
    }

    private sealed class Stored
    {
        public List<string>? Review { get; set; }
    }
}
