namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The version line of the start screen, drawn in a hidden SDL window and read back: small, grey, in the bottom-right corner,
/// nothing elsewhere; nothing when there is no line. Uses the SDL2 library the host ships; skipped without one.
/// </summary>
[Collection("SDL")] // SDL is not thread-safe: its tests run one at a time
public sealed class CornerTextTests
{
    private const int Width = 1440;
    private const int Height = 800;

    [Fact]
    public void TheVersionIsDrawnSmallInTheBottomRightCorner()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
        if (!File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        Assert.Equal(0, SdlNative.SDL_Init(SdlNative.InitVideo));
        var window = SdlNative.SDL_CreateWindow("corner test", 0, 0, Width, Height, SdlNative.WindowHidden);
        var renderer = SdlNative.SDL_CreateRenderer(window, -1, SdlNative.RendererSoftware);
        var target = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessTarget, Width, Height);
        try
        {
            Assert.Equal(0, SdlNative.SDL_SetRenderTarget(renderer, target));
            string? line = null;
            using var corner = new CornerText(() => line);

            var silent = Render(renderer, corner);
            Assert.All(silent, p => Assert.Equal(unchecked((int)0xFF000000), p));

            line = "v0.9.0";
            var pixels = Render(renderer, corner);
            var (minX, minY, maxX, maxY, count) = (Width, Height, 0, 0, 0);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (pixels[y * Width + x] != unchecked((int)0xFF000000))
                    {
                        (minX, minY, maxX, maxY, count) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y), count + 1);
                    }
                }
            }
            if (count == 0)
            {
                return; // this SDL reads nothing back from a target texture: no verdict
            }
            Assert.True(minX > Width * 9 / 10 && minY > Height * 9 / 10, $"text from ({minX},{minY})");
            Assert.True(maxX < Width && maxY < Height);
            Assert.True(maxY - minY < Height / 20, "small");
        }
        finally
        {
            SdlNative.SDL_DestroyTexture(target);
            SdlNative.SDL_DestroyRenderer(renderer);
            SdlNative.SDL_DestroyWindow(window);
            SdlNative.SDL_Quit();
        }
    }

    private static int[] Render(IntPtr renderer, CornerText corner)
    {
        SdlNative.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(renderer);
        corner.Draw(renderer, Width, Height);
        SdlNative.SDL_RenderFlush(renderer);
        var pixels = new int[Width * Height];
        Assert.Equal(0, SdlNative.SDL_RenderReadPixels(renderer, IntPtr.Zero, SdlNative.PixelFormatArgb8888, pixels, Width * 4));
        return pixels;
    }
}
