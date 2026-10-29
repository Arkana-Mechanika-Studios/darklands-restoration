using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Darklands.Host.Sdl;

/// <summary>
/// A folder dialog on Linux, for the launcher's Change…: SDL3's own (the desktop portal, else zenity) when the SDL2 library is
/// sdl2-compat on top of SDL3 (current Arch, Fedora ...), else the first of zenity, kdialog, yad or qarma installed. Each runs
/// while the window's events are pumped; none is a new dependency of the program.
/// </summary>
internal static class LinuxDialogs
{
    /// <summary>
    /// The folder chosen, or null when the player cancelled (or closed the program meanwhile). <paramref name="whileOpen"/> runs over
    /// and over meanwhile and returns false when the program is to close. Throws <see cref="NotSupportedException"/> when no dialog
    /// could be shown.
    /// </summary>
    public static string? PickFolder(string title, Func<bool> whileOpen)
    {
        var problems = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Sdl3Dialog.TryPick(home, whileOpen, problems, out var folder))
        {
            return folder;
        }
        foreach (var (tool, arguments) in Tools(title, home))
        {
            if (FindOnPath(tool) is not { } path)
            {
                continue;
            }
            if (TryRun(path, arguments, whileOpen, problems, out folder))
            {
                return folder;
            }
        }
        var why = problems.Count > 0 ? " (" + string.Join("; ", problems) + ")" : "";
        Console.Error.WriteLine("Launcher: no folder dialog could be shown" + why + ".");
        throw new NotSupportedException("No folder dialog on this system" + why
            + ". Type or paste (Ctrl+V) the folder, or drop it on this window; installing zenity gives a dialog.");
    }

    /// <summary>The dialog programs tried, in order (kdialog first on KDE), with their arguments.</summary>
    private static IEnumerable<(string Tool, string[] Arguments)> Tools(string title, string home)
    {
        var start = home.Length > 0 ? home.TrimEnd('/') + "/" : "/";
        var zenity = new[] { "--file-selection", "--directory", "--title=" + title, "--filename=" + start };
        var kdialog = ("kdialog", new[] { "--getexistingdirectory", start, "--title", title });
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
        {
            yield return kdialog;
        }
        yield return ("zenity", zenity);
        yield return ("qarma", zenity);
        yield return ("yad", ["--file", "--directory", "--title=" + title, "--filename=" + start]);
        yield return kdialog;
    }

    private static string? FindOnPath(string tool)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(folder, tool);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Runs one dialog program (no shell): true with the folder (null: cancelled) when it ran, false when it failed to show
    /// (its error output is noted in <paramref name="problems"/>).
    /// </summary>
    private static bool TryRun(string path, string[] arguments, Func<bool> whileOpen, List<string> problems, out string? folder)
    {
        folder = null;
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("not started");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            problems.Add(Path.GetFileName(path) + ": " + e.Message);
            return false;
        }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var clock = Stopwatch.StartNew();
            while (!process.WaitForExit(15))
            {
                if (!whileOpen())
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    return true;
                }
            }
            process.WaitForExit();
            var chosen = output.Result.Split('\n').Select(l => l.Trim().TrimEnd('|')).FirstOrDefault(l => l.Length > 0);
            if (process.ExitCode == 0 && chosen is not null)
            {
                folder = chosen;
                return true;
            }
            // a dialog that could not open (no display, a missing library) ends at once with an error; a cancel does not print one
            var error = errors.Result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains("WARNING", StringComparison.OrdinalIgnoreCase)).ToList();
            if (clock.ElapsedMilliseconds < 1500 && error.Count > 0)
            {
                problems.Add(Path.GetFileName(path) + ": " + error[0]);
                return false;
            }
            return true;
        }
    }
}

/// <summary>
/// SDL3's folder dialog (SDL_ShowOpenFolderDialog: the XDG desktop portal, else zenity), reached in the SDL3 library that
/// sdl2-compat runs on: the same library and instance, so SDL_PumpEvents through SDL2 serves its D-Bus replies.
/// </summary>
internal static unsafe class Sdl3Dialog
{
    private static readonly object Lock = new();
    private static int _generation;
    private static (int Generation, bool Done, string? Folder, string? Error) _result;
    private static IntPtr _getError;

    public static bool TryPick(string home, Func<bool> whileOpen, List<string> problems, out string? folder)
    {
        folder = null;
        if (!SdlNative.LinkedVersion().Sdl2Compat)
        {
            return false;
        }
        if (!NativeLibrary.TryLoad("libSDL3.so.0", out var sdl3)
            || !NativeLibrary.TryGetExport(sdl3, "SDL_ShowOpenFolderDialog", out var show)
            || !NativeLibrary.TryGetExport(sdl3, "SDL_GetError", out var getError))
        {
            problems.Add("SDL3's dialog: not found");
            return false;
        }
        int generation;
        lock (Lock)
        {
            _getError = getError;
            generation = ++_generation;
            _result = (generation, false, null, null);
        }
        var start = Marshal.StringToCoTaskMemUTF8(home.Length > 0 ? home : "/");
        try
        {
            // void SDL_ShowOpenFolderDialog(SDL_DialogFileCallback callback, void *userdata, SDL_Window *window, const char *default_location, bool allow_many)
            ((delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, void>, IntPtr, IntPtr, IntPtr, byte, void>)show)(
                &Chosen, generation, IntPtr.Zero, start, 0);
            while (true)
            {
                lock (Lock)
                {
                    if (_result.Done)
                    {
                        break;
                    }
                }
                if (!whileOpen())
                {
                    return true; // the program closes: its answer is not waited for
                }
                Thread.Sleep(10);
            }
        }
        finally
        {
            // SDL copies the default location before it returns
            Marshal.FreeCoTaskMem(start);
        }
        lock (Lock)
        {
            if (_result.Error is { } error)
            {
                problems.Add("SDL3's dialog: " + error);
                return false;
            }
            folder = _result.Folder;
            return true;
        }
    }

    // SDL_DialogFileCallback(void *userdata, const char * const *filelist, int filter): NULL list: an error; an empty one: cancelled
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Chosen(IntPtr userdata, IntPtr filelist, int filter)
    {
        lock (Lock)
        {
            if ((int)userdata != _result.Generation)
            {
                return;
            }
            if (filelist == IntPtr.Zero)
            {
                // SDL's error is kept per thread: read on the thread that reports it
                var error = Marshal.PtrToStringUTF8(((delegate* unmanaged[Cdecl]<IntPtr>)_getError)()) ?? "";
                _result = (_result.Generation, true, null, error.Length > 0 ? error : "failed");
                return;
            }
            var first = Marshal.ReadIntPtr(filelist);
            _result = (_result.Generation, true, first == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(first), null);
        }
    }
}
