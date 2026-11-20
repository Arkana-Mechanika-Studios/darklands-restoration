using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.Localization;

/// <summary>
/// Where and how one slot of a screen is drawn instead of the original's: at (<see cref="X"/>, <see cref="Y"/>) in game pixels (null:
/// where the original draws it), <see cref="Width"/> pixels wide (null: the room it had), on up to <see cref="Lines"/> lines, in font
/// slot <see cref="Font"/> (0: its own). Today a slot is a screen text (its key); the table's shape leaves room for other slots
/// (buttons, pictures, list areas) later.
/// </summary>
public sealed record SlotLayout(
    [property: JsonPropertyName("x")] int? X = null,
    [property: JsonPropertyName("y")] int? Y = null,
    [property: JsonPropertyName("width")] int? Width = null,
    [property: JsonPropertyName("lines")] int Lines = 1,
    [property: JsonPropertyName("font")] int Font = 0)
{
    /// <summary>Whether the layout changes nothing (the original's).</summary>
    [JsonIgnore]
    public bool IsOriginal => X is null && Y is null && Width is null && Lines <= 1 && Font == 0;
}

/// <summary>The limits a pack's layouts are checked against: the language packs' (strict), later a looser one for interface mods.</summary>
/// <param name="Name">How the check names it.</param>
/// <param name="MaxMove">How far a slot may move from the original's place, in game pixels each way (null: anywhere on the screen).</param>
/// <param name="MaxLines">The most lines a text may wrap onto.</param>
/// <param name="WithinRoom">Whether a slot must stay within the room its text had (the free space the original leaves it).</param>
public sealed record LayoutProfile(string Name, int? MaxMove, int MaxLines, bool WithinRoom)
{
    /// <summary>A translation pack's: at most 16 px from the original's place, 1–3 lines, within the room the text had.</summary>
    public static readonly LayoutProfile LanguagePack = new("language pack", 16, 3, true);
}

/// <summary>
/// The screen layouts of a pack (<c>interface/layout.json</c>, the folder the mod spec reserves for interface changes), by screen and
/// slot: <c>{"screens": {"BarterScreen": {"exe:DS:2FF2": {"y": 35, "width": 200, "lines": 2}}}}</c>. A translation pack uses it to give
/// its words room (a wider box where the screen has room, two or three lines, a narrower font of the pack, a small move); every other
/// slot, and every slot with no pack, is drawn exactly where and how the original draws it. The format holds plain places and sizes:
/// the limits are the check's (<see cref="LayoutProfile"/>).
/// </summary>
// Not in the original: the 1.1 translation packs (docs/LOCALISATION.md, screen layouts per language).
public static class SlotLayouts
{
    /// <summary>The file in a pack's folder.</summary>
    public const string FileName = "interface/layout.json";

    /// <summary>The screen's size in game pixels.</summary>
    public const int ScreenWidth = 320, ScreenHeight = 200;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private sealed record File([property: JsonPropertyName("screens")] Dictionary<string, Dictionary<string, SlotLayout>>? Screens);

    private static string PathIn(string folder) => Path.Combine(folder, "interface", "layout.json");

