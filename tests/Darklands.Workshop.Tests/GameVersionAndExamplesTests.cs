using System.Xml.Linq;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The 1.1 release: the game's version (Version, Directory.Build.props), the oldest game the Workshop plays with (an older one said
/// so in words, WorkshopVersion.OlderGameWords), and the makers' examples, which come with the Workshop (Workshop/examples) and no
/// longer with the player's download.
/// </summary>
public sealed class GameVersionAndExamplesTests
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
    public void TheReleaseIs113AndTheWorkshopNeedsThe110Game()
    {
        var props = XDocument.Load(Path.Combine(Root(), "Directory.Build.props"));
        Assert.Equal("1.1.3", props.Descendants("Version").Single().Value);
        Assert.Equal(props.Descendants("WorkshopNeedsGame").Single().Value, WorkshopVersion.NeedsGame);
        Assert.Equal("1.1.0", WorkshopVersion.NeedsGame);
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.0.0-rc6", true)]
    [InlineData("0.9.6", true)]
    [InlineData("1.0.0+abc1234", true)]
    [InlineData("1.1.0", false)]
    [InlineData("1.1.0-rc1", false)]
    [InlineData("1.1.2", false)]
    [InlineData("1.1.3", false)]
    [InlineData("1.2", false)]
    [InlineData("2.0.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("unknown", false)]
    public void AGameOlderThanTheWorkshopNeedsIsToldApart(string? game, bool older) =>
        Assert.Equal(older, WorkshopVersion.IsOlderGame(game));

    [Fact]
    public void AnOlderGameIsSaidSoInWords()
    {
        var words = WorkshopVersion.OlderGameWords("1.0.0+abc1234");
        Assert.Contains("version 1.0.0,", words, StringComparison.Ordinal);
        Assert.Contains("needs version 1.1.0 or later", words, StringComparison.Ordinal);
        Assert.Contains("releases page", words, StringComparison.Ordinal);
        Assert.Contains("saved games and settings stay", words, StringComparison.Ordinal);
        Assert.DoesNotContain("abc1234", words, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExamplesComeWithTheWorkshopNotWithThePlayersDownload()
    {
        // beside the Workshop (its build, so its release: Workshop/examples)
        var examples = Path.Combine(AppContext.BaseDirectory, "examples");
        // a read-only shelf, its folders named so that none looks like a game folder
        Assert.True(File.Exists(Path.Combine(examples, "example-audio-pack", "audio.json")));
        Assert.True(File.Exists(Path.Combine(examples, "example-audio-pack", "music", "travel-2.ogg")));
        Assert.True(File.Exists(Path.Combine(examples, "example-mod-wayside-shrine", "mod.json")));
        Assert.True(Directory.Exists(Path.Combine(examples, "sample-languages", "sample-fr")));
        // the game's project ships the HD fonts and the languages README in packs, and no example (a player's 1.0 copies stay: the
        //   release deletes nothing)
        var game = File.ReadAllText(Path.Combine(Root(), "src", "Darklands.Host.Sdl", "Darklands.Host.Sdl.csproj"));
        Assert.Contains(@"Link=""packs\fonts\hd-fonts\", game, StringComparison.Ordinal);
        Assert.DoesNotContain("audio-pack-example\\**", game, StringComparison.Ordinal);
        Assert.DoesNotContain(@"docs\mods\**", game, StringComparison.Ordinal);
        // the player's own folders, empty but for their READMEs: packs\audio and mods (packs\languages too)
        Assert.Contains(@"Link=""packs\audio\README.txt""", game, StringComparison.Ordinal);
        Assert.Contains(@"Link=""mods\README.txt""", game, StringComparison.Ordinal);
        Assert.Contains(@"Link=""packs\languages\README.txt""", game, StringComparison.Ordinal);
        Assert.Single(game.Split('\n'), l => l.Contains(@"Link=""mods\", StringComparison.Ordinal));
    }
}
