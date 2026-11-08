using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Darklands.Host.Sdl;

/// <summary>
/// The launcher's frameless window on Windows: Windows' default handling of a window's activation (WM_NCACTIVATE, sent when the
/// window gains or loses the focus, and when a dialog it owns opens or closes) and of its frame's painting (WM_NCPAINT) draws an
/// old-style title bar over the top of a window without one, for a moment: a flash. While the window is frameless its messages
/// pass through here first: activation goes on as before but is told not to paint the frame (lParam -1), and the frame is not
/// painted (it has none). Found by sampling the screen over the window as it was activated (2026-11-08).
/// </summary>
internal static class FramelessWindow
{
    private const int WindowProcedure = -4; // GWLP_WNDPROC
    private const uint NonClientPaint = 0x0085, NonClientActivate = 0x0086;

    private static IntPtr _window;
    private static IntPtr _sdlProcedure;

    /// <summary>Puts the filter in front of SDL's window procedure (once).</summary>
    [SupportedOSPlatform("windows")]
    public static unsafe void Attach(IntPtr window)
    {
        if (window == IntPtr.Zero || _window != IntPtr.Zero)
        {
            return;
        }
        _window = window;
        _sdlProcedure = SetWindowLongPtrW(window, WindowProcedure, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&Procedure);
    }

    /// <summary>The window the filter is on (zero: none; tests).</summary>
    internal static IntPtr Attached => _window;

    /// <summary>Gives the window SDL's procedure alone again (Play: before the window is replaced by the game's).</summary>
    [SupportedOSPlatform("windows")]
    public static void Detach()
    {
        if (_window != IntPtr.Zero && _sdlProcedure != IntPtr.Zero)
        {
            SetWindowLongPtrW(_window, WindowProcedure, _sdlProcedure);
        }
        _window = IntPtr.Zero;
        _sdlProcedure = IntPtr.Zero;
    }

    /// <summary>What the filter passes on to SDL: activation without the frame's painting; nothing for the frame's painting.</summary>
    internal static (bool Pass, IntPtr LParam) Filter(uint message, IntPtr lParam) => message switch
    {
        NonClientActivate => (true, -1),
        NonClientPaint => (false, IntPtr.Zero),
        _ => (true, lParam),
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr Procedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        var (pass, l) = Filter(message, lParam);
        if (!pass)
        {
            return IntPtr.Zero;
        }
        return CallWindowProcW(_sdlProcedure, window, message, wParam, l);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr procedure, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
