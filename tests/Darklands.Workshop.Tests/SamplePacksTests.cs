using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Localization;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The sample translation packs that come with the Workshop (Workshop/examples/languages, docs/language-samples): short excerpts of
/// the opening only, each marked a sample, keyed by id with no English and nothing of the German edition, the CJK ones with their
/// TrueType font and its licence; the start page lists them in order, and opening one offers the font from the player's game.
/// With DARKLANDS_WORKSHOP_SHOTS set, the start page and a sample in the editor are saved there (the translator's guide's pictures).
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class SamplePacksTests
{
    private static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "examples", "languages");

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
            Assert.InRange(pack.Messages.Count, 1, 40);
            foreach (var message in pack.Messages.Select(m => m.Message.Id))
            {
                Assert.True(message.StartsWith("exe-", StringComparison.Ordinal) || Cards.Any(c => message.StartsWith("msg-" + c[1..].ToLowerInvariant() + "-", StringComparison.Ordinal)), message);
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
        Assert.All(Order.Except(["sample-ja", "sample-ko"]), f => Assert.False(Directory.Exists(Path.Combine(Shipped, f, "fonts")), f));
    }

    [Fact]
    public async Task TheStartPageListsTheSamplesAndOpeningOneOffersItsFont()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var shots = Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS");
        // a copy of the samples: opening one, building its font, must not touch the shipped ones
        var root = Path.Combine(Path.GetTempPath(), "dl-samples-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(Shipped, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(root, Path.GetRelativePath(Shipped, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        var before = WorkshopApp.SamplesFolder;
        try
        {
            WorkshopApp.SamplesFolder = root;
            // the French sample's font from the game (as the offer builds it), for the editor's picture
            FontNeeds.Build(game, Path.Combine(root, "sample-fr"), "fr");
            var session = WorkshopWindowTests.Session.Value;
            await session.Dispatch(() =>
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

                var start = new WorkshopWindow(new WorkshopSettings { GameFolder = game, RecentPacks = [Path.Combine(root, "sample-ru")] }, []) { Width = 1280, Height = 860 };
                start.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var words = Words(start);
                Assert.Contains("Samples", words);
                Assert.Contains("New to translating? Read the translator's guide", words);
                // in their order, and a sample once opened is not among your packs
                var names = words.Where(w => w.EndsWith("(sample)", StringComparison.Ordinal)).ToList();
                Assert.Equal(["Français (sample)", "Español (sample)", "Polski (sample)", "Русский (sample)", "Ελληνικά (sample)", "日本語 (sample)", "한국어 (sample)", "Français, mises en page (sample)"], names);
                Shot(start, "samples-1-start");
                start.Close();

                // a sample whose letters the game's fonts lack: opening it offers the font
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, []) { Width = 1280, Height = 800 };
                window.Show();
                window.OpenSampleForTests(Path.Combine(root, "sample-pl"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var offer = Assert.Single(window.OwnedWindows);
                Assert.Equal("A font for your language", offer.Title);
                offer.Close();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                // one that has its font: nothing asked, the church in the editor as the game draws it
                window.OpenSampleForTests(Path.Combine(root, "sample-fr"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.OwnedWindows);
                window.Editor!.Select(WorkshopWindow.SampleStart);
                Shot(window, "samples-2-card-text");
                // the layouts sample: a screen text drawn on two lines by the pack's layout
                window.OpenSampleForTests(Path.Combine(root, "sample-fr-layouts"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                foreach (var w in window.OwnedWindows.ToList())
                {
                    w.Close();
                }
                // every text the sample lays out, without a warning
                foreach (var key in new[] { "exe:290E:0148", "exe:DS:4515", "exe:DS:454D", "exe:DS:3671", "exe:DS:36C8", "exe:DS:2FF2" })
                {
                    window.Editor!.Select(key);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.DoesNotContain(Words(window), w => w.StartsWith("Layout:", StringComparison.Ordinal));
                }
                Shot(window, "samples-3-screen-text");
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            WorkshopApp.SamplesFolder = before;
            Directory.Delete(root, recursive: true);
        }
    }
}
