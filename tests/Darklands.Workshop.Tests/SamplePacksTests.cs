using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Localization;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The sample translation packs that come with the Workshop (Workshop/examples/sample-languages, a read-only shelf; source
/// docs/language-samples): short excerpts of the opening only, each marked a sample, keyed by id with no English and nothing of the
/// German edition, the CJK ones with their TrueType font and its licence. The start page lists them in order, each with Play (from
/// the shelf, test saves apart) and Start from this sample (a copy in the game's packs/languages, named, opened, its font offered);
/// a pack from elsewhere says it is outside the game's packs and is added on request. Nothing changes the shelf.
/// With DARKLANDS_WORKSHOP_SHOTS set, the start page and the outside-pack line are saved there as pictures.
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class SamplePacksTests
{
    private static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "examples", "sample-languages");

    private static readonly string[] Order = ["sample-fr", "sample-es", "sample-pl", "sample-ru", "sample-el", "sample-ja", "sample-ko", "sample-fr-layouts"];

    // the excerpt: the city square and the church by day, and the menu bar's and the party information screen's headings (with
    //   the layouts sample's screen texts)
    private static readonly string[] Cards = ["$CITYS00", "$CITYC00"];

    [Fact]
    public void TheSamplesAreShortExcerptsOfTheOpeningMarkedAsSamples()
    {
        Assert.Equal(Order.Order(StringComparer.Ordinal), Directory.GetDirectories(Shipped).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var folder in Order.Select(f => Path.Combine(Shipped, f)))
        {
            var pack = LanguagePack.Load(folder, out var why);
            Assert.True(pack is not null, why);
            Assert.EndsWith("(sample)", pack!.Name, StringComparison.Ordinal);
            Assert.NotEqual("de", pack.Language);
            Assert.InRange(pack.Messages.Count, 1, 60);
            foreach (var message in pack.Messages.Select(m => m.Message.Id))
            {
                Assert.True(message.StartsWith("exe-", StringComparison.Ordinal) || message.StartsWith("id-rw-money-", StringComparison.Ordinal)
                    || Cards.Any(c => message.StartsWith("msg-" + c[1..].ToLowerInvariant() + "-", StringComparison.Ordinal)), message);
            }
            // only its own words: the excerpt's files, by id (no template, no boxes, no English)
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).ToList();
            Assert.All(files, f => Assert.True(f == "pack.json" || f.StartsWith("text/", StringComparison.Ordinal) || f.StartsWith("fonts/", StringComparison.Ordinal) || f == "interface/layout.json", f));
            foreach (var text in files.Where(f => f.EndsWith(".ftl", StringComparison.Ordinal)))
            {
                var words = File.ReadAllText(Path.Combine(folder, text));
                foreach (var english in new[] { "You come to the", "the church of", "You decide to", "main square", "Leave by" })
                {
                    Assert.DoesNotContain(english, words, StringComparison.Ordinal);
                }
            }
        }
        // the CJK samples: their TrueType font (one per pack, so each plays copied alone) with its licence; the others no font
        foreach (var cjk in new[] { "sample-ja", "sample-ko" })
        {
            Assert.True(File.Exists(Path.Combine(Shipped, cjk, "fonts", "NotoSansCJK-Regular.ttc")));
            Assert.True(File.Exists(Path.Combine(Shipped, cjk, "fonts", "LICENSE-OFL.txt")));
        }
        // the others: their pixel font packs, built from the original fonts in their format (the user's decision, 2026-11-22)
        Assert.All(Order.Except(["sample-ja", "sample-ko"]), f =>
        {
            Assert.True(File.Exists(Path.Combine(Shipped, f, "fonts", "slot1.json")), f);
            Assert.True(File.Exists(Path.Combine(Shipped, f, "fonts", "slot3.png")), f);
        });
        // every card sample has the money words $Money fills an amount with
        Assert.All(Order.Except(["sample-fr-layouts"]), f => Assert.True(LanguagePack.Load(Path.Combine(Shipped, f), out _)!.Localizer.IsTranslated(Darklands.Core.GameTextIds.MoneyFlorins), f));
        // and in the published Workshop too: the single-file program would otherwise take the font into itself, leaving the packs
        //   without it (the build's output above does not show that)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }
        var project = File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Darklands.Workshop", "Darklands.Workshop.csproj"));
        var fontLines = project.Where(l => l.Contains("NotoSansCJK-Regular.ttc", StringComparison.Ordinal) && l.Contains("<None", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, fontLines.Count);
        Assert.All(fontLines, l => Assert.Contains("ExcludeFromSingleFile=\"true\"", l, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSamplesArePlayedFromTheShelfAndPacksStartedFromThemLiveInTheGamesPacks()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var shots = Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS");
        // a game program's folder of its own (its packs: fonts/hd-fonts, languages), the Workshop's own folder, and a pack elsewhere
        var root = Path.Combine(Path.GetTempPath(), "dl-samples-" + Guid.NewGuid().ToString("N"));
        var program = Path.Combine(root, "DarklandsRP");
        var languages = Path.Combine(program, "packs", "languages");
        var hd = Directory.CreateDirectory(Path.Combine(program, "packs", "fonts", "hd-fonts")).FullName;
        Directory.CreateDirectory(languages);
        var elsewhere = Path.Combine(root, "Packs", "fr");
        // the shelf as shipped, and a fingerprint of it: nothing the Workshop does may change it
        string Fingerprint() => string.Join("\n", Directory.EnumerateFiles(Shipped, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => $"{Path.GetRelativePath(Shipped, f)} {new FileInfo(f).Length} {File.GetLastWriteTimeUtc(f).Ticks}"));
        var shelf = Fingerprint();
        var (programBefore, workshopBefore) = (WorkshopApp.GameProgramFolder, WorkshopSettings.Folder);
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        try
        {
            WorkshopApp.GameProgramFolder = program;
            WorkshopSettings.Folder = Path.Combine(root, "workshop-settings");
            WorkshopWindow.StartProcessForTests = start => { started.Add(start); return true; };
            var session = WorkshopWindowTests.Session.Value;
            await session.Dispatch(async () =>
            {
                void Shot(Window window, string name)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    if (shots is { Length: > 0 })
                    {
                        Directory.CreateDirectory(shots);
                        window.CaptureRenderedFrame()?.Save(Path.Combine(shots, name + ".png"));
                    }
                }
                static List<string> Words(Window window) =>
                    window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
                NameDialog Asked(Window window)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    return Assert.IsType<NameDialog>(Assert.Single(window.OwnedWindows));
                }

                var start = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, []) { Width = 1280, Height = 860 };
                start.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var words = Words(start);
                Assert.Contains("Samples", words);
                Assert.Contains("New to translating? Read the translator's guide", words);
                Assert.Equal(["Français (sample)", "Español (sample)", "Polski (sample)", "Русский (sample)", "Ελληνικά (sample)", "日本語 (sample)", "한국어 (sample)", "Français, mises en page (sample)"],
                    words.Where(w => w.EndsWith("(sample)", StringComparison.Ordinal)).ToList());
                // two buttons a sample, and no Open
                Assert.Equal(8, words.Count(w => w == "Start from this sample"));
                Assert.Equal(8, words.Count(w => w == "Play"));
                Assert.DoesNotContain("Open", words);
                Shot(start, "samples-1-start");

                // Play: straight from the shelf, at the church, test saves in the Workshop's own folder, HD fonts for letters the game lacks
                start.PlaySampleForTests(Path.Combine(Shipped, "sample-ru"));
                var played = Assert.Single(started);
                var args = played.ArgumentList.ToList();
                Assert.Equal(Path.Combine(Shipped, "sample-ru"), args[args.IndexOf("--language-pack") + 1]);
                Assert.Equal(Path.Combine(root, "workshop-settings", "sample-saves", "sample-ru"), args[args.IndexOf("--saves") + 1]);
                // the sample brings its pixel font: no HD fonts asked for
                Assert.DoesNotContain("--font-pack", args);
                Assert.Equal("card:$CITYC00#0", args[args.IndexOf("--start-at") + 1]);
                // a CJK sample brings its own TrueType font: none given
                started.Clear();
                start.PlaySampleForTests(Path.Combine(Shipped, "sample-ja"));
                Assert.DoesNotContain("--font-pack", Assert.Single(started).ArgumentList);
                started.Clear();

                // Start from this sample: the name asked (the language suggested), the copy in the game's packs/languages, opened
                var copying = start.StartFromSampleForTests(Path.Combine(Shipped, "sample-pl"));
                var ask = Asked(start);
                Assert.Contains("copy of this sample", ask.Text, StringComparison.Ordinal);
                Assert.Equal("pl", ask.Suggested);
                ask.Answer("polski");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                // its font came with it: nothing asked
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Empty(start.OwnedWindows);
                await copying;
                var mine = Path.Combine(languages, "polski");
                Assert.True(File.Exists(Path.Combine(mine, "text", "pl", "msg", "$CITYC00.ftl")));
                Assert.Equal("Polski", LanguagePack.Load(mine, out _)!.Name);
                Assert.Null(start.OutsideNoticeForTests);
                Assert.Equal(mine, start.PackFolderForTests);
                // the same name again: asked again, nothing overwritten
                copying = start.StartFromSampleForTests(Path.Combine(Shipped, "sample-pl"));
                ask = Asked(start);
                Assert.Equal("pl", ask.Suggested);
                ask.Answer("polski");
                ask = Asked(start);
                Assert.Contains("There is already a pack named \"polski\"", ask.Text, StringComparison.Ordinal);
                ask.Answer(null);
                await copying;
                Assert.Equal("Polski", LanguagePack.Load(mine, out _)!.Name);

                // a pack opened from elsewhere: edited and played as any other, said to be outside the game's packs, added on request
                TextProject.Create(elsewhere, "fr", "Mon français", "a translator", game).Save();
                start.OpenForTests(elsewhere);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.StartsWith("Not in the game's packs: the launcher won't list it.", start.OutsideNoticeForTests, StringComparison.Ordinal);
                Shot(start, "outside-pack");
                var adding = start.AddToTheGameForTests();
                ask = Asked(start);
                Assert.Equal("fr", ask.Suggested);
                ask.Answer("fr");
                await adding;
                Assert.True(File.Exists(Path.Combine(languages, "fr", "pack.json")));
                Assert.True(File.Exists(Path.Combine(elsewhere, "pack.json")));
                Assert.Null(start.OutsideNoticeForTests);
                start.Close();
            }, CancellationToken.None);
        }
        finally
        {
            WorkshopWindow.StartProcessForTests = null;
            (WorkshopApp.GameProgramFolder, WorkshopSettings.Folder) = (programBefore, workshopBefore);
            Directory.Delete(root, recursive: true);
        }
        // the shelf: untouched
        Assert.Equal(shelf, Fingerprint());
    }

    [Fact]
    public async Task AMoneyAmountInATextWithTheMoneyWordsNotTranslatedGetsANote()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "dl-moneynote-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = Path.Combine(root, "fr");
            var project = TextProject.Create(pack, "fr", "Essai", "a translator", game);
            const string Church = "msg:$CITYC00#0";
            var church = project.Entries.First(e => e.Key == Church);
            Assert.Contains("$Money1", church.English, StringComparison.Ordinal);
            project.Set(church, church.English.Replace("You come to the", "Vous arrivez à", StringComparison.Ordinal));
            project.Save();
            var session = WorkshopWindowTests.Session.Value;
            await session.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [pack]) { Width = 1280, Height = 800 };
                window.Show();
                window.Editor!.Select(Church);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.StartsWith("$Money… fills in an amount from texts you haven't translated yet: the money words", window.Editor.FillNoteForTests, StringComparison.Ordinal);
                // a text without a filled placeholder: no note
                window.Editor.Select("msg:$CITYC00#1");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(window.Editor.FillNoteForTests);
                window.Close();
            }, CancellationToken.None);
            // the money words translated: no note
            project = TextProject.Open(pack, game);
            foreach (var id in Darklands.Core.GameTextIds.MoneyWords)
            {
                var entry = project.Entries.First(e => e.Key == id);
                project.Set(entry, entry.English);
            }
            project.Save();
            await session.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [pack]) { Width = 1280, Height = 800 };
                window.Show();
                window.Editor!.Select(Church);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(window.Editor.FillNoteForTests);
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
