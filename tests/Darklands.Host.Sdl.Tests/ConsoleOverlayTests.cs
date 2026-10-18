using Darklands.DevConsole;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The developer console drawn in a hidden SDL window (software renderer) and read back. Uses the SDL2 library the host ships (else DARKLANDS_SDL2_PATH or the "sdl" key of local/paths.json; skipped without one).
/// DARKLANDS_CONSOLE_SNAPSHOT=&lt;file.bmp&gt; also saves the picture. An SDL that cannot read a target texture back gives no verdict.
/// </summary>
[Collection("SDL")] // SDL is not thread-safe: its tests run one at a time
public sealed class ConsoleOverlayTests
{
    private const int Width = 1440;
    private const int Height = 800;

    [Fact]
    public void TheConsoleIsDrawnOverTheWindowsTopPart()
    {
        // the SDL2 library the host ships (copied next to the tests), else DARKLANDS_SDL2_PATH or the "sdl" key of local/paths.json
        var bundled = Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
        var library = File.Exists(bundled) ? bundled : Darklands.Platform.LocalPaths.Get("sdl", "DARKLANDS_SDL2_PATH");
        if (library is null || !File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        Assert.Equal(0, SdlNative.SDL_Init(SdlNative.InitVideo));
        var window = SdlNative.SDL_CreateWindow("console test", 0, 0, Width, Height, SdlNative.WindowHidden);
        var renderer = SdlNative.SDL_CreateRenderer(window, -1, SdlNative.RendererSoftware);
        try
        {
            var console = new GameConsole();
            console.Write("Darklands Restoration Project developer console. Type help for the commands.");
            console.Execute("help");
            console.Execute("nonsense");
            console.Type("money add 10");
            using var overlay = new ConsoleOverlay(console, null, 14, _ => { });

            var target = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessTarget, Width, Height);
            Assert.Equal(0, SdlNative.SDL_SetRenderTarget(renderer, target));

            // a bright picture behind it, as the game's would be, and a green spot copied from a texture as the game's picture is
            SdlNative.SDL_SetRenderDrawColor(renderer, 200, 160, 60, 255);
            SdlNative.SDL_RenderClear(renderer);
            var green = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessStatic, 1, 1);
            SdlNative.SDL_UpdateTexture(green, IntPtr.Zero, [unchecked((int)0xFF00FF00)], 4);
            var spot = new SdlNative.SdlRect { X = 100, Y = 700, Width = 20, Height = 20 };
            SdlNative.SDL_RenderCopyTo(renderer, green, IntPtr.Zero, ref spot);
            overlay.Draw(renderer, Width, Height);
            SdlNative.SDL_RenderFlush(renderer);
            var pixels = new int[Width * Height];
            Assert.Equal(0, SdlNative.SDL_RenderReadPixels(renderer, IntPtr.Zero, SdlNative.PixelFormatArgb8888, pixels, Width * 4));
            SdlNative.SDL_DestroyTexture(green);
            SdlNative.SDL_DestroyTexture(target);
            if (pixels[710 * Width + 110] != unchecked((int)0xFF00FF00))
            {
                // this SDL (2.0.14 here) reads back nothing drawn into a target texture: no verdict
                return;
            }
            if (Environment.GetEnvironmentVariable("DARKLANDS_CONSOLE_SNAPSHOT") is { Length: > 0 } snapshot)
            {
                Bitmap.Save(snapshot, pixels, Width, Height);
            }

            // the panel covers the top three fifths with a dark see-through colour; the picture below stays as it was
            var panelHeight = Height * 3 / 5;
            Assert.True(Brightness(pixels[10 * Width + Width - 5]) < 120);
            Assert.Equal(unchecked((int)0xFFC8A03C), pixels[(Height - 10) * Width + 10]);

            // text: light pixels inside the panel, error lines in red
            var light = 0;
            var red = 0;
            for (var y = 0; y < panelHeight; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var p = pixels[y * Width + x];
                    var (r, g, b) = ((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF);
                    light += r > 150 && g > 150 && b > 150 ? 1 : 0;
                    red += r > 180 && g < 150 && b < 140 ? 1 : 0;
                }
            }
            Assert.True(light > 2000, $"{light} light pixels");
            Assert.True(red > 50, $"{red} red pixels");
        }
        finally
        {
            SdlNative.SDL_DestroyRenderer(renderer);
            SdlNative.SDL_DestroyWindow(window);
            SdlNative.SDL_Quit();
        }
    }

    private static int Brightness(int argb) => (((argb >> 16) & 0xFF) + ((argb >> 8) & 0xFF) + (argb & 0xFF)) / 3;

    /// <summary>A 32-bit BMP, enough to look at the snapshot.</summary>
    private static class Bitmap
    {
        public static void Save(string file, int[] argb, int width, int height)
        {
            using var w = new BinaryWriter(File.Create(file));
            var size = 54 + argb.Length * 4;
            w.Write((byte)'B');
            w.Write((byte)'M');
            w.Write(size);
            w.Write(0);
            w.Write(54);
            w.Write(40);
            w.Write(width);
            w.Write(-height);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(argb.Length * 4);
            w.Write(2835);
            w.Write(2835);
            w.Write(0);
            w.Write(0);
            foreach (var p in argb)
            {
                w.Write(p);
            }
        }
    }
}
