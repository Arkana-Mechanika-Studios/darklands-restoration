using System.Runtime.InteropServices;
using System.Text;

namespace Darklands.Host.Sdl;

/// <summary>
/// Where the program's messages go on Windows. DarklandsRP.exe is a windowed program (no console window opens with the game);
/// its messages still reach a file or pipe its output is redirected to (kept as it is), the terminal it was started from (the
/// program attaches to its parent's console: --version, --help and the start messages show there as before), and the session
/// log in every case (docs/REPORTING.md). --log-window opens a console window of its own for them when there is no terminal.
/// Elsewhere (Linux) nothing changes: the program writes to the terminal it has, if any.
/// </summary>
internal static class ConsoleWindow
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    /// <summary>Connects the program's output as described above; run first, before anything writes to the console.</summary>
    /// <returns>Where the output goes: "redirected", "terminal", "window" or "none".</returns>
    public static string Connect(bool ownWindow)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "terminal";
        }
        if (HasHandle(StdOutputHandle) || HasHandle(StdErrorHandle))
        {
            // a file or pipe given by the parent (tools, a redirect in a terminal): .NET's console writes there already
            return "redirected";
        }
        if (AttachConsole(AttachParentProcess))
        {
            Reopen();
            return "terminal";
        }
        if (ownWindow && AllocConsole())
        {
            SetConsoleOutputCP(65001); // the new window shows the messages as UTF-8, as they are written
            Reopen();
            return "window";
        }
        return "none";
    }

    private static bool HasHandle(int which)
    {
        var handle = GetStdHandle(which);
        return handle != IntPtr.Zero && handle != new IntPtr(-1);
    }

    // the console's own output, written through at once (the terminal's or the new window's)
    private static void Reopen()
    {
        try
        {
            var stream = new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
        }
        catch (IOException)
        {
            // no usable console after all: the messages go to the session log only
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint codePage);
}
