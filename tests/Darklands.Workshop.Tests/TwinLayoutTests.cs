using Darklands.Localization;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>An embossed heading is two texts a pixel apart (its shadow and its face): a layout of one is given to the other.</summary>
public sealed class TwinLayoutTests
{
    [Fact]
    public void AnEmbossedHeadingsTwinTakesTheSameLayoutAPixelApart()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-twin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Test", null, game);
            // the party information screen's heading: its shadow (exe:DS:3671, at 134, 6) and its face (exe:DS:36C8, at 133, 5)
            var shadow = project.Entries.First(e => e.Key == "exe:DS:3671");
            Assert.Equal("exe:DS:36C8", project.TwinOf(shadow)?.Key);
            project.SetLayout(shadow, new SlotLayout(X: 118, Width: 180));
            var face = project.Entries.First(e => e.Key == "exe:DS:36C8");
            Assert.Equal(new SlotLayout(X: 117, Width: 180), project.LayoutOf(face));
            project.SetLayout(face, null);
            Assert.Null(project.LayoutOf(shadow));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
