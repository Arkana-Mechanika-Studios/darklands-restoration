using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl;

/// <summary>
/// An SDL3 window that shows the PC's picture as <see cref="DisplayLayout"/> places it (base size
/// 720×400: the text screen at its own resolution, the 320×200 graphics screen doubled to 640×400
/// and centred) and turns SDL keyboard and mouse events into game input.
/// </summary>
internal sealed class SdlHost : IHost, IVideo, IInput, IDisposable
{
    private readonly IntPtr _window;
    private readonly IntPtr _renderer;
    private readonly IntPtr _texture;
    private readonly int[] _argb = new int[Screen.Width * Screen.Height];
    private readonly int[] _textArgb = new int[TextModeRaster.Width * TextModeRaster.Height];
    private IntPtr _textTexture;
    // the keyboard (PcKeyboard): SDL's presses and releases go in as the keyboard's make and break codes, its typematic repeats and
    // the BIOS keyboard buffer with the game's keyboard interrupt in front of it (bounded, a held key's repeats collapsed as in the original)
    // orig: root 044A:020E (the INT 9 handler: it only sees what the keyboard sends; the original never programs the typematic rate, so the machine's own applies)
    // DIVERGENCE: the held key's repeats come from DOSBox's typematic (KeyTypematic: 500 ms, then every 33 ms) counted from its press,
    //   not from SDL's operating-system repeat events nor the PC/AT keyboard's power-on 10.9/s, at which the original's map stands
    //   in about 7 of its 18.2 passes a second while an arrow is held (DOSBox gives a key to every pass); the developer console keeps the OS repeats
    private readonly PcKeyboard _keyboard = new();
    private readonly System.Diagnostics.Stopwatch _keyClock = System.Diagnostics.Stopwatch.StartNew();
    private SdlNative.SdlRect _viewport;
    private SdlNative.SdlRect _textViewport;
    // how the renderer stretches the picture, which the mouse follows (SDL's software renderer: no graphics card, or SDL_RENDER_DRIVER=software)
    private readonly PictureSampling _sampling;
    // orig: the mouse driver's reset (INT 33h function 0, the game's mouse start) puts the pointer at the middle of the screen: (160, 100) in
    //   the game's coordinates (the tester's mouse start, orig_side._mouse_init; Host.Headless starts there too). Until the host's pointer
    //   moves over the window the game's pointer stays there, not at (0, 0) (the top-left corner, partly off the screen with the map's
    //   hot spot (5, 0)); tests: MouseMappingTests.TheGamesPointerStartsAtTheMiddleOfTheScreen
    private MouseState _mouse = new(StartX, StartY, MouseButtons.None);

    /// <summary>Where the game's pointer is before the host's pointer moves over the window: the middle of the screen.</summary>
    internal const int StartX = 160, StartY = 100;
    private bool _shift;
    private bool _framePresented;
    private bool _showingText;
    private HdFrame? _hdFrame;
    private readonly Dictionary<RgbaImage, (IntPtr Texture, int Version)> _hdTextures = [];

