using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// Screenshots of each picture filter for a person to judge, not a check (drawn by the GPU renderer's shader where it starts): with DARKLANDS_PICFILTER_SHOTS set to
/// "&lt;folder of 320x200 PNG frames&gt;;&lt;output folder&gt;" every frame is shown in a 1920x1080 window with each filter, read back
/// and saved as &lt;frame&gt;-&lt;filter&gt;.png, with &lt;frame&gt;-zoom.png: the same 480x300 piece of the window under each filter, side by side.
/// Without the variable it does nothing.
/// </summary>
[Collection("SDL")]
public sealed class PictureFilterShots
{
    [Fact]
    public void SaveTheFiltersOnRealFrames()
    {
        var setting = Environment.GetEnvironmentVariable("DARKLANDS_PICFILTER_SHOTS");
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (string.IsNullOrEmpty(setting) || !File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        var parts = setting.Split(';');
        var output = Directory.CreateDirectory(parts[1]).FullName;
        using var host = new SdlHost("filter shots", 1920, 1080, gpu: true);
        host.Pump();
        foreach (var file in Directory.GetFiles(parts[0], "*.png").Order())
        {
            var screen = ToScreen(PngCodec.Read(file));
            var name = Path.GetFileNameWithoutExtension(file);
            var pieces = new List<(uint[] Pixels, int Width)>();
            foreach (var filter in Enum.GetValues<PictureFilter>())
            {
                host.Filter = filter;
                host.Present(screen);
                var shown = host.ReadBack(out var w, out var h);
                var argb = shown.Select(p => (uint)p | 0xFF000000u).ToArray();
                PngWriter.Write(argb, w, h, Path.Combine(output, $"{name}-{PictureFilters.Name(filter)}.png"));
                var g = DisplayLayout.Graphics(w, h);
                // the CPU reference of the same filter, the picture's rectangle only (what a renderer must draw)
                var source = screen.Pixels.ToArray().Select(i => screen.Palette[i]).ToArray();
                var reference = PictureFilterReference.Render(source, Screen.Width, Screen.Height, filter, g.Width, g.Height);
                PngWriter.Write(reference, g.Width, g.Height,
                    Path.Combine(Directory.CreateDirectory(Path.Combine(output, "reference")).FullName, $"{name}-{PictureFilters.Name(filter)}.png"));
                pieces.Add((Crop(argb, w, g.X + g.Width / 2 - 240, g.Y + g.Height / 3, 480, 300), 480));
            }
            // the CRT with its curvature (display.crtCurvature)
            host.Filter = PictureFilter.Crt;
            host.Curvature = true;
            host.Present(screen);
            var curved = host.ReadBack(out var cw, out var ch).Select(p => (uint)p | 0xFF000000u).ToArray();
            PngWriter.Write(curved, cw, ch, Path.Combine(output, $"{name}-crt-curved.png"));
            host.Curvature = false;
            var zoom = new uint[pieces.Count * 488 * 300];
            for (var i = 0; i < pieces.Count; i++)
            {
                for (var y = 0; y < 300; y++)
                {
                    Array.Copy(pieces[i].Pixels, y * 480, zoom, y * pieces.Count * 488 + i * 488, 480);
                }
            }
            PngWriter.Write(zoom, pieces.Count * 488, 300, Path.Combine(output, $"{name}-zoom.png"));
        }
    }

    private static uint[] Crop(uint[] argb, int width, int x0, int y0, int w, int h)
    {
        var piece = new uint[w * h];
        for (var y = 0; y < h; y++)
        {
            Array.Copy(argb, (y0 + y) * width + x0, piece, y * w, w);
        }
        return piece;
    }

    /// <summary>A 320x200 picture as the game's screen: its colours (at most 256) become the palette.</summary>
    private static Screen ToScreen(RgbaImage image)
    {
        Assert.Equal((Screen.Width, Screen.Height), (image.Width, image.Height));
        var screen = new Screen();
        var index = new Dictionary<uint, byte>();
        for (var i = 0; i < image.Argb.Length; i++)
        {
            var colour = image.Argb[i] | 0xFF000000u;
            if (!index.TryGetValue(colour, out var n))
            {
                n = index[colour] = (byte)index.Count;
                screen.Palette[n] = colour;
            }
            screen.Pixels[i] = n;
        }
        return screen;
    }
}
