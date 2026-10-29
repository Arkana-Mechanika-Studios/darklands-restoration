namespace Darklands.Host.Sdl.Tests;

/// <summary>The launcher's packs as this host reads them: the packs folders, and a folder read as the game reads a pack.</summary>
public sealed class LauncherPackTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dkhostpacks").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheExamplePackShipsBesideTheProgramAndReadsCleanly()
    {
        var example = Path.Combine(AppContext.BaseDirectory, "packs", "audio-pack-example");
        Assert.Contains(example, LauncherHost.PackFolders(null));
        Assert.Equal(new Launcher.PackStatus(0, 3, null), LauncherHost.DescribePack(example));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "docs", "PACKS.md")));
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
