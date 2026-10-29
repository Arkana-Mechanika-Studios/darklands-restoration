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

    public IReadOnlyList<LauncherInput> Wait()
    {
        var inputs = new List<LauncherInput>();
        if (SdlNative.SDL_WaitEvent(out var e) == 0)
        {
            return [new CloseRequested()];
        }
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
    }
}
