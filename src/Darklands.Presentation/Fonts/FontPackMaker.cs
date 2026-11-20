using System.Text;
using System.Text.Json;
using Darklands.Content;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Fonts;

/// <summary>
/// Writes a font pack in the game's own pixel style (docs/LOCALISATION.md, font packs): <c>fonts/slot1..3.png</c> and <c>.json</c>
/// made by <see cref="PixelFontBuilder"/> from the player's FONTS.FNT (its glyphs never leave the player's machine), and glyph charts
/// for judging the letters (<c>charts/slotN-&lt;script&gt;.png</c>, four times the pixels, with the letters in order in a .txt).
/// </summary>
// Not in the original: the 1.1 font packs.
public static class FontPackMaker
{
    private const int Columns = 32;

    /// <summary>Writes the pack's fonts and charts into <paramref name="folder"/>; the number of glyphs of each slot.</summary>
    public static IReadOnlyList<int> Make(GameFiles files, string folder, FontScripts scripts)
    {
        ArgumentNullException.ThrowIfNull(files);
        var assets = new GameAssets(files);
        var fonts = Path.Combine(folder, FontPack.Folder);
        var charts = Path.Combine(folder, "charts");
        Directory.CreateDirectory(fonts);
        Directory.CreateDirectory(charts);
        var counts = new List<int>();
        for (var slot = 1; slot <= 3; slot++)
        {
            var font = assets.Font(slot);
            var glyphs = PixelFontBuilder.Build(font, scripts);
            Write(fonts, slot, glyphs, font.CharacterHeight, font.CharacterSpacing, font.LineSpacing);
            foreach (var (name, codes) in ChartSets(glyphs, scripts))
            {
                Chart(Path.Combine(charts, $"slot{slot}-{name}"), glyphs, codes, font.CharacterHeight);
            }
            counts.Add(glyphs.Count);
        }
        return counts;
    }

    private static void Write(string folder, int slot, IReadOnlyDictionary<int, PixelGlyph> glyphs, int height, int spacing, int lineSpacing)
    {
        var ordered = glyphs.OrderBy(g => g.Key).ToList();
        var cell = ordered.Max(g => g.Value.Width) + 1;
        var rows = (ordered.Count + Columns - 1) / Columns;
        var (width, imageHeight) = (Columns * cell, rows * (height + 1));
        var argb = new uint[width * imageHeight];
        var boxes = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var (code, glyph) = ordered[i];
            var (x0, y0) = (i % Columns * cell, i / Columns * (height + 1));
            for (var y = 0; y < glyph.Height; y++)
            {
                for (var x = 0; x < glyph.Width; x++)
                {
                    if (glyph[x, y])
                    {
                        argb[(y0 + y) * width + x0 + x] = 0xFFFFFFFFu;
                    }
                }
            }
            boxes[char.ConvertFromUtf32(code)] = [x0, y0, glyph.Width];
        }
        PngWriter.WriteRgba(argb, width, imageHeight, Path.Combine(folder, $"slot{slot}.png"));
        var json = new Dictionary<string, object>
        {
            ["image"] = $"slot{slot}.png",
            ["height"] = height,
            ["spacing"] = spacing,
            ["lineSpacing"] = lineSpacing,
            ["glyphs"] = boxes,
        };
        File.WriteAllText(Path.Combine(folder, $"slot{slot}.json"),
            JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n",
            new UTF8Encoding(false));
    }

    private static IEnumerable<(string Name, List<int> Codes)> ChartSets(IReadOnlyDictionary<int, PixelGlyph> glyphs, FontScripts scripts)
    {
        List<int> In(Func<int, bool> pick) => [.. glyphs.Keys.Where(pick).Order()];
        yield return ("game", In(c => c < 0x80 || c is 'ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü' or 'ß'));
        if (scripts.HasFlag(FontScripts.Latin))
        {
            yield return ("latin", In(c => c is >= 0xA0 and < 0x0250 && c is not ('ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü' or 'ß') || c is >= 0x2000 and < 0x2100));
        }
        if (scripts.HasFlag(FontScripts.Greek))
        {
            yield return ("greek", In(c => c is >= 0x0370 and < 0x0400));
        }
        if (scripts.HasFlag(FontScripts.Cyrillic))
        {
            yield return ("cyrillic", In(c => c is >= 0x0400 and < 0x0500));
        }
    }

    // the glyphs at four times their pixels, 16 a row, dark on parchment with a light frame per cell; the letters in a .txt beside it
    private static void Chart(string path, IReadOnlyDictionary<int, PixelGlyph> glyphs, List<int> codes, int height)
    {
        const int Scale = 4;
        const int PerRow = 16;
        var cell = (glyphs.Values.Max(g => g.Width) + 2) * Scale;
        var cellHeight = (height + 2) * Scale;
        var rows = Math.Max(1, (codes.Count + PerRow - 1) / PerRow);
        var (width, imageHeight) = (PerRow * cell, rows * cellHeight);
        var argb = new uint[width * imageHeight];
        Array.Fill(argb, 0xFFE8DCC0u);
        var text = new StringBuilder();
        for (var i = 0; i < codes.Count; i++)
        {
            var glyph = glyphs[codes[i]];
            var (x0, y0) = (i % PerRow * cell, i / PerRow * cellHeight);
            for (var y = 0; y < cellHeight; y++)
            {
                argb[(y0 + y) * width + x0] = 0xFFC8B898u;
            }
            for (var x = 0; x < cell; x++)
            {
                argb[y0 * width + x0 + x] = 0xFFC8B898u;
            }
            for (var y = 0; y < glyph.Height; y++)
            {
                for (var x = 0; x < glyph.Width; x++)
                {
                    if (!glyph[x, y])
                    {
                        continue;
                    }
                    for (var dy = 0; dy < Scale; dy++)
                    {
                        for (var dx = 0; dx < Scale; dx++)
                        {
                            argb[(y0 + (y + 1) * Scale + dy) * width + x0 + (x + 1) * Scale + dx] = 0xFF201810u;
                        }
                    }
                }
            }
            text.Append(char.ConvertFromUtf32(codes[i])).Append(i % PerRow == PerRow - 1 ? '\n' : ' ');
        }
        PngWriter.Write(argb, width, imageHeight, path + ".png");
        File.WriteAllText(path + ".txt", text.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
    }
}