    /// <summary>Opens the window at the size of <see cref="DisplayLayout.WindowSize"/> (720×400 by default).</summary>
    /// <param name="frameless">Opened without a frame or title bar (the launcher's window: giving a framed window's frame away later
    /// leaves Windows painting its old title bar over it now and then).</param>
    public SdlHost(string title, int width = DisplayLayout.BaseWidth, int height = DisplayLayout.BaseHeight, bool fullscreen = false, bool frameless = false)
    {
        if (!SdlNative.SDL_Init(SdlNative.InitVideo | SdlNative.InitEvents))
        {
            throw new SdlHostException("SDL_Init failed: " + SdlNative.GetErrorText());
        }
        // the key events' symbols as SDL2 gave them: the layout's own (no French digits or Latin letters put in by SDL3)
        SdlNative.SDL_SetHint(SdlNative.HintKeycodeOptions, "none");
        var scale = fullscreen ? 1 : WindowScale(SdlNative.SDL_GetPrimaryDisplay());
        _window = SdlNative.CreateCentredWindow(title, Scaled(width, scale), Scaled(height, scale),
            SdlNative.WindowResizable | SdlNative.WindowHighPixelDensity
            | (fullscreen ? SdlNative.WindowFullscreen : 0) | (frameless ? SdlNative.WindowBorderless : 0));
        if (_window == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateWindow failed: " + SdlNative.GetErrorText());
        }
        // SDL's choice of the graphics card's renderers, else its software one (SDL_RENDER_DRIVER names one); the screen's refresh
        // paces a graphics card's presents, as SDL2's SDL_RENDERER_PRESENTVSYNC did (the software renderer never waited)
        _renderer = SdlNative.SDL_CreateRenderer(_window, null);
        if (_renderer == IntPtr.Zero)
        {
            _renderer = SdlNative.SDL_CreateRenderer(_window, "software");
        }
        if (_renderer == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateRenderer failed: " + SdlNative.GetErrorText());
        }
        _sampling = IsSoftware(_renderer) ? PictureSampling.SdlSoftware : PictureSampling.Centre;
        if (_sampling != PictureSampling.SdlSoftware)
        {
            SdlNative.SDL_SetRenderVSync(_renderer, 1);
        }
        _texture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888,
            SdlNative.TextureAccessStreaming, Screen.Width, Screen.Height);
        SdlNative.SDL_SetTextureScaleMode(_texture, SdlNative.ScaleModeNearest);
        SdlNative.SDL_SetTextureBlendMode(_texture, SdlNative.BlendModeNone);
        SdlNative.SDL_HideCursor();
        _filter = new SdlPictureFilter(_renderer);
        UpdateViewport();
    }

    public IVideo Video => this;

    public IInput Input => this;

    /// <summary>The game's sound; silent unless the program attaches an audio output.</summary>
    public IAudio Audio { get; set; } = new SilentAudio();

    public IClock Clock { get; } = new SystemClock();

    /// <summary>The game's timer: the audio's when sound plays, else one on <see cref="Clock"/>.</summary>
    public IGameTimer Timer
    {
        get => _timer ??= new ClockGameTimer(Clock);
        set => _timer = value;
    }

    private IGameTimer? _timer;

    /// <summary>The 8×16 font of the text screen (the program start's banner).</summary>
    public TextFont TextFont { get; set; } = TextFont.FromEnvironment();

    public bool QuitRequested { get; private set; }

    /// <summary>
    /// The developer console (null: none). F11 opens and closes it; while it is open it takes the keyboard and the mouse,
    /// so the game receives nothing, and it is drawn over the window outside the game's picture.
    /// </summary>
    // Host feature (like F12's screenshots): the original never reads F11 (the BIOS keyboard call it uses does not report it), so the game loses no key.
    public ConsoleOverlay? Console { get; set; }

    /// <summary>What Ctrl-F12 does (null: nothing, the key goes to the game as before): the host makes a problem report.</summary>
    public Action? ReportRequested { get; set; }

    /// <summary>A line drawn small in the window's bottom-right corner when it gives one (the version on the start screen).</summary>
    public CornerText? Corner { get; set; }

    /// <summary>
    /// Replacement graphics (null: none): each frame is drawn as the renderer's items, the game's frame first and the
    /// replacements over it. The host only puts the items on the graphics card.
    /// </summary>
    public HdRenderer? Renderer { get; set; }

    /// <summary>
    /// How the graphics screen's picture is drawn on the window (docs/SETTINGS.md display.filter); Ctrl+F11 goes to the next one.
    /// Only the window's picture changes: the frame the game made (screenshots, problem reports, what the oracles compare) does not.
    /// </summary>
    // Host feature: like F11 and F12, Ctrl+F11 is a key the original never reads (its BIOS keyboard call does not report F11), so the game loses no key.
    public PictureFilter Filter
    {
        get => _filter.Filter;
        set => _filter.Filter = value;
    }

    private readonly IPictureFilterRenderer _filter;

    public MouseState Mouse => _mouse;

    /// <summary>
    /// False from the window's "mouse left" event (SDL_EVENT_WINDOW_MOUSE_LEAVE) to its "mouse entered" event or the next motion over it: the
    /// game leaves its drawn pointer out of the picture meanwhile. The mouse state the game reads keeps the last position over the window
    /// (on entering it takes the point where the host's pointer came in). At the start: true (the game's pointer at the middle until the
    /// host's pointer first moves over the window or enters it, MouseState start above).
    /// </summary>
    // orig: root 047C:1293 (pointer-show: the mouse driver draws the pointer; under DOS the mouse never leaves the screen)
    // DIVERGENCE: a host enhancement (the user's decision): while the host's pointer is outside the window the game's drawn pointer (the
    //   gauntlet, the battle's hand) is left out of the picture shown and comes back where it enters; only the picture changes, never the
    //   game or its number stream (the mouse state keeps the last position over the window); Host.Headless and the oracle tools always
    //   say true (IInput.PointerInWindow); tests: MouseMappingTests.TheGamesPointerIsHiddenWhileTheMouseIsOutsideTheWindow,
    //   PointerHotSpotTests.TheGauntletIsLeftOutWhileTheHostsPointerIsOutsideTheWindow
    public bool PointerInWindow => _pointerInWindow;

    private bool _pointerInWindow = true;

    private bool InWindow(int windowX, int windowY)
    {
        SdlNative.SDL_GetWindowSize(_window, out var ww, out var wh);
        return windowX >= 0 && windowY >= 0 && windowX < ww && windowY < wh;
    }

    public bool ShiftHeld => _shift;

    public bool TryReadKeyTransition(out int scanCode, out bool released) => _keyboard.TryReadTransition(out scanCode, out released);

    /// <summary>The PC scan code (set 1) of the keys the game's keyboard interrupt looks at, 0 for the others.</summary>
    private static int PcScanCode(int sdlScancode) => sdlScancode switch
    {
        82 or 96 => 0x48, // up, keypad 8
        81 or 90 => 0x50, // down, keypad 2
        80 or 92 => 0x4B, // left, keypad 4
        79 or 94 => 0x4D, // right, keypad 6
        74 or 95 => 0x47, // home, keypad 7
        77 or 89 => 0x4F, // end, keypad 1
        75 or 97 => 0x49, // page up, keypad 9
        78 or 91 => 0x51, // page down, keypad 3
        93 => 0x4C,       // keypad 5
        86 => 0x4A,       // keypad minus
        87 => 0x4E,       // keypad plus
        53 => 0x29,       // `
        49 => 0x2B,       // backslash
        _ => 0,
    };

    public bool CtrlHeld => (SdlNative.SDL_GetModState() & (SdlNative.KeyModLeftCtrl | SdlNative.KeyModRightCtrl)) != 0;

    public bool TakeCtrlBreak() => _keyboard.TakeCtrlBreak();

    /// <summary>Ctrl+Pause (SDL's Pause scancode 72 with a Ctrl key), or SDL's Cancel (155, what some systems report for Break).</summary>
    // C: plain Pause (the BIOS's pause loop, which holds the program until the next key) is not modelled: the key goes nowhere
    internal static bool IsCtrlBreak(int scancode, int mod) => scancode == 72 && (mod & SdlNative.KeyModCtrl) != 0 || scancode == 155;

    /// <summary>The pointer to a screen pixel: the window point at the middle of that pixel of the picture.</summary>
    public void MovePointer(int x, int y)
    {
        SdlNative.SDL_GetWindowSize(_window, out var ww, out var wh);
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var rw, out var rh);
        var g = Graphics;
        var px = g.X + (x * 2 + 1) * g.Width / (2 * Screen.Width);
        var py = g.Y + (y * 2 + 1) * g.Height / (2 * Screen.Height);
        SdlNative.SDL_WarpMouseInWindow(_window, px * ww / Math.Max(1, rw), py * wh / Math.Max(1, rh));
        _mouse = _mouse with { X = Math.Clamp(x, 0, Screen.Width - 1), Y = Math.Clamp(y, 0, Screen.Height - 1) };
    }

    public void Present(Screen screen)
    {
        if (Renderer is not null)
        {
            PresentHd(screen, Renderer);
            return;
        }
        var pixels = screen.Pixels;
        var palette = screen.Palette;
        for (var i = 0; i < pixels.Length; i++)
        {
            _argb[i] = unchecked((int)palette[pixels[i]]);
        }
        SdlNative.SDL_UpdateTexture(_texture, IntPtr.Zero, _argb, Screen.Width * 4);
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        UpdateViewport();
        DrawFrame();
        _showingText = false;
        FinishFrame();
    }

    /// <summary>The renderer's items for the picture's place in the window: the frame's texture, then the replacements.</summary>
    private void PresentHd(Screen screen, HdRenderer renderer)
    {
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        UpdateViewport();
        _hdFrame = renderer.Render(screen, Graphics);
        SdlNative.SDL_UpdateTexture(_texture, IntPtr.Zero, _hdFrame.Base.Texture.Argb, Screen.Width * 4);
        DrawHdFrame(_hdFrame);
        _showingText = false;
        FinishFrame();
    }

    /// <summary>
    /// Draws a frame's items: the base on the frame's own streaming texture, every other picture (replacements, the
    /// overlay) on a texture kept while the picture object is in use and uploaded again when its version moves.
    /// </summary>
    private void DrawHdFrame(HdFrame frame)
    {
        var used = new HashSet<RgbaImage>(ReferenceEqualityComparer.Instance);
        foreach (var item in frame.Items)
        {
            var destination = ToSdl(item.Destination);
            if (ReferenceEquals(item, frame.Base))
            {
                _filter.DrawGame(_texture, Screen.Width, Screen.Height, item.Destination, blended: false);
                continue;
            }
            used.Add(item.Texture);
            if (item.Placement is null)
            {
                // the overlay (the game's frame again, transparent where a replacement shows): through the filter as the base
                _filter.DrawGame(HdTexture(item), item.Texture.Width, item.Texture.Height, item.Destination, blended: true);
                continue;
            }
            SdlNative.RenderCopy(_renderer, HdTexture(item), destination);
        }
        // the CRT's mask over everything, replacements included (the monitor shows them through it too)

        _filter.Finish(frame.Output);
        foreach (var image in _hdTextures.Keys.Where(image => !used.Contains(image)).ToList())
        {
            SdlNative.SDL_DestroyTexture(_hdTextures[image].Texture);
            _hdTextures.Remove(image);
        }
    }

    private IntPtr HdTexture(DrawItem item)
    {
        var image = item.Texture;
        if (_hdTextures.TryGetValue(image, out var kept) && kept.Version == image.Version)
        {
            return kept.Texture;
        }
        var texture = kept.Texture;
        if (texture == IntPtr.Zero)
        {
            texture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStatic, image.Width, image.Height);
            SdlNative.SDL_SetTextureBlendMode(texture, SdlNative.BlendModeBlend);
            SdlNative.SDL_SetTextureScaleMode(texture, item.Filter == DrawFilter.Linear ? SdlNative.ScaleModeLinear : SdlNative.ScaleModeNearest);
        }
        if (kept.Texture != IntPtr.Zero && kept.Version == image.Version - 1 && image.LastChange is var (x, y, w, h))
        {
            // only the rectangle the last change touched
            var area = new SdlNative.SdlRect { X = x, Y = y, Width = w, Height = h };
            unsafe
            {
                fixed (uint* pixels = &image.Argb[y * image.Width + x])
                {
                    SdlNative.SDL_UpdateTexture(texture, ref area, (IntPtr)pixels, image.Width * 4);
                }
            }
        }
        else
        {
            SdlNative.SDL_UpdateTexture(texture, IntPtr.Zero, image.Argb, image.Width * 4);
        }
        _hdTextures[image] = (texture, image.Version);
        return texture;
    }

    /// <summary>The console over the picture (when open), then the frame to the window.</summary>
    private void FinishFrame()
    {
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var w, out var h);
        Corner?.Draw(_renderer, w, h);
        if (Console is { Console.IsOpen: true } overlay)
        {
            overlay.Draw(_renderer, w, h);
        }
        SdlNative.SDL_RenderPresent(_renderer);
        _framePresented = true;
    }

    /// <summary>Shows the last picture again with the console over it (the game may not present while it waits).</summary>
    private void Redraw()
    {
        DrawLast();
        FinishFrame();
    }

    /// <summary>The last picture at the window's current size, not yet shown.</summary>
    private void DrawLast()
    {
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        UpdateViewport();
        if (_showingText && _textTexture != IntPtr.Zero)
        {
            SdlNative.RenderCopy(_renderer, _textTexture, _textViewport);
        }
        else if (_hdFrame is not null)
        {
            // the items at the window's current size: the renderer places them again
            _hdFrame = HdRenderer.Relayout(_hdFrame, Graphics);
            DrawHdFrame(_hdFrame);
        }
        else
        {
            DrawFrame();
        }
    }

    /// <summary>The game's frame (in <see cref="_texture"/>) at the picture's place, through the picture filter.</summary>
    private void DrawFrame()
    {
        _filter.DrawGame(_texture, Screen.Width, Screen.Height, Graphics, blended: false);
        _filter.Finish(Graphics);
    }

    /// <summary>How the renderer in use stretches the picture (tests).</summary>
    internal PictureSampling Sampling => _sampling;

    /// <summary>The window's size in the coordinates its mouse events use (tests).</summary>
    internal (int Width, int Height) WindowSize
    {
        get
        {
            SdlNative.SDL_GetWindowSize(_window, out var width, out var height);
            return (width, height);
        }
    }

    /// <summary>The last picture drawn again and read back from the renderer, as ARGB in drawable pixels (tests).</summary>
    internal int[] ReadBack(out int width, out int height)
    {
        DrawLast();
        SdlNative.SDL_GetRenderOutputSize(_renderer, out width, out height);
        var pixels = new int[width * height];
        if (!SdlNative.ReadPixels(_renderer, pixels, width, height))
        {
            throw new SdlHostException("SDL_RenderReadPixels failed: " + SdlNative.GetErrorText());
        }
        return pixels;
    }

    /// <summary>The text screen at 720×400 (9×16 cells), at its own resolution (scaled with the window).</summary>
    public void PresentText(TextScreen screen)
    {
        if (_textTexture == IntPtr.Zero)
        {
            _textTexture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888,
                SdlNative.TextureAccessStreaming, TextModeRaster.Width, TextModeRaster.Height);
            SdlNative.SDL_SetTextureScaleMode(_textTexture, SdlNative.ScaleModeNearest);
            SdlNative.SDL_SetTextureBlendMode(_textTexture, SdlNative.BlendModeNone);
        }
        TextModeRaster.Render(screen, TextFont, Clock.Milliseconds, System.Runtime.InteropServices.MemoryMarshal.Cast<int, uint>(_textArgb.AsSpan()));
        SdlNative.SDL_UpdateTexture(_textTexture, IntPtr.Zero, _textArgb, TextModeRaster.Width * 4);
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        UpdateViewport();
        SdlNative.RenderCopy(_renderer, _textTexture, _textViewport);
        _showingText = true;
        FinishFrame();
    }

    public void Pump()
    {
        // A release that came in the same pump as its press is applied now, so the game saw the click
        // (the original's mouse driver latches presses; a tap within one frame must not be lost).
        if (_releaseNextPump != MouseButtons.None)
        {
            _mouse = _mouse with { Buttons = _mouse.Buttons & ~_releaseNextPump };
            _releaseNextPump = MouseButtons.None;
        }
        var pressedThisPump = MouseButtons.None;
        var toggledThisPump = false;
        while (SdlNative.SDL_PollEvent(out var e))
        {
            // Ctrl+F11: the next picture filter (Filter), shown at once even while the game waits
            if (IsNextFilterKey(e))
            {
                if (e.Keyboard.Repeat == 0)
                {
                    NextFilter();
                }
                continue;
            }
            if (e.Type == SdlNative.EventKeyDown && e.Keyboard.Scancode == SdlNative.ScancodeF11 && Console is not null)
            {
                if (e.Keyboard.Repeat == 0)
                {
                    SetConsoleOpen(!Console.Console.IsOpen);
                    toggledThisPump = true;
                }
                continue;
            }
            // Ctrl-F12: a problem report (docs/REPORTING.md). A host key like F11 and F12: the original never reads F11 or F12, so the game loses no key.
            if (e.Type == SdlNative.EventKeyDown && e.Keyboard.Scancode == SdlNative.ScancodeF12
                && (e.Keyboard.Mod & SdlNative.KeyModCtrl) != 0 && ReportRequested is not null)
            {
                if (e.Keyboard.Repeat == 0)
                {
                    ReportRequested();
                }
                continue;
            }
            if (Console is { Console.IsOpen: true } && ConsoleEvent(e, toggledThisPump))
            {
                continue;
            }
            switch (e.Type)
            {
                case SdlNative.EventQuit:
                    QuitRequested = true;
                    break;
                case SdlNative.EventKeyDown:
                    _shift = (e.Keyboard.Mod & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
                    if (e.Keyboard.Repeat != 0)
                    {
                        break;   // the operating system's own repeat: the keyboard's typematic (PcKeyboard.Tick below) stands in for it
                    }
                    if (IsCtrlBreak(e.Keyboard.Scancode, e.Keyboard.Mod))
                    {
                        // Ctrl+Pause (the PC keyboard's Break, E0 46 with Ctrl) or a key the system calls Break: to the game as the BIOS
                        // delivers it (PcKeyboard.CtrlBreak; GameUi.QuitCheck ends the program at the card engine's next check)
                        _keyboard.CtrlBreak();
                        break;
                    }
                    _keyboard.KeyDown(SdlKeyMap.ToGameKey(e.Keyboard.Scancode, e.Keyboard.Key, e.Keyboard.Mod),
                        PcScanCode(e.Keyboard.Scancode), (int)e.Keyboard.Scancode, _keyClock.Elapsed.TotalMilliseconds);
                    break;
                case SdlNative.EventKeyUp:
                    _shift = (e.Keyboard.Mod & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
                    _keyboard.KeyUp(PcScanCode(e.Keyboard.Scancode), (int)e.Keyboard.Scancode);
                    break;
                case SdlNative.EventMouseMotion:
                    _mouse = _mouse with { X = ToScreenX(Whole(e.Motion.X)), Y = ToScreenY(Whole(e.Motion.Y)) };
                    // (a button held captures the mouse: its motion outside the window keeps the pointer hidden)
                    _pointerInWindow = InWindow(Whole(e.Motion.X), Whole(e.Motion.Y));
                    break;
                case SdlNative.EventWindowMouseLeave:
                    _pointerInWindow = false;
                    break;
                case SdlNative.EventWindowMouseEnter:
                    // where the host's pointer came back in (the motion events that follow move it on)
                    SdlNative.SDL_GetMouseState(out var ex, out var ey);
                    _mouse = _mouse with { X = ToScreenX(Whole(ex)), Y = ToScreenY(Whole(ey)) };
                    _pointerInWindow = true;
                    break;
                case SdlNative.EventMouseButtonDown:
                case SdlNative.EventMouseButtonUp:
                    var button = e.Button.Button == SdlNative.MouseButtonRight ? MouseButtons.Right
                        : e.Button.Button == SdlNative.MouseButtonLeft ? MouseButtons.Left : MouseButtons.None;
                    var buttons = _mouse.Buttons;
                    if (e.Type == SdlNative.EventMouseButtonDown)
                    {
                        buttons |= button;
                        pressedThisPump |= button;
                    }
                    else if ((pressedThisPump & button) != 0)
                    {
                        _releaseNextPump |= button;
                    }
                    else
                    {
                        buttons &= ~button;
                    }
                    _mouse = new MouseState(ToScreenX(Whole(e.Button.X)), ToScreenY(Whole(e.Button.Y)), buttons);
                    break;
            }
        }
        // the typematic repeats of the key held last that came due since the previous pump
        _keyboard.Tick(_keyClock.Elapsed.TotalMilliseconds);
        // the game may pump without presenting while it waits: the console is drawn on (and taken away when it closes)
        if (Console is not null && (toggledThisPump || (Console.Console.IsOpen && !_framePresented)))
        {
            Redraw();
        }
        _framePresented = false;
    }

    private MouseButtons _releaseNextPump;

    // the wheel's turn not yet a whole tick (SDL3 gives fractions; SDL2 gave whole ticks)
    private float _wheel;

    /// <summary>The whole ticks of the wheel turned, the rest kept for the next turn.</summary>
    private int WheelTicks(float y)
    {
        _wheel += y;
        var ticks = (int)_wheel;
        _wheel -= ticks;
        return ticks;
    }

    /// <summary>A press of Ctrl+F11 (either Ctrl key; not with Alt), the key that goes to the next picture filter.</summary>
    internal static bool IsNextFilterKey(SdlNative.SdlEvent e) =>
        e.Type == SdlNative.EventKeyDown && e.Keyboard.Scancode == SdlNative.ScancodeF11
        && (e.Keyboard.Mod & SdlNative.KeyModCtrl) != 0 && (e.Keyboard.Mod & SdlNative.KeyModAlt) == 0;

    /// <summary>The next picture filter, the last picture drawn again with it.</summary>
    internal void NextFilter()
    {
        Filter = PictureFilters.Next(Filter);
        System.Console.Error.WriteLine($"Picture: {PictureFilters.Label(Filter)}");
        Redraw();
    }

    private void SetConsoleOpen(bool open)
    {
        Console!.Console.IsOpen = open;
        _keyboard.StopRepeating();   // the console takes the keyboard; what it types repeats with the operating system's rate
        if (open)
        {
            SdlNative.SDL_StartTextInput(_window);
        }
        else
        {
            SdlNative.SDL_StopTextInput(_window);
        }
    }

    /// <summary>An event while the console is open: true when the console takes it (the game does not see it).</summary>
    private bool ConsoleEvent(SdlNative.SdlEvent e, bool toggledThisPump)
    {
        var console = Console!.Console;
        switch (e.Type)
        {
            case SdlNative.EventTextInput:
                if (!toggledThisPump)
                {
                    console.Type(e.InputText());
                }
                return true;
            case SdlNative.EventKeyDown:
                switch (e.Keyboard.Key)
                {
                    case SdlNative.KeyEscape: SetConsoleOpen(false); break;
                    case SdlNative.KeyReturn or SdlNative.KeyKeypadEnter: console.Submit(); break;
                    case SdlNative.KeyBackspace: console.Backspace(); break;
                    case SdlNative.KeyDelete: console.Delete(); break;
                    case SdlNative.KeyLeft: console.MoveCursor(-1); break;
                    case SdlNative.KeyRight: console.MoveCursor(1); break;
                    case SdlNative.KeyHome: console.Home(); break;
                    case SdlNative.KeyEnd: console.End(); break;
                    case SdlNative.KeyUp: console.Recall(-1); break;
                    case SdlNative.KeyDown: console.Recall(1); break;
                    case SdlNative.KeyPageUp: console.Scroll(10); break;
                    case SdlNative.KeyPageDown: console.Scroll(-10); break;
                }
                return true;
            case SdlNative.EventMouseWheel:
                console.Scroll(WheelTicks(e.Wheel.Y) * 3);
                return true;
            case SdlNative.EventMouseMotion or SdlNative.EventMouseButtonDown or SdlNative.EventMouseButtonUp:
                return true;
            default:
                // key releases still reach the game: a key held when the console opened is let go
                return false;
        }
    }

    public bool TryReadKey(out int key) => _keyboard.TryReadKey(out key);

    /// <summary>The SDL window and renderer, for another user of the window before the game (the launcher).</summary>
    internal IntPtr Window => _window;

    internal IntPtr SdlRenderer => _renderer;

    /// <summary>The system's mouse pointer (the game draws its own, so it is hidden while the game runs).</summary>
    internal static void ShowPointer(bool on)
    {
        if (on)
        {
            SdlNative.SDL_ShowCursor();
        }
        else
        {
            SdlNative.SDL_HideCursor();
        }
    }

    /// <summary>The smallest size the player can give the window (1 x 1: no limit).</summary>
    internal void MinimumSize(int width, int height)
    {
        var scale = WindowScale(SdlNative.SDL_GetDisplayForWindow(_window));
        SdlNative.SDL_SetWindowMinimumSize(_window, Math.Max(1, Scaled(width, scale)), Math.Max(1, Scaled(height, scale)));
    }

    /// <summary>Gives the window a new size (centred) or the whole screen.</summary>
    internal void Resize(int width, int height, bool fullscreen)
    {
        var scale = WindowScale(SdlNative.SDL_GetDisplayForWindow(_window));
        SdlNative.SDL_SetWindowFullscreen(_window, false);
        SdlNative.SDL_SetWindowSize(_window, Scaled(width, scale), Scaled(height, scale));
        SdlNative.SDL_SetWindowPosition(_window, SdlNative.WindowCentered, SdlNative.WindowCentered);
        if (fullscreen)
        {
            SdlNative.SDL_SetWindowFullscreen(_window, true);
        }
        // SDL3 may carry these out later (on Wayland, X11): the picture is laid out for the size they give
        SdlNative.SDL_SyncWindow(_window);
        UpdateViewport();
        Clear();
    }

    /// <summary>
    /// SDL3 sizes Windows' windows in the screen's pixels (SDL2's program was scaled up by Windows on a screen at more than 100 %,
    /// a little blurred): the program's window sizes (the 720x400 base, the launcher's 1280x720 ...) are multiplied by the screen's
    /// scale there, so that a window keeps the size it had with SDL2, now sharp. Elsewhere SDL3 scales itself (Wayland, macOS) or
    /// never did (X11): 1.
    /// </summary>
    private static float WindowScale(uint display) =>
        OperatingSystem.IsWindows() && display != 0 && SdlNative.SDL_GetDisplayContentScale(display) is var s && s > 0 ? s : 1;

    private static int Scaled(int length, float scale) => (int)Math.Round(length * scale);

    /// <summary>A window coordinate (SDL3 gives them as fractions) in whole points, as SDL2 gave them.</summary>
    private static int Whole(float coordinate) => (int)MathF.Floor(coordinate);

    /// <summary>Both buffers black, so nothing of a previous picture (or of no picture yet) shows until the next frame.</summary>
    internal void Clear()
    {
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        for (var i = 0; i < 2; i++)
        {
            SdlNative.SDL_RenderClear(_renderer);
            SdlNative.SDL_RenderPresent(_renderer);
        }
    }

    /// <summary>The usable part of the main screen before any window is open, or null.</summary>
    internal static (int Width, int Height)? MainScreen()
    {
        if (!SdlNative.SDL_Init(SdlNative.InitVideo | SdlNative.InitEvents))
        {
            return null;
        }
        return Usable(SdlNative.SDL_GetPrimaryDisplay());
    }

    /// <summary>The usable part of the screen the window is on (without the task bar), or null.</summary>
    internal (int Width, int Height)? UsableScreen() => Usable(SdlNative.SDL_GetDisplayForWindow(_window));

    /// <summary>A display's usable part in the program's window sizes (<see cref="WindowScale"/>), or null.</summary>
    private static (int Width, int Height)? Usable(uint display)
    {
        if (display == 0 || !SdlNative.SDL_GetDisplayUsableBounds(display, out var r))
        {
            return null;
        }
        var scale = WindowScale(display);
        return ((int)(r.Width / scale), (int)(r.Height / scale));
    }

    public void Dispose()
    {
        foreach (var (texture, _) in _hdTextures.Values)
        {
            SdlNative.SDL_DestroyTexture(texture);
        }
        _filter.Dispose();
        SdlNative.SDL_DestroyTexture(_texture);
        if (_textTexture != IntPtr.Zero)
        {
            SdlNative.SDL_DestroyTexture(_textTexture);
        }
        SdlNative.SDL_DestroyRenderer(_renderer);
        SdlNative.SDL_DestroyWindow(_window);
        SdlNative.SDL_Quit();
    }

    /// <summary>Where the two pictures go in the window's drawable area (<see cref="DisplayLayout"/>).</summary>
    private void UpdateViewport()
    {
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var w, out var h);
        _viewport = ToSdl(DisplayLayout.Graphics(w, h));
        _textViewport = ToSdl(DisplayLayout.Text(w, h));
    }

    private static SdlNative.SdlRect ToSdl(DisplayRectangle r) =>
        new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    // Mouse events arrive in window coordinates; with high-DPI the renderer may be larger.
    private int ToScreenX(int windowX)
    {
        SdlNative.SDL_GetWindowSize(_window, out var ww, out _);
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var rw, out _);
        var x = windowX * rw / Math.Max(1, ww);
        return DisplayLayout.ToScreen(Graphics, x, 0, _sampling).X;
    }

    private static bool IsSoftware(IntPtr renderer) =>
        string.Equals(SdlNative.Text(SdlNative.SDL_GetRendererName(renderer)), "software", StringComparison.Ordinal);

    private int ToScreenY(int windowY)
    {
        SdlNative.SDL_GetWindowSize(_window, out _, out var wh);
        SdlNative.SDL_GetRenderOutputSize(_renderer, out _, out var rh);
        var y = windowY * rh / Math.Max(1, wh);
        return DisplayLayout.ToScreen(Graphics, 0, y, _sampling).Y;
    }

    private DisplayRectangle Graphics => new(_viewport.X, _viewport.Y, _viewport.Width, _viewport.Height);
}
