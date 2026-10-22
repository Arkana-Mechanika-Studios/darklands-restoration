using System.Runtime.InteropServices;

namespace Darklands.Host.Sdl;

/// <summary>Shift held as the program starts: the player asks for the launcher.</summary>
internal static partial class StartKeys
{
    private const int VirtualKeyShift = 0x10;

    /// <summary>Before any window: Windows tells the keyboard's state; elsewhere false (see <see cref="ShiftHeldInWindow"/>).</summary>
    public static bool ShiftHeldBeforeWindow() =>
        OperatingSystem.IsWindows() && (GetAsyncKeyState(VirtualKeyShift) & 0x8000) != 0;

    /// <summary>Once the window is open: SDL's view of the modifiers (it reads them when the window takes the keyboard).</summary>
    public static bool ShiftHeldInWindow()
    {
        SdlNative.SDL_PumpEvents();
        return (SdlNative.SDL_GetModState() & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);
}
