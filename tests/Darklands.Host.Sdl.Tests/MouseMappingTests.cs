using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The mouse at every window size: a real SDL window shows a picture whose pixels name their own game coordinates, the
/// picture is read back from the renderer, and a mouse event at each window position must give the game pixel shown
/// there. The pointer the game moves (MovePointer) must come back as the same game pixel. Uses the SDL3 library the host
/// ships; skipped without one. The windows are shown briefly (and fullscreen once).
/// </summary>
[Collection("SDL")]
public sealed class MouseMappingTests
{
    [Theory]
    [InlineData(720, 400, false, false)]
    [InlineData(1440, 800, false, false)]
    [InlineData(2160, 1200, false, false)]
    [InlineData(1920, 1080, false, false)]
    [InlineData(1920, 1200, false, false)]
    [InlineData(1366, 768, false, false)]
    [InlineData(1600, 900, false, false)]
    [InlineData(1280, 1024, false, false)]
    [InlineData(800, 600, false, false)]
    [InlineData(1000, 700, false, false)]
    [InlineData(1013, 571, false, false)]
    [InlineData(500, 300, false, false)]
    [InlineData(2560, 1440, false, false)]
    [InlineData(1366, 768, false, true)]
    [InlineData(1920, 1080, false, true)]
    [InlineData(0, 0, true, false)]
    [InlineData(0, 0, true, true)]
    // SDL's software renderer (no graphics card: Linux CI, a VM; or SDL_RENDER_DRIVER=software) stretches in truncated fixed point
    [InlineData(1366, 768, false, false, true)]
    [InlineData(1013, 571, false, false, true)]
    [InlineData(1920, 1080, false, false, true)]
    [InlineData(500, 300, false, false, true)]
    [InlineData(2560, 1440, false, false, true)]
    [InlineData(1366, 768, false, true, true)]
    [InlineData(0, 0, true, false, true)]
    public void EveryWindowPositionGivesTheGamePixelShownThere(int width, int height, bool fullscreen, bool hd, bool software = false)
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        if (software)
        {
            // read when the renderer is made; SDL_Quit (the host's Dispose) clears it again
            SdlNative.SDL_SetHint(SdlNative.HintRenderDriver, "software");
        }
        using var host = fullscreen
            ? new SdlHost("mouse test", DisplayLayout.BaseWidth, DisplayLayout.BaseHeight, fullscreen: true)
            : new SdlHost("mouse test", width, height);
        if (software)
        {
            Assert.Equal(PictureSampling.SdlSoftware, host.Sampling);
        }
        if (hd)
        {
            host.Renderer = new HdRenderer(HdPack.Empty);
        }
        for (var i = 0; i < 10; i++)
        {
            host.Pump();
        }
        // The real pointer moving over the shown window (or the window appearing under it) queues motion events of its own,
        // and one landing after a pushed event would replace it: only the pushed events may reach the host. Its window "mouse entered"
        // events too: the host takes the real pointer's place then (SdlHost.PointerInWindow).
        SdlNative.SDL_SetEventEnabled(SdlNative.EventMouseMotion, false);
        NoWindowEvents();
        host.Pump();

        // The picture, three times: the low byte of x, the high bit of x, then y; the green channel marks the picture.
        var screen = new Screen();
        for (var i = 0; i < 256; i++)
        {
            screen.Palette[i] = 0xFF00C000u | (uint)i << 16;
        }
        int[] Shown(Func<int, int, int> index)
        {
            for (var y = 0; y < Screen.Height; y++)
            {
                for (var x = 0; x < Screen.Width; x++)
                {
                    screen.Pixels[y * Screen.Width + x] = (byte)index(x, y);
                }
            }
            host.Present(screen);
            return host.ReadBack(out _, out _);
        }
        var low = Shown((x, _) => x & 0xFF);
        var high = Shown((x, _) => x >> 8);
        var rows = Shown((_, y) => y);
        host.ReadBack(out var rw, out var rh);
        var (ww, wh) = host.WindowSize;
        bool Inside(int p) => (low[p] & 0xFF00) == 0xC000;
        int GameX(int p) => (low[p] >> 16 & 0xFF) | (high[p] >> 16 & 0xFF) << 8;
        int GameY(int p) => rows[p] >> 16 & 0xFF;

        var failures = new List<string>();
        var ties = 0;
        var graphics = DisplayLayout.Graphics(rw, rh);
        void Check(int wx, int wy)
        {
            var px = Math.Min(rw - 1, wx * rw / ww);
            var py = Math.Min(rh - 1, wy * rh / wh);
            var p = py * rw + px;
            if (!Inside(p))
            {
                return; // the black border: clamped to the edge, nothing to compare
            }
            Push(SdlNative.EventMouseMotion, wx, wy);
            host.Pump();
            var expected = (GameX(p), GameY(p));
            var actual = (host.Mouse.X, host.Mouse.Y);
            // a centre exactly between two game pixels: the GPU's rounding picks either one
            var tieX = DisplayLayout.IsTie(px - graphics.X, graphics.Width, Screen.Width) && Math.Abs(expected.Item1 - actual.X) == 1;
            var tieY = DisplayLayout.IsTie(py - graphics.Y, graphics.Height, Screen.Height) && Math.Abs(expected.Item2 - actual.Y) == 1;
            if (tieX || tieY)
            {
                expected = (tieX ? actual.X : expected.Item1, tieY ? actual.Y : expected.Item2);
                ties++;
            }
            if (expected != actual && failures.Count < 12)
            {
                failures.Add($"window ({wx},{wy}) shows {(GameX(p), GameY(p))}, mouse gives {actual}");
            }
        }
        for (var wx = 0; wx < ww; wx++)
        {
            Check(wx, wh / 2);
        }
        for (var wy = 0; wy < wh; wy++)
        {
            Check(ww / 2, wy);
        }
        for (var wy = 0; wy < wh; wy += 7)
        {
            for (var wx = 0; wx < ww; wx += 13)
            {
                Check(wx, wy);
            }
        }

