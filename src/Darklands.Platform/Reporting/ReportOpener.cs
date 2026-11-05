using System.Diagnostics;

namespace Darklands.Platform.Reporting;

/// <summary>Opens a folder or a web page with the system's own program (Explorer, xdg-open, open); a failure is quiet: the path is said.</summary>
public static class ReportOpener
{
    /// <summary>What starts a program: replaced in tests so that nothing opens.</summary>
    public static Func<ProcessStartInfo, bool> Start { get; set; } = info =>
    {
        using var process = Process.Start(info);
        return process is not null;
    };

    /// <summary>Opens the folder holding <paramref name="file"/>, with the file selected where the system can.</summary>
    public static bool ShowFile(string file, Action<string> say)
    {
        var folder = Path.GetDirectoryName(file) ?? file;
        var ok = Try(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                return Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, Arguments = "/select,\"" + file + "\"" });
            }
            return Start(OpenWith(OperatingSystem.IsMacOS() ? "open" : "xdg-open", folder));
        });
        if (!ok)
        {
            say("Report: could not open the folder; the file is " + file);
        }
        return ok;
    }

    /// <summary>Opens a web address in the browser.</summary>
    public static bool OpenUrl(string url, Action<string> say)
    {
        var ok = Try(() => OperatingSystem.IsWindows()
            ? Start(new ProcessStartInfo(url) { UseShellExecute = true })
            : Start(OpenWith(OperatingSystem.IsMacOS() ? "open" : "xdg-open", url)));
        if (!ok)
        {
            say("Report: could not open the browser; open this address yourself: " + url);
        }
        return ok;
    }

    private static ProcessStartInfo OpenWith(string program, string argument)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false };
        info.ArgumentList.Add(argument);
        return info;
    }

    private static bool Try(Func<bool> start)
    {
        try
        {
            return start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
