using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The start page's packs are translation packs only: the HD fonts pack the release puts in packs/fonts (a font-only pack.json, no
/// language), a font-only pack loose in packs/ and an audio pack are not listed. And the New pack dialog fills the pack's name and folder
/// from the language as it is typed, a letter at a time, until the player types a name of their own, which it then keeps.
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class StartPackListAndNewPackTests
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

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [Fact]
    public async Task TheStartPageListsTranslationPacksOnlyNotTheHdFonts()
    {
        var root = Path.Combine(Path.GetTempPath(), "dl-startpacks-" + Guid.NewGuid().ToString("N"));
        var program = Path.Combine(root, "DarklandsRP");
        var programBefore = WorkshopApp.GameProgramFolder;
        try
        {
            // the release's layout: packs/fonts/hd-fonts from its source (docs/hd-fonts-pack), the player's packs/languages/fr, an audio
            //   pack in packs/audio, and a font-only pack put straight into packs
            CopyFolder(Path.Combine(Root(), "docs", "hd-fonts-pack"), Path.Combine(program, "packs", "fonts", "hd-fonts"));
            CopyFolder(Path.Combine(Root(), "docs", "hd-fonts-pack"), Path.Combine(program, "packs", "my-fonts"));
            var fr = Directory.CreateDirectory(Path.Combine(program, "packs", "languages", "fr", "text", "fr")).Parent!.Parent!.FullName;
            File.WriteAllText(Path.Combine(fr, "pack.json"), "{ \"language\": \"fr\", \"name\": \"Français\" }\n");
            File.WriteAllText(Path.Combine(fr, "text", "fr", "ui.ftl"), "id-rw-notice-press-a-key = Appuyez sur une touche.\n");
            var audio = Directory.CreateDirectory(Path.Combine(program, "packs", "audio", "sounds")).FullName;
            File.WriteAllText(Path.Combine(audio, "audio.json"), "{ \"name\": \"Sounds\" }\n");
            // the catalog the start page reads
            var found = PackCatalog.Find(PackCatalog.PackFolders(program, null), []);
            Assert.Equal([fr], found.Select(p => p.Folder));
            // and the start page itself
            WorkshopApp.GameProgramFolder = program;
            await WorkshopWindowTests.Session.Value.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings(), []) { Width = 1280, Height = 800 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(window.StartPacksForTests, p => p.Name == "HD fonts" || p.Folder.Contains("hd-fonts", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(window.StartPacksForTests, p => p.Folder == fr);
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            WorkshopApp.GameProgramFolder = programBefore;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NewPackFollowsTheLanguageTypedALetterAtATimeAndKeepsATypedName()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var packs = Path.Combine(Path.GetTempPath(), "dl-newpack-" + Guid.NewGuid().ToString("N"), "packs", "languages");
        await WorkshopWindowTests.Session.Value.Dispatch(() =>
        {
            var dialog = new NewPackDialog(game, packs, null) { Width = 560 };
            dialog.Show();
            // the jobs Show() posts (the boxes' first TextChanged) run before anything is typed, as in the program
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var typed in new[] { "f", "fr", "fra" })
            {
                dialog.TypeSearchForTests(typed);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal("fr", dialog.LanguageTag);
            Assert.Equal("Français", dialog.PackName);
            Assert.Equal(Path.Combine(packs, "fr"), dialog.Folder);
            // a name of the player's own: kept when the language changes, the folder still follows
            dialog.TypeNameForTests("Mon français");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var typed in new[] { "d", "de", "deu" })
            {
                dialog.TypeSearchForTests(typed);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal("de", dialog.LanguageTag);
            Assert.Equal("Mon français", dialog.PackName);
            Assert.Equal(Path.Combine(packs, "de"), dialog.Folder);
            dialog.Close();
        }, CancellationToken.None);
    }
}
