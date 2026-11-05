using System.Runtime.InteropServices;
using Darklands.Platform.Reporting;

namespace Darklands.Host.Sdl;

/// <summary>What the player chose in the crash notice.</summary>
internal enum CrashChoice
{
    OpenForm,
    ShowFile,
    Close,
}

/// <summary>The crash notice: what happened, where the report is.</summary>
internal sealed record CrashNotice(string Title, string Message, string ReportFile);

/// <summary>
/// The program's last word when something unexpected ends it: logs the exception, makes a crash report (docs/REPORTING.md) and
/// shows a notice with three buttons: open the report form (the folder with the zip and the issue form), show the file, close.
/// It must not throw itself and does not run twice at the same time. The notice is a seam, so that a test can answer it.
/// </summary>
internal sealed class CrashHandler(ReportService reports, Func<CrashNotice, CrashChoice> show, Action<string> say)
{
    private int _busy;

    /// <summary>Hooks the handlers for exceptions nobody caught (the program ends after them; report and notice) and for tasks nobody
    /// waited for (logged only).</summary>
    public void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Handle(e.ExceptionObject as Exception ?? new InvalidOperationException(Convert.ToString(e.ExceptionObject, System.Globalization.CultureInfo.InvariantCulture)),
                "unhandled exception");
        // a task nobody waited for does not end the program: it is only logged, with no notice and no report
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Quietly(() => reports.Log?.WriteLine("Unobserved task exception (the game goes on): " + e.Exception, error: true));
        };
    }

    /// <summary>Logs <paramref name="exception"/>, makes the report and shows the notice; the report, or null when none could be made.</summary>
    public ReportResult? Handle(Exception exception, string kind)
    {
        // a crash inside the handler (or a second one while the notice is up) is only logged
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            Quietly(() => reports.Log?.WriteLine("Crash: another " + kind + " while the report is made: " + exception.GetType().Name + ": " + exception.Message, error: true));
            return null;
        }
        ReportResult? result = null;
        try
        {
            Quietly(() => reports.Log?.WriteLine("CRASH (" + kind + "): " + exception, error: true));
            try
            {
                result = reports.Build(crash: true, source: null, playerText: null, exception);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Quietly(() => reports.Log?.WriteLine("Crash: the report could not be made: " + e.Message, error: true));
            }
            var where = result?.Zip ?? (reports.Log is { } log ? log.Path : reports.Locations.Logs);
            var message = (result is null
                    ? "The program ran into an unexpected error and has to close. A report could not be made; the log is here:\n"
                    : "The program ran into an unexpected error and has to close.\nA report was saved here; nothing was sent anywhere:\n")
                + where + "\n\n" + exception.GetType().Name + ": " + exception.Message
                + "\n\nPlease report it: open the form and attach the file.";
            var notice = new CrashNotice("Darklands Restoration Project", message, where);
            for (var n = 0; n < 5; n++)
            {
                var choice = CrashChoice.Close;
                try
                {
                    choice = show(notice);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    Quietly(() => say(message));
                }
                if (choice == CrashChoice.Close)
                {
                    break;
                }
                try
                {
                    if (choice == CrashChoice.OpenForm)
                    {
                        var url = reports.IssueUrl(crash: true, source: null, playerText: null, exception);
                        if (result is not null)
                        {
                            reports.Present(result, url, say);
                        }
                        else
                        {
                            ReportOpener.OpenUrl(url, say);
                        }
                    }
                    else if (result is not null)
                    {
                        ReportOpener.ShowFile(result.Zip, say);
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    Quietly(() => say("Report: " + e.Message));
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // nothing more can be done
            Quietly(() => say("Crash handler failed: " + e.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
        return result;
    }

    private static void Quietly(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
    }

    /// <summary>The notice as an SDL message box with the three buttons; Close when the box cannot be shown.</summary>
    public static CrashChoice ShowWithSdl(CrashNotice notice)
    {
        string[] labels = ["Open report form", "Show the file", "Close"];
        var texts = new IntPtr[labels.Length];
        var buttons = IntPtr.Zero;
        var title = IntPtr.Zero;
        var message = IntPtr.Zero;
        try
        {
            var size = Marshal.SizeOf<SdlNative.MessageBoxButton>();
            buttons = Marshal.AllocHGlobal(size * labels.Length);
            for (var i = 0; i < labels.Length; i++)
            {
                texts[i] = Marshal.StringToCoTaskMemUTF8(labels[i]);
                // the last button is the Escape and Enter default (SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT = 1, ESCAPEKEY_DEFAULT = 2)
                var button = new SdlNative.MessageBoxButton { Flags = i == labels.Length - 1 ? 3u : 0u, Id = i, Text = texts[i] };
                Marshal.StructureToPtr(button, buttons + i * size, false);
            }
            title = Marshal.StringToCoTaskMemUTF8(notice.Title);
            message = Marshal.StringToCoTaskMemUTF8(notice.Message);
            var data = new SdlNative.MessageBoxData
            {
                Flags = 0x10,   // SDL_MESSAGEBOX_ERROR
                Title = title,
                Message = message,
                ButtonCount = labels.Length,
                Buttons = buttons,
            };
            return SdlNative.SDL_ShowMessageBox(ref data, out var id) == 0 && id is >= 0 and < 3 ? (CrashChoice)id : CrashChoice.Close;
        }
        finally
        {
            foreach (var t in texts)
            {
                Marshal.FreeCoTaskMem(t);
            }
            Marshal.FreeHGlobal(buttons);
            Marshal.FreeCoTaskMem(title);
            Marshal.FreeCoTaskMem(message);
        }
    }
}
