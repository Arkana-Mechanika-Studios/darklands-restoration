using Darklands.Platform;

namespace Darklands.Platform.Tests;

public class LocalPathsTests
{
    [Fact]
    public void AnAbsolutePathIsKept()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "game");
        Assert.Equal(absolute, LocalPaths.Resolve(absolute, Path.Combine(Path.GetTempPath(), "root", "code"), null));
    }

    [Fact]
    public void ARelativePathIsUnderTheFolderHoldingTheRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");
        Assert.Equal(Path.Combine(root, "game", "DARKLAND"), LocalPaths.Resolve("game/DARKLAND", Path.Combine(root, "code"), null));
    }

    [Fact]
    public void ARelativePathIsUnderDarklandsRootWhenSet()
    {
        var root = Path.Combine(Path.GetTempPath(), "elsewhere");
        Assert.Equal(Path.Combine(root, "reference", "checkout"),
            LocalPaths.Resolve("reference/checkout", Path.Combine(Path.GetTempPath(), "root", "code"), root));
    }
}
