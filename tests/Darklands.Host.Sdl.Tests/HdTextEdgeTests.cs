using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// A font pack's text in a real SDL window at sizes that are not whole multiples of 320x200 and with each picture filter: the
/// frame's own 1x glyphs must not show beside the HD letters (a Discord report, 1.1.1: thin ticks of the low-res letters at the
/// HD letters' edges). Red text on grey: wherever the text layer draws nothing, the window must show grey. Uses the SDL3 library
/// the host ships; skipped without one.
/// With DARKLANDS_HDTEXT_SHOTS set to "&lt;font pack folder&gt;;&lt;output folder&gt;" a second test saves the window, read back, for
/// a person to look at (three pieces of text like the report's, at two sizes, with each filter). Without it it does nothing.
/// </summary>
[Collection("SDL")]
public sealed class HdTextEdgeTests
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

    [Theory]
    [InlineData(1440, 800, PictureFilter.Sharp, false)]   // 1280x800: a whole multiple
    [InlineData(1366, 768, PictureFilter.Sharp, false)]
    [InlineData(1013, 571, PictureFilter.Sharp, false)]
    [InlineData(1920, 1080, PictureFilter.Sharp, false)]
    [InlineData(1000, 700, PictureFilter.Sharp, false)]
    [InlineData(1366, 768, PictureFilter.Sharp, true)]
    [InlineData(1366, 768, PictureFilter.Even, true)]
    [InlineData(1013, 571, PictureFilter.Even, true)]
    [InlineData(1366, 768, PictureFilter.Hq, true)]
    [InlineData(1366, 768, PictureFilter.Soft, true)]
    [InlineData(1013, 571, PictureFilter.Soft, true)]
    public void NoPixelOfTheLowResLettersShowsBesideTheHdLetters(int width, int height, PictureFilter filter, bool gpu)
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        var folder = Path.Combine(Path.GetTempPath(), "dl-hdtext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(Font, Path.Combine(folder, "mono.ttf"));
            File.WriteAllText(Path.Combine(folder, "slot1.json"), """{ "ttf": "mono.ttf", "size": 8, "spacing": 0 }""");
            var font = TtfFont.Load(Path.Combine(folder, "slot1.json"), out var problem);
            Assert.True(font is not null, problem);
            var screen = new Screen();
            screen.Palette[5] = 0xFF808080u;
            screen.Palette[15] = 0xFFFF0000u;
            var canvas = new Canvas(screen);
            canvas.Fill(0, 0, Screen.Width, Screen.Height, 5);
            var painter = new TextPainter(null!) { PackFont = font };
            // lines at every offset of a few pixels, so their edges fall on every kind of boundary between window pixels
            for (var line = 0; line < 14; line++)
            {
                painter.DrawCodes(canvas, 3 + line % 7, 4 + line * 13, [.. "Autosave: battle 42 99 lAo".Select(c => (int)c)], 15);
            }

            using var host = new SdlHost("hd text test", width, height, gpu: gpu);
            host.Renderer = new HdRenderer(HdPack.Empty);
            host.Filter = filter;
            for (var i = 0; i < 5; i++)
            {
                host.Pump();
            }
            host.Present(screen);
            var shown = host.ReadBack(out var rw, out var rh);
            var graphics = DisplayLayout.Graphics(rw, rh);
            var layer = new HdRenderer(HdPack.Empty).Render(screen, graphics).Items[^1].Texture;
            Assert.Equal((graphics.Width, graphics.Height), (layer.Width, layer.Height));
            var leaks = new List<string>();
            var count = 0;
            for (var y = 0; y < graphics.Height; y++)
            {
                for (var x = 0; x < graphics.Width; x++)
                {
                    if (layer.Argb[y * layer.Width + x] >> 24 != 0)
                    {
                        continue;   // the HD letters and their boxes: drawn by the layer
                    }
                    var p = shown[(graphics.Y + y) * rw + graphics.X + x];
                    var (r, g) = (p >> 16 & 0xFF, p >> 8 & 0xFF);
                    if (r - g > 24)
                    {
                        count++;
                        if (leaks.Count < 8)
                        {
                            leaks.Add($"({x},{y}) {p & 0xFFFFFF:X6}");
                        }
                    }
                }
            }
            Assert.True(count == 0, $"{count} window pixels show the low-res letters' colour beside the HD letters: {string.Join(", ", leaks)}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SaveTheTextAtSizesThatAreNotWhole()
    {
        var setting = Environment.GetEnvironmentVariable("DARKLANDS_HDTEXT_SHOTS");
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (string.IsNullOrEmpty(setting) || !File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        var parts = setting.Split(';');
        var output = Directory.CreateDirectory(parts[1]).FullName;
        var mono = TtfFont.Load(Path.Combine(parts[0], "fonts", "slot2.json"), out var p1)!;
        var serif = TtfFont.Load(Path.Combine(parts[0], "fonts", "slot3.json"), out var p2) ?? mono;
        Assert.True(mono is not null, p1 + p2);
        var screen = new Screen();
        screen.Palette[1] = 0xFFA8A8A8u;
        screen.Palette[2] = 0xFF989898u;
        screen.Palette[3] = 0xFF242490u;
        screen.Palette[4] = 0xFF000000u;
        screen.Palette[6] = 0xFF00006Cu;
        screen.Palette[7] = 0xFFFFFFFFu;
        screen.Palette[8] = 0xFFD8B0A0u;
        screen.Palette[9] = 0xFFC09888u;
        screen.Palette[10] = 0xFFE4E4E4u;
        var canvas = new Canvas(screen);
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                screen.Pixels[y * Screen.Width + x] = (byte)(y < 70 ? ((x * 7 + y * 13) % 5 < 2 ? 2 : 1) : y < 110 ? 6 : ((x / 3 + y / 4) % 3 == 0 ? 9 : 8));
            }
        }
        var painter = new TextPainter(null!) { PackFont = mono };
        // the saved-games list's lines: a pale shadow a pixel down and right, then the line (LoadGameScreen.Shadowed)
        foreach (var (y, text, colour) in new (int Y, string Text, byte Colour)[] { (12, "Autosave: battle", 3), (28, "Autosave", 4), (44, "Autosave: battle", 4) })
        {
            painter.DrawCodes(canvas, 10, y + 1, [.. text.Select(c => (int)c)], 10);
            painter.DrawCodes(canvas, 9, y, [.. text.Select(c => (int)c)], colour);
        }
        painter.DrawCodes(canvas, 12, 86, [.. "42  42  99".Select(c => (int)c)], 7);
        painter = new TextPainter(null!) { PackFont = serif };
        painter.DrawCodes(canvas, 9, 130, [.. "Offering some refreshment".Select(c => (int)c)], 4);
        painter.DrawCodes(canvas, 9, 146, [.. "reason together.\"".Select(c => (int)c)], 4);
        foreach (var (w, h) in new[] { (1366, 768), (1013, 571) })
        {
            using var host = new SdlHost("hd text shots", w, h, gpu: true);
            host.Renderer = new HdRenderer(HdPack.Empty);
            host.Pump();
            foreach (var filter in new[] { PictureFilter.Sharp, PictureFilter.Even, PictureFilter.Hq, PictureFilter.Soft })
            {
                host.Filter = filter;
                host.Present(screen);
                var shown = host.ReadBack(out var rw, out var rh).Select(p => (uint)p | 0xFF000000u).ToArray();
                PngWriter.Write(shown, rw, rh, Path.Combine(output, $"{w}x{h}-{PictureFilters.Name(filter)}.png"));
            }
        }
    }
}
