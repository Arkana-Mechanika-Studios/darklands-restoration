using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Darklands.Host.Sdl;

/// <summary>
/// The SDL 3 library could not be loaded: what the player is told, in plain words, before any window can open. On Windows a message
/// box (SDL3.dll comes with the zip: it was left behind); on Linux a dialog through zenity or kdialog when the desktop has one (most
/// do), with the commands that install SDL 3 and the Steam way for the systems that have none (Ubuntu 24.04, Debian 12). Always on
/// the standard error too (the log of the run, a terminal).
/// </summary>
internal static partial class MissingSdl
{
    internal const string Title = "Darklands Restoration: SDL 3 is missing";

    /// <summary>The message for this system.</summary>
    internal static string Message(bool windows) => windows
        ? "SDL3.dll was not found next to DarklandsRP.exe, or it could not be loaded.\n\n"
          + "It comes with the game's zip: unpack the whole zip again into a folder of its own, and start DarklandsRP.exe from there."
        : "Darklands Restoration needs SDL 3 (libSDL3.so.0, SDL 3.2 or later), and this system does not have it.\n\n"
          + "Install it:\n"
          + "  Debian 13, Ubuntu 25.04 or later: sudo apt install libsdl3-0\n"
          + "  Fedora: sudo dnf install SDL3\n"
          + "  Arch: sudo pacman -S sdl3\n\n"
          + "Ubuntu 24.04 and Debian 12 have no SDL 3: add DarklandsRP to Steam as a non-Steam game (the Steam runtime has it), "
          + "or put a libSDL3.so.0 beside the program.";

    /// <summary>Tells the player; returns the program's exit code.</summary>
    internal static int Tell()
    {
        var windows = OperatingSystem.IsWindows();
        var message = Message(windows);
        Console.Error.WriteLine(Title);
        Console.Error.WriteLine(message);
        try
        {
            if (windows)
            {
                _ = MessageBoxW(IntPtr.Zero, message, Title, 0x10 /* MB_ICONERROR */);
            }
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                _ = Show("zenity", ["--error", "--title", Title, "--text", message])
                    || Show("kdialog", ["--title", Title, "--error", message]);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // no message box to show: the standard error has it
        }
        return 1;
    }

    /// <summary>Runs a dialog program and waits for the player to close it; false when the system has no such program.</summary>
    private static bool Show(string program, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false };
            foreach (var a in arguments)
            {
                start.ArgumentList.Add(a);
            }
            using var dialog = Process.Start(start);
            dialog?.WaitForExit();
            return dialog is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
