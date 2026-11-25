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
    /// <summary>
    /// A text: the words drawn, the key they came from (null: a literal or text of a game file) and its arguments; the widest it was
    /// drawn, the least room it had (<see cref="NoRoom"/>: not known), whether that room was a window's (else the screen's right edge),
    /// the font slot it was drawn in (0: not known), and the arguments by name (Fluent's: <c>arg0</c> ...) of its widest drawing.
    /// </summary>
    public readonly record struct Entry(string Text, string? Key, string? Arguments, int WidestDrawn, int Room = NoRoom, bool Windowed = false,
        int Font = 0, IReadOnlyList<KeyValuePair<string, string>>? Values = null)
    {
        /// <summary>Where the text was first drawn (null: not said): a translation pack's layout moves it from there.</summary>
        public (int X, int Y)? At { get; init; }

        /// <summary>The screens the text was drawn on (<see cref="ScreenOf"/>; empty when not asked).</summary>
        public IReadOnlyCollection<string> Screens { get; init; } = [];
    }

    /// <summary>
    /// Names the screen a text is being drawn on (a developer build sets it: Presentation's ScreenNames); null: screens are not recorded.
    /// Asked for a text's first draws only.
    /// </summary>
    public static Func<string?>? ScreenOf { get; set; }

    private const int ScreenSamples = 24;

    private static readonly Dictionary<int, (HashSet<string> Screens, int Samples)> ScreensOf = [];

    /// <summary>The room of a text drawn with no known room.</summary>
    public const int NoRoom = int.MaxValue;

    /// <summary>The most texts kept; later ones get no id (the plane says nothing for them).</summary>
    public const int Capacity = 1 << 16;

    private const int RememberCapacity = 1 << 14;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly List<Entry> Entries = [default];
    private static readonly Dictionary<string, (string Key, string? Arguments, IReadOnlyList<KeyValuePair<string, string>>? Values)> Keys = new(StringComparer.Ordinal);

    /// <summary>On: the screens' texts are kept for the plane (developer builds turn it on with the inspector).</summary>
    public static bool Recording { get; set; }

    /// <summary>
    /// <paramref name="shown"/>, the words of key <paramref name="key"/> (with <paramref name="arguments"/>, and by name
    /// <paramref name="values"/>), remembered so that its drawing is put down to the key; returns <paramref name="shown"/>.
    /// </summary>
    public static string Keyed(string shown, string key, string? arguments = null, IReadOnlyList<KeyValuePair<string, string>>? values = null)
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
            Keys[shown] = (key, arguments, values);
        }
        return shown;
    }

    /// <summary>
    /// The id of <paramref name="text"/> as drawn <paramref name="width"/> pixels wide (0: not recording, empty, or the table full), with
    /// <paramref name="room"/> pixels for it (a window's when <paramref name="windowed"/>) in font slot <paramref name="font"/>.
    /// </summary>
    public static int Drawn(string text, int width, int room = NoRoom, bool windowed = false, int font = 0, (int X, int Y)? at = null)
    {
        if (!Recording || text.Length == 0)
        {
            return 0;
        }
        lock (Gate)
        {
            var (key, arguments, values) = Keys.TryGetValue(text, out var k) ? k : (null, null, null);
            var name = key is null ? "\u0001" + text : key + "\u0001" + arguments + "\u0001" + text;
            if (!Ids.TryGetValue(name, out var id))
            {
                if (Entries.Count >= Capacity)
                {
                    return 0;
                }
                id = Entries.Count;
                Entries.Add(new Entry(text, key, arguments, width, room, windowed, font, values) { At = at });
                Ids[name] = id;
            }
            else
            {
                var e = Entries[id];
                if (e.WidestDrawn < width || room < e.Room || e.Font == 0 || e.At is null && at is not null)
                {
                    Entries[id] = e with
                    {
                        WidestDrawn = Math.Max(e.WidestDrawn, width),
                        Room = Math.Min(e.Room, room),
                        Windowed = room < e.Room ? windowed : e.Windowed,
                        Font = e.Font == 0 ? font : e.Font,
                        At = e.At ?? at,
                    };
                }
            }
            if (ScreenOf is { } screenOf)
            {
                if (!ScreensOf.TryGetValue(id, out var seen))
                {
                    ScreensOf[id] = seen = (new HashSet<string>(StringComparer.Ordinal), 0);
                }
                if (seen.Samples < ScreenSamples)
                {
                    if (screenOf() is { } screen)
                    {
                        seen.Screens.Add(screen);
                    }
                    ScreensOf[id] = (seen.Screens, seen.Samples + 1);
                }
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
    public static IReadOnlyList<Entry> KeyedTexts() => [.. AllTexts().Where(e => e.Key is not null)];

    /// <summary>Every text kept so far, keyed or not (with its screens): the untranslated-text audit looks at those with no key.</summary>
    public static IReadOnlyList<Entry> AllTexts()
    {
        lock (Gate)
        {
            return Entries.Skip(1).Select((e, i) => ScreensOf.TryGetValue(i + 1, out var s) ? e with { Screens = [.. s.Screens.Order(StringComparer.Ordinal)] } : e)
                .ToList();
        }
    }
}
