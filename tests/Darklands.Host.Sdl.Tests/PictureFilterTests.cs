using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The picture filters in a real SDL window (read back from the renderer): sharp is the default and draws hard pixels as
/// before; smooth blends only at the edges between pixels and is the same as sharp at whole multiples; the CRT darkens rows
/// between the picture's lines and keeps most of the brightness; none of them touches the game's frame (the screen the
/// screenshots, the problem reports and the oracles take). Uses the SDL2 library the host ships; skipped without one.
/// </summary>
[Collection("SDL")]
public sealed class PictureFilterTests
{
    private static bool HaveSdl()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
        if (!File.Exists(library))
        {
            return false;
        }
        SdlNative.ConfigureLibrary(library);
        return true;
    }

    /// <summary>A picture of two colours in 2x2 checks (as the game's dithering) and a few solid bands.</summary>
    private static Screen Picture()
    {
        var screen = new Screen();
        screen.Palette[1] = 0xFFC08040;
        screen.Palette[2] = 0xFF204060;
        screen.Palette[3] = 0xFF808080;
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                screen.Pixels[y * Screen.Width + x] = (byte)(y < 100 ? ((x / 2 + y / 2) % 2 == 0 ? 1 : 2) : 3);
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

    private static SdlHost Open(int width, int height, bool hd, string? driver = null)
    {
        if (driver is not null)
        {
            SdlNative.SDL_SetHint("SDL_RENDER_DRIVER", driver);
        }
        var host = new SdlHost("filter test", width, height);
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
    }

    [Theory]
    [InlineData(1920, 1080, false, null)]
    [InlineData(1920, 1080, true, null)]
    [InlineData(1013, 571, false, null)]
    [InlineData(1920, 1080, false, "software")]
    public void SharpShowsOnlyThePicturesOwnColours(int width, int height, bool hd, string? driver)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd, driver);
        var screen = Picture();
        var shown = Shown(host, screen, PictureFilter.Sharp, out var rw, out var rh);
        var graphics = DisplayLayout.Graphics(rw, rh);
        var colours = new HashSet<int> { unchecked((int)screen.Palette[1]), unchecked((int)screen.Palette[2]), unchecked((int)screen.Palette[3]) };
        foreach (var (x, y) in Inside(graphics))
        {
            Assert.Contains(shown[y * rw + x], colours);
        }
    }

    [Theory]
    [InlineData(1440, 800, false)]   // the picture 1280x800: 4x
    [InlineData(1440, 800, true)]
    [InlineData(2160, 1200, false)]  // 1920x1200: 6x
    public void SmoothIsSharpAtWholeMultiples(int width, int height, bool hd)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd);
        var screen = Picture();
        var sharp = Shown(host, screen, PictureFilter.Sharp, out _, out _);
        var smooth = Shown(host, screen, PictureFilter.Smooth, out _, out _);
        Assert.Equal(sharp, smooth);
    }

    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1920, 1080, true)]
    [InlineData(1013, 571, false)]
    public void SmoothBlendsOnlyAtTheEdgesBetweenPixels(int width, int height, bool hd)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd);
        var screen = Picture();
        var sharp = Shown(host, screen, PictureFilter.Sharp, out var rw, out var rh);
        var smooth = Shown(host, screen, PictureFilter.Smooth, out _, out _);
        var graphics = DisplayLayout.Graphics(rw, rh);
        var differing = 0;
        var inside = 0;
        foreach (var (x, y) in Inside(graphics))
        {
            inside++;
            var p = y * rw + x;
            if (sharp[p] != smooth[p])
            {
                differing++;
                // a blend between neighbours: each channel between the two colours of the checks (or the grey of the bands)
                Assert.InRange(smooth[p] >> 16 & 0xFF, 0x20, 0xC0);
            }
        }
        Assert.InRange(differing, 1, inside / 2);
        // the solid band (bottom half) is the same colour everywhere, away from its top edge
        var band = DisplayLayout.Graphics(rw, rh);
        Assert.Equal(unchecked((int)screen.Palette[3]), smooth[(band.Y + band.Height * 3 / 4) * rw + band.X + band.Width / 2]);
    }

    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1920, 1080, true)]
    public void TheCrtDarkensBetweenLinesAndKeepsMostOfTheBrightness(int width, int height, bool hd)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd);
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
        // outside the picture: still black
        Assert.Equal(sharp[0], crt[0]);
    }

    [Fact]
    public void NoFilterTouchesTheGamesFrame()
    {
        if (!HaveSdl())
        {
            return;
        }
        foreach (var hd in new[] { false, true })
        {
            using var host = Open(1920, 1080, hd);
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
        Assert.True(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, SdlNative.KeyModCtrl & 0x40)));
        Assert.True(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, SdlNative.KeyModCtrl & 0x80)));
        Assert.False(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, 0)));
        Assert.False(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF11, 0x40 | 0x100)));   // Ctrl+Alt
        Assert.False(SdlHost.IsNextFilterKey(Key(SdlNative.ScancodeF12, 0x40)));           // Ctrl-F12: the problem report
        using var host = Open(720, 400, hd: false);
        var e = Key(SdlNative.ScancodeF11, 0x40);
        Assert.Equal(1, SdlNative.SDL_PushEvent(ref e));
        host.Pump();
        Assert.Equal(PictureFilter.Smooth, host.Filter);
        // the game does not get the key
        Assert.False(host.TryReadKey(out _));
    }

    /// <summary>
    /// The renderer draws each filter as the CPU reference does (PictureFilterReference): the same picture within the rounding
    /// of the graphics card (a few levels, more on the few pixels where a centre falls exactly between two picture pixels).
    /// The renderer that replaces SDL2's (SDL3's shaders) is held to the same.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, PictureFilter.Sharp)]
    [InlineData(1920, 1080, PictureFilter.Smooth)]
    [InlineData(1920, 1080, PictureFilter.Crt)]
    [InlineData(1013, 571, PictureFilter.Smooth)]
    [InlineData(1013, 571, PictureFilter.Crt)]
    public void TheRendererDrawsAsTheReference(int width, int height, PictureFilter filter)
    {
        if (!HaveSdl())
        {
            return;
        }
        using var host = Open(width, height, hd: false);
        var screen = Picture();
        var shown = Shown(host, screen, filter, out var rw, out var rh);
        var g = DisplayLayout.Graphics(rw, rh);
        var argb = screen.Pixels.ToArray().Select(i => screen.Palette[i]).ToArray();
        var reference = PictureFilterReference.Render(argb, Screen.Width, Screen.Height, filter, g.Width, g.Height);
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
        Assert.True(total / (3.0 * pixels) < 2.0, $"mean difference {total / (3.0 * pixels):F2}");
        Assert.True(far < pixels / 100, $"{far} of {pixels} pixels differ by more than 12");
    }

    private static SdlNative.SdlEvent Key(int scancode, int mod)
    {
        var e = new SdlNative.SdlEvent { Type = SdlNative.EventKeyDown };
        e.Keyboard.Type = SdlNative.EventKeyDown;
        e.Keyboard.State = 1;
        e.Keyboard.Keysym.Scancode = scancode;
        e.Keyboard.Keysym.Sym = scancode == SdlNative.ScancodeF11 ? 1073741892 : SdlNative.KeyF12;
        e.Keyboard.Keysym.Mod = (ushort)mod;
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
