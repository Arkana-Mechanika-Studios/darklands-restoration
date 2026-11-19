using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// A font pack's TrueType font (L-6b set 2): whole-pixel metrics and 1x glyphs for the game's frame, and the text layer that draws the
/// glyphs the frame shows again at the output's resolution; a frame without such glyphs composes as before.
/// </summary>
public sealed class TextLayerTests
{
    private static readonly string Font = Path.Combine(RepoRoot(), "src", "Darklands.Host.Sdl", "Resources", "DejaVuSansMono.ttf");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Darklands.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? ".";
    }

    private static TtfFont Load(string folder, double size)
    {
        File.Copy(Font, Path.Combine(folder, "mono.ttf"));
        File.WriteAllText(Path.Combine(folder, "slot1.json"), $$"""{ "ttf": "mono.ttf", "size": {{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "spacing": 0 }""");
        var font = TtfFont.Load(Path.Combine(folder, "slot1.json"), out var problem);
        Assert.True(font is not null, problem);
        return font!;
    }

    private static T InTemp<T>(Func<string, T> run)
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-ttf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            return run(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ATrueTypeFontHasWholePixelMetricsAndInkedGlyphs()
    {
        InTemp(folder =>
        {
            var font = Load(folder, 8);
            Assert.InRange(font.Height, 7, 10);
            Assert.True(font.Has('A') && font.Has('é') && font.Has('Ж') && font.Has(' '));
            Assert.False(font.Has(0x1F600));   // no emoji in DejaVu Sans Mono
            Assert.Equal(font.Advance('A'), font.Advance('é'));   // a monospaced font
            Assert.True(font.Advance('A') > 0);
            Assert.Contains(Enumerable.Range(0, font.Advance('A') * font.Height), i => font.Pixel('A', i % font.Advance('A'), i / font.Advance('A')));
            Assert.Contains(Enumerable.Range(0, font.Advance('.') * font.Height), i => font.Pixel('.', i % font.Advance('.'), i / font.Advance('.')));
            var (_, _, w, h, coverage) = font.Raster('A', 4, 4);
            Assert.True(w > 8 && h > 16);
            Assert.Contains(coverage, c => c == 255);
            return 0;
        });
    }

    [Fact]
    public void TheLayerDrawsTheFramesGlyphsAgainOverTheirBackground()
    {
        InTemp(folder =>
        {
            var font = Load(folder, 8);
            var screen = new Screen();
            screen.Palette[5] = 0xFF203040u;
            screen.Palette[15] = 0xFFFFFFFFu;
            var canvas = new Canvas(screen);
            canvas.Fill(0, 0, Screen.Width, Screen.Height, 5);
            var painter = new TextPainter(null!) { PackFont = font };
            painter.DrawCodes(canvas, 20, 30, [.. "Hé".Select(c => (int)c)], 15);
            var glyphPixels = Enumerable.Range(0, screen.Texels.Length).Where(i => screen.Texels[i] <= -2).ToList();
            Assert.NotEmpty(glyphPixels);
            var renderer = new HdRenderer(HdPack.Empty);
            var output = new DisplayRectangle(0, 0, 4 * Screen.Width, 4 * Screen.Height);
            var frame = renderer.Render(screen, output);
            Assert.Equal(2, frame.Items.Count);
            var picture = FrameCompositor.Compose(frame, output.Width, output.Height);
            // the text's colour where the glyphs are fully inked, only the background and the text's shades elsewhere in the box
            Assert.Contains(picture.Argb, p => p == 0xFFFFFFFFu);
            var hard = 0;
            foreach (var at in glyphPixels)
            {
                var (x, y) = (at % Screen.Width, at / Screen.Width);
                // a 1x glyph pixel's 4x4 cell is never left whole in the text's colour at its corners unless the outline covers them
                var cell = Enumerable.Range(0, 16).Select(k => picture.Argb[(4 * y + k / 4) * picture.Width + 4 * x + k % 4]).ToList();
                hard += cell.All(p => p == 0xFFFFFFFFu) ? 1 : 0;
            }
            Assert.True(hard < glyphPixels.Count, "the 1x pixels show through unchanged");
            // drawn over: the glyphs are gone from the frame, and from the layer
            canvas.Fill(0, 0, Screen.Width, Screen.Height, 5);
            Assert.Single(renderer.Render(screen, output).Items);
            return 0;
        });
    }

    [Fact]
    public void AFrameWithoutScalableGlyphsComposesAsBefore()
    {
        var screen = new Screen();
        for (var i = 0; i < screen.Pixels.Length; i++)
        {
            screen.Pixels[i] = (byte)(i * 7);
            screen.Palette[i & 0xFF] = 0xFF000000u | (uint)(i * 2654435761u >> 8 & 0xFFFFFF);
        }
        var output = new DisplayRectangle(0, 0, 640, 400);
        var frame = new HdRenderer(HdPack.Empty).Render(screen, output);
        Assert.Single(frame.Items);
        var picture = FrameCompositor.Compose(frame, 640, 400);
        for (var y = 0; y < 400; y += 37)
        {
            for (var x = 0; x < 640; x += 29)
            {
                Assert.Equal(screen.Palette[screen.Pixels[y / 2 * Screen.Width + x / 2]] | 0xFF000000u, picture.Argb[y * 640 + x]);
            }
        }
    }
}
