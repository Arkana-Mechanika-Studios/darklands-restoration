using Darklands.Audio.Driver;
using Darklands.Audio.Mt32;
using Darklands.Content;

namespace Darklands.Audio.Tests;

/// <summary>
/// The Roland driver without MT-32 ROMs: the OPL3 FM drivers play instead, with a warning. Needs the original game files
/// (DARKLANDS_DIR, or the "game" key of local/paths.json); without them these tests pass without checking anything.
/// </summary>
public sealed class SoundFallbackTests
{
    private static GameFiles? Files()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) && File.Exists(Path.Combine(folder, "PSOUND.DC")) ? new GameFiles(folder) : null;
    }

    [Theory]
    [InlineData("mt32")]
    [InlineData("cm32l")]
    public void WithoutRomsTheOpl3DriversPlayWithAWarning(string choice)
    {
        if (Files() is not { } files)
        {
            return;
        }
        var empty = Directory.CreateTempSubdirectory("darklands-no-roms").FullName;
        try
        {
            var lines = new List<string>();
            using var selected = SoundSelection.Select(files, choice, empty, 44100, lines.Add);
            Assert.NotNull(selected);
            Assert.Null(selected.Midi);
            Assert.Equal(DriverKind.Fm, selected.Setup.Kind);
            Assert.Equal(SoundCard.SoundBlasterPro2, selected.Setup.Card.Card);
            Assert.Contains(lines, l => SoundSelection.IsWarning(l) && l.Contains("OPL3", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}
