namespace Darklands.Platform;

/// <summary>
/// The texts behind the text plane of a frame (<see cref="Screen.Texels"/> where <see cref="Screen.Owners"/> is 0): one id per
/// text drawn, with the key it was resolved from when the screens asked for it by key (<c>exe:DS:2F71</c>, with its arguments),
/// interned for the whole program like <see cref="ScreenOwners"/>, so the ids mean the same in every copy of a frame. Id 0 is no
/// text. Kept only while <see cref="Recording"/> is on (the developer's inspector turns it on); otherwise nothing is kept.
/// </summary>
// Not in the original: a development aid (local post-1.0 design note, the dev-only inspector, step I-3). It never changes a pixel.
// The widest drawing of each text is kept too: the box a translation of the key must fit (decision 12, for L-7w).
public static class ScreenTextPlane
{
    /// <summary>A text: the words drawn, the key they came from (null: a literal or text of a game file) and its arguments.</summary>
    public readonly record struct Entry(string Text, string? Key, string? Arguments, int WidestDrawn);

    /// <summary>The most texts kept; later ones get no id (the plane says nothing for them).</summary>
    public const int Capacity = 1 << 16;

    private const int RememberCapacity = 1 << 14;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly List<Entry> Entries = [default];
    private static readonly Dictionary<string, (string Key, string? Arguments)> Keys = new(StringComparer.Ordinal);

    /// <summary>On: the screens' texts are kept for the plane (developer builds turn it on with the inspector).</summary>
    public static bool Recording { get; set; }

    /// <summary>
    /// <paramref name="shown"/>, the words of key <paramref name="key"/> (with <paramref name="arguments"/>), remembered so that its
    /// drawing is put down to the key; returns <paramref name="shown"/>.
    /// </summary>
    public static string Keyed(string shown, string key, string? arguments = null)
    {
        if (!Recording || shown.Length == 0 || key.Length == 0)
        {
            return shown;
        }
        lock (Gate)
        {
            if (Keys.Count >= RememberCapacity && !Keys.ContainsKey(shown))
            {
                Keys.Clear();
            }
            Keys[shown] = (key, arguments);
        }
        return shown;
    }

    /// <summary>The id of <paramref name="text"/> as drawn <paramref name="width"/> pixels wide (0: not recording, empty, or the table full).</summary>
    public static int Drawn(string text, int width)
    {
        if (!Recording || text.Length == 0)
        {
            return 0;
        }
        lock (Gate)
        {
            var (key, arguments) = Keys.TryGetValue(text, out var k) ? k : (null, null);
            var name = key is null ? "\u0001" + text : key + "\u0001" + arguments + "\u0001" + text;
            if (!Ids.TryGetValue(name, out var id))
            {
                if (Entries.Count >= Capacity)
                {
                    return 0;
                }
                id = Entries.Count;
                Entries.Add(new Entry(text, key, arguments, width));
                Ids[name] = id;
            }
            else if (Entries[id].WidestDrawn < width)
            {
                Entries[id] = Entries[id] with { WidestDrawn = width };
            }
            return id;
        }
    }

    /// <summary>The text of an id (null for 0 or an unknown id).</summary>
    public static Entry? Of(int id)
    {
        lock (Gate)
        {
            return id > 0 && id < Entries.Count ? Entries[id] : null;
        }
    }

    /// <summary>Every keyed text kept so far, with the widest it was drawn (the boxes of decision 12).</summary>
    public static IReadOnlyList<Entry> KeyedTexts()
    {
        lock (Gate)
        {
            return Entries.Where(e => e.Key is not null).ToList();
        }
    }
}
