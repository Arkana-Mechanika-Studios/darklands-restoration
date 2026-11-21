namespace Darklands.Platform.Tests;

/// <summary>The release's packs folder (a subfolder per kind, loose packs still found) and a tool's way to the game's program.</summary>
public sealed class PackLayoutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dkpacklayout").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([_root, .. parts])).FullName;

    [Fact]
    public void ThePacksOfEachKindAndTheLoosePacksAreFound()
    {
        var hd = Folder("packs", "fonts", "hd-fonts");
        var de = Folder("packs", "languages", "de");
        var fr = Folder("packs", "languages", "fr");
        var tones = Folder("packs", "audio", "audio-pack-example");
        var loose = Folder("packs", "my-pictures");
        Assert.Equal([hd, de, fr, tones, loose], PackLayout.PacksIn(Path.Combine(_root, "packs")).ToArray());
        // a pack that happens to be named after a kind is a pack, not a kind's folder
        var audio = Folder("other", "audio");
        File.WriteAllText(Path.Combine(audio, "audio.json"), "{}");
        Assert.Equal([audio], PackLayout.PacksIn(Path.Combine(_root, "other")).ToArray());
        Assert.Empty(PackLayout.PacksIn(Path.Combine(_root, "absent")));
        Assert.Equal(Path.Combine(_root, "packs", "languages"), PackLayout.KindFolder(_root, PackLayout.Languages));
    }

    [Fact]
    public void AToolFindsTheGamesProgramBesideItOrOneFolderUp()
    {
        var name = OperatingSystem.IsWindows() ? "DarklandsRP.exe" : "DarklandsRP";
        var game = Folder("DarklandsRP");
        var workshop = Folder("DarklandsRP", "Workshop");
        Assert.Equal(workshop, PackLayout.GameProgramFolder(workshop));   // no game anywhere: its own folder
        File.WriteAllBytes(Path.Combine(game, name), []);
        Assert.Equal(game, PackLayout.GameProgramFolder(workshop));       // the release: one folder up
        Assert.Equal(game, PackLayout.GameProgramFolder(workshop + Path.DirectorySeparatorChar));
        File.WriteAllBytes(Path.Combine(workshop, name), []);
        Assert.Equal(workshop, PackLayout.GameProgramFolder(workshop));   // a developer's build: beside it wins
    }
}
