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

    /// <summary>A picture of two colours in 2x2 checks (as the game's dithering), a solid band, a ramp of greys and diagonal stripes.</summary>
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
                // 2 x 2 checks; a grey band; a ramp of greys beside 45-degree stripes (staircase edges, for HQ)
                screen.Pixels[y * Screen.Width + x] = (byte)(y < 100 ? ((x / 2 + y / 2) % 2 == 0 ? 1 : 2) : y < 170 ? 3
                    : x < 160 ? 16 + x * 64 / Screen.Width : (x + y) % 40 < 20 ? 1 : 2);
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
        return Ready(host, hd);
    }

    private static SdlHost Ready(SdlHost host, bool hd)
    {
        if (hd)
        {
            host.Renderer = new HdRenderer(HdPack.Empty);
        }
        host.Pump();
        return host;
    }

    /// <summary>
    /// The GPU renderer on Vulkan (SDL_GPU_DRIVER=vulkan): what Linux and the Steam Deck use, here on Windows. Null where no Vulkan
    /// driver is present (the GPU renderer does not start on it). Where it starts, the program's SPIR-V shader must draw.
    /// </summary>
    private static SdlHost? OpenVulkan(int width, int height, bool hd)
    {
        SdlNative.SDL_SetHint(SdlNative.HintGpuDriver, "vulkan");
        var host = new SdlHost("filter test (Vulkan)", width, height, gpu: true);
        if (host.RendererName != "gpu")
        {
            host.Dispose();
            return null;
        }
        Assert.Equal("vulkan", host.GpuDriver);
        Assert.Equal("shaders", host.FilterKind);
        return Ready(host, hd);
    }

    /// <summary>The filters on Vulkan (the SPIR-V shader) draw as the reference, the HD frame and the curvature included.</summary>
    [Theory]
    [InlineData(1920, 1080, PictureFilter.Even, false, false)]
    [InlineData(1920, 1080, PictureFilter.Soft, false, false)]
    [InlineData(1920, 1080, PictureFilter.SoftCrt, false, false)]
    [InlineData(1920, 1080, PictureFilter.Hq, false, false)]
    [InlineData(1920, 1080, PictureFilter.Crt, false, false)]
    [InlineData(1920, 1080, PictureFilter.CrtPlus, false, false)]
    [InlineData(1013, 571, PictureFilter.Crt, false, false)]
    [InlineData(1013, 571, PictureFilter.CrtPlus, false, false)]
    [InlineData(1920, 1080, PictureFilter.Crt, true, false)]
    [InlineData(1920, 1080, PictureFilter.SoftCrt, true, false)]
    [InlineData(1920, 1080, PictureFilter.Crt, false, true)]
    [InlineData(1920, 1080, PictureFilter.CrtPlus, false, true)]
    [InlineData(1920, 1080, PictureFilter.Sharp, false, false)]
    public void OnVulkanTheShaderDrawsAsTheReference(int width, int height, PictureFilter filter, bool hd, bool curved)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = OpenVulkan(width, height, hd);
        if (host is null)
        {
            return;   // no Vulkan driver here
        }
        host.Curvature = curved;
        AssertLikeTheReference(host, filter, curvature: curved ? SdlShaderPictureFilter.CurvatureAmount : 0, tolerance: hd ? 2.5 : 2.0);
        // no black picture: the middle of the picture shows the picture's colours
        var shown = host.ReadBack(out var rw, out var rh);
        var g = DisplayLayout.Graphics(rw, rh);
        Assert.NotEqual(0, shown[(g.Y + g.Height / 4) * rw + g.X + g.Width / 2] & 0xFFFFFF);
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
    public void EvenIsSharpAtWholeMultiples(int width, int height, bool hd, bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd, gpu);
        var screen = Picture();
        var sharp = Shown(host, screen, PictureFilter.Sharp, out _, out _);
        var even = Shown(host, screen, PictureFilter.Even, out _, out _);
        Assert.Equal(sharp, even);
    }

    /// <summary>
    /// The renderer draws each filter as the CPU reference does: the same picture within the rounding of the graphics card (a few
    /// levels; more on the few pixels where a centre falls exactly between two picture pixels).
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, PictureFilter.Sharp, false)]
    [InlineData(1920, 1080, PictureFilter.Even, false)]
    [InlineData(1920, 1080, PictureFilter.Crt, false)]
    [InlineData(1013, 571, PictureFilter.Even, false)]
    [InlineData(1013, 571, PictureFilter.Crt, false)]
    [InlineData(1920, 1080, PictureFilter.Sharp, true)]
    [InlineData(1920, 1080, PictureFilter.Even, true)]
    [InlineData(1920, 1080, PictureFilter.Crt, true)]
    [InlineData(1013, 571, PictureFilter.Even, true)]
    [InlineData(1013, 571, PictureFilter.Crt, true)]
    [InlineData(2560, 1440, PictureFilter.Crt, true)]
    [InlineData(1920, 1080, PictureFilter.Soft, true)]
    [InlineData(1920, 1080, PictureFilter.SoftCrt, true)]
    [InlineData(1920, 1080, PictureFilter.Hq, true)]
    [InlineData(1920, 1080, PictureFilter.CrtPlus, true)]
    [InlineData(1013, 571, PictureFilter.Soft, true)]
    [InlineData(1013, 571, PictureFilter.SoftCrt, true)]
    [InlineData(1013, 571, PictureFilter.Hq, true)]
    [InlineData(1013, 571, PictureFilter.CrtPlus, true)]
    [InlineData(2560, 1440, PictureFilter.Hq, true)]
    [InlineData(2560, 1440, PictureFilter.CrtPlus, true)]
    [InlineData(1440, 800, PictureFilter.CrtPlus, true)]
    // without the shader (render targets): Soft and HQ as Even, Soft CRT and CRT+ as CRT
    [InlineData(1920, 1080, PictureFilter.Soft, false)]
    [InlineData(1920, 1080, PictureFilter.SoftCrt, false)]
    [InlineData(1920, 1080, PictureFilter.Hq, false)]
    [InlineData(1920, 1080, PictureFilter.CrtPlus, false)]
    public void TheRendererDrawsAsTheReference(int width, int height, PictureFilter filter, bool gpu)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd: false, gpu);
        AssertLikeTheReference(host, filter, curvature: 0, expected: Drawn(host, filter));
    }

    /// <summary>
    /// The filters with an HD pack (the game's frame then drawn as the overlay too): the same picture. The CRT kinds draw the frame
    /// into a canvas and lay their scanlines and mask over all of it (CRT+ reads the game's pixels back from the canvas at their
    /// middles and draws its beams from them).
    /// </summary>
    [Theory]
    [InlineData(PictureFilter.Crt, false, 2.5, 0.01)]
    [InlineData(PictureFilter.Crt, true, 2.5, 0.01)]
    [InlineData(PictureFilter.Even, true, 2.0, 0.01)]
    [InlineData(PictureFilter.Soft, true, 2.0, 0.01)]
    [InlineData(PictureFilter.Hq, true, 2.0, 0.01)]
    [InlineData(PictureFilter.SoftCrt, true, 2.5, 0.01)]
    [InlineData(PictureFilter.CrtPlus, true, 2.5, 0.01)]
    public void TheFiltersOverAnHdFrameAreTheSame(PictureFilter filter, bool gpu, double tolerance, double farShare)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(1920, 1080, hd: true, gpu);
        AssertLikeTheReference(host, filter, curvature: 0, tolerance, Drawn(host, filter), farShare);
    }

    /// <summary>The CRT kinds' curvature (display.crtCurvature) is drawn by the shader as the reference bends the picture.</summary>
    [Theory]
    [InlineData(PictureFilter.Crt)]
    [InlineData(PictureFilter.SoftCrt)]
    [InlineData(PictureFilter.CrtPlus)]
    public void TheCurvatureIsTheReferences(PictureFilter filter)
    {
        if (!HaveSdl() || !OperatingSystem.IsWindows())
        {
            return;
        }
        using var host = Open(1920, 1080, hd: false, gpu: true);
        host.Curvature = true;
        AssertLikeTheReference(host, filter, curvature: SdlShaderPictureFilter.CurvatureAmount);
        // the corners are black, the middle is not
        var shown = host.ReadBack(out var rw, out var rh);
        var g = DisplayLayout.Graphics(rw, rh);
        Assert.Equal(0, shown[(g.Y + 1) * rw + g.X + 1] & 0xFFFFFF);
        Assert.NotEqual(0, shown[(g.Y + g.Height / 2) * rw + g.X + g.Width / 2] & 0xFFFFFF);
    }

    /// <summary>
    /// The filter the host draws for <paramref name="filter"/>: the filter itself with the shader; without it (no graphics card, as
    /// on a Linux build machine, where the GPU renderer gives way to render targets) the one drawn in its place.
    /// </summary>
    private static PictureFilter Drawn(SdlHost host, PictureFilter filter) =>
        host.FilterKind == "shaders" ? filter : PictureFilters.WithoutShader(filter);

    /// <summary>
    /// The filter drawn by the host and read back against the CPU reference of <paramref name="expected"/> (the filter itself unless
    /// the renderer draws another in its place): a mean difference under <paramref name="tolerance"/> levels, and fewer than
    /// <paramref name="farShare"/> of the pixels more than 12 levels off (the graphics card's rounding where a centre falls exactly
    /// between two picture pixels).
    /// </summary>
    private static void AssertLikeTheReference(SdlHost host, PictureFilter filter, double curvature, double tolerance = 2.0,
        PictureFilter? expected = null, double farShare = 0.01)
    {
        var screen = Picture();
        var shown = Shown(host, screen, filter, out var rw, out var rh);
        var g = DisplayLayout.Graphics(rw, rh);
        var argb = screen.Pixels.ToArray().Select(i => screen.Palette[i]).ToArray();
        var reference = PictureFilterReference.Render(argb, Screen.Width, Screen.Height, expected ?? filter, g.Width, g.Height, curvature);
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
        Assert.True(far < pixels * farShare, $"{host.FilterKind}: {far} of {pixels} pixels differ by more than 12");
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
        Assert.Equal(PictureFilter.Even, host.Filter);
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
