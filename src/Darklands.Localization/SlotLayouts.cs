using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.Localization;

/// <summary>
/// One slot of a screen. Of kind <see cref="SlotKinds.Text"/> (the default, the 1.1 slots): where and how a screen text (its key) is
/// drawn instead of the original's: at (<see cref="X"/>, <see cref="Y"/>) in game pixels (null: where the original draws it),
/// <see cref="Width"/> pixels wide (null: the room it had), on up to <see cref="Lines"/> lines, in font slot <see cref="Font"/> (0: its
/// own). Of kind <see cref="SlotKinds.Area"/>: a hit rectangle a view draws and tests (a row, a button), at (X, Y), Width by
/// <see cref="Height"/>. Of kind <see cref="SlotKinds.List"/>: a list of <see cref="Rows"/> rows of <see cref="RowHeight"/> pixels,
/// at (X, Y), Width wide, scrolled by the view. An area or a list is declared by a view (the 1.2 view registry: its drawing and its hit
/// tests read the same entry, so moving one moves the other); the classic screens declare none.
/// </summary>
public sealed record SlotLayout(
    [property: JsonPropertyName("x")] int? X = null,
    [property: JsonPropertyName("y")] int? Y = null,
    [property: JsonPropertyName("width")] int? Width = null,
    [property: JsonPropertyName("lines")] int Lines = 1,
    [property: JsonPropertyName("font")] int Font = 0,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("height")] int? Height = null,
    [property: JsonPropertyName("rows")] int? Rows = null,
    [property: JsonPropertyName("rowHeight")] int? RowHeight = null)
{
    /// <summary>The slot's kind: <see cref="SlotKinds.Text"/> when none is given.</summary>
    [JsonIgnore]
    public string KindOrText => Kind ?? SlotKinds.Text;

    /// <summary>Whether this is a screen text's layout (the 1.1 kind).</summary>
    [JsonIgnore]
    public bool IsText => KindOrText == SlotKinds.Text;

    /// <summary>Whether the layout changes nothing (a text where the original draws it; an area or a list is a declaration, never original).</summary>
    [JsonIgnore]
    public bool IsOriginal => IsText && X is null && Y is null && Width is null && Lines <= 1 && Font == 0 && Height is null && Rows is null && RowHeight is null;

    /// <summary>An area's or a list's rectangle in game pixels (a list: its rows' height together); null for a text or a slot missing a field.</summary>
    [JsonIgnore]
    public (int X, int Y, int Width, int Height)? Rectangle => KindOrText switch
    {
        SlotKinds.Area when X is { } x && Y is { } y && Width is { } w && Height is { } h => (x, y, w, h),
        SlotKinds.List when X is { } x && Y is { } y && Width is { } w && Rows is { } n && RowHeight is { } r => (x, y, w, n * r),
        _ => null,
    };

    /// <summary>The list row at game pixel (<paramref name="px"/>, <paramref name="py"/>), 0 the first (−1: off the list, or not a list).</summary>
    public int RowAt(int px, int py) =>
        KindOrText == SlotKinds.List && Rectangle is var (x, y, w, h) && RowHeight is > 0 and var r && px >= x && px < x + w && py >= y && py < y + h ? (py - y) / r : -1;

    /// <summary>Whether game pixel (<paramref name="px"/>, <paramref name="py"/>) is inside an area's or a list's rectangle.</summary>
    public bool Contains(int px, int py) => Rectangle is var (x, y, w, h) && px >= x && px < x + w && py >= y && py < y + h;
}

/// <summary>The kinds of slot (<see cref="SlotLayout.Kind"/>).</summary>
public static class SlotKinds
{
    /// <summary>A screen text, by its key (1.1).</summary>
    public const string Text = "text";

    /// <summary>A hit rectangle: a row, a button (1.2).</summary>
    public const string Area = "area";

    /// <summary>Rows of equal height, scrolled (1.2).</summary>
    public const string List = "list";
}

/// <summary>The limits a pack's layouts are checked against: the language packs' (strict), and the interface mods' (looser).</summary>
/// <param name="Name">How the check names it.</param>
/// <param name="MaxMove">How far a slot may move from the original's place, in game pixels each way (null: anywhere on the screen).</param>
/// <param name="MaxLines">The most lines a text may wrap onto.</param>
/// <param name="WithinRoom">Whether a slot must stay within the room its text had (the free space the original leaves it).</param>
/// <param name="Areas">Whether it may declare areas and lists (<see cref="SlotKinds"/>), or only lay out texts.</param>
public sealed record LayoutProfile(string Name, int? MaxMove, int MaxLines, bool WithinRoom, bool Areas = false)
{
    /// <summary>A translation pack's: texts only, at most 16 px from the original's place, 1–3 lines, within the room the text had.</summary>
    public static readonly LayoutProfile LanguagePack = new("language pack", 16, 3, true);

    /// <summary>An interface mod's (1.3, against the 1.2 view registry): texts anywhere on the screen, areas and lists.</summary>
    public static readonly LayoutProfile InterfaceMod = new("interface mod", null, 3, false, Areas: true);
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

    /// <summary>How a problem that is a warning starts: the layout may cover another text of the screen (if both are drawn at once).</summary>
    public const string MayCover = "it may cover";

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
                if (!layout.IsText)
                {
                    foreach (var problem in AreaProblems(layout, profile))
                    {
                        Add(problem);
                    }
                    if (boxes.ContainsKey(slot))
                    {
                        // the slots of a screen share one set of names: an area under a text's key would be taken for the text's layout
                        Add($"an {layout.KindOrText} is named like a text of the screen ({slot}): give it a name of its own");
                    }
                    continue;
                }
                if (layout.Height is not null || layout.Rows is not null || layout.RowHeight is not null)
                {
                    Add("height, rows and rowHeight are an area's or a list's: a text has lines");
                }
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
                        // a text of the same screen, not always drawn with it (another page, a menu's): a warning, not an error
                        Add($"{MayCover} {other.Key} if drawn at the same time");
                    }
                }
            }
        }
        return problems;
    }

    // an area's or a list's own checks: a kind the profile allows, its fields, on the screen
    private static IEnumerable<string> AreaProblems(SlotLayout layout, LayoutProfile profile)
    {
        var kind = layout.KindOrText;
        if (kind is not (SlotKinds.Area or SlotKinds.List))
        {
            yield return $"kind \"{kind}\": {SlotKinds.Text}, {SlotKinds.Area} or {SlotKinds.List}";
            yield break;
        }
        if (!profile.Areas)
        {
            yield return $"an {kind} is for interface views, not a {profile.Name} (it lays out texts only)";
            yield break;
        }
        if (layout.X is null || layout.Y is null || layout.Width is null)
        {
            yield return $"an {kind} needs x, y and width";
        }
        if (layout.Width is < 1)
        {
            yield return $"width {layout.Width}: at least 1 pixel";
        }
        if (kind == SlotKinds.Area && layout.Height is not >= 1)
        {
            yield return "an area needs a height of at least 1 pixel";
        }
        if (kind == SlotKinds.List && (layout.Rows is not >= 1 || layout.RowHeight is not >= 1))
        {
            yield return "a list needs rows and a rowHeight of at least 1";
        }
        if (layout.Lines != 1 || layout.Font != 0)
        {
            yield return "lines and font are a text's";
        }
        if (layout.Rectangle is var (x, y, w, h) && (x < 0 || y < 0 || x + w > ScreenWidth || y + h > ScreenHeight))
        {
            yield return "it runs off the screen";
        }
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
