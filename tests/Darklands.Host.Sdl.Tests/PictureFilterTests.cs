using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The picture filters in a real SDL window (read back from the renderer), on both ways of drawing them: SDL's GPU renderer
/// with the shader (gpu: true; on Windows, Direct3D 12) and SDL's own renderer choice with render targets and blend modes (and
/// its software renderer). Sharp is the default and draws hard pixels as before; every filter draws what the CPU reference
/// (PictureFilterReference) draws; none of them touches the game's frame (the screen the screenshots, the problem reports and
/// the oracles take). Uses the SDL3 library the host ships; skipped without one.
/// </summary>
[Collection("SDL")]
public sealed class PictureFilterTests
{
    private static bool HaveSdl()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library))
        {
            return false;
        }
        SdlNative.ConfigureLibrary(library);
        return true;
    }

    /// <summary>A picture of two colours in 2x2 checks (as the game's dithering), a solid band and a ramp of greys.</summary>
    private static Screen Picture()
    {
        var screen = new Screen();
        screen.Palette[1] = 0xFFC08040;
        screen.Palette[2] = 0xFF204060;
        screen.Palette[3] = 0xFF808080;
        for (var i = 0; i < 64; i++)
        {
            screen.Palette[16 + i] = 0xFF000000u | (uint)(i * 4) * 0x010101u;
        }
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                screen.Pixels[y * Screen.Width + x] = (byte)(y < 100 ? ((x / 2 + y / 2) % 2 == 0 ? 1 : 2) : y < 170 ? 3 : 16 + x * 64 / Screen.Width);
            }
        }
        return screen;
    }

    private static int[] Shown(SdlHost host, Screen screen, PictureFilter filter, out int width, out int height)
    {
        host.Filter = filter;
        host.Present(screen);
        return host.ReadBack(out width, out height);
    }

    private static SdlHost Open(int width, int height, bool hd, bool gpu = false, string? driver = null)
    {
        if (driver is not null)
        {
            // read when the renderer is made; SDL_Quit (the host's Dispose) clears it again
            SdlNative.SDL_SetHint(SdlNative.HintRenderDriver, driver);
        }
        var host = new SdlHost("filter test", width, height, gpu: gpu);
        if (gpu && OperatingSystem.IsWindows())
        {
            // Windows has Direct3D 12 and the program its DXIL shader: the GPU renderer starts and draws the filters itself
            Assert.Equal("gpu", host.RendererName);
            Assert.Equal("shaders", host.FilterKind);
        }
        else if (!gpu)
        {
            Assert.Equal("render targets", host.FilterKind);
        }
        if (hd)
        {
            host.Renderer = new HdRenderer(HdPack.Empty);
        }
        host.Pump();
        return host;
    }

    [Fact]
    public void SharpIsTheDefault()
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(720, 400, hd: false);
        Assert.Equal(PictureFilter.Sharp, host.Filter);
        Assert.False(host.Curvature);
    }

    [Theory]
    [InlineData(1920, 1080, false, false, null)]
    [InlineData(1920, 1080, true, false, null)]
    [InlineData(1013, 571, false, false, null)]
    [InlineData(1920, 1080, false, false, "software")]
    [InlineData(1920, 1080, false, true, null)]
    [InlineData(1920, 1080, true, true, null)]
    public void SharpShowsOnlyThePicturesOwnColours(int width, int height, bool hd, bool gpu, string? driver)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd, gpu, driver);
        var screen = Picture();
        var shown = Shown(host, screen, PictureFilter.Sharp, out var rw, out var rh);
        var graphics = DisplayLayout.Graphics(rw, rh);
        var colours = screen.Palette.Select(c => unchecked((int)c)).ToHashSet();
        foreach (var (x, y) in Inside(graphics))
        {
            Assert.Contains(shown[y * rw + x], colours);
        }
    }

    [Theory]
    [InlineData(1440, 800, false, false)]   // the picture 1280x800: 4x
    [InlineData(1440, 800, true, false)]
    [InlineData(2160, 1200, false, false)]  // 1920x1200: 6x
    [InlineData(1440, 800, false, true)]
    [InlineData(1440, 800, true, true)]
    public void SmoothIsSharpAtWholeMultiples(int width, int height, bool hd, bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd, gpu);
        var screen = Picture();
        var sharp = Shown(host, screen, PictureFilter.Sharp, out _, out _);
        var smooth = Shown(host, screen, PictureFilter.Smooth, out _, out _);
        Assert.Equal(sharp, smooth);
    }

    /// <summary>
    /// The renderer draws each filter as the CPU reference does: the same picture within the rounding of the graphics card (a few
    /// levels; more on the few pixels where a centre falls exactly between two picture pixels).
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, PictureFilter.Sharp, false)]
    [InlineData(1920, 1080, PictureFilter.Smooth, false)]
    [InlineData(1920, 1080, PictureFilter.Crt, false)]
    [InlineData(1013, 571, PictureFilter.Smooth, false)]
    [InlineData(1013, 571, PictureFilter.Crt, false)]
    [InlineData(1920, 1080, PictureFilter.Sharp, true)]
    [InlineData(1920, 1080, PictureFilter.Smooth, true)]
    [InlineData(1920, 1080, PictureFilter.Crt, true)]
    [InlineData(1013, 571, PictureFilter.Smooth, true)]
    [InlineData(1013, 571, PictureFilter.Crt, true)]
    [InlineData(2560, 1440, PictureFilter.Crt, true)]
    public void TheRendererDrawsAsTheReference(int width, int height, PictureFilter filter, bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd: false, gpu);
        AssertLikeTheReference(host, filter, curvature: 0);
    }

    /// <summary>The CRT with an HD pack: the frame drawn into a canvas and the mask over all of it gives the same picture.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCrtOverAnHdFrameIsTheSame(bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(1920, 1080, hd: true, gpu);
        AssertLikeTheReference(host, PictureFilter.Crt, curvature: 0, tolerance: 2.5);
    }

    /// <summary>The CRT's curvature (display.crtCurvature) is drawn by the shader as the reference bends the picture.</summary>
    [Fact]
    public void TheCurvatureIsTheReferences()
    {
        if (!HaveSdl() || !OperatingSystem.IsWindows())
        {
            return;
        }
        using var host = Open(1920, 1080, hd: false, gpu: true);
        host.Curvature = true;
        AssertLikeTheReference(host, PictureFilter.Crt, curvature: SdlShaderPictureFilter.CurvatureAmount);
        // the corners are black, the middle is not
        var shown = host.ReadBack(out var rw, out var rh);
        var g = DisplayLayout.Graphics(rw, rh);
        Assert.Equal(0, shown[(g.Y + 1) * rw + g.X + 1] & 0xFFFFFF);
        Assert.NotEqual(0, shown[(g.Y + g.Height / 2) * rw + g.X + g.Width / 2] & 0xFFFFFF);
    }

    private static void AssertLikeTheReference(SdlHost host, PictureFilter filter, double curvature, double tolerance = 2.0)
    {
        var screen = Picture();
        var shown = Shown(host, screen, filter, out var rw, out var rh);
        var g = DisplayLayout.Graphics(rw, rh);
        var argb = screen.Pixels.ToArray().Select(i => screen.Palette[i]).ToArray();
        var reference = PictureFilterReference.Render(argb, Screen.Width, Screen.Height, filter, g.Width, g.Height, curvature);
        long total = 0;
        var far = 0;
        for (var y = 0; y < g.Height; y++)
        {
            for (var x = 0; x < g.Width; x++)
            {
                var a = shown[(g.Y + y) * rw + g.X + x];
                var b = reference[y * g.Width + x];
                var worst = 0;
                for (var shift = 0; shift < 24; shift += 8)
                {
                    var d = Math.Abs((a >> shift & 0xFF) - (int)(b >> shift & 0xFF));
                    total += d;
                    worst = Math.Max(worst, d);
                }
                if (worst > 12)
                {
                    far++;
                }
            }
        }
        var pixels = g.Width * g.Height;
        var mean = total / (3.0 * pixels);
        Assert.True(mean < tolerance, $"{host.FilterKind}: mean difference {mean:F2}");
        Assert.True(far < pixels / 100, $"{host.FilterKind}: {far} of {pixels} pixels differ by more than 12");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCrtDarkensBetweenLinesAndKeepsMostOfTheBrightness(bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(1920, 1080, hd: false, gpu);
        var screen = Picture();
        var sharp = Shown(host, screen, PictureFilter.Sharp, out var rw, out var rh);
        var crt = Shown(host, screen, PictureFilter.Crt, out _, out _);
        var g = DisplayLayout.Graphics(rw, rh);
        // the grey band: rows vary (scanlines), and the mean is near the sharp picture's
        double Mean(int[] pixels, int y) => Enumerable.Range(g.X + 3, g.Width - 6)
            .Average(x => (pixels[y * rw + x] >> 16 & 0xFF) + (pixels[y * rw + x] >> 8 & 0xFF) + (pixels[y * rw + x] & 0xFF));
        var rows = Enumerable.Range(g.Y + g.Height * 6 / 10, g.Height / 10).Select(y => Mean(crt, y)).ToList();
        Assert.True(rows.Max() > rows.Min() * 1.4, $"scanlines: {rows.Min()}..{rows.Max()}");
        var kept = rows.Average() / Mean(sharp, g.Y + g.Height * 3 / 4);
        Assert.InRange(kept, 0.8, 1.02);
        Assert.Equal(sharp[0], crt[0]);   // outside the picture: still black
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoFilterTouchesTheGamesFrame(bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        foreach (var hd in new[] { false, true })
        {
            using var host = Open(1920, 1080, hd, gpu);
            var screen = Picture();
            var pixels = screen.Pixels.ToArray();
            var palette = screen.Palette.ToArray();
            HdFrame? before = null;
            foreach (var filter in Enum.GetValues<PictureFilter>())
            {
                Shown(host, screen, filter, out _, out _);
                Assert.Equal(pixels, screen.Pixels.ToArray());
                Assert.Equal(palette, screen.Palette.ToArray());
                if (hd)
                {
                    // the renderer's frame (what Host.Headless saves as .hd.png) is made before the filter and is the same with each
                    var frame = host.Renderer!.Render(screen, new DisplayRectangle(0, 0, 1280, 800));
                    before ??= frame;
                    Assert.Equal(before.Base.Texture.Argb, frame.Base.Texture.Argb);
                }
            }
        }
    }

    [Fact]
    public void CtrlF11GoesToTheNextFilterAndF11AloneDoesNot()
    {
        if (!HaveSdl())
        {
            return;
        }
        Assert.True(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, 0x40)));                // left Ctrl
        Assert.True(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, 0x80)));                // right Ctrl
        Assert.False(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, 0)));
        Assert.False(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, 0x40 | 0x100)));       // Ctrl+Alt
        Assert.False(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF12, 0x40)));               // Ctrl-F12: the problem report
        using var host = Open(720, 400, hd: false);
        var e = Key(SdlNative.ScancodeF11, 0x40);
        Assert.True(SdlNative.SDL_PushEvent(ref e));
        host.Pump();
        Assert.Equal(PictureFilter.Smooth, host.Filter);
        Assert.False(host.TryReadKey(out _));   // the game does not get the key
    }

    private static SdlNative.SdlEvent Key(int scancode, int mod)
    {
        var e = new SdlNative.SdlEvent { Type = SdlNative.EventKeyDown };
        e.Keyboard.Type = SdlNative.EventKeyDown;
        e.Keyboard.Down = 1;
        e.Keyboard.Scancode = scancode;
        e.Keyboard.Key = scancode == SdlNative.ScancodeF11 ? 0x40000044 : 0x40000045;
        e.Keyboard.Mod = (ushort)mod;
        return e;
    }

    private static IEnumerable<(int X, int Y)> Inside(DisplayRectangle g)
    {
        // a step of 7 window pixels each way: enough to meet every kind of pixel edge, fast to check
        for (var y = g.Y; y < g.Y + g.Height; y += 7)
        {
            for (var x = g.X; x < g.X + g.Width; x += 7)
            {
                yield return (x, y);
            }
        }
    }
}
