using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Darklands.Launcher;

namespace Darklands.Host.Sdl;

/// <summary>The SDL window as the launcher's surface: its frames shown through one streaming texture, SDL's events as its input.</summary>
internal sealed class SdlLauncherSurface : ILauncherSurface, IDisposable
{
    private readonly SdlHost _host;
    private readonly TypedKeys _typing = new();
    private IntPtr _texture;
    private (int Width, int Height) _textureSize;

    public SdlLauncherSurface(SdlHost host)
    {
        _host = host;
        SdlHost.ShowPointer(true);
        // a folder dropped on the window (SDL reports files and folders alike)
        SdlNative.SDL_EventState(SdlNative.EventDropFile, 1);
        // no title bar while the launcher shows (its painting fills the window, its own buttons close and minimise it): a drag
        // outside its controls moves the window, the edges resize it; the game's window gets its frame back (Dispose). The window is
        // opened without a frame (LauncherHost.Open); after the game's it loses its frame here.
        _current = this;
        SdlNative.SDL_SetWindowBordered(_host.Window, 0);
        if (OperatingSystem.IsWindows())
        {
            FramelessWindow.Attach(SdlNative.WindowsHandle(_host.Window));
        }
        unsafe
        {
            SdlNative.SDL_SetWindowHitTest(_host.Window, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int*, IntPtr, int>)&HitTest, IntPtr.Zero);
        }
    }

    // the surface the hit test asks (one launcher window at a time)
    private static SdlLauncherSurface? _current;
    private (int X, int Y, int Width, int Height)[] _controls = [];

    /// <summary>How far in from the window's edges (window coordinates) a drag resizes it.</summary>
    internal const int ResizeEdge = 6;

    public bool Frameless => true;

    /// <summary>Fullscreen, as large as its screen, or in gamescope (the Steam Deck's Game Mode, which shows every window over the whole screen).</summary>
    public bool FillsScreen
    {
        get
        {
            if ((SdlNative.SDL_GetWindowFlags(_host.Window) & SdlNative.WindowFullscreen) != 0 || InGamescope())
            {
                return true;
            }
            var display = SdlNative.SDL_GetWindowDisplayIndex(_host.Window);
            SdlNative.SDL_GetWindowSize(_host.Window, out var w, out var h);
            return display >= 0 && SdlNative.SDL_GetDisplayBounds(display, out var screen) == 0 && w >= screen.Width && h >= screen.Height;
        }
    }

    /// <summary>gamescope says so in the environment of the programs it runs.</summary>
    internal static bool InGamescope() =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"), "gamescope", StringComparison.OrdinalIgnoreCase)
        || Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY") is { Length: > 0 };

    public void Minimise() => SdlNative.SDL_MinimizeWindow(_host.Window);

    public void Controls(IReadOnlyList<(int X, int Y, int Width, int Height)> areas) => _controls = [.. areas];

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int HitTest(IntPtr window, int* point, IntPtr data)
    {
        if (_current is not { } surface)
        {
            return SdlNative.HitTestNormal;
        }
        SdlNative.SDL_GetWindowSize(window, out var ww, out var wh);
        var (pw, ph) = surface.Size;
        return Region(point[0], point[1], ww, wh, pw, ph, surface._controls, surface.FillsScreen);
    }

    /// <summary>
    /// What a point of the window (window coordinates) does: near an edge it resizes the window, over a control (surface pixels) it is
    /// clicked, elsewhere (the painting, the parchment's bare parts) it drags the window. A window that fills the screen is neither
    /// moved nor resized.
    /// </summary>
    internal static int Region(int x, int y, int windowWidth, int windowHeight, int pixelWidth, int pixelHeight,
        IReadOnlyList<(int X, int Y, int Width, int Height)> controls, bool fillsScreen)
    {
        if (fillsScreen)
        {
            return SdlNative.HitTestNormal;
        }
        bool left = x < ResizeEdge, right = x >= windowWidth - ResizeEdge, top = y < ResizeEdge, bottom = y >= windowHeight - ResizeEdge;
        switch (top, bottom, left, right)
        {
            case (true, _, true, _): return SdlNative.HitTestResizeTopLeft;
            case (true, _, _, true): return SdlNative.HitTestResizeTopRight;
            case (_, true, _, true): return SdlNative.HitTestResizeBottomRight;
            case (_, true, true, _): return SdlNative.HitTestResizeBottomLeft;
            case (true, _, _, _): return SdlNative.HitTestResizeTop;
            case (_, true, _, _): return SdlNative.HitTestResizeBottom;
            case (_, _, true, _): return SdlNative.HitTestResizeLeft;
            case (_, _, _, true): return SdlNative.HitTestResizeRight;
        }
        var px = x * pixelWidth / Math.Max(1, windowWidth);
        var py = y * pixelHeight / Math.Max(1, windowHeight);
        foreach (var c in controls)
        {
            if (px >= c.X && px < c.X + c.Width && py >= c.Y && py < c.Y + c.Height)
            {
                return SdlNative.HitTestNormal;
            }
        }
        return SdlNative.HitTestDraggable;
    }

    public (int Width, int Height) Size
    {
        get
        {
            SdlNative.SDL_GetRendererOutputSize(_host.SdlRenderer, out var w, out var h);
            return (Math.Max(1, w), Math.Max(1, h));
        }
    }

    public (int Width, int Height)? DisplaySize => _host.UsableScreen();

    /// <summary>Wakes <see cref="Wait"/> (from any thread): the launcher draws again.</summary>
    public static void Wake()
    {
        var e = new SdlNative.SdlEvent { Type = SdlNative.EventUser };
        SdlNative.SDL_PushEvent(ref e);
    }

    /// <summary>Windows' folder dialog; on Linux SDL3's or a dialog program's (<see cref="LinuxDialogs"/>); null elsewhere.</summary>
    public Func<string, string?>? PickFolder
    {
        get
        {
            if (OperatingSystem.IsLinux())
            {
                return title =>
                {
                    var folder = LinuxDialogs.PickFolder(title, PumpWhileOpen);
                    SdlNative.SDL_FlushEvents(SdlNative.EventKeyDown, SdlNative.EventMouseWheel);
                    return folder;
                };
            }
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }
            // CA1416 cannot see the guard above from inside the lambda
#pragma warning disable CA1416
            return title =>
            {
                // the window's messages are handled while the dialog is open; what was clicked or typed meanwhile is dropped
                var folder = WindowsDialogs.PickFolder(title, SdlNative.WindowsHandle(_host.Window), SdlNative.SDL_PumpEvents);
                SdlNative.SDL_FlushEvents(SdlNative.EventKeyDown, SdlNative.EventMouseWheel);
                return folder;
            };
#pragma warning restore CA1416
        }
    }

    /// <summary>Windows' file dialog, or a dialog program on Linux; null elsewhere. For the launcher's SoundFont row.</summary>
    public Func<string, string?>? PickFile
    {
        get
        {
            if (OperatingSystem.IsLinux())
            {
                return title =>
                {
                    var file = LinuxDialogs.PickFile(title, PumpWhileOpen);
                    SdlNative.SDL_FlushEvents(SdlNative.EventKeyDown, SdlNative.EventMouseWheel);
                    return file;
                };
            }
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }
#pragma warning disable CA1416
            return title =>
            {
                var file = WindowsDialogs.PickFile(title, SdlNative.WindowsHandle(_host.Window), SdlNative.SDL_PumpEvents);
                SdlNative.SDL_FlushEvents(SdlNative.EventKeyDown, SdlNative.EventMouseWheel);
                return file;
            };
#pragma warning restore CA1416
        }
    }

    public unsafe void Present(ReadOnlySpan<uint> pixels)
    {
        var size = Size;
        if (_texture == IntPtr.Zero || _textureSize != size)
        {
            if (_texture != IntPtr.Zero)
            {
                SdlNative.SDL_DestroyTexture(_texture);
            }
            _texture = SdlNative.SDL_CreateTexture(_host.SdlRenderer, SdlNative.PixelFormatArgb8888,
                (int)SdlNative.TextureAccessStreaming, size.Width, size.Height);
            _textureSize = size;
        }
        fixed (uint* data = pixels)
        {
            SdlNative.SDL_UpdateTextureFrom(_texture, IntPtr.Zero, (IntPtr)data, size.Width * 4);
        }
        var target = new SdlNative.SdlRect { X = 0, Y = 0, Width = size.Width, Height = size.Height };
        SdlNative.SDL_RenderClear(_host.SdlRenderer);
        SdlNative.SDL_RenderCopyTo(_host.SdlRenderer, _texture, IntPtr.Zero, ref target);
        SdlNative.SDL_RenderPresent(_host.SdlRenderer);
    }

    /// <summary>While a dialog is open: the window's events are handled (and dropped); false once the window is closed.</summary>
    private static bool PumpWhileOpen()
    {
        while (SdlNative.SDL_PollEvent(out var e) != 0)
        {
            if (e.Type == SdlNative.EventQuit)
            {
                // kept for the launcher, which then closes
                SdlNative.SDL_PushEvent(ref e);
                return false;
            }
        }
        return true;
    }

    public IReadOnlyList<LauncherInput> Wait(TimeSpan timeout)
    {
        if (SdlNative.SDL_WaitEventTimeout(out var e, (int)Math.Max(1, timeout.TotalMilliseconds)) == 0)
        {
            return [new TimePassed()];
        }
        return Take(e);
    }

    public IReadOnlyList<LauncherInput> Wait()
    {
        if (SdlNative.SDL_WaitEvent(out var e) == 0)
        {
            return [new CloseRequested()];
        }
        return Take(e);
    }

    /// <summary>The event that came and the others waiting, as the launcher's input.</summary>
    private IReadOnlyList<LauncherInput> Take(SdlNative.SdlEvent e)
    {
        var inputs = new List<LauncherInput>();
        do
        {
            foreach (var input in _typing.Take(e, ToInput(e)))
            {
                inputs.Add(input);
            }
        }
        while (SdlNative.SDL_PollEvent(out e) != 0);
        // nothing the launcher reads (a key release, the pointer leaving): draw again anyway, harmlessly
        return inputs.Count > 0 ? inputs : [new SurfaceResized()];
    }

    public void TextInput(bool on)
    {
        _typing.On = on;
        if (on)
        {
            SdlNative.SDL_StartTextInput();
        }
        else
        {
            SdlNative.SDL_StopTextInput();
        }
    }

    public string? Clipboard() => SdlNative.TakeString(SdlNative.SDL_GetClipboardText());

    private LauncherInput? ToInput(SdlNative.SdlEvent e)
    {
        switch (e.Type)
        {
            case SdlNative.EventQuit:
                return new CloseRequested();
            case SdlNative.EventWindow when e.WindowEvent == SdlNative.WindowEventSizeChanged:
                return new SurfaceResized();
            case SdlNative.EventDropFile:
                return SdlNative.TakeString(e.DropFile) is { } path ? new PathDropped(path) : null;
            case SdlNative.EventTextInput:
                return new TextTyped(e.InputText());
            case SdlNative.EventMouseButtonDown when e.Button.Button == SdlNative.MouseButtonLeft:
                var (x, y) = ToPixels(e.Button.X, e.Button.Y);
                return new PointerPressed(x, y);
            case SdlNative.EventMouseButtonUp when e.Button.Button == SdlNative.MouseButtonLeft:
                var (ux, uy) = ToPixels(e.Button.X, e.Button.Y);
                return new PointerReleased(ux, uy);
            case SdlNative.EventMouseMotion:
                var (mx, my) = ToPixels(e.Motion.X, e.Motion.Y);
                return new PointerMoved(mx, my);
            case SdlNative.EventKeyDown:
                var mod = e.Keyboard.Keysym.Mod;
                var shift = (mod & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
                var control = (mod & (SdlNative.KeyModLeftCtrl | SdlNative.KeyModRightCtrl)) != 0;
                LauncherKey? key = e.Keyboard.Keysym.Sym switch
                {
                    SdlNative.KeyUp => LauncherKey.Up,
                    SdlNative.KeyDown => LauncherKey.Down,
                    SdlNative.KeyLeft => LauncherKey.Left,
                    SdlNative.KeyRight => LauncherKey.Right,
                    SdlNative.KeyReturn or SdlNative.KeyKeypadEnter => LauncherKey.Enter,
                    SdlNative.KeyEscape => LauncherKey.Escape,
                    SdlNative.KeyTab => LauncherKey.Tab,
                    SdlNative.KeyBackspace => LauncherKey.Backspace,
                    SdlNative.KeyDelete => LauncherKey.Delete,
                    SdlNative.KeyHome => LauncherKey.Home,
                    SdlNative.KeyEnd => LauncherKey.End,
                    SdlNative.KeyV when control => LauncherKey.Paste,
                    SdlNative.KeyInsert when shift => LauncherKey.Paste,
                    _ => null,
                };
                return key is { } k ? new KeyPressed(k, shift, control) : null;
            default:
                return null;
        }
    }

    // events come in window coordinates; with high DPI the drawing area has more pixels
    private (int X, int Y) ToPixels(int x, int y)
    {
        SdlNative.SDL_GetWindowSize(_host.Window, out var ww, out var wh);
        var (pw, ph) = Size;
        return (x * pw / Math.Max(1, ww), y * ph / Math.Max(1, wh));
    }

    public void Dispose()
    {
        if (_texture != IntPtr.Zero)
        {
            SdlNative.SDL_DestroyTexture(_texture);
        }
        SdlHost.ShowPointer(false);
        SdlNative.SDL_SetWindowHitTest(_host.Window, IntPtr.Zero, IntPtr.Zero);
        _current = null;
        // the window stays frameless (and filtered, on Windows) until the game takes it (ToGame) or it is hidden and closed (Leave)
    }

    /// <summary>
    /// Play: the window, as the game's, has its frame again (fullscreen: SDL shows none anyway). It is made black first, as the
    /// game's first picture is: else the launcher's painting showed inside the frame for a moment (seen 2026-11-08).
    /// </summary>
    public void ToGame()
    {
        _host.Clear();
        if (OperatingSystem.IsWindows())
        {
            FramelessWindow.Detach();
        }
        SdlNative.SDL_SetWindowBordered(_host.Window, 1);
    }

    /// <summary>
    /// The launcher left without Play: the window is hidden as it is, without its frame, before the program closes it (giving it its
    /// frame back first showed the title bar for a moment as the program ended: the user's check of 2026-11-08).
    /// </summary>
    public void Leave() => SdlNative.SDL_HideWindow(_host.Window);
}
