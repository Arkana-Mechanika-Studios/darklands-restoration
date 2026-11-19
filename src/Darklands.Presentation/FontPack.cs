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
        if (ReadImage is not { } read)
        {
            report($"font pack: {folder}: this program cannot read the fonts' pictures; the game's fonts are used");
            return fonts;
        }
        for (var slot = 1; slot <= 3; slot++)
        {
            var json = Path.Combine(folder, $"slot{slot}.json");
            if (!File.Exists(json))
            {
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
}
