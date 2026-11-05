using System.IO.Compression;
using Darklands.Platform.Reporting;

namespace Darklands.Host.Sdl.Tests;

/// <summary>The crash handler and the commit stamp (docs/REPORTING.md). Nothing here shows a window or opens a program.</summary>
public sealed class CrashHandlerTests : IDisposable
{
    // the project's scratch folder on the developer's machine, else the system's temporary folder
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    private readonly Func<System.Diagnostics.ProcessStartInfo, bool> _oldStart = ReportOpener.Start;

    private readonly List<System.Diagnostics.ProcessStartInfo> _started = [];

    public CrashHandlerTests()
    {
        Directory.CreateDirectory(_root);
        ReportOpener.Start = info =>
        {
            _started.Add(info);
            return true;
        };
    }

    public void Dispose()
    {
        ReportOpener.Start = _oldStart;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // a file the program still has open
    private static string ReadOpen(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

    private (ReportService Service, SessionLog Log) Service()
    {
        var locations = ReportLocations.Choose(Path.Combine(_root, "darklands.settings.json"), canWrite: _ => true);
        var log = SessionLog.Start(locations.Logs, ["header"])!;
        return (new ReportService(locations, "1.0.0-test", "abc1234", log, new SaveTracker(locations.Session), new Scrubber(null, null)), log);
    }

    [Fact]
    public void A_crash_makes_a_report_logs_the_exception_and_shows_the_notice()
    {
        var (service, log) = Service();
        using var _ = log;
        CrashNotice? shown = null;
        var handler = new CrashHandler(service, notice =>
        {
            shown = notice;
            return CrashChoice.Close;
        }, _ => { });

        var result = handler.Handle(new InvalidOperationException("simulated"), "unhandled exception");

        Assert.NotNull(result);
        Assert.True(File.Exists(result.Zip));
        Assert.Equal(result.Zip, shown!.ReportFile);
        Assert.Contains(result.Zip, shown.Message);
        Assert.Contains("simulated", shown.Message);
        using var archive = ZipFile.OpenRead(result.Zip);
        using var reader = new StreamReader(archive.GetEntry("report.txt")!.Open());
        var text = reader.ReadToEnd();
        Assert.Contains("Trigger: crash", text);
        Assert.Contains("InvalidOperationException: simulated", text);
        Assert.Contains("CRASH (unhandled exception)", ReadOpen(log.Path));
        Assert.Empty(_started);   // Close opens nothing
    }

    [Fact]
    public void The_buttons_open_the_form_and_the_folder_or_only_the_file_and_then_close()
    {
        var (service, log) = Service();
        using var _ = log;
        var answers = new Queue<CrashChoice>([CrashChoice.ShowFile, CrashChoice.OpenForm, CrashChoice.Close]);
        var handler = new CrashHandler(service, _ => answers.Dequeue(), _ => { });

        handler.Handle(new IOException("x"), "unhandled exception");

        Assert.Empty(answers);
        // the file once; then the folder and the browser
        Assert.Equal(3, _started.Count);
        Assert.Contains(_started, s => s.FileName.StartsWith("https://github.com/Arkana-Mechanika-Studios/darklands-restoration/issues/new", StringComparison.Ordinal)
            || s.ArgumentList.Any(a => a.StartsWith("https://github.com/", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_handler_does_not_throw_when_the_notice_or_the_report_fails()
    {
        var (service, log) = Service();
        using var _ = log;
        var said = new List<string>();
        var failing = new CrashHandler(service, _ => throw new DllNotFoundException("no SDL"), said.Add);
        var result = failing.Handle(new InvalidOperationException("a"), "unhandled exception");
        Assert.NotNull(result);
        Assert.Contains(said, s => s.Contains("unexpected error", StringComparison.Ordinal));

        // a report that cannot be written (the reports folder is a file): the notice still comes, pointing at the log
        var blocked = Path.Combine(_root, "blocked");
        var locations = new ReportLocations(blocked, Path.Combine(blocked, "logs"), Path.Combine(blocked, "reports"), Path.Combine(blocked, "session"));
        File.WriteAllText(blocked, "a file where a folder should be");
        CrashNotice? shown = null;
        var broken = new CrashHandler(new ReportService(locations, "v", "c", null, new SaveTracker(locations.Session), new Scrubber(null, null)),
            n =>
            {
                shown = n;
                return CrashChoice.Close;
            }, _ => { });
        Assert.Null(broken.Handle(new InvalidOperationException("b"), "unhandled exception"));
        Assert.Contains("could not be made", shown!.Message);
    }

    [Fact]
    public void A_second_crash_while_the_notice_is_up_does_not_re_enter()
    {
        var (service, log) = Service();
        using var _ = log;
        ReportResult? inner = null;
        CrashHandler? handler = null;
        var innerCalled = false;
        handler = new CrashHandler(service, _ =>
        {
            innerCalled = true;
            inner = handler!.Handle(new InvalidOperationException("second"), "unobserved task exception");
            return CrashChoice.Close;
        }, _ => { });

        var outer = handler.Handle(new InvalidOperationException("first"), "unhandled exception");

        Assert.True(innerCalled);
        Assert.Null(inner);
        Assert.NotNull(outer);
        Assert.Contains("another unobserved task exception", ReadOpen(log.Path));
        // and the guard is released afterwards
        Assert.NotNull(handler.Handle(new InvalidOperationException("third"), "unhandled exception"));
    }

    [Fact]
    public void The_build_carries_its_commit()
    {
        var commit = ReportHost.Commit(typeof(CrashHandler).Assembly);

        Assert.NotEqual("unknown", commit);   // built inside a git checkout or by CI; a source copy without either would say unknown
        Assert.Matches("^[0-9a-f]{7,40}$", commit);
    }

    [Fact]
    public void A_build_without_the_stamp_says_unknown()
    {
        Assert.Equal("unknown", ReportHost.Commit(typeof(string).Assembly));
    }
}
