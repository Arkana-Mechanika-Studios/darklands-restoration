using System.Reflection;
using System.Runtime.InteropServices;

namespace Darklands.Host.Sdl;

internal static class SdlNative
{
    internal const uint InitVideo = 0x00000020;
    internal const uint InitAudio = 0x00000010;
    internal const uint InitEvents = 0x00004000;
    internal const uint WindowShown = 0x00000004;
    internal const uint WindowHidden = 0x00000008;
    internal const uint WindowResizable = 0x00000020;
    internal const uint WindowAllowHighDpi = 0x00002000;
    internal const uint WindowFullscreenDesktop = 0x00001001;
    internal const uint RendererSoftware = 0x00000001;
    internal const uint RendererAccelerated = 0x00000002;
    internal const uint RendererPresentVsync = 0x00000004;
    internal const uint TextureAccessStreaming = 1;
    internal const uint TextureAccessStatic = 0;
    internal const uint TextureAccessTarget = 2;
    internal const uint PixelFormatArgb8888 = 372645892;
    internal const int ScaleModeNearest = 0;
    internal const int ScaleModeLinear = 1;
    internal const int BlendModeBlend = 1;
    internal const uint EventQuit = 0x100;
    internal const uint EventKeyDown = 0x300;
    internal const uint EventKeyUp = 0x301;
    internal const uint EventMouseMotion = 0x400;
    internal const uint EventMouseButtonDown = 0x401;
    internal const uint EventMouseButtonUp = 0x402;
    internal const uint EventMouseWheel = 0x403;
    internal const uint EventTextInput = 0x303;
    internal const uint EventWindow = 0x200;
    internal const uint EventDropFile = 0x1000;
    internal const uint EventUser = 0x8000;
    internal const byte WindowEventSizeChanged = 6;
    internal const int WindowCentered = 0x2FFF0000;
    internal const int KeyTab = 9;
    internal const int KeyV = 118;
    internal const int ScancodeF11 = 68;
    internal const byte MouseButtonLeft = 1;
    internal const byte MouseButtonRight = 3;
    internal const int KeyReturn = 13;
    internal const int KeyEscape = 27;
    internal const int KeySpace = 32;
    internal const int KeyMinus = 45;
    internal const int KeyBackspace = 8;
    internal const int Key1 = 49;
    internal const int Key2 = 50;
    internal const int Key3 = 51;
    internal const int Key4 = 52;
    internal const int Key5 = 53;
    internal const int KeyA = 97;
    internal const int KeyB = 98;
    internal const int KeyC = 99;
    internal const int KeyD = 100;
    internal const int KeyE = 101;
    internal const int KeyF = 102;
    internal const int KeyG = 103;
    internal const int KeyJ = 106;
    internal const int KeyL = 108;
    internal const int KeyM = 109;
    internal const int KeyP = 112;
    internal const int KeyQ = 113;
    internal const int KeyR = 114;
    internal const int KeyS = 115;
    internal const int KeyT = 116;
    internal const int KeyU = 117;
    internal const int KeyF1 = 1073741882;
    internal const int KeyF2 = 1073741883;
    internal const int KeyF3 = 1073741884;
    internal const int KeyF4 = 1073741885;
    internal const int KeyF5 = 1073741886;
    internal const int KeyF6 = 1073741887;
    internal const int KeyF10 = 1073741891;
    internal const int KeyF12 = 1073741893;
    internal const int KeyPageUp = 1073741899;
    internal const int KeyHome = 1073741898;
    internal const int KeyEnd = 1073741901;
    internal const int KeyDelete = 127;
    internal const int KeyKeypadEnter = 1073741912;
    internal const int KeyPageDown = 1073741902;
    internal const int KeyRight = 1073741903;
    internal const int KeyLeft = 1073741904;
    internal const int KeyDown = 1073741905;
    internal const int KeyUp = 1073741906;
    internal const int KeyKeypadMinus = 1073741910;
    internal const int KeyLeftShift = 1073742049;
    internal const int KeyRightShift = 1073742053;
    internal const int KeyLeftCtrl = 1073742048;
    internal const int KeyRightCtrl = 1073742052;
    internal const int KeyModLeftShift = 0x0001;
    internal const int KeyModRightShift = 0x0002;
    internal const int KeyModLeftCtrl = 0x0040;
    internal const int KeyModRightCtrl = 0x0080;
    internal const int KeyModAlt = 0x0300;
    internal const int KeyModGui = 0x0C00;
    internal const int KeyModCaps = 0x2000;
    internal const int KeyModAltGr = 0x4000;
    internal const int KeyInsert = 1073741897;

    private const string LibraryName = "SDL2";
    private static readonly object ResolverLock = new();
    private static string? _explicitLibraryPath;
    private static bool _resolverInstalled;

