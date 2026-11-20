using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.Localization;

/// <summary>
/// The box of one keyed text, as the screens drew it in recorded sessions (L-7w): the widest the English was drawn, the least room
/// it had (to a window's last column when <see cref="Windowed"/>, else to the screen's right edge), the font slot, and the
/// arguments of its widest drawing by their Fluent names (to measure a translation the same way).
/// </summary>
public sealed record TextBox(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("english")] int English,
    [property: JsonPropertyName("room")] int Room,
    [property: JsonPropertyName("windowed")] bool Windowed,
    [property: JsonPropertyName("font")] int Font,
    [property: JsonPropertyName("args")] IReadOnlyDictionary<string, string>? Arguments)
{
    /// <summary>The screens the text was drawn on (the translators' editor groups by them; empty: not recorded).</summary>
    [JsonPropertyName("screens")]
    public IReadOnlyList<string> Screens { get; init; } = [];
}

/// <summary>
/// The boxes file (<c>boxes.json</c>, beside a translator's template): keys and numbers (and the arguments a session drew), never
/// the game's words. Made by a developer build's runs of recorded sessions (the text plane, I-3) and merged: the widest English,
/// the least room.
/// </summary>
// Not in the original: the 1.1 translation packs (docs/LOCALISATION.md, L-7w).
public static class TextBoxes
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record File(
        [property: JsonPropertyName("base")] string Base,
        [property: JsonPropertyName("boxes")] IReadOnlyList<TextBox> Boxes);

    /// <summary>The boxes of <paramref name="boxes"/> merged by key: the widest English (with its arguments), the least room (and its kind), the first font.</summary>
    public static IReadOnlyList<TextBox> Merge(IEnumerable<TextBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var byKey = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        foreach (var b in boxes)
        {
            if (!byKey.TryGetValue(b.Key, out var was))
            {
                byKey[b.Key] = b;
                continue;
            }
            var wider = b.English > was.English;
            var tighter = b.Room < was.Room;
            byKey[b.Key] = was with
            {
                Screens = [.. was.Screens.Union(b.Screens, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                English = wider ? b.English : was.English,
                Arguments = wider ? b.Arguments : was.Arguments,
                Room = tighter ? b.Room : was.Room,
                Windowed = tighter ? b.Windowed : was.Windowed,
                Font = was.Font != 0 ? was.Font : b.Font,
            };
        }
        return [.. byKey.Values.OrderBy(b => b.Key, StringComparer.Ordinal)];
    }

    /// <summary>Writes <paramref name="boxes"/> (merged) to <paramref name="path"/>.</summary>
    public static void Write(string path, IEnumerable<TextBox> boxes) =>
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new File("483.07", Merge(boxes)), Options));

    /// <summary>The boxes of a file written by <see cref="Write"/>; of a folder, every <c>*.json</c> in it merged.</summary>
    public static IReadOnlyList<TextBox> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Order(StringComparer.Ordinal).ToArray() : [path];
        return Merge(files.SelectMany(f => JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(f), Options)?.Boxes ?? []));
    }
}
