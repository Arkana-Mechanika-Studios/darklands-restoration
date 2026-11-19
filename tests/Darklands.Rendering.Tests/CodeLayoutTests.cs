using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The Unicode text path (L-6b): the card layout over codes is the byte layout's own loop, so every English block laid out from its
/// codes gives the bytes' lines, pixels, option areas and heights, whatever passages are shown, in the card window's font and in font 0
/// (whose control codes have widths).
/// </summary>
public sealed class CodeLayoutTests
{
    [Fact]
    public void EveryEnglishBlockLaysOutTheSameFromItsCodes()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        var blocks = OriginalTextExtractor.Extract(files).Entries.Where(e => e.Key.StartsWith("msg:", StringComparison.Ordinal)).ToList();
        Assert.True(blocks.Count > 3000);
        foreach (var slot in new[] { 3, 1 })
        {
            var painter = new TextPainter(assets.Font(slot));
            var layout = new CardTextLayout(painter);
            foreach (var (shown, n) in new[] { (true, 0), (false, 1), (true, 2) })
            {
                var conditions = Enumerable.Range(0, 12).Select(k => n == 2 ? k % 2 == 0 : shown).ToList();
                foreach (var entry in blocks)
                {
                    var raw = entry.Raw.ToArray();
                    var codes = TextCodes.OfBytes(raw);
                    var settings = new CardTextLayout.Settings
                    {
                        Conditions = conditions,
                        Color = 15,
                        OptionColor = 0x80,
                        OptionMode = TextMode.Xor,
                        DropCap = letter => Presentation.Graphics.DropCaps.Get(assets, letter),
                    };
                    var byBytes = new Canvas(new Screen());
                    var byCodes = new Canvas(new Screen());
                    var bytesAreas = layout.Draw(byBytes, raw, 67, 11, 230, settings);
                    var bytesEnd = layout.EndY;
                    var codesAreas = layout.Draw(byCodes, codes, 67, 11, 230, settings);
                    Assert.True(bytesAreas.SequenceEqual(codesAreas), $"{entry.Key} slot {slot}: option areas");
                    Assert.True(bytesEnd == layout.EndY, $"{entry.Key} slot {slot}: end y");
                    Assert.True(byBytes.Screen.Pixels.AsSpan().SequenceEqual(byCodes.Screen.Pixels), $"{entry.Key} slot {slot}: pixels");
                    Assert.True(layout.MeasureHeight(raw, 230, 0) == layout.MeasureHeight(codes, 230, 0), $"{entry.Key} slot {slot}: height");
                }
            }
        }
    }

    [Fact]
    public void ALetterTheGameCannotHoldIsDrawnAsAQuestionMarkWithoutAFontPack()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var painter = new TextPainter(new GameAssets(files).Font(1));
        var question = new Canvas(new Screen());
        painter.Draw(question, 5, 5, "Gar?on", 15);
        var cedilla = new Canvas(new Screen());
        painter.Draw(cedilla, 5, 5, "Garçon", 15);
        Assert.Equal(question.Screen.Pixels, cedilla.Screen.Pixels);
        Assert.Equal(painter.Measure("Gar?on"), painter.Measure("Garçon"));
    }

    [Fact]
    public void APackFontDrawsItsOwnLettersInTheScreensColour()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-font-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // a 2-pixel-wide 'ç' (a column of two pixels set at x 0, rows 0 and 2) and a 3-wide 'a'
            File.WriteAllText(Path.Combine(folder, "slot1.json"), """{ "image": "slot1.png", "height": 3, "spacing": 1, "glyphs": { "ç": [0, 0, 2], "a": [2, 0, 3] } }""");
            var alpha = new byte[5 * 3];
            alpha[0 * 5 + 0] = 255;
            alpha[2 * 5 + 0] = 255;
            alpha[1 * 5 + 3] = 255;
            var font = BitmapFont.Load(Path.Combine(folder, "slot1.json"), _ => (5, 3, alpha), out var problem);
            Assert.True(font is not null, problem);
            var painter = new TextPainter(GameFolder.Files is { } files ? new GameAssets(files).Font(1) : null!) { PackFont = font };
            if (GameFolder.Files is null)
            {
                return;
            }
            var canvas = new Canvas(new Screen());
            var end = painter.Draw(canvas, 10, 20, "ça", 9);
            Assert.Equal(10 + 3 + 4, end);
            Assert.Equal(9, canvas[10, 20]);
            Assert.Equal(9, canvas[10, 22]);
            Assert.Equal(0, canvas[10, 21]);
            Assert.Equal(9, canvas[10 + 3 + 1, 21]);
            Assert.Equal(7, painter.Measure("ça"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
