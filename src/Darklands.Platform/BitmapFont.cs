using System.Globalization;
using System.Text.Json;

namespace Darklands.Platform;

/// <summary>
/// A font pack's bitmap font (docs/LOCALISATION.md, font packs): a picture of its glyphs and a JSON file of its metrics,
/// <c>fonts/slotN.json</c> for the game's font slot N (1..3):
/// <code>
/// { "image": "slot3.png", "height": 7, "lineSpacing": 1, "spacing": 1,
///   "glyphs": { "A": [0, 0, 5], "é": [6, 0, 4], ... } }
/// </code>
/// each glyph its x, y and width in the picture (its height the font's); a pixel is set where the picture's alpha is 128 or more.
/// The glyphs are drawn in the screen's colour, as the game's fonts are.
/// </summary>
// Not in the original: the 1.1 font packs (L-6b, set 1).
public sealed class BitmapFont : IFont
{
    private readonly Dictionary<int, (int X, int Y, int Width)> _glyphs;
    private readonly int _imageWidth;
    private readonly byte[] _alpha;
    private readonly int _spacing;

    private BitmapFont(int height, int lineSpacing, int spacing, Dictionary<int, (int, int, int)> glyphs, int imageWidth, byte[] alpha)
    {
        Height = height;
        LineHeight = height + lineSpacing;
        _spacing = spacing;
        _glyphs = glyphs;
        _imageWidth = imageWidth;
        _alpha = alpha;
    }

    public int Height { get; }

    public int LineHeight { get; }

    /// <summary>The codes the font has glyphs for.</summary>
    public IReadOnlyCollection<int> Codes => _glyphs.Keys;

    public bool Has(int code) => _glyphs.ContainsKey(code);

    public int Advance(int code) => _glyphs.TryGetValue(code, out var g) ? g.Width + _spacing : 0;

    public bool Pixel(int code, int x, int y)
    {
        if (!_glyphs.TryGetValue(code, out var g) || x < 0 || x >= g.Width || y < 0 || y >= Height)
        {
            return false;
        }
        var at = (g.Y + y) * _imageWidth + g.X + x;
        return at < _alpha.Length && _alpha[at] >= 128;
    }

    /// <summary>
    /// The font of <paramref name="json"/> (a <c>slotN.json</c>); its picture read by <paramref name="readImage"/> (width, height, one alpha byte a
    /// pixel) from beside it. Null with the reason in <paramref name="problem"/> when it cannot be used.
    /// </summary>
    public static BitmapFont? Load(string json, Func<string, (int Width, int Height, byte[] Alpha)> readImage, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(readImage);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(json), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            int Number(string name, int fallback) => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;
            var height = Number("height", 0);
            if (height is < 1 or > 64)
            {
                problem = $"{json}: \"height\" must be 1 to 64 pixels";
                return null;
            }
            if (!root.TryGetProperty("image", out var imageName) || imageName.GetString() is not { Length: > 0 } image)
            {
                problem = $"{json}: no \"image\"";
                return null;
            }
            var (width, imageHeight, alpha) = readImage(Path.Combine(Path.GetDirectoryName(json) ?? "", image));
            var glyphs = new Dictionary<int, (int, int, int)>();
            if (root.TryGetProperty("glyphs", out var list) && list.ValueKind == JsonValueKind.Object)
            {
                foreach (var glyph in list.EnumerateObject())
                {
                    var name = glyph.Name;
                    var code = name.Length > 0 ? char.ConvertToUtf32(name, 0) : -1;
                    if (code < 0 || name.Length != char.ConvertFromUtf32(code).Length)
                    {
                        problem = $"{json}: glyph \"{name}\" is not one character";
                        return null;
                    }
                    var box = glyph.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    if (box.Length != 3 || box[0] < 0 || box[1] < 0 || box[2] < 0 || box[0] + box[2] > width || box[1] + height > imageHeight)
                    {
                        problem = string.Create(CultureInfo.InvariantCulture, $"{json}: glyph \"{name}\" must be [x, y, width] inside the {width}x{imageHeight} picture");
                        return null;
                    }
                    glyphs[code] = (box[0], box[1], box[2]);
                }
            }
            problem = null;
            return new BitmapFont(height, Number("lineSpacing", 1), Number("spacing", 1), glyphs, width, alpha);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or InvalidDataException or ArgumentException)
        {
            problem = $"{json}: {e.Message}";
            return null;
        }
    }
}
