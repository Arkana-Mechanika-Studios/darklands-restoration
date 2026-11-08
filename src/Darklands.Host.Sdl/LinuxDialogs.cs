using System.Diagnostics;

namespace Darklands.Host.Sdl;

/// <summary>
/// A folder or file dialog on Linux, for the launcher's Change… and its SoundFont row: SDL3's own (<see cref="SdlDialogs"/>: the
/// XDG desktop portal, else zenity), else the first of kdialog, zenity, qarma or yad installed, for a system where SDL3's cannot
/// show one (no portal and no zenity: a bare window manager, some Steam Deck Game Mode setups). Each runs while the window's
/// events are pumped; none is a new dependency of the program.
/// </summary>
internal static class LinuxDialogs
{
    /// <summary>
    /// The folder chosen, or null when the player cancelled (or closed the program meanwhile). <paramref name="whileOpen"/> runs over
    /// and over meanwhile and returns false when the program is to close. Throws <see cref="NotSupportedException"/> when no dialog
    /// could be shown.
    /// </summary>
    public static string? PickFolder(IntPtr window, string title, string start, Func<bool> whileOpen) => Pick(window, title, start, whileOpen, folders: true);

    /// <summary>A file, as <see cref="PickFolder"/>.</summary>
    public static string? PickFile(IntPtr window, string title, string start, Func<bool> whileOpen) => Pick(window, title, start, whileOpen, folders: false);

    private static string? Pick(IntPtr window, string title, string start, Func<bool> whileOpen, bool folders)
    {
        var problems = new List<string>();
        var where = SdlDialogs.Location(start.Length > 0 ? start : "/");
        string? folder = null;
        try
        {
            return SdlDialogs.Pick(window, title, where, folders, whileOpen);
        }
        catch (NotSupportedException e)
        {
            problems.Add("SDL3's dialog: " + e.Message);
        }
        foreach (var (tool, arguments) in Tools(title, where, folders))
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
    private static IEnumerable<(string Tool, string[] Arguments)> Tools(string title, string start, bool folders)
    {
        // start: a folder (ending with /) or a file
        var zenity = folders ? new[] { "--file-selection", "--directory", "--title=" + title, "--filename=" + start }
            : ["--file-selection", "--title=" + title, "--filename=" + start];
        var kdialog = ("kdialog", folders ? new[] { "--getexistingdirectory", start, "--title", title } : ["--getopenfilename", start, "--title", title]);
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
        {
            yield return kdialog;
        }
        yield return ("zenity", zenity);
        yield return ("qarma", zenity);
        yield return ("yad", folders ? ["--file", "--directory", "--title=" + title, "--filename=" + start] : ["--file", "--title=" + title, "--filename=" + start]);
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
