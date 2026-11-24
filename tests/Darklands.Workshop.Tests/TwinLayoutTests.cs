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

    /// <summary>
    /// The 1.2 slot kinds in a pack's layouts: the Workshop reads an area or a list as a view's, not as a text's layout (even under a
    /// text's key), and keeps it as it is when a text's layout is edited and the pack saved.
    /// </summary>
    [Fact]
    public void AreasAndListsAreKeptAndAreNotATextsLayout()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-areas-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Test", null, game);
            var heading = project.Entries.First(e => e.Key == "exe:DS:3671");
            var area = new SlotLayout(X: 10, Y: 20, Width: 30, Kind: SlotKinds.Area, Height: 9);
            var list = new SlotLayout(X: 10, Y: 40, Width: 100, Kind: SlotKinds.List, Rows: 4, RowHeight: 9);
            project.Layouts["PartyInfoScreen"] = new Dictionary<string, SlotLayout>(StringComparer.Ordinal) { ["exe:DS:3671"] = area, ["members"] = list };
            Assert.Null(project.LayoutOf(heading));
            // a text of the same screen laid out, the pack saved and opened again: the area and the list are as they were
            var face = project.Entries.First(e => e.Key == "exe:DS:36C8");
            project.SetLayout(face, new SlotLayout(X: 120, Width: 150));
            project.Save();
            var again = TextProject.Open(folder, game);
            Assert.Equal(list, again.Layouts["PartyInfoScreen"]["members"]);
            Assert.Equal(new SlotLayout(X: 120, Width: 150), again.LayoutOf(again.Entries.First(e => e.Key == "exe:DS:36C8")));
            // the heading's own key: SetLayout of the face gave its twin (the heading) a text layout over the area
            Assert.True(again.Layouts["PartyInfoScreen"]["exe:DS:3671"].IsText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