    /// <summary>The layouts of the pack in <paramref name="folder"/> by screen, then slot (empty: none, or a file that cannot be read, said in <paramref name="problem"/>).</summary>
    public static Dictionary<string, Dictionary<string, SlotLayout>> Load(string folder, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(folder);
        problem = null;
        var path = PathIn(folder);
        var none = new Dictionary<string, Dictionary<string, SlotLayout>>(StringComparer.Ordinal);
        if (!System.IO.File.Exists(path))
        {
            return none;
        }
        try
        {
            var file = JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path));
            foreach (var (screen, slots) in file?.Screens ?? [])
            {
                none[screen] = new Dictionary<string, SlotLayout>(slots, StringComparer.Ordinal);
            }
            return none;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            problem = $"{FileName}: {e.Message}; the original layout is used";
            return new Dictionary<string, Dictionary<string, SlotLayout>>(StringComparer.Ordinal);
        }
    }

    /// <summary>Writes <paramref name="layouts"/> (the original ones left out; none: the file removed).</summary>
    public static void Save(string folder, IReadOnlyDictionary<string, Dictionary<string, SlotLayout>> layouts)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(layouts);
        var path = PathIn(folder);
        var kept = new SortedDictionary<string, Dictionary<string, SlotLayout>>(StringComparer.Ordinal);
        foreach (var (screen, slots) in layouts)
        {
            var changed = slots.Where(s => !s.Value.IsOriginal).OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
            if (changed.Count > 0)
            {
                kept[screen] = changed;
            }
        }
        if (kept.Count == 0)
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new File(new Dictionary<string, Dictionary<string, SlotLayout>>(kept, StringComparer.Ordinal)), Options));
    }

    /// <summary>
    /// What is wrong with <paramref name="layouts"/> under <paramref name="profile"/>: a move past its limit (or a text whose place no
    /// session recorded, moved), lines past its limit, a font slot not 1–3, a width past the room the text has (its box in
    /// <paramref name="boxes"/>), off the screen, or over another text of the same screen (where the boxes have their places). Each:
    /// the screen, the slot and the reason.
    /// </summary>
    public static IReadOnlyList<(string Screen, string Slot, string Problem)> Validate(IReadOnlyDictionary<string, Dictionary<string, SlotLayout>> layouts,
        IReadOnlyDictionary<string, TextBox> boxes, LayoutProfile profile, int lineHeight = 9)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(profile);
        var problems = new List<(string, string, string)>();
        foreach (var (screen, slots) in layouts.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            foreach (var (slot, layout) in slots.OrderBy(l => l.Key, StringComparer.Ordinal))
            {
                void Add(string what) => problems.Add((screen, slot, what));
                if (layout.Lines < 1 || layout.Lines > profile.MaxLines)
                {
                    Add($"{layout.Lines} lines: 1 to {profile.MaxLines} in a {profile.Name}");
                }
                if (layout.Font is < 0 or > 3)
                {
                    Add($"font slot {layout.Font}: 1, 2 or 3 (0: the text's own)");
                }
                if (layout.Width is < 1)
                {
                    Add($"width {layout.Width}: at least 1 pixel");
                }
                boxes.TryGetValue(slot, out var box);
                if ((layout.X is not null || layout.Y is not null) && box is not { X: not null, Y: not null })
                {
                    Add("no session recorded where the original draws this text: it cannot be moved yet");
                    continue;
                }
                if (box is null)
                {
                    continue;
                }
                var (dx, dy) = ((layout.X ?? box.X ?? 0) - (box.X ?? 0), (layout.Y ?? box.Y ?? 0) - (box.Y ?? 0));
                if (profile.MaxMove is { } most && (Math.Abs(dx) > most || Math.Abs(dy) > most))
                {
                    Add($"moved by ({dx}, {dy}): at most {most} pixels each way in a {profile.Name}");
                }
                if (profile.WithinRoom && layout.Width is { } width && box.Room > 0 && width > box.Room - dx)
                {
                    Add($"{width} px wide: the text has {box.Room - dx} px of room {(box.Windowed ? "to its window's edge" : "to the screen's edge")}");
                }
                if (box.X is null || box.Y is null)
                {
                    continue;
                }
                var area = Area(box, layout, lineHeight);
                if (area.X < 0 || area.Y < 0 || area.X + area.Width > ScreenWidth || area.Y + area.Height > ScreenHeight)
                {
                    Add("it runs off the screen");
                }
                foreach (var other in boxes.Values)
                {
                    if (other.Key != slot && other.X is not null && other.Y is not null && other.Screens.Contains(screen, StringComparer.Ordinal)
                        && !Overlap(Area(box, null, lineHeight), Area(other, null, lineHeight))
                        && Overlap(area, Area(other, slots.GetValueOrDefault(other.Key), lineHeight)))
                    {
                        Add($"it overlaps {other.Key}");
                    }
                }
            }
        }
        return problems;
    }

    /// <summary>Where a text is drawn with <paramref name="layout"/>: at its place (else its box's), as wide as given (else as the English), its lines tall.</summary>
    public static (int X, int Y, int Width, int Height) Area(TextBox box, SlotLayout? layout, int lineHeight = 9)
    {
        ArgumentNullException.ThrowIfNull(box);
        layout ??= new SlotLayout();
        return (layout.X ?? box.X ?? 0, layout.Y ?? box.Y ?? 0, layout.Width ?? box.English, Math.Max(1, layout.Lines) * lineHeight - 1);
    }

    private static bool Overlap((int X, int Y, int Width, int Height) a, (int X, int Y, int Width, int Height) b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
