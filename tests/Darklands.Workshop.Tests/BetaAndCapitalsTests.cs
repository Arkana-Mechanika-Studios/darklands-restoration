using System.Xml.Linq;
using Avalonia.Headless;
using Darklands.Content;
using Darklands.Presentation.Graphics;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The Workshop's beta version (one source in the build; recorded by every pack it saves and every export; said in a report) and a
/// pack's own decorated capitals (read by the game first, listed, imported and checked by the Workshop).
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class BetaAndCapitalsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void TheWorkshopsVersionIsTheBuildsOne()
    {
        var props = XDocument.Load(Path.Combine(Root(), "Directory.Build.props"));
        var version = props.Descendants("WorkshopVersion").Single().Value;
        Assert.Equal(version, WorkshopVersion.Current);
        Assert.Equal("1.1.0-beta.3", WorkshopVersion.Current);
        Assert.True(WorkshopVersion.IsBeta);
        Assert.Equal("Darklands Workshop " + version, WorkshopVersion.Label);
        // a pre-release's notes are there (the pre-release workflow refuses one without)
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "workshop-releases", version + ".md")));
    }

    [Fact]
    public void PacksAndExportsRecordTheWorkshopThatWroteThem()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-beta-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            var manifest = Path.Combine(folder, "pack.json");
            Assert.Contains("\"workshop\": \"" + WorkshopVersion.Current + "\"", File.ReadAllText(manifest), StringComparison.Ordinal);
            // an older Workshop's pack, with a field the game does not know: the game reads it, a save records this Workshop
            File.WriteAllText(manifest, "{ \"language\": \"de\", \"name\": \"Test\", \"base\": \"483.07\", \"workshop\": \"1.1.0-beta.0\", \"future\": { \"x\": 1 } }");
            var pack = Darklands.Localization.LanguagePack.Load(folder, out _);
            Assert.NotNull(pack);
            Assert.Equal("1.1.0-beta.0", pack.Workshop);
            project = TextProject.Open(folder, game);
            var card = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            project.Set(card, card.English.Replace("You come to the", "Ihr kommt zur", StringComparison.Ordinal));
            project.Save();
            Assert.Equal(WorkshopVersion.Current, Darklands.Localization.LanguagePack.WorkshopOf(folder));
            Assert.Contains("\"future\"", File.ReadAllText(manifest), StringComparison.Ordinal);

            var items = TextExport.Items(project, [card], Glossary.Load(folder));
            Assert.Contains("\"workshop\": \"" + WorkshopVersion.Current + "\"", TextExport.Json(project, items), StringComparison.Ordinal);
            Assert.StartsWith("# written by " + WorkshopVersion.Label, TextExport.Csv(project, items), StringComparison.Ordinal);
            Assert.Contains(">" + WorkshopVersion.Label + "</note>", TextXliff.Write(project, items), StringComparison.Ordinal);
            // and they read back as before
            Assert.NotEmpty(TextImport.Read(TextExport.Csv(project, items), ".csv"));
            Assert.NotEmpty(TextImport.Read(TextXliff.Write(project, items), ".xlf"));

            // the Workshop's report says its version and the pack's
            var facts = WorkshopReport.Facts(game, project).ToDictionary(f => f.Key, f => f.Value);
            Assert.Equal(WorkshopVersion.Label + " (beta)", facts["Program"]);
            Assert.Equal(WorkshopVersion.Label, facts["Pack written by"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACapitalsFileNamesItsLetter()
    {
        Assert.Equal(0x3A6, PackCapitals.CodeOf("Φ.png"));
        Assert.Equal(0x3A6, PackCapitals.CodeOf("U+03A6.png"));
        Assert.Equal(0x3A6, PackCapitals.CodeOf("03a6.png"));
        Assert.Equal('A', PackCapitals.CodeOf("A.png"));
        Assert.Null(PackCapitals.CodeOf("capital.png"));
        Assert.Null(PackCapitals.CodeOf("AB.png"));
        Assert.Equal("U+0416.png", PackCapitals.FileNameOf('Ж'));
    }

    private static void UsePngReader() =>
        PackCapitals.ReadImage = path => { var image = Darklands.Rendering.PngCodec.Read(path); return (image.Width, image.Height, image.Argb); };

    [Fact]
    public async Task APacksOwnCapitalIsCheckedImportedAndDrawnFirst()
    {
        if (GameFolder.Files is not { } files || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        UsePngReader();
        var folder = Path.Combine(Path.GetTempPath(), "dl-capitals-" + Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(folder + "-templates");
        try
        {
            var project = TextProject.Create(folder, "ru", "Test", null, game);
            var card = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            var russian = card.English.Replace("You come to the", "Жители ведут вас к", StringComparison.Ordinal);
            Assert.StartsWith("Жители", CardRows.Parse(russian)[0].Field ?? "", StringComparison.Ordinal);
            project.Set(card, russian);
            project.Save();
            FontNeeds.Build(game, folder, "ru");
            var assets = new GameAssets(files);

            // the game's A as a template: in the capitals' colours, so it reads back as the game's very picture
            var a = PackCapitals.Template(assets, 'A')!;
            var (picture, problem) = PackCapitals.Convert(PackCapitals.Size, PackCapitals.Size, a, PackCapitals.Colours(assets), 'A');
            Assert.Null(problem);
            Assert.Equal(DropCaps.Get(assets, (byte)'A')!.Pixels.ToArray(), picture!.Pixels.ToArray());
            Assert.NotNull(PackCapitals.Convert(21, 20, new uint[21 * 20], PackCapitals.Colours(assets), 'A').Problem);
            Assert.Contains("is not one of", PackCapitals.Convert(20, 20, Enumerable.Repeat(0xFF123456u, 400).ToArray(), PackCapitals.Colours(assets), 'A').Problem, StringComparison.Ordinal);

            // before: Ж has no capital (the check's note, the list's "box")
            Assert.Contains(project.Check(), f => f.Severity == Darklands.Presentation.LanguagePackCheck.Severity.Note && f.Message.Contains("'Ж', which has no decorated capital", StringComparison.Ordinal));
            var preview = new TextPreview(project);
            var boxed = preview.Render(card, card.Translation);
            Assert.Equal(CapitalSource.Box, CapitalList.Of(project, preview.Capitals).Single(c => c.Code == 'Ж').Source);

            await WorkshopWindowTests.Session.Value.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [folder]);
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Editor!.Select(card.Key);
                var capitals = window.ShowCapitalsForTests()!;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(26, capitals.SaveTemplates(templates));
                // a wrong picture is refused, said why; the template of Ж's look (the game's X here) is taken
                var wrong = Path.Combine(templates, "wrong.png");
                Darklands.Rendering.PngCodec.Write(new Darklands.Rendering.RgbaImage(16, 16, new uint[256]), wrong);
                Assert.False(capitals.ImportFile('Ж', wrong));
                Assert.Contains("20×20", capitals.Message, StringComparison.Ordinal);
                Assert.True(capitals.ImportFile('Ж', Path.Combine(templates, "X.png")));
                Assert.True(File.Exists(Path.Combine(folder, "capitals", "U+0416.png")));
                Assert.Equal(CapitalSource.Pack, capitals.Listed.Single(c => c.Code == 'Ж').Source);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS") is { Length: > 0 } shots)
                {
                    Directory.CreateDirectory(shots);
                    capitals.CaptureRenderedFrame()?.Save(Path.Combine(shots, "11-capitals.png"));
                    window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "12-capital-in-preview.png"));
                }
                capitals.Close();
                window.Close();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return 0;
            }, CancellationToken.None);

            // after: the preview draws the pack's picture (the game's X) where the box was; the check has no note
            var drawn = new TextPreview(project).Render(card, card.Translation);
            Assert.NotEqual(boxed.Argb, drawn.Argb);
            Assert.DoesNotContain(project.Check(), f => f.Message.Contains("'Ж', which has no decorated capital", StringComparison.Ordinal));

            // the game: the card screen's layout takes the pack's picture first; without it, the game's way (the box) as before
            var painter = new TextPainter(assets.Font(3)) { Capitals = PackCapitals.Load(folder, assets).Get };
            var plain = new TextPainter(assets.Font(3));
            int[] codes = ['Ж', 'и', ' ', 'x'];
            var withPack = new Canvas(new Darklands.Platform.Screen());
            var without = new Canvas(new Darklands.Platform.Screen());
            var settings = new CardTextLayout.Settings { Color = 15, DropCap = letter => DropCaps.Get(assets, letter) };
            new CardTextLayout(painter).Draw(withPack, codes, 10, 10, 200, settings);
            new CardTextLayout(plain).Draw(without, codes, 10, 10, 200, settings);
            var x = DropCaps.Get(assets, (byte)'X')!;
            for (var py = 0; py < 20; py++)
            {
                for (var px = 0; px < 20; px++)
                {
                    if (x.Pixels[py * 20 + px] != 0)
                    {
                        Assert.Equal(x.Pixels[py * 20 + px], withPack[10 + px, 10 + py]);
                    }
                }
            }
            Assert.NotEqual(withPack.Screen.Pixels, without.Screen.Pixels);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            if (Directory.Exists(templates))
            {
                Directory.Delete(templates, recursive: true);
            }
        }
    }
}
