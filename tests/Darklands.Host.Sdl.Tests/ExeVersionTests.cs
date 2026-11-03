using System.Text;
using Darklands.Content;
using Darklands.Content.Formats.Exe;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The check made before the game starts: the English DARKLAND.EXE 483.07 passes; another build (as the German release, whose
/// tables hold other words) is refused with a message for the launcher instead of a game that ends at its first screen.
/// The real-EXE cases need the original game files (DARKLANDS_DIR, or the "game" key of local/paths.json) and are skipped without them.
/// </summary>
public sealed class ExeVersionTests
{
    private static byte[]? Exe()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? new GameFiles(folder).Read("DARKLAND.EXE") : null;
    }

    [Fact]
    public void TheEnglishExePasses()
    {
        if (Exe() is not { } exe)
        {
            return;
        }
        Assert.Null(ExeVersion.Problem(exe));
    }

    [Fact]
    public void AnExeWithOtherWordsInItsTablesIsRefused()
    {
        if (Exe() is not { } exe)
        {
            return;
        }
        // the first background's name, as a translated build would have it
        var english = Encoding.ASCII.GetBytes("Nobility\0");
        var at = exe.AsSpan().IndexOf(english);
        Assert.True(at > 0);
        Encoding.ASCII.GetBytes("Adel\0\0\0\0\0").CopyTo(exe, at);
        Assert.Equal(ExeVersion.NotSupported, ExeVersion.Problem(exe));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(100_000)]
    public void AFileThatIsNoDarklandsExeIsRefused(int length) =>
        Assert.Equal(ExeVersion.NotSupported, ExeVersion.Problem(new byte[length]));
}
