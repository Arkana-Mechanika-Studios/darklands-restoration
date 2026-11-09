using System.Reflection;
using System.Runtime.InteropServices;

namespace Darklands.Host.Sdl;

/// <summary>
/// The SDL3 functions the host uses (SDL 3.4). SDL3's <c>bool</c> is one byte: every one is marshalled as <see cref="UnmanagedType.U1"/>,
/// and its functions answer true on success (SDL2's answered 0). Strings go to SDL as UTF-8.
/// </summary>
internal static class SdlNative
{
    internal const uint InitVideo = 0x00000020;
    internal const uint InitAudio = 0x00000010;
    internal const uint InitEvents = 0x00004000;
    // SDL_WindowFlags (64-bit in SDL3); a window shows when it is made unless it is hidden (SDL2's SDL_WINDOW_SHOWN is gone)
    internal const ulong WindowFullscreen = 0x00000001;
    internal const ulong WindowHidden = 0x00000008;
    internal const ulong WindowBorderless = 0x00000010;
    internal const ulong WindowResizable = 0x00000020;
    internal const ulong WindowHighPixelDensity = 0x00002000;
    internal const int TextureAccessStatic = 0;
    internal const int TextureAccessStreaming = 1;
    internal const int TextureAccessTarget = 2;
    internal const uint PixelFormatArgb8888 = 372645892;
    internal const int ScaleModeNearest = 0;
    internal const int ScaleModeLinear = 1;
    // SDL3 gives a texture with an alpha channel alpha blending when it is made (SDL2: none): the game's pictures set "none" again
    internal const uint BlendModeNone = 0;
    internal const uint BlendModeBlend = 1;
    internal const uint EventQuit = 0x100;
    // SDL2's one window event (0x200, its kind in the event) is a range of event types in SDL3
    internal const uint EventWindowFirst = 0x202;
    internal const uint EventWindowResized = 0x206;
    internal const uint EventWindowPixelSizeChanged = 0x207;
    internal const uint EventWindowMouseEnter = 0x20C;
    internal const uint EventWindowMouseLeave = 0x20D;
    internal const uint EventWindowLast = 0x21A;
    internal const uint EventKeyDown = 0x300;
    internal const uint EventKeyUp = 0x301;
    internal const uint EventTextInput = 0x303;
    internal const uint EventMouseMotion = 0x400;
    internal const uint EventMouseButtonDown = 0x401;
    internal const uint EventMouseButtonUp = 0x402;
    internal const uint EventMouseWheel = 0x403;
    internal const uint MouseWheelFlipped = 1;
    internal const uint EventDropFile = 0x1000;
    internal const uint EventUser = 0x8000;
    internal const int WindowCentered = 0x2FFF0000;
    internal const int KeyTab = 9;
    internal const int KeyV = 118;
    internal const int ScancodeF11 = 68;
    internal const int ScancodeF12 = 69;
    internal const int KeyModCtrl = 0x00C0;
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

    private const string LibraryName = "SDL3";
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

    /// <summary>Whether an SDL3 library can be found and loaded (the places <see cref="ConfigureLibrary"/> set up), before any call needs it.</summary>
    internal static bool CanLoad() => ResolveLibrary(LibraryName, typeof(SdlNative).Assembly, null) != IntPtr.Zero;

