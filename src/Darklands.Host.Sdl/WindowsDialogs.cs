using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Darklands.Host.Sdl;

/// <summary>
/// Windows' side of SDL3's dialogs (<see cref="SdlDialogs"/>): Windows puts the dialog at the launcher's top edge, its title bar where
/// the frameless window's would be; it is moved to the middle of the launcher's window instead, once it shows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDialogs
{
    /// <summary>
    /// What runs while a dialog over <paramref name="owner"/> (a window handle, or zero) is open: the window's messages handled
    /// (the dialog sends the owner some: else both wait for each other; what was clicked or typed meanwhile is dropped after), and
    /// the dialog centred once.
    /// </summary>
    public static Func<bool> WhileOpen(IntPtr owner)
    {
        var centred = owner == IntPtr.Zero;
        return () =>
        {
            SdlNative.SDL_PumpEvents();
            centred = centred || Centre(owner);
            return true;
        };
    }

    /// <summary>Moves the dialog <paramref name="owner"/> owns to the middle of it; false while there is none yet.</summary>
    private static unsafe bool Centre(IntPtr owner)
    {
        _owner = owner;
        _found = IntPtr.Zero;
        EnumWindows(&OwnedDialog, IntPtr.Zero);
        if (_found == IntPtr.Zero || !GetWindowRect(owner, out var o) || !GetWindowRect(_found, out var d))
        {
            return false;
        }
        int w = d.Right - d.Left, h = d.Bottom - d.Top;
        SetWindowPos(_found, IntPtr.Zero, o.Left + (o.Right - o.Left - w) / 2, Math.Max(o.Top, o.Top + (o.Bottom - o.Top - h) / 2), 0, 0,
            NoSize | NoZOrder | NoActivate);
        return true;
    }

    private static IntPtr _owner, _found;

    [UnmanagedCallersOnly]
    private static unsafe int OwnedDialog(IntPtr window, IntPtr data)
    {
        var name = stackalloc char[16];
        var length = GetClassNameW(window, name, 16);
        if (GetWindow(window, OwnerWindow) == _owner && IsWindowVisible(window) && new string(name, 0, length) == "#32770")
        {
            _found = window;
            return 0;
        }
        return 1;
    }

    private const uint OwnerWindow = 4, NoSize = 0x1, NoZOrder = 0x4, NoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern unsafe int EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern unsafe int GetClassNameW(IntPtr window, char* name, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint relation);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