        // The game moving the pointer (the cursor keys, a menu's default) must land where the game then reads it.
        for (var x = 0; x < Screen.Width; x++)
        {
            host.MovePointer(x, x * Screen.Height / Screen.Width);
            var wanted = (x, x * Screen.Height / Screen.Width);
            var (cx, cy) = (host.Mouse.X, host.Mouse.Y);
            host.Pump();
            if ((host.Mouse.X, host.Mouse.Y) != wanted && failures.Count < 24)
            {
                failures.Add($"MovePointer{wanted} read back as ({host.Mouse.X},{host.Mouse.Y}) (set {(cx, cy)})");
            }
        }

        Console.WriteLine($"{ww}x{wh}{(hd ? " HD" : "")}{(fullscreen ? " fullscreen" : "")}: {ties} tie positions");
        Assert.True(failures.Count == 0,
            $"window {ww}x{wh}, drawable {rw}x{rh}{(hd ? ", HD" : "")}{(fullscreen ? ", fullscreen" : "")}:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Before the host's pointer moves over the window the game's pointer is where the mouse driver's reset puts it, the middle of
    /// the screen (160, 100) as in the original and the headless host, not the top-left corner (0, 0) (a report of the gauntlet at the
    /// top-left on the inn roster, where no key moves the pointer: vis10 roster-colour); the host's motion events then move it.
    /// </summary>
    [Fact]
    public void TheGamesPointerStartsAtTheMiddleOfTheScreen()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        using var host = new SdlHost("mouse start test");
        Assert.Equal(new MouseState(160, 100, MouseButtons.None), host.Mouse);
        // no motion or window events: the real pointer over the window would queue its own (entering: the host takes its place)
        SdlNative.SDL_SetEventEnabled(SdlNative.EventMouseMotion, false);
        NoWindowEvents();
        host.Pump();
        Assert.Equal(new MouseState(160, 100, MouseButtons.None), host.Mouse);
    }

    /// <summary>
    /// Host enhancement (the user's decision): from the window's "mouse left" event until the mouse is back over it (its "mouse entered"
    /// event or a motion inside) the host says the pointer is outside, so the game leaves its drawn pointer out of the picture
    /// (PointerHotSpotTests); the mouse state the game reads keeps its last position meanwhile. A motion outside the window (a button held
    /// captures the mouse) keeps it hidden.
    /// </summary>
    [Fact]
    public void TheGamesPointerIsHiddenWhileTheMouseIsOutsideTheWindow()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        using var host = new SdlHost("mouse leave test");
        for (var i = 0; i < 10; i++)
        {
            host.Pump();
        }
        Assert.True(host.PointerInWindow);
        // only the pushed events: the real pointer would queue motion and enter / leave events of its own
        SdlNative.SDL_SetEventEnabled(SdlNative.EventMouseMotion, false);
        NoWindowEvents();
        host.Pump();
        var (ww, wh) = host.WindowSize;
        Push(SdlNative.EventMouseMotion, ww / 2, wh / 2);
        host.Pump();
        var over = host.Mouse;
        Assert.True(host.PointerInWindow);

        PushWindow(SdlNative.EventWindowMouseLeave);
        host.Pump();
        Assert.False(host.PointerInWindow);
        Assert.Equal(over, host.Mouse);
        // a motion outside the window (the mouse captured while a button is held) does not show it
        Push(SdlNative.EventMouseMotion, ww + 20, -5);
        host.Pump();
        Assert.False(host.PointerInWindow);

        PushWindow(SdlNative.EventWindowMouseEnter);
        host.Pump();
        Assert.True(host.PointerInWindow);
        PushWindow(SdlNative.EventWindowMouseLeave);
        host.Pump();
        Assert.False(host.PointerInWindow);
        // back over the window by a motion alone: shown at that point
        Push(SdlNative.EventMouseMotion, ww / 4, wh / 4);
        host.Pump();
        Assert.True(host.PointerInWindow);
        Assert.NotEqual(over, host.Mouse);
    }

    private static void PushWindow(uint windowEvent)
    {
        var e = new SdlNative.SdlEvent { Type = windowEvent };
        Assert.True(SdlNative.SDL_PushEvent(ref e));
    }

    /// <summary>No window events of SDL's own (SDL2's one window event is a range of event types in SDL3).</summary>
    private static void NoWindowEvents()
    {
        for (var type = SdlNative.EventWindowFirst; type <= SdlNative.EventWindowLast; type++)
        {
            SdlNative.SDL_SetEventEnabled(type, false);
        }
    }

    private static void Push(uint type, int x, int y)
    {
        var e = new SdlNative.SdlEvent { Type = type };
        e.Motion.Type = type;
        e.Motion.X = x;
        e.Motion.Y = y;
        Assert.True(SdlNative.SDL_PushEvent(ref e));
    }
}
