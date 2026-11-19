using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// A pack's fonts (docs/LOCALISATION.md, font packs): <c>fonts/slot1.json</c> .. <c>fonts/slot3.json</c> with their pictures, each the
/// game's font slot it replaces (<see cref="BitmapFont"/>). In a translation pack, or a pack of its own (<c>pack.json</c> with
/// <c>"fonts": true</c>). Its fonts have their own metrics: they are used with a translation, never with Classic's English.
/// </summary>
// Not in the original: the 1.1 font packs (L-6b, set 1).
public static class FontPack
{
    /// <summary>
    /// How the program reads a font's picture (width, height, one alpha byte a pixel): set by the hosts (the PNG reader is theirs);
    /// null: a pack's fonts cannot be read and the game's are used.
    /// </summary>
    public static Func<string, (int Width, int Height, byte[] Alpha)>? ReadImage { get; set; }

    /// <summary>
    /// How the program reads a slot's TrueType / OpenType font (a <c>slotN.json</c> naming <c>"ttf"</c>): set by the hosts (the
    /// rasteriser is theirs, Rendering's TtfFont); null: such a font cannot be used and the game's is.
    /// </summary>
    public static Func<string, (IFont? Font, string? Problem)>? LoadScalable { get; set; }

    /// <summary>The folder of a pack's fonts.</summary>
    public const string Folder = "fonts";

    /// <summary>The fonts in <paramref name="pack"/>'s <c>fonts</c> folder by slot (none: empty); what is wrong goes to <paramref name="report"/>.</summary>
    public static IReadOnlyDictionary<int, IFont> Load(string pack, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(report);
        var fonts = new Dictionary<int, IFont>();
        var folder = Path.Combine(pack, Folder);
        if (!Directory.Exists(folder))
        {
            return fonts;
        }
        for (var slot = 1; slot <= 3; slot++)
        {
            var json = Path.Combine(folder, $"slot{slot}.json");
            if (!File.Exists(json))
            {
                continue;
            }
            if (NamesTrueType(json))
            {
                var (scalable, why) = LoadScalable?.Invoke(json) ?? (null, $"{json}: this program cannot draw TrueType fonts");
                if (scalable is not null)
                {
                    fonts[slot] = scalable;
                }
                else
                {
                    report($"font pack: {why}; the game's font {slot} is used");
                }
                continue;
            }
            if (ReadImage is not { } read)
            {
                report($"font pack: {json}: this program cannot read the fonts' pictures; the game's font {slot} is used");
                continue;
            }
            if (BitmapFont.Load(json, read, out var problem) is { } font)
            {
                fonts[slot] = font;
            }
            else
            {
                report($"font pack: {problem}; the game's font {slot} is used");
            }
        }
        return fonts;
    }

    // a slot's file names a TrueType / OpenType font ("ttf") rather than a picture
    private static bool NamesTrueType(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json),
                new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && document.RootElement.TryGetProperty("ttf", out _);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
