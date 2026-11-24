namespace Darklands.Host.Sdl.Tests;

/// <summary>The launcher's packs as this host reads them: the packs folders, and a folder read as the game reads a pack.</summary>
public sealed class LauncherPackTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dkhostpacks").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheReleasesPacksShipBesideTheProgramInTheirKindsFolders()
    {
        // the one pack shipped: the HD fonts (the example sound pack and the example mod are the Workshop's since 1.1,
        //   Workshop/examples: GameVersionAndExamplesTests, which also checks the game's project ships neither; an older build's
        //   copies may still lie in this output, so their absence is not asserted here)
        var hd = Path.Combine(AppContext.BaseDirectory, "packs", "fonts", "hd-fonts");
        var found = LauncherHost.PackFolders(null);
        Assert.Contains(hd, found);
        Assert.True(LauncherHost.DescribePack(hd).Fonts > 0);
        // the player's own folders: languages, audio and mods, empty but for their READMEs
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "packs", "languages", "README.txt")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "packs", "audio", "README.txt")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "mods", "README.txt")));
        // and the Roland ROMs' folder, with its README (the files that go there, none included): 1.0's zip had the folder
        var roms = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MT32_ROMS", "README.txt"));
        Assert.Contains("MT-32", roms, StringComparison.Ordinal);
        Assert.Contains("CM-32L", roms, StringComparison.Ordinal);
        Assert.Contains("No ROM files are included", roms, StringComparison.Ordinal);
        // the players' guides beside the program (the makers' guides are the Workshop's: Workshop/docs)
        foreach (var guide in new[] { "PACKS.md", "MODS.md", "SETTINGS.md", "FIXED.md", "REPORTING.md" })
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "docs", guide)), guide);
        }
    }

    [Fact]
    public void ABrokenManifestIsReportedAndPacksNextToTheGameAreFound()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "Darklands", "DARKLAND")).FullName;
        var pack = Directory.CreateDirectory(Path.Combine(_root, "Darklands", "packs", "mine")).FullName;
        Directory.CreateDirectory(Path.Combine(game, "packs", "inside"));
        File.WriteAllBytes(Path.Combine(pack, "inn-1.wav"), [0]);
        File.WriteAllText(Path.Combine(pack, "audio.json"), "{ \"cues\": ");
        var found = LauncherHost.PackFolders(game);
        Assert.Contains(pack, found);
        Assert.Contains(Path.Combine(game, "packs", "inside"), found);
        var status = LauncherHost.DescribePack(pack);
        Assert.Equal(1, status.Sounds);
        Assert.Contains("audio.json", status.Problem, StringComparison.Ordinal);
    }
}
