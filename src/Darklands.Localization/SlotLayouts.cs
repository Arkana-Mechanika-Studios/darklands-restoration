using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.Localization;

/// <summary>
/// A pack's change to where one screen text is drawn: moved by (<see cref="Dx"/>, <see cref="Dy"/>) pixels (within
/// <see cref="SlotLayouts.MaxMove"/>), given <see cref="Width"/> pixels (null: the room it had), wrapped onto up to
/// <see cref="Lines"/> lines, in font slot <see cref="Font"/> (0: its own).
/// </summary>
public sealed record SlotLayout(
    [property: JsonPropertyName("dx")] int Dx = 0,
    [property: JsonPropertyName("dy")] int Dy = 0,
    [property: JsonPropertyName("width")] int? Width = null,
    [property: JsonPropertyName("lines")] int Lines = 1,
    [property: JsonPropertyName("font")] int Font = 0)
{
    /// <summary>Whether the layout changes nothing (the original's).</summary>
    [JsonIgnore]
    public bool IsOriginal => Dx == 0 && Dy == 0 && Width is null && Lines <= 1 && Font == 0;
}

/// <summary>
/// A translation pack's screen layouts (<c>layout.json</c>): for the screen texts it names, a wider box where the screen has room, two
/// or three lines, a narrower font of the pack, or a small move. Every other text, and every text with no pack, is drawn exactly where
/// and how the original draws it.
/// </summary>
// Not in the original: the 1.1 translation packs (docs/LOCALISATION.md, screen layouts per language).
public static class SlotLayouts
{
    /// <summary>The file in a pack's folder.</summary>
    public const string FileName = "layout.json";

    /// <summary>How far a text may be moved, in game pixels each way.</summary>
    public const int MaxMove = 16;

    /// <summary>The most lines a screen text may wrap onto.</summary>
    public const int MaxLines = 3;

    /// <summary>The screen's size in game pixels.</summary>
    public const int ScreenWidth = 320, ScreenHeight = 200;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private sealed record File([property: JsonPropertyName("slots")] Dictionary<string, SlotLayout>? Slots);

    /// <summary>The layouts of the pack in <paramref name="folder"/> (empty: none, or a file that cannot be read, said in <paramref name="problem"/>).</summary>
    public static IReadOnlyDictionary<string, SlotLayout> Load(string folder, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(folder);
        problem = null;
        var path = Path.Combine(folder, FileName);
        if (!System.IO.File.Exists(path))
        {
            return new Dictionary<string, SlotLayout>(StringComparer.Ordinal);
        }
        try
        {
            var file = JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path));
            return new Dictionary<string, SlotLayout>(file?.Slots ?? [], StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            problem = $"{FileName}: {e.Message}; the original layout is used";
            return new Dictionary<string, SlotLayout>(StringComparer.Ordinal);
        }
    }

    /// <summary>Writes <paramref name="layouts"/> (the original ones left out; none: the file removed).</summary>
    public static void Save(string folder, IReadOnlyDictionary<string, SlotLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(layouts);
        var path = Path.Combine(folder, FileName);
        var kept = layouts.Where(l => !l.Value.IsOriginal).OrderBy(l => l.Key, StringComparer.Ordinal).ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        if (kept.Count == 0)
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
            return;
        }
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new File(kept), Options));
    }

    /// <summary>
    /// What is wrong with <paramref name="layouts"/>: a move past <see cref="MaxMove"/>, lines past <see cref="MaxLines"/>, a font slot not
    /// 1–3, a width past the room the text has (its box in <paramref name="boxes"/>), off the screen, or over another text of the same
    /// screen (where the boxes have their places). Each: the key and the reason.
    /// </summary>
    public static IReadOnlyList<(string Key, string Problem)> Validate(IReadOnlyDictionary<string, SlotLayout> layouts, IReadOnlyDictionary<string, TextBox> boxes, int lineHeight = 9)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(boxes);
        var problems = new List<(string, string)>();
        foreach (var (key, layout) in layouts.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            if (Math.Abs(layout.Dx) > MaxMove || Math.Abs(layout.Dy) > MaxMove)
            {
                problems.Add((key, $"moved by ({layout.Dx}, {layout.Dy}): at most {MaxMove} pixels each way"));
            }
            if (layout.Lines is < 1 or > MaxLines)
            {
                problems.Add((key, $"{layout.Lines} lines: 1 to {MaxLines}"));
            }
            if (layout.Font is < 0 or > 3)
            {
                problems.Add((key, $"font slot {layout.Font}: 1, 2 or 3 (0: the text's own)"));
            }
            if (layout.Width is < 1)
            {
                problems.Add((key, $"width {layout.Width}: at least 1 pixel"));
            }
            if (!boxes.TryGetValue(key, out var box))
            {
                continue;
            }
            if (layout.Width is { } width && box.Room > 0 && width > box.Room - layout.Dx)
            {
                problems.Add((key, $"{width} px wide: the text has {box.Room - layout.Dx} px of room {(box.Windowed ? "to its window's edge" : "to the screen's edge")}"));
            }
            if (box.X is null || box.Y is null)
            {
                continue;
            }
            var area = Area(box, layout, lineHeight);
            if (area.X < 0 || area.Y < 0 || area.X + area.Width > ScreenWidth || area.Y + area.Height > ScreenHeight)
            {
                problems.Add((key, "it runs off the screen"));
            }
            foreach (var other in boxes.Values)
            {
                if (other.Key != key && other.X is not null && other.Y is not null && other.Screens.Intersect(box.Screens, StringComparer.Ordinal).Any()
                    && !Overlap(Area(box, null, lineHeight), Area(other, null, lineHeight))
                    && Overlap(area, Area(other, layouts.GetValueOrDefault(other.Key), lineHeight)))
                {
                    problems.Add((key, $"it overlaps {other.Key}"));
                }
            }
        }
        return problems;
    }

    /// <summary>Where a text is drawn with <paramref name="layout"/>: its box moved, as wide as given (else as the English), its lines tall.</summary>
    public static (int X, int Y, int Width, int Height) Area(TextBox box, SlotLayout? layout, int lineHeight = 9)
    {
        ArgumentNullException.ThrowIfNull(box);
        layout ??= new SlotLayout();
        return ((box.X ?? 0) + layout.Dx, (box.Y ?? 0) + layout.Dy, layout.Width ?? box.English, Math.Max(1, layout.Lines) * lineHeight - 1);
    }

    private static bool Overlap((int X, int Y, int Width, int Height) a, (int X, int Y, int Width, int Height) b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