    internal static void ConfigureLibrary(string? explicitLibraryPath)
    {
        lock (ResolverLock)
        {
            _explicitLibraryPath = explicitLibraryPath;
            if (_resolverInstalled)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(SdlNative).Assembly, ResolveLibrary);
            _resolverInstalled = true;
        }
    }

    internal static string GetErrorText()
    {
        var pointer = SDL_GetError();
        return pointer == IntPtr.Zero ? "unknown SDL error" : Marshal.PtrToStringUTF8(pointer) ?? "unknown SDL error";
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in CandidateLibraryPaths())
        {
            var usable = !Path.IsPathRooted(candidate) || File.Exists(candidate);
            if (usable && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidateLibraryPaths()
    {
        if (!string.IsNullOrWhiteSpace(_explicitLibraryPath))
        {
            yield return _explicitLibraryPath;
        }

        var configured = Environment.GetEnvironmentVariable("DARKLANDS_SDL2_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
        yield return "SDL2.dll";
        // Linux (SteamOS included): a copy beside the program first, then the system's (the runtime tries ld.so's paths for a bare name)
        yield return Path.Combine(AppContext.BaseDirectory, "libSDL2-2.0.so.0");
        yield return "libSDL2-2.0.so.0";
        yield return "libSDL2-2.0.so";
        yield return "libSDL2.so";
        yield return "libSDL2.dylib";
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_Init(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_Quit();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_InitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetError();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int SDL_SetHint(string name, string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr SDL_CreateWindow(
        string title,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyWindow(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_GetWindowSize(IntPtr window, out int width, out int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyRenderer(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetRendererOutputSize(IntPtr renderer, out int width, out int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateTexture(
        IntPtr renderer,
        uint format,
        int access,
        int width,
        int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyTexture(IntPtr texture);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetTextureScaleMode(IntPtr texture, int scaleMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetTextureBlendMode(IntPtr texture, int blendMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rectangle, int[] pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rectangle, uint[] pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_UpdateTexture(IntPtr texture, ref SdlRect rectangle, IntPtr pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_RenderClear(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetRenderDrawColor(
        IntPtr renderer,
        byte red,
        byte green,
        byte blue,
        byte alpha);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderCopy")]
    internal static extern int SDL_RenderCopyTo(
        IntPtr renderer,
        IntPtr texture,
        IntPtr source,
        ref SdlRect destination);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_RenderPresent(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderCopy")]
    internal static extern int SDL_RenderCopyPart(IntPtr renderer, IntPtr texture, ref SdlRect source, ref SdlRect destination);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_RenderFlush(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetRenderTarget(IntPtr renderer, IntPtr texture);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_RenderReadPixels(IntPtr renderer, IntPtr rectangle, uint format, int[] pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_RenderFillRect(IntPtr renderer, ref SdlRect rectangle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetRenderDrawBlendMode(IntPtr renderer, int blendMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetTextureColorMod(IntPtr texture, byte red, byte green, byte blue);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_StartTextInput();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_StopTextInput();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_PollEvent(out SdlEvent @event);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_PushEvent(ref SdlEvent @event);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte SDL_EventState(uint type, int state);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_ShowCursor(int toggle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetModState();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_WarpMouseInWindow(IntPtr window, int x, int y);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void SDL_SetWindowTitle(IntPtr window, string title);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_OpenAudioDevice(
        IntPtr device,
        int iscapture,
        ref SdlAudioSpec desired,
        out SdlAudioSpec obtained,
        int allowedChanges);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_PauseAudioDevice(uint deviceId, int pauseOn);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_QueueAudio(uint deviceId, IntPtr data, uint length);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_ClearQueuedAudio(uint deviceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetQueuedAudioSize(uint deviceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_CloseAudioDevice(uint deviceId);

    /// <summary>SDL_AudioCallback: fill <paramref name="stream"/> with <paramref name="length"/> bytes.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AudioCallback(IntPtr userdata, IntPtr stream, int length);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlAudioSpec
    {
        internal int Freq;
        internal ushort Format;
        internal byte Channels;
        internal byte Silence;
        internal ushort Samples;
        internal uint Size;
        internal IntPtr Callback;
        internal IntPtr Userdata;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_WaitEvent(out SdlEvent @event);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_PumpEvents();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_FlushEvents(uint minType, uint maxType);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_SetWindowSize(IntPtr window, int width, int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_SetWindowPosition(IntPtr window, int x, int y);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_SetWindowFullscreen(IntPtr window, uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetWindowDisplayIndex(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetDesktopDisplayMode(int displayIndex, out SdlDisplayMode mode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetDisplayUsableBounds(int displayIndex, out SdlRect rectangle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int SDL_OpenURL([MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetClipboardText();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_free(IntPtr memory);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_UpdateTexture")]
    internal static extern int SDL_UpdateTextureFrom(IntPtr texture, IntPtr rectangle, IntPtr pixels, int pitch);

    /// <summary>A string SDL allocated (clipboard text, a dropped file's path), read as UTF-8 and freed.</summary>
    internal static string? TakeString(IntPtr text)
    {
        if (text == IntPtr.Zero)
        {
            return null;
        }
        var value = Marshal.PtrToStringUTF8(text);
        SDL_free(text);
        return value;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_GetVersion(out SdlVersion version);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetWindowWMInfo(IntPtr window, ref SdlSysWmInfo info);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetCurrentAudioDriver();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetNumAudioDrivers();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetAudioDriver(int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetNumAudioDevices(int isCapture);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetAudioDeviceName(int index, int isCapture);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetAudioDeviceStatus(uint deviceId);

    /// <summary>SDL 2.24 and later: the default output device's name (freed with SDL_free) and its own format.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetDefaultAudioInfo(out IntPtr name, out SdlAudioSpec spec, int isCapture);

    /// <summary>A string SDL owns (not freed), or null.</summary>
    internal static string? Text(IntPtr text) => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);

    /// <summary>The SDL2 library's version, e.g. "2.32.56"; sdl2-compat (SDL2 on top of SDL3) numbers its patch level 50 and up.</summary>
    internal static (string Text, bool Sdl2Compat) LinkedVersion()
    {
        SDL_GetVersion(out var v);
        return ($"{v.Major}.{v.Minor}.{v.Patch}", v.Major == 2 && v.Patch >= 50);
    }

    /// <summary>The window's Windows handle (HWND), or zero elsewhere or when SDL cannot tell.</summary>
    internal static IntPtr WindowsHandle(IntPtr window)
    {
        var info = new SdlSysWmInfo();
        SDL_GetVersion(out info.Version);
        return SDL_GetWindowWMInfo(window, ref info) != 0 && info.Subsystem == 1 ? info.Window : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlVersion
    {
        internal byte Major;
        internal byte Minor;
        internal byte Patch;
    }

    // SDL_SysWMinfo: the version, the subsystem (1: Windows), then a union whose Windows member starts with the HWND
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct SdlSysWmInfo
    {
        [FieldOffset(0)]
        internal SdlVersion Version;

        [FieldOffset(4)]
        internal int Subsystem;

        [FieldOffset(8)]
        internal IntPtr Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlDisplayMode
    {
        internal uint Format;
        internal int Width;
        internal int Height;
        internal int RefreshRate;
        internal IntPtr DriverData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlRect
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlKeysym
    {
        internal int Scancode;
        internal int Sym;
        internal ushort Mod;
        internal uint Unused;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlKeyboardEvent
    {
        internal uint Type;
        internal uint Timestamp;
        internal uint WindowId;
        internal byte State;
        internal byte Repeat;
        internal byte Padding2;
        internal byte Padding3;
        internal SdlKeysym Keysym;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlMouseMotionEvent
    {
        internal uint Type;
        internal uint Timestamp;
        internal uint WindowId;
        internal uint Which;
        internal uint State;
        internal int X;
        internal int Y;
        internal int XRelative;
        internal int YRelative;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlMouseButtonEvent
    {
        internal uint Type;
        internal uint Timestamp;
        internal uint WindowId;
        internal uint Which;
        internal byte Button;
        internal byte State;
        internal byte Clicks;
        internal byte Padding1;
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlMouseWheelEvent
    {
        internal uint Type;
        internal uint Timestamp;
        internal uint WindowId;
        internal uint Which;
        internal int X;
        internal int Y;
        internal uint Direction;
    }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct SdlEvent
    {
        [FieldOffset(0)]
        internal uint Type;

        [FieldOffset(0)]
        internal SdlKeyboardEvent Keyboard;

        [FieldOffset(0)]
        internal SdlMouseMotionEvent Motion;

        [FieldOffset(0)]
        internal SdlMouseButtonEvent Button;

        [FieldOffset(0)]
        internal SdlMouseWheelEvent Wheel;

        // SDL_DropEvent: char *file after type and timestamp (the receiver frees it)
        [FieldOffset(8)]
        internal IntPtr DropFile;

        // SDL_WindowEvent: Uint8 event after type, timestamp and window id
        [FieldOffset(12)]
        internal byte WindowEvent;

        // SDL_TextInputEvent: char text[32] (UTF-8, zero-terminated) after type, timestamp and window id
        [FieldOffset(12)]
        internal long Text0;

        [FieldOffset(20)]
        internal long Text1;

        [FieldOffset(28)]
        internal long Text2;

        [FieldOffset(36)]
        internal long Text3;

        /// <summary>The text of an <see cref="EventTextInput"/> event.</summary>
        internal readonly string InputText()
        {
            Span<byte> bytes = stackalloc byte[32];
            BitConverter.TryWriteBytes(bytes, Text0);
            BitConverter.TryWriteBytes(bytes[8..], Text1);
            BitConverter.TryWriteBytes(bytes[16..], Text2);
            BitConverter.TryWriteBytes(bytes[24..], Text3);
            var end = bytes.IndexOf((byte)0);
            return System.Text.Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
        }
    }
}

internal sealed class SdlHostException : Exception
{
    internal SdlHostException(string message)
        : base(message)
    {
    }
}
