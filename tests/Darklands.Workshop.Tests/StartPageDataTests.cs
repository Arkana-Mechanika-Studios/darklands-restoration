using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The start page's data: the languages of a new pack (found by any of their names or tag), the check of another tag, and the packs found on disk.</summary>
public sealed class StartPageDataTests
{
    [Fact]
    public void TheLanguagesAreFoundByTheirNamesOrTag()
    {
        Assert.Equal("pl", Languages.Find("pol")[0].Tag);
        Assert.Equal("pl", Languages.Find("Polski")[0].Tag);
        Assert.Contains(Languages.Find("portug"), l => l.Tag == "pt-BR");
        Assert.Equal("el", Languages.Find("Ελλην")[0].Tag);
        Assert.Equal("ru", Languages.Find("ru")[0].Tag);
        Assert.Equal(Languages.All.Count, Languages.Find("").Count);
        Assert.Equal("Français", Languages.Of("FR")!.Native);
        Assert.Null(Languages.Of("xx"));
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("pt-BR", true)]
    [InlineData("sr-Latn", true)]
    [InlineData("es-419", true)]
    [InlineData("gsw-CH", true)]
    [InlineData("zh-Hant-TW", true)]
    [InlineData("de-CH-1996", true)]
    [InlineData("", false)]
    [InlineData("f", false)]
    [InlineData("fr_FR", false)]
    [InlineData("français", false)]
    [InlineData("en-", false)]
    public void ATagIsCheckedAsBcp47(string tag, bool wellFormed) => Assert.Equal(wellFormed, Languages.IsWellFormed(tag));

    [Fact]
    public void TheMakersGuidesComeWithTheWorkshopAndItLooksForTheGameBesideOrAbove()
    {
        // in a release: DarklandsRP/Workshop/docs (the game's own docs folder keeps the players' guides only)
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "docs", "LOCALISATION.md")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "docs", "modding", "README.md")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Inter-OFL.txt")));
        Assert.Equal(Darklands.Platform.PackLayout.GameProgramFolder(AppContext.BaseDirectory), WorkshopApp.GameProgramFolder);
        Assert.Equal(Path.Combine(WorkshopApp.GameProgramFolder, OperatingSystem.IsWindows() ? "DarklandsRP.exe" : "DarklandsRP"), WorkshopApp.GameProgram);
    }

    [Fact]
    public void TheWorkshopsIconIsItsOwnFrom16To256Pixels()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var ico = File.ReadAllBytes(Path.Combine(dir.FullName, "src", "Darklands.Workshop", "Assets", "DarklandsWorkshop.ico"));
        var game = File.ReadAllBytes(Path.Combine(dir.FullName, "src", "Darklands.Host.Sdl", "Resources", "DarklandsRP.ico"));
        Assert.Equal([0, 0, 1, 0], ico[..4]);
        var sizes = Enumerable.Range(0, BitConverter.ToUInt16(ico, 4)).Select(i => ico[6 + i * 16] == 0 ? 256 : ico[6 + i * 16]).ToArray();
        Assert.Equal([16, 24, 32, 48, 64, 128, 256], sizes);
        Assert.NotEqual(game, ico);
    }

    [Fact]
    public void ThePacksAreFoundOnceEachTheNewestFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "dl-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            string Pack(string folder, string language, string name, DateTime when)
            {
                var path = Path.Combine(root, folder);
                Directory.CreateDirectory(Path.Combine(path, "text", language));
                File.WriteAllText(Path.Combine(path, "pack.json"), $$"""{ "language": "{{language}}", "name": "{{name}}", "author": "A" }""");
                File.WriteAllText(Path.Combine(path, "text", language, "a.ftl"), "exe-ds-2ff2 = x\nvar-him1 = { $sex -> [female] her *[male] him }\n");
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetLastWriteTime(file, when);
                }
                return path;
            }
            var fr = Pack(Path.Combine("packs", "fr"), "fr", "Français", new DateTime(2026, 11, 1));
            // the release's layout (packs/languages) and a loose pack (fr, as in 1.0) are both found
            var ru = Pack(Path.Combine("packs", "languages", "ru"), "ru", "Русский", new DateTime(2026, 11, 18));
            var other = Pack("elsewhere", "el", "Ελληνικά", new DateTime(2026, 10, 1));
            Directory.CreateDirectory(Path.Combine(root, "packs", "not-a-pack"));
            var found = PackCatalog.Find([Path.Combine(root, "packs")], [other, fr]);
            Assert.Equal(["ru", "fr", "el"], found.Select(p => p.Language));
            Assert.Equal(1, found[0].Translated);   // its own variable left out
            Assert.Equal("A", found[0].Author);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
