using System.IO.Compression;
using Darklands.Platform.Reporting;

namespace Darklands.Platform.Tests;

/// <summary>The problem report (docs/REPORTING.md): the session log, the report zip, scrubbing, pruning, the issue address.</summary>
public sealed class ReportingTests : IDisposable
{
    private const string Profile = @"C:\Users\Zebediah";

    // the project's scratch folder on the developer's machine, else the system's temporary folder
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    public ReportingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Scrubber TestScrubber => new(Profile, "Zebediah");

    private sealed record Fixture(string Root, string Game, string Saves, string Settings, ReportLocations Locations, SaveTracker Tracker, ReportService Service);

    private Fixture MakeFixture()
    {
        var game = Folder("game");
        File.WriteAllBytes(Path.Combine(game, "DARKLAND.EXE"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(game, "CACHE.TMP"), "cache");
        File.WriteAllText(Path.Combine(game, "CHARACTR.TMP"), "characters");
        var saves = Folder(Path.Combine("game", "SAVES"));
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE0.SAV"), [9, 9]);
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE1.SAV"), [7, 7, 7]);
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE1.BSV"), [8]);
        File.WriteAllText(Path.Combine(saves, "DKSAVE1.MODS.JSON"), "{}");
        File.SetLastWriteTimeUtc(Path.Combine(saves, "DKSAVE0.SAV"), DateTime.UtcNow.AddHours(-2));
        var settings = Path.Combine(Folder("program"), "darklands.settings.json");
        File.WriteAllText(settings, "{ \"gameFolder\": \"C:\\\\Users\\\\Zebediah\\\\Games\\\\Darklands\" }");
        var locations = ReportLocations.Choose(settings, canWrite: _ => true);
        Directory.CreateDirectory(locations.Logs);
        var tracker = new SaveTracker(locations.Session);
        var log = SessionLog.Start(locations.Logs, ["start of the log: " + Profile + @"\AppData"], new DateTime(2026, 11, 5, 10, 0, 0));
        Assert.NotNull(log);
        var service = new ReportService(locations, "1.0.0-test", "abc1234", log, tracker, TestScrubber)
        {
            SettingsFile = settings,
            GameFolder = game,
            Facts = () => [("Window", "1440x800"), ("Mods", "none")],
        };
        service.SavesFolders.Add(saves);
        return new Fixture(_root, game, saves, settings, locations, tracker, service);
    }

    // a file the program still has open (a crash would leave it so)
    private static string ReadOpen(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

    private static List<string> Names(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return [.. archive.Entries.Select(e => e.FullName)];
    }

    private static string Text(string zip, string entry)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_manual_report_holds_the_report_logs_settings_saves_and_working_files()
    {
        var f = MakeFixture();
        f.Tracker.Used(f.Saves, "DKSAVE1.SAV", loading: true);
        File.WriteAllBytes(Path.Combine(f.Saves, "DKSAVE1.SAV"), [1, 1, 1, 1]);   // changed after the load
        var picture = Path.Combine(_root, "frame.png");
        File.WriteAllBytes(picture, [0x89, 0x50]);
        f.Service.Screenshot = () => picture;

        var result = f.Service.Build(crash: false, "test", "the guard walks through the wall", null);

        var names = Names(result.Zip);
        Assert.Equal(result.Entries.OrderBy(n => n), names.OrderBy(n => n));
        foreach (var expected in new[]
        {
            "report.txt", "settings/darklands.settings.json", "screenshot.png",
            "saves/last-used/DKSAVE1.SAV", "saves/last-used/DKSAVE1.BSV", "saves/last-used/DKSAVE1.MODS.JSON",
            "saves/at-load/DKSAVE1.SAV", "saves/at-load/DKSAVE1.BSV", "saves/at-load/DKSAVE1.MODS.JSON",
            "game/CACHE.TMP", "game/CHARACTR.TMP",
        })
        {
            Assert.Contains(expected, names);
        }
        Assert.Single(names, n => n.StartsWith("logs/darklands-", StringComparison.Ordinal));
        // the newest save is the one last used, so it is not packed twice
        Assert.DoesNotContain(names, n => n.StartsWith("saves/newest/", StringComparison.Ordinal));
        // the load-time copy is the state before the change
        using var archive = ZipFile.OpenRead(result.Zip);
        Assert.Equal(3, archive.GetEntry("saves/at-load/DKSAVE1.SAV")!.Length);
        Assert.Equal(4, archive.GetEntry("saves/last-used/DKSAVE1.SAV")!.Length);

        var report = Text(result.Zip, "report.txt");
        Assert.Contains("Trigger: manual (test)", report);
        Assert.Contains("Version: 1.0.0-test", report);
        Assert.Contains("Commit: abc1234", report);
        Assert.Contains("Window: 1440x800", report);
        Assert.Contains("the guard walks through the wall", report);
        Assert.Contains("SHA-256 9F64A747E1B97F131FABB6B447296C9B6F0201E79FB3C5356E6C77E89B6A806A", report);   // 01 02 03 04
        Assert.Contains("DKSAVE1.SAV", report);
    }

    [Fact]
    public void A_crash_report_carries_the_exception_and_the_newest_save_when_it_is_not_the_one_used()
    {
        var f = MakeFixture();
        f.Tracker.Used(f.Saves, "DKSAVE0.SAV", loading: false);
        Exception error;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            error = e;
        }

        var result = f.Service.Build(crash: true, null, null, error);

        var report = Text(result.Zip, "report.txt");
        Assert.Contains("Trigger: crash", report);
        Assert.Contains("InvalidOperationException: boom", report);
        Assert.Contains("A_crash_report_carries", report);   // the stack
        Assert.Contains("saves/last-used/DKSAVE0.SAV", Names(result.Zip));
        Assert.DoesNotContain("saves/at-load/DKSAVE0.SAV", Names(result.Zip));   // saved, not loaded: no load-time copy
        Assert.Contains("saves/newest/DKSAVE1.SAV", Names(result.Zip));
        Assert.Contains("saves/newest/DKSAVE1.BSV", Names(result.Zip));
    }

    [Fact]
    public void The_user_name_is_scrubbed_from_every_text_file_but_not_from_the_saves()
    {
        var f = MakeFixture();
        File.WriteAllText(Path.Combine(f.Saves, "DKSAVE1.MODS.JSON"), "{\"by\": \"Zebediah\"}");
        f.Tracker.Used(f.Saves, "DKSAVE1.SAV", loading: true);

        var result = f.Service.Build(crash: false, "test", @"player says: C:\Users\Zebediah\x and Zebediah", new IOException(@"cannot open C:\Users\Zebediah\Desktop\a.txt"));

        foreach (var name in Names(result.Zip).Where(n => n.EndsWith(".txt", StringComparison.Ordinal) || n.EndsWith(".log", StringComparison.Ordinal) || n.EndsWith(".json", StringComparison.Ordinal) && n.StartsWith("settings/", StringComparison.Ordinal)))
        {
            var text = Text(result.Zip, name);
            Assert.DoesNotContain("Zebediah", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("<user>", Text(result.Zip, "report.txt"));
        Assert.Contains("<user>", Text(result.Zip, "settings/darklands.settings.json"));
        Assert.Contains("<user>", Text(result.Zip, Names(result.Zip).Single(n => n.EndsWith(".log", StringComparison.Ordinal))));
        // saved games are copied as they are
        Assert.Contains("Zebediah", Text(result.Zip, "saves/last-used/DKSAVE1.MODS.JSON"));
    }

    [Theory]
    [InlineData(@"open C:\Users\Zebediah\AppData\a.txt", "open <user>\\AppData\\a.txt")]
    [InlineData("open C:/Users/Zebediah/AppData/a.txt", "open <user>/AppData/a.txt")]
    [InlineData(@"\""C:\\Users\\Zebediah\\Games\""", @"\""<user>\\Games\""")]
    [InlineData("/home/zebediah/.config/x", "<user>/.config/x")]
    [InlineData(@"D:\Users\Other\x", @"<user>\x")]
    [InlineData("name ZEBEDIAH here", "name <user> here")]
    [InlineData(@"C:\Users\Public\Documents", @"C:\Users\Public\Documents")]
    public void The_scrubber_replaces_the_profile_and_the_name_in_every_form(string text, string expected)
    {
        Assert.Equal(expected, TestScrubber.Scrub(text));
    }

    [Fact]
    public void A_short_user_name_is_not_replaced_as_a_word()
    {
        Assert.Equal("a <user>\\x and ab", new Scrubber(@"C:\Users\ab", "ab").Scrub(@"a C:\Users\ab\x and ab"));
    }

    [Fact]
    public void The_newest_five_logs_are_kept()
    {
        var logs = Folder("logs");
        for (var i = 0; i < 8; i++)
        {
            using var log = SessionLog.Start(logs, ["x"], new DateTime(2026, 11, 5, 10, i, 0));
        }
        var names = SessionLog.Logs(logs).Select(Path.GetFileName).ToList();
        Assert.Equal(5, names.Count);
        Assert.Equal("darklands-20261105-100700.log", names[0]);
        Assert.Equal("darklands-20261105-100300.log", names[^1]);
    }

    [Fact]
    public void Two_logs_started_in_the_same_second_do_not_clash()
    {
        var logs = Folder("logs2");
        using var a = SessionLog.Start(logs, ["a"], new DateTime(2026, 11, 5, 10, 0, 0));
        using var b = SessionLog.Start(logs, ["b"], new DateTime(2026, 11, 5, 10, 0, 0));
        Assert.NotEqual(a!.Path, b!.Path);
    }

    [Fact]
    public void The_log_has_its_header_and_each_line_is_on_disk_at_once()
    {
        var logs = Folder("logs3");
        using var log = SessionLog.Start(logs, ["Version 1", "Settings file: none"]);
        log!.WriteLine("last words", error: true);
        // read while the log is still open: a crash would leave exactly this
        var text = ReadOpen(log.Path);
        Assert.Contains("Version 1", text);
        Assert.Contains("Settings file: none", text);
        Assert.Contains("! last words", text);
    }

    [Fact]
    public void Console_output_and_error_go_to_the_log_as_well()
    {
        var logs = Folder("logs4");
        var oldOut = Console.Out;
        var oldError = Console.Error;
        var sink = new StringWriter();
        Console.SetOut(sink);
        Console.SetError(sink);
        try
        {
            using var log = SessionLog.Start(logs, ["h"]);
            log!.CaptureConsole();
            Console.WriteLine("hello out");
            Console.Error.WriteLine("hello error");
            Console.Write("partial ");
            Console.WriteLine("line");
            var text = ReadOpen(log.Path);
            Assert.Contains("   hello out", text);
            Assert.Contains(" ! hello error", text);
            Assert.Contains("partial line", text);
            Assert.Contains("hello out", sink.ToString());   // still written where it was
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldError);
        }
    }

    [Fact]
    public void The_newest_ten_reports_are_kept()
    {
        var f = MakeFixture();
        for (var i = 0; i < 13; i++)
        {
            ReportBuilder.Build(new ReportInput(f.Locations.Reports, f.Locations.Logs, "v", "c", false, "t", null, null, [], null, null, [], null, null, TestScrubber,
                Now: new DateTime(2026, 11, 5, 10, 0, i)));
        }
        var zips = Directory.GetFiles(f.Locations.Reports, "darklands-report-*.zip").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(10, zips.Count);
        Assert.Equal("darklands-report-20261105-100003.zip", zips[0]);
        Assert.Equal("darklands-report-20261105-100012.zip", zips[^1]);
        Assert.Empty(Directory.GetFiles(f.Locations.Reports, "*.part"));
    }

    [Fact]
    public void A_file_over_the_limit_is_left_out_and_the_report_says_so()
    {
        var f = MakeFixture();
        using (var big = File.Create(Path.Combine(f.Game, "CACHE.TMP")))
        {
            big.SetLength(ReportBuilder.MaxFileBytes + 1);
        }

        var result = f.Service.Build(crash: false, "test", null, null);

        Assert.DoesNotContain("game/CACHE.TMP", Names(result.Zip));
        Assert.Contains("game/CHARACTR.TMP", Names(result.Zip));
        Assert.Contains(result.Notes, n => n.Contains("game/CACHE.TMP", StringComparison.Ordinal) && n.Contains("20 MB", StringComparison.Ordinal));
        Assert.Contains("Left out:", Text(result.Zip, "report.txt"));
    }

    [Fact]
    public void A_report_with_nothing_to_pack_still_has_report_txt()
    {
        var f = MakeFixture();
        var empty = new ReportService(ReportLocations.Choose(null, Folder("bare"), canWrite: _ => true), "v", "unknown", null, new SaveTracker(Path.Combine(_root, "none")), TestScrubber);

        var result = empty.Build(crash: false, "test", null, null);

        Assert.Equal(["report.txt"], Names(result.Zip));
        Assert.Contains("Commit: unknown", Text(result.Zip, "report.txt"));
        Assert.Contains("DARKLAND.EXE: not found", Text(result.Zip, "report.txt"));
    }

    [Fact]
    public void The_save_tracker_remembers_the_last_game_across_a_restart()
    {
        var f = MakeFixture();
        f.Tracker.Used(f.Saves, "dksave1.sav", loading: true);

        var again = new SaveTracker(f.Locations.Session);

        Assert.Equal("DKSAVE1.SAV", again.Last!.Name);
        Assert.True(again.Last.Loaded);
        Assert.True(File.Exists(Path.Combine(again.AtLoadFolder, "DKSAVE1.SAV")));
        Assert.True(File.Exists(Path.Combine(again.AtLoadFolder, "DKSAVE1.BSV")));
        Assert.False(File.Exists(Path.Combine(again.AtLoadFolder, "DKSAVE0.SAV")));
    }

    [Fact]
    public void The_save_tracker_never_throws_for_a_missing_file()
    {
        var tracker = new SaveTracker(Folder("session"));
        tracker.Used(Path.Combine(_root, "nowhere"), "DKSAVE9.SAV", loading: true);
        Assert.Equal("DKSAVE9.SAV", tracker.Last!.Name);
    }

    [Fact]
    public void The_folders_are_beside_the_settings_file_or_in_the_user_folder_when_that_cannot_be_written()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;   // the drive-letter paths below
        }
        var beside = ReportLocations.Choose(@"D:\Game\darklands.settings.json", appData: @"C:\Roaming", canWrite: _ => true);
        Assert.Equal(@"D:\Game\logs", beside.Logs);
        Assert.Equal(@"D:\Game\reports", beside.Reports);

        var fallback = ReportLocations.Choose(@"D:\Game\darklands.settings.json", appData: @"C:\Roaming", canWrite: p => !p.StartsWith("D:", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(@"C:\Roaming", "DarklandsRestoration", "logs"), fallback.Logs);
        Assert.Equal(Path.Combine(@"C:\Roaming", "DarklandsRestoration", "reports"), fallback.Reports);
    }

    [Fact]
    public void The_issue_address_fills_the_form_fields_and_is_encoded()
    {
        var url = IssueForm.Url(IssueForm.KindCrash, "1.0.0-rc1 (commit abc1234)", "Windows 11, .NET 10", "line one\nline two & more = 100%", "Crash: X");

        Assert.StartsWith("https://github.com/Arkana-Mechanika-Studios/darklands-restoration/issues/new?template=bug_report.yml&", url);
        Assert.Contains("&kind=Crash&", url);
        Assert.Contains("&version=1.0.0-rc1%20%28commit%20abc1234%29&", url);
        Assert.Contains("&system=Windows%2011%2C%20.NET%2010&", url);
        Assert.Contains("&title=Crash%3A%20X", url);
        Assert.EndsWith("&what_happened=line%20one%0Aline%20two%20%26%20more%20%3D%20100%25", url);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("line one\nline two & more = 100%", query["what_happened"]);
    }

    [Fact]
    public void The_issue_address_is_kept_under_the_limit()
    {
        var huge = string.Join('\n', Enumerable.Range(0, 2000).Select(i => "   at Some.Very.Long.Namespace.Type.Method" + i + "(String argument) in C:\\a\\b.cs:line " + i));

        var url = IssueForm.Url(IssueForm.KindOther, "1.0.0-rc1 (commit abc1234)", "Linux", huge);

        Assert.True(url.Length <= IssueForm.MaxLength, url.Length.ToString());
        Assert.True(url.Length > 4000);   // cut to fit, not dropped
        Assert.Contains("&what_happened=", url);
        Assert.Equal(IssueForm.MaxLength, 6000);
    }

    [Fact]
    public void The_issue_address_survives_unusual_text()
    {
        var url = IssueForm.Url(IssueForm.KindCrash, "v", "s", "ünïcode ✓ \u2028 \"quotes\" <b>");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("ünïcode ✓ \u2028 \"quotes\" <b>", query["what_happened"]);
        Assert.Equal("Crash", query["kind"]);
    }

    [Fact]
    public void The_service_builds_an_address_with_the_exception_and_without_the_name()
    {
        var f = MakeFixture();
        var url = f.Service.IssueUrl(crash: true, null, null, new IOException(@"cannot open C:\Users\Zebediah\a.txt"));

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("Crash", query["kind"]);
        Assert.Equal("1.0.0-test (commit abc1234)", query["version"]);
        Assert.Contains("IOException", query["what_happened"]);
        Assert.DoesNotContain("Zebediah", url, StringComparison.OrdinalIgnoreCase);
        Assert.True(url.Length <= IssueForm.MaxLength);
    }

    /// <summary>Issue #3: a player's own report leaves "What happened" to the player, or gives only what they wrote in the program.</summary>
    [Fact]
    public void A_players_report_leaves_what_happened_to_the_player()
    {
        var f = MakeFixture();
        var empty = System.Web.HttpUtility.ParseQueryString(new Uri(f.Service.IssueUrl(crash: false, "Ctrl-F12", null, null)).Query);
        Assert.Null(empty["what_happened"]);
        Assert.Equal("1.0.0-test (commit abc1234)", empty["version"]);
        var written = System.Web.HttpUtility.ParseQueryString(new Uri(f.Service.IssueUrl(crash: false, "Ctrl-F12", "  The stairs did nothing. ", null)).Query);
        Assert.Equal("The stairs did nothing.", written["what_happened"]);
    }

    [Fact]
    public void Opening_the_folder_and_the_browser_fails_quietly_with_the_path_said()
    {
        var old = ReportOpener.Start;
        var said = new List<string>();
        try
        {
            ReportOpener.Start = _ => throw new System.ComponentModel.Win32Exception("no xdg-open");
            Assert.False(ReportOpener.ShowFile(@"C:\x\reports\a.zip", said.Add));
            Assert.False(ReportOpener.OpenUrl("https://example.org/x", said.Add));
        }
        finally
        {
            ReportOpener.Start = old;
        }
        Assert.Contains(said, s => s.Contains(@"C:\x\reports\a.zip", StringComparison.Ordinal));
        Assert.Contains(said, s => s.Contains("https://example.org/x", StringComparison.Ordinal));
    }

    [Fact]
    public void Opening_the_folder_and_the_browser_uses_the_systems_program()
    {
        var old = ReportOpener.Start;
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        try
        {
            ReportOpener.Start = info =>
            {
                started.Add(info);
                return true;
            };
            Assert.True(ReportOpener.ShowFile(Path.Combine(_root, "a.zip"), _ => { }));
            Assert.True(ReportOpener.OpenUrl("https://example.org/x", _ => { }));
        }
        finally
        {
            ReportOpener.Start = old;
        }
        Assert.Equal(2, started.Count);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("explorer.exe", started[0].FileName);
            Assert.Contains("/select,", started[0].Arguments, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("xdg-open", started[1].FileName);
        }
    }
}