    internal static string GetErrorText()
    {
        var pointer = SDL_GetError();
        var text = pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
        return string.IsNullOrEmpty(text) ? "unknown SDL error" : text;
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
                // an SDL2 library named in an older settings file (sdl.library) or DARKLANDS_SDL3_PATH: passed over, said
                if (!NativeLibrary.TryGetExport(handle, "SDL_CreateWindowWithProperties", out _))
                {
                    NativeLibrary.Free(handle);
                    Console.Error.WriteLine($"SDL: {candidate} is not an SDL3 library (SDL 3.2 or later); passed over.");
                    continue;
                }
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

        var configured = Environment.GetEnvironmentVariable("DARKLANDS_SDL3_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        yield return "SDL3.dll";
        // Linux (SteamOS included): a copy beside the program first, then the system's or the Steam runtime's (the runtime tries
        // ld.so's paths for a bare name)
        yield return Path.Combine(AppContext.BaseDirectory, "libSDL3.so.0");
        yield return "libSDL3.so.0";
        yield return "libSDL3.so";
        yield return "libSDL3.dylib";
    }

    // ---- start, hints, errors

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_Init(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_Quit();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetError();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    /// <summary>SDL_HINT_KEYCODE_OPTIONS ("SDL_KEYCODE_OPTIONS"): how the key events' key codes are made.</summary>
    internal const string HintKeycodeOptions = "SDL_KEYCODE_OPTIONS";

    /// <summary>SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES: the frames of an audio device's buffer (asked when it is opened).</summary>
    internal const string HintAudioDeviceSampleFrames = "SDL_AUDIO_DEVICE_SAMPLE_FRAMES";

    /// <summary>SDL_HINT_RENDER_DRIVER: the renderer to make ("software": no graphics card).</summary>
    internal const string HintRenderDriver = "SDL_RENDER_DRIVER";

    // ---- properties (SDL3's named values of a window, a dialog ...)

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_CreateProperties();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyProperties(uint properties);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetStringProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetNumberProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetBooleanProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.U1)] bool value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetPointerProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetPointerProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr defaultValue);

    // ---- windows and displays

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateWindowWithProperties(uint properties);

    /// <summary>
    /// A window at the middle of the main screen, or of <paramref name="display"/> (SDL_WINDOWPOS_CENTERED_DISPLAY; SDL_CreateWindow
    /// cannot place it), or IntPtr.Zero (SDL_GetError says why).
    /// </summary>
    internal static IntPtr CreateCentredWindow(string title, int width, int height, ulong flags, uint display = 0)
    {
        var properties = SDL_CreateProperties();
        try
        {
            SDL_SetStringProperty(properties, "SDL.window.create.title", title);
            SDL_SetNumberProperty(properties, "SDL.window.create.x", WindowCentered | (int)display);
            SDL_SetNumberProperty(properties, "SDL.window.create.y", WindowCentered | (int)display);
            SDL_SetNumberProperty(properties, "SDL.window.create.width", width);
            SDL_SetNumberProperty(properties, "SDL.window.create.height", height);
            SDL_SetNumberProperty(properties, "SDL.window.create.flags", (long)flags);
            return SDL_CreateWindowWithProperties(properties);
        }
        finally
        {
            SDL_DestroyProperties(properties);
        }
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int width, int height, ulong flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyWindow(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetWindowSize(IntPtr window, out int width, out int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowSize(IntPtr window, int width, int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowMinimumSize(IntPtr window, int width, int height);

    /// <summary>Takes the window's frame and title bar away (false) or gives them back (true).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowBordered(IntPtr window, [MarshalAs(UnmanagedType.U1)] bool bordered);

    /// <summary>
    /// Sets the function that says what a point of the window is (<c>SDL_HitTestResult (*)(SDL_Window*, const SDL_Point*, void*)</c>):
    /// a normal point, one that drags the window, or an edge that resizes it; IntPtr.Zero: none.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowHitTest(IntPtr window, IntPtr callback, IntPtr data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_MinimizeWindow(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_HideWindow(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ShowWindow(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RaiseWindow(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong SDL_GetWindowFlags(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowPosition(IntPtr window, int x, int y);

    /// <summary>The window over the whole screen (true: SDL3's "desktop" fullscreen, no mode change) or windowed again (false).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowFullscreen(IntPtr window, [MarshalAs(UnmanagedType.U1)] bool fullscreen);

    /// <summary>Waits until the window's size, place and fullscreen state asked for have been made (or a short time passed).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SyncWindow(IntPtr window);

    /// <summary>The display the window is on (0: an error).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetDisplayForWindow(IntPtr window);

    /// <summary>The main display (0: an error).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetPrimaryDisplay();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetDisplayBounds(uint display, out SdlRect rectangle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetDisplayUsableBounds(uint display, out SdlRect rectangle);

    /// <summary>The display's scale for its contents (1.5 at Windows' 150 %; 0 on an error).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern float SDL_GetDisplayContentScale(uint display);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetWindowProperties(IntPtr window);

    /// <summary>The window's Windows handle (HWND), or zero elsewhere or when SDL cannot tell.</summary>
    internal static IntPtr WindowsHandle(IntPtr window) =>
        SDL_GetPointerProperty(SDL_GetWindowProperties(window), "SDL.window.win32.hwnd", IntPtr.Zero);

    // SDL_HitTestResult
    internal const int HitTestNormal = 0, HitTestDraggable = 1, HitTestResizeTopLeft = 2, HitTestResizeTop = 3, HitTestResizeTopRight = 4,
        HitTestResizeRight = 5, HitTestResizeBottomRight = 6, HitTestResizeBottom = 7, HitTestResizeBottomLeft = 8, HitTestResizeLeft = 9;

    // ---- the renderer and its textures

    /// <summary>The renderer: <paramref name="name"/> null: SDL's choice (a graphics card's, else the software one); "software".</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateRenderer(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyRenderer(IntPtr renderer);

    /// <summary>The renderer's name ("direct3d11", "opengl", "software" ...), owned by SDL.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetRendererName(IntPtr renderer);

    /// <summary>1: present waits for the screen's refresh; 0: it does not.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetRenderVSync(IntPtr renderer, int vsync);

    /// <summary>The window's drawing area in pixels (SDL2's SDL_GetRenderOutputSize).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetRenderOutputSize(IntPtr renderer, out int width, out int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int width, int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyTexture(IntPtr texture);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetTextureScaleMode(IntPtr texture, int scaleMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetTextureBlendMode(IntPtr texture, uint blendMode);

    // the picture filters (SdlPictureFilter, SdlShaderPictureFilter): SDL_BLENDMODE_MOD, SDL_ComposeCustomBlendMode's factors and operation
    internal const uint BlendModeMod = 0x4;
    internal const uint BlendModeBlendPremultiplied = 0x10;
    internal const int BlendFactorZero = 1, BlendFactorOne = 2, BlendFactorOneMinusSrcAlpha = 6, BlendFactorDstColor = 7;
    internal const int BlendOperationAdd = 1;

    /// <summary>A blend mode of its own: dst = src * srcFactor (op) dst * dstFactor, colour and alpha apart (SDL_BLENDMODE_INVALID when the renderer has none).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_ComposeCustomBlendMode(int srcColorFactor, int dstColorFactor, int colorOperation,
        int srcAlphaFactor, int dstAlphaFactor, int alphaOperation);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetTextureBlendMode(IntPtr texture, out uint blendMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetTextureScaleMode(IntPtr texture, out int scaleMode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateRendererWithProperties(uint properties);

    // SDL_CreateRendererWithProperties' keys (SDL 3.4: the GPU renderer told which shader formats the program brings)
    internal const string PropRendererCreateName = "SDL.renderer.create.name";
    internal const string PropRendererCreateWindow = "SDL.renderer.create.window";
    internal const string PropRendererCreateShadersDxil = "SDL.renderer.create.gpu.shaders_dxil";
    internal const string PropRendererCreateShadersSpirv = "SDL.renderer.create.gpu.shaders_spirv";

    // SDL_GPUShaderFormat, SDL_GPUShaderStage
    internal const uint GpuShaderFormatSpirv = 1u << 1, GpuShaderFormatDxil = 1u << 3;
    internal const int GpuShaderStageFragment = 1;

    /// <summary>SDL_GPUShaderCreateInfo.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuShaderCreateInfo
    {
        internal nuint CodeSize;
        internal IntPtr Code;
        internal IntPtr Entrypoint;
        internal uint Format;
        internal int Stage;
        internal uint NumSamplers;
        internal uint NumStorageTextures;
        internal uint NumStorageBuffers;
        internal uint NumUniformBuffers;
        internal uint Props;
    }

    /// <summary>SDL_GPURenderStateCreateInfo (SDL 3.4).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuRenderStateCreateInfo
    {
        internal IntPtr FragmentShader;
        internal int NumSamplerBindings;
        internal IntPtr SamplerBindings;
        internal int NumStorageTextures;
        internal IntPtr StorageTextures;
        internal int NumStorageBuffers;
        internal IntPtr StorageBuffers;
        internal uint Props;
    }

    /// <summary>The GPU device of SDL's "gpu" renderer (null with any other renderer).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetGPURendererDevice(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetGPUShaderFormats(IntPtr device);

    /// <summary>The GPU device's backend: "direct3d12", "vulkan", "metal".</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetGPUDeviceDriver(IntPtr device);

    /// <summary>SDL_HINT_GPU_DRIVER: the GPU backend SDL's GPU devices use (a test forces "vulkan").</summary>
    internal const string HintGpuDriver = "SDL_GPU_DRIVER";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateGPUShader(IntPtr device, ref GpuShaderCreateInfo createInfo);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_ReleaseGPUShader(IntPtr device, IntPtr shader);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_CreateGPURenderState(IntPtr renderer, ref GpuRenderStateCreateInfo createInfo);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetGPURenderStateFragmentUniforms(IntPtr state, uint slotIndex, float[] data, uint length);

    /// <summary>The render state for the draws that follow (IntPtr.Zero: SDL's own shaders again).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetGPURenderState(IntPtr renderer, IntPtr state);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyGPURenderState(IntPtr state);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetTextureColorMod(IntPtr texture, byte red, byte green, byte blue);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_UpdateTexture(IntPtr texture, IntPtr rectangle, int[] pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_UpdateTexture(IntPtr texture, IntPtr rectangle, uint[] pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_UpdateTexture(IntPtr texture, ref SdlRect rectangle, IntPtr pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_UpdateTexture")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_UpdateTextureFrom(IntPtr texture, IntPtr rectangle, IntPtr pixels, int pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RenderClear(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetRenderDrawColor(IntPtr renderer, byte red, byte green, byte blue, byte alpha);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetRenderDrawBlendMode(IntPtr renderer, uint blendMode);

    /// <summary>The whole texture into <paramref name="destination"/> (SDL2's SDL_RenderCopy with no source rectangle).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderTexture")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RenderTextureTo(IntPtr renderer, IntPtr texture, IntPtr source, ref SdlFRect destination);

    /// <summary>A part of the texture into <paramref name="destination"/>.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderTexture")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RenderTexturePart(IntPtr renderer, IntPtr texture, ref SdlFRect source, ref SdlFRect destination);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RenderFillRect(IntPtr renderer, ref SdlFRect rectangle);

    /// <summary>The whole texture into a rectangle of whole pixels (SDL2's SDL_RenderCopy(renderer, texture, NULL, &amp;destination)).</summary>
    internal static bool RenderCopy(IntPtr renderer, IntPtr texture, SdlRect destination)
    {
        var target = new SdlFRect(destination.X, destination.Y, destination.Width, destination.Height);
        return SDL_RenderTextureTo(renderer, texture, IntPtr.Zero, ref target);
    }

    /// <summary>A part of the texture into a rectangle, both in whole pixels.</summary>
    internal static bool RenderCopyPart(IntPtr renderer, IntPtr texture, SdlRect source, SdlRect destination)
    {
        var from = new SdlFRect(source.X, source.Y, source.Width, source.Height);
        var target = new SdlFRect(destination.X, destination.Y, destination.Width, destination.Height);
        return SDL_RenderTexturePart(renderer, texture, ref from, ref target);
    }

    /// <summary>A rectangle of whole pixels filled with the draw colour.</summary>
    internal static bool FillRect(IntPtr renderer, SdlRect rectangle)
    {
        var area = new SdlFRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        return SDL_RenderFillRect(renderer, ref area);
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RenderPresent(IntPtr renderer);

    /// <summary>Hands the drawing waiting in SDL to the graphics card (SDL2's SDL_RenderFlush).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_FlushRenderer(IntPtr renderer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetRenderTarget(IntPtr renderer, IntPtr texture);

    /// <summary>The drawing read back as a new surface (freed with SDL_DestroySurface), or IntPtr.Zero.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_RenderReadPixels(IntPtr renderer, IntPtr rectangle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_ConvertSurface(IntPtr surface, uint format);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroySurface(IntPtr surface);

    /// <summary>SDL_Surface's public part.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlSurface
    {
        internal uint Flags;
        internal uint Format;
        internal int Width;
        internal int Height;
        internal int Pitch;
        internal IntPtr Pixels;
    }

    /// <summary>
    /// The renderer's drawing (its target texture, else the window) read back as ARGB8888, <paramref name="width"/> x
    /// <paramref name="height"/>; false (SDL_GetError says why) when it cannot be read.
    /// </summary>
    internal static bool ReadPixels(IntPtr renderer, int[] pixels, int width, int height)
    {
        var read = SDL_RenderReadPixels(renderer, IntPtr.Zero);
        if (read == IntPtr.Zero)
        {
            return false;
        }
        var argb = SDL_ConvertSurface(read, PixelFormatArgb8888);
        SDL_DestroySurface(read);
        if (argb == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            var surface = Marshal.PtrToStructure<SdlSurface>(argb);
            var rows = Math.Min(height, surface.Height);
            var columns = Math.Min(width, surface.Width);
            for (var y = 0; y < rows; y++)
            {
                Marshal.Copy(surface.Pixels + y * surface.Pitch, pixels, y * width, columns);
            }
            return true;
        }
        finally
        {
            SDL_DestroySurface(argb);
        }
    }

    // ---- events, keyboard, mouse

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_StartTextInput(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_StopTextInput(IntPtr window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_PollEvent(out SdlEvent @event);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_PushEvent(ref SdlEvent @event);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_WaitEvent(out SdlEvent @event);

    /// <summary>As SDL_WaitEvent, at most <paramref name="milliseconds"/>: false when nothing came (or an error).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_WaitEventTimeout(out SdlEvent @event, int milliseconds);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_PumpEvents();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_FlushEvents(uint minType, uint maxType);

    /// <summary>Lets events of a type in (true) or drops them (false; those waiting are dropped too) (SDL2's SDL_EventState).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_SetEventEnabled(uint type, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ShowCursor();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_HideCursor();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ushort SDL_GetModState();

    /// <summary>The layout's key for a scancode with the given modifiers (key_event false: the modifiers apply whatever SDL_KEYCODE_OPTIONS says).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetKeyFromScancode(int scancode, ushort modstate, [MarshalAs(UnmanagedType.U1)] bool keyEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_WarpMouseInWindow(IntPtr window, float x, float y);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetMouseState(out float x, out float y);

    // ---- sound: SDL3's audio streams (SDL2's devices with a callback, or a queue)

    /// <summary>SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK: the system's default output.</summary>
    internal const uint AudioDeviceDefaultPlayback = 0xFFFFFFFF;

    /// <summary>SDL_AudioStreamCallback: <paramref name="additional"/> more bytes are wanted now (<paramref name="total"/>: in all).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AudioStreamCallback(IntPtr userdata, IntPtr stream, int additional, int total);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlAudioSpec
    {
        internal uint Format;
        internal int Channels;
        internal int Freq;
    }

    /// <summary>
    /// The device opened with a stream bound to it, its frames in <paramref name="spec"/> (SDL converts them for the device); paused
    /// until SDL_ResumeAudioStreamDevice. <paramref name="callback"/> IntPtr.Zero: frames are put in (queued) by the program.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_OpenAudioDeviceStream(uint device, ref SdlAudioSpec spec, IntPtr callback, IntPtr userdata);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ResumeAudioStreamDevice(IntPtr stream);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_AudioStreamDevicePaused(IntPtr stream);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_PutAudioStreamData(IntPtr stream, IntPtr data, int length);

    /// <summary>Bytes put in and not yet taken by the device (-1: an error).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetAudioStreamQueued(IntPtr stream);

    /// <summary>Closes the stream's device too (one opened by SDL_OpenAudioDeviceStream).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_DestroyAudioStream(IntPtr stream);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SDL_GetAudioStreamDevice(IntPtr stream);

    /// <summary>A device's own format and its buffer's frames (the default output's: <see cref="AudioDeviceDefaultPlayback"/>).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetAudioDeviceFormat(uint device, out SdlAudioSpec spec, out int sampleFrames);

    /// <summary>The output devices' ids (freed with SDL_free), <paramref name="count"/> of them.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetAudioPlaybackDevices(out int count);

    /// <summary>A device's name, owned by SDL.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetAudioDeviceName(uint device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetCurrentAudioDriver();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetNumAudioDrivers();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetAudioDriver(int index);

    /// <summary>The output devices' names (the system's view now).</summary>
    internal static List<(uint Id, string Name)> PlaybackDevices()
    {
        var list = new List<(uint, string)>();
        var ids = SDL_GetAudioPlaybackDevices(out var count);
        if (ids == IntPtr.Zero)
        {
            return list;
        }
        for (var i = 0; i < count; i++)
        {
            var id = (uint)Marshal.ReadInt32(ids, i * 4);
            list.Add((id, Text(SDL_GetAudioDeviceName(id)) ?? "?"));
        }
        SDL_free(ids);
        return list;
    }

    // ---- the system: browser, message boxes, clipboard, dialogs

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_OpenURL([MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ShowMessageBox(ref MessageBoxData data, out int buttonId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageBoxButton
    {
        internal uint Flags;
        internal int Id;
        internal IntPtr Text;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageBoxData
    {
        internal uint Flags;
        internal IntPtr Window;
        internal IntPtr Title;
        internal IntPtr Message;
        internal int ButtonCount;
        internal IntPtr Buttons;
        internal IntPtr ColorScheme;
    }

    /// <summary>The clipboard's text (freed with SDL_free; an empty string when there is none).</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetClipboardText();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_free(IntPtr memory);

    /// <summary>
    /// SDL_ShowFileDialogWithProperties: 0 a file to open, 2 a folder. The answer comes to <paramref name="callback"/>
    /// (<c>void (*)(void *userdata, const char * const *filelist, int filter)</c>) later, maybe on another thread, while the
    /// program's events are pumped.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_ShowFileDialogWithProperties(int type, IntPtr callback, IntPtr userdata, uint properties);

    internal const int FileDialogOpenFile = 0, FileDialogOpenFolder = 2;

    /// <summary>A string SDL allocated (clipboard text), read as UTF-8 and freed.</summary>
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

    /// <summary>A string SDL owns (not freed), or null.</summary>
    internal static string? Text(IntPtr text) => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);

    /// <summary>SDL's version as a number: major * 1000000 + minor * 1000 + micro.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetVersion();

    /// <summary>The SDL library's version, e.g. "3.4.20".</summary>
    internal static string LinkedVersion()
    {
        var v = SDL_GetVersion();
        return $"{v / 1000000}.{v / 1000 % 1000}.{v % 1000}";
    }

    // ---- structures

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlRect
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
    }

    /// <summary>SDL_FRect: SDL3 draws to float rectangles.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SdlFRect
    {
        internal float X;
        internal float Y;
        internal float Width;
        internal float Height;

        internal SdlFRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    // SDL3's events: type, a reserved word, a 64-bit timestamp, then the window's id at offset 16

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct SdlKeyboardEvent
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(16)] internal uint WindowId;
        [FieldOffset(20)] internal uint Which;
        /// <summary>SDL_Scancode: the key's place (USB usage), as in SDL2.</summary>
        [FieldOffset(24)] internal int Scancode;
        /// <summary>SDL_Keycode: the key's symbol in the layout, unshifted (SDL2's keysym.sym).</summary>
        [FieldOffset(28)] internal int Key;
        [FieldOffset(32)] internal ushort Mod;
        [FieldOffset(34)] internal ushort Raw;
        [FieldOffset(36)] internal byte Down;
        [FieldOffset(37)] internal byte Repeat;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct SdlMouseMotionEvent
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(16)] internal uint WindowId;
        [FieldOffset(20)] internal uint Which;
        [FieldOffset(24)] internal uint State;
        [FieldOffset(28)] internal float X;
        [FieldOffset(32)] internal float Y;
        [FieldOffset(36)] internal float XRelative;
        [FieldOffset(40)] internal float YRelative;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct SdlMouseButtonEvent
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(16)] internal uint WindowId;
        [FieldOffset(20)] internal uint Which;
        [FieldOffset(24)] internal byte Button;
        [FieldOffset(25)] internal byte Down;
        [FieldOffset(26)] internal byte Clicks;
        [FieldOffset(28)] internal float X;
        [FieldOffset(32)] internal float Y;
    }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct SdlMouseWheelEvent
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(16)] internal uint WindowId;
        [FieldOffset(20)] internal uint Which;
        [FieldOffset(24)] internal float X;
        [FieldOffset(28)] internal float Y;
        [FieldOffset(32)] internal uint Direction;
        [FieldOffset(36)] internal float MouseX;
        [FieldOffset(40)] internal float MouseY;
        /// <summary>The scroll in whole ticks (SDL 3.2.12 and later; SDL2's integer y).</summary>
        [FieldOffset(44)] internal int IntegerX;
        [FieldOffset(48)] internal int IntegerY;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
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

        // SDL_WindowEvent: data1, data2 after the window id
        [FieldOffset(16)]
        internal uint WindowId;

        // SDL_TextInputEvent: const char *text (UTF-8, SDL's, valid until the next event is taken)
        [FieldOffset(24)]
        internal IntPtr Text;

        // SDL_DropEvent: the window id, x, y (floats), const char *source, const char *data (SDL's: not freed)
        [FieldOffset(40)]
        internal IntPtr DropData;

        /// <summary>The text of an <see cref="EventTextInput"/> event.</summary>
        internal readonly string InputText() => SdlNative.Text(Text) ?? "";

        /// <summary>The path of an <see cref="EventDropFile"/> event.</summary>
        internal readonly string? DroppedPath() => SdlNative.Text(DropData);

        /// <summary>A window event (SDL2's SDL_WINDOWEVENT): one of SDL3's window event types.</summary>
        internal readonly bool IsWindowEvent => Type is >= EventWindowFirst and <= EventWindowLast;
    }
}

internal sealed class SdlHostException : Exception
{
    internal SdlHostException(string message)
        : base(message)
    {
    }
}
