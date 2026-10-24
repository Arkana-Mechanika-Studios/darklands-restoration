using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl;

/// <summary>
/// An SDL2 window that shows the PC's picture as <see cref="DisplayLayout"/> places it (base size
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
    // the BIOS keyboard buffer with the game's keyboard interrupt in front of it (BiosKeyBuffer): SDL's presses and repeats go in
    // as the keyboard's makes and typematic repeats, bounded and with a held key's repeats collapsed as in the original
    private readonly BiosKeyBuffer _keys = new();
    private SdlNative.SdlRect _viewport;
    private SdlNative.SdlRect _textViewport;
    private MouseState _mouse;
    private bool _shift;
    private bool _framePresented;
    private bool _showingText;
    private HdFrame? _hdFrame;
    private readonly Dictionary<RgbaImage, (IntPtr Texture, int Version)> _hdTextures = [];

    /// <summary>Opens the window at the size of <see cref="DisplayLayout.WindowSize"/> (720×400 by default).</summary>
    public SdlHost(string title, int width = DisplayLayout.BaseWidth, int height = DisplayLayout.BaseHeight, bool fullscreen = false)
    {
        if (SdlNative.SDL_Init(SdlNative.InitVideo | SdlNative.InitEvents) != 0)
        {
            throw new SdlHostException("SDL_Init failed: " + SdlNative.GetErrorText());
        }
        _window = SdlNative.SDL_CreateWindow(title, 0x2FFF0000, 0x2FFF0000, width, height,
            SdlNative.WindowShown | SdlNative.WindowResizable | SdlNative.WindowAllowHighDpi
            | (fullscreen ? SdlNative.WindowFullscreenDesktop : 0));
        if (_window == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateWindow failed: " + SdlNative.GetErrorText());
        }
        _renderer = SdlNative.SDL_CreateRenderer(_window, -1, SdlNative.RendererAccelerated | SdlNative.RendererPresentVsync);
        if (_renderer == IntPtr.Zero)
        {
            _renderer = SdlNative.SDL_CreateRenderer(_window, -1, SdlNative.RendererSoftware);
        }
        _texture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888,
            (int)SdlNative.TextureAccessStreaming, Screen.Width, Screen.Height);
        SdlNative.SDL_SetTextureScaleMode(_texture, SdlNative.ScaleModeNearest);
        SdlNative.SDL_ShowCursor(0);
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

    /// <summary>A line drawn small in the window's bottom-right corner when it gives one (the version on the start screen).</summary>
    public CornerText? Corner { get; set; }

    /// <summary>
    /// Replacement graphics (null: none): each frame is drawn as the renderer's items, the game's frame first and the
    /// replacements over it. The host only puts the items on the graphics card.
    /// </summary>
    public HdRenderer? Renderer { get; set; }

    public MouseState Mouse => _mouse;

    public bool ShiftHeld => _shift;

    private readonly Queue<(int ScanCode, bool Released)> _transitions = new();

    public bool TryReadKeyTransition(out int scanCode, out bool released)
    {
        if (_transitions.TryDequeue(out var t))
        {
            (scanCode, released) = t;
            return true;
        }
        (scanCode, released) = (0, false);
        return false;
    }

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

    /// <summary>The pointer to a screen pixel: the window point at the middle of that pixel of the picture.</summary>
    public void MovePointer(int x, int y)
    {
        SdlNative.SDL_GetWindowSize(_window, out var ww, out var wh);
        SdlNative.SDL_GetRendererOutputSize(_renderer, out var rw, out var rh);
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
        SdlNative.SDL_RenderCopyTo(_renderer, _texture, IntPtr.Zero, ref _viewport);
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
                SdlNative.SDL_RenderCopyTo(_renderer, _texture, IntPtr.Zero, ref destination);
                continue;
            }
            used.Add(item.Texture);
            SdlNative.SDL_RenderCopyTo(_renderer, HdTexture(item), IntPtr.Zero, ref destination);
        }
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
            texture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessStatic, image.Width, image.Height);
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
        SdlNative.SDL_GetRendererOutputSize(_renderer, out var w, out var h);
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
            SdlNative.SDL_RenderCopyTo(_renderer, _textTexture, IntPtr.Zero, ref _textViewport);
        }
        else if (_hdFrame is not null)
        {
            // the items at the window's current size: the renderer places them again
            _hdFrame = HdRenderer.Relayout(_hdFrame, Graphics);
            DrawHdFrame(_hdFrame);
        }
        else
        {
            SdlNative.SDL_RenderCopyTo(_renderer, _texture, IntPtr.Zero, ref _viewport);
        }
    }

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
        SdlNative.SDL_GetRendererOutputSize(_renderer, out width, out height);
        var pixels = new int[width * height];
        if (SdlNative.SDL_RenderReadPixels(_renderer, IntPtr.Zero, SdlNative.PixelFormatArgb8888, pixels, width * 4) != 0)
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
                (int)SdlNative.TextureAccessStreaming, TextModeRaster.Width, TextModeRaster.Height);
            SdlNative.SDL_SetTextureScaleMode(_textTexture, SdlNative.ScaleModeNearest);
        }
        TextModeRaster.Render(screen, TextFont, Clock.Milliseconds, System.Runtime.InteropServices.MemoryMarshal.Cast<int, uint>(_textArgb.AsSpan()));
        SdlNative.SDL_UpdateTexture(_textTexture, IntPtr.Zero, _textArgb, TextModeRaster.Width * 4);
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        UpdateViewport();
        SdlNative.SDL_RenderCopyTo(_renderer, _textTexture, IntPtr.Zero, ref _textViewport);
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
        while (SdlNative.SDL_PollEvent(out var e) != 0)
        {
            if (e.Type == SdlNative.EventKeyDown && e.Keyboard.Keysym.Scancode == SdlNative.ScancodeF11 && Console is not null)
            {
                if (e.Keyboard.Repeat == 0)
                {
                    SetConsoleOpen(!Console.Console.IsOpen);
                    toggledThisPump = true;
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
                    _shift = (e.Keyboard.Keysym.Mod & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
                    if (e.Keyboard.Repeat == 0 && PcScanCode(e.Keyboard.Keysym.Scancode) is var make and not 0)
                    {
                        _transitions.Enqueue((make, false));
                    }
                    var key = SdlKeyMap.ToGameKey(e.Keyboard.Keysym.Scancode, e.Keyboard.Keysym.Sym, e.Keyboard.Keysym.Mod);
                    if (key != Keys.None)
                    {
                        _keys.Press(key);
                    }
                    else
                    {
                        _keys.ScanCode();
                    }
                    break;
                case SdlNative.EventKeyUp:
                    _keys.ScanCode();
                    _shift = (e.Keyboard.Keysym.Mod & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
                    if (PcScanCode(e.Keyboard.Keysym.Scancode) is var brk and not 0)
                    {
                        _transitions.Enqueue((brk, true));
                    }
                    break;
                case SdlNative.EventMouseMotion:
                    _mouse = _mouse with { X = ToScreenX(e.Motion.X), Y = ToScreenY(e.Motion.Y) };
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
                    _mouse = new MouseState(ToScreenX(e.Button.X), ToScreenY(e.Button.Y), buttons);
                    break;
            }
        }
        // the game may pump without presenting while it waits: the console is drawn on (and taken away when it closes)
        if (Console is not null && (toggledThisPump || (Console.Console.IsOpen && !_framePresented)))
        {
            Redraw();
        }
        _framePresented = false;
    }

    private MouseButtons _releaseNextPump;

    private void SetConsoleOpen(bool open)
    {
        Console!.Console.IsOpen = open;
        if (open)
        {
            SdlNative.SDL_StartTextInput();
        }
        else
        {
            SdlNative.SDL_StopTextInput();
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
                switch (e.Keyboard.Keysym.Sym)
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
                console.Scroll(e.Wheel.Y * 3);
                return true;
            case SdlNative.EventMouseMotion or SdlNative.EventMouseButtonDown or SdlNative.EventMouseButtonUp:
                return true;
            default:
                // key releases still reach the game: a key held when the console opened is let go
                return false;
        }
    }

    public bool TryReadKey(out int key) => _keys.TryRead(out key);

    /// <summary>The SDL window and renderer, for another user of the window before the game (the launcher).</summary>
    internal IntPtr Window => _window;

    internal IntPtr SdlRenderer => _renderer;

    /// <summary>The system's mouse pointer (the game draws its own, so it is hidden while the game runs).</summary>
    internal static void ShowPointer(bool on) => SdlNative.SDL_ShowCursor(on ? 1 : 0);

    /// <summary>Gives the window a new size (centred) or the whole screen.</summary>
    internal void Resize(int width, int height, bool fullscreen)
    {
        SdlNative.SDL_SetWindowFullscreen(_window, 0);
        SdlNative.SDL_SetWindowSize(_window, width, height);
        SdlNative.SDL_SetWindowPosition(_window, SdlNative.WindowCentered, SdlNative.WindowCentered);
        if (fullscreen)
        {
            SdlNative.SDL_SetWindowFullscreen(_window, SdlNative.WindowFullscreenDesktop);
        }
        UpdateViewport();
        Clear();
    }

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
        if (SdlNative.SDL_Init(SdlNative.InitVideo | SdlNative.InitEvents) != 0)
        {
            return null;
        }
        return SdlNative.SDL_GetDisplayUsableBounds(0, out var r) == 0 ? (r.Width, r.Height) : null;
    }

    /// <summary>The usable part of the screen the window is on (without the task bar), or null.</summary>
    internal (int Width, int Height)? UsableScreen()
    {
        var display = SdlNative.SDL_GetWindowDisplayIndex(_window);
        return display >= 0 && SdlNative.SDL_GetDisplayUsableBounds(display, out var r) == 0 ? (r.Width, r.Height) : null;
    }

    public void Dispose()
    {
        foreach (var (texture, _) in _hdTextures.Values)
        {
            SdlNative.SDL_DestroyTexture(texture);
        }
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
        SdlNative.SDL_GetRendererOutputSize(_renderer, out var w, out var h);
        _viewport = ToSdl(DisplayLayout.Graphics(w, h));
        _textViewport = ToSdl(DisplayLayout.Text(w, h));
    }

    private static SdlNative.SdlRect ToSdl(DisplayRectangle r) =>
        new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    // Mouse events arrive in window coordinates; with high-DPI the renderer may be larger.
    private int ToScreenX(int windowX)
    {
        SdlNative.SDL_GetWindowSize(_window, out var ww, out _);
        SdlNative.SDL_GetRendererOutputSize(_renderer, out var rw, out _);
        var x = windowX * rw / Math.Max(1, ww);
        return DisplayLayout.ToScreen(Graphics, x, 0).X;
    }

    private int ToScreenY(int windowY)
    {
        SdlNative.SDL_GetWindowSize(_window, out _, out var wh);
        SdlNative.SDL_GetRendererOutputSize(_renderer, out _, out var rh);
        var y = windowY * rh / Math.Max(1, wh);
        return DisplayLayout.ToScreen(Graphics, 0, y).Y;
    }

    private DisplayRectangle Graphics => new(_viewport.X, _viewport.Y, _viewport.Width, _viewport.Height);
}
