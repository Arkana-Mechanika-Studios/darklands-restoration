namespace Darklands.Localization.Tests;

/// <summary>A pack's screen layouts (interface/layout.json, by screen and slot): read and written, and checked against a profile.</summary>
public sealed class SlotLayoutsTests
{
    private static Dictionary<string, Dictionary<string, SlotLayout>> On(string screen, string slot, SlotLayout layout) =>
        new() { [screen] = new Dictionary<string, SlotLayout> { [slot] = layout } };

    [Fact]
    public void LayoutsAreWrittenAndReadBackWithoutTheOriginalOnes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-layouts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Empty(SlotLayouts.Load(folder, out var none));
            Assert.Null(none);
            var layouts = On("BarterScreen", "exe:DS:2FF2", new SlotLayout(Y: 35, Width: 200, Lines: 2));
            layouts["LootScreen"] = new Dictionary<string, SlotLayout> { ["exe:DS:2F71"] = new() };
            SlotLayouts.Save(folder, layouts);
            Assert.True(File.Exists(Path.Combine(folder, "interface", "layout.json")));
            var read = SlotLayouts.Load(folder, out var problem);
            Assert.Null(problem);
            var (screen, slots) = Assert.Single(read);
            Assert.Equal("BarterScreen", screen);
            Assert.Equal(new SlotLayout(Y: 35, Width: 200, Lines: 2), Assert.Single(slots).Value);
            SlotLayouts.Save(folder, new Dictionary<string, Dictionary<string, SlotLayout>>());
            Assert.False(File.Exists(Path.Combine(folder, "interface", "layout.json")));
            File.WriteAllText(Path.Combine(folder, "interface", "layout.json"), "{ not json");
            Assert.Empty(SlotLayouts.Load(folder, out problem));
            Assert.NotNull(problem);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ALanguagePackKeepsToItsProfile()
    {
        var boxes = new Dictionary<string, TextBox>
        {
            ["a"] = new("a", 60, 100, false, 1, null) { Screens = ["BarterScreen"], X = 80, Y = 40 },
            ["b"] = new("b", 60, 100, false, 1, null) { Screens = ["BarterScreen"], X = 80, Y = 49 },
            ["c"] = new("c", 60, 100, false, 1, null) { Screens = ["BarterScreen"], X = 80, Y = 195 },
            ["d"] = new("d", 60, 100, false, 1, null) { Screens = ["BarterScreen"] },
        };
        string[] Problems(string slot, SlotLayout layout) =>
            [.. SlotLayouts.Validate(On("BarterScreen", slot, layout), boxes, LayoutProfile.LanguagePack).Select(p => p.Problem)];
        Assert.Empty(Problems("a", new SlotLayout(X: 84, Width: 90)));
        Assert.Contains(Problems("a", new SlotLayout(X: 97)), p => p.Contains("at most 16", StringComparison.Ordinal));
        Assert.Contains(Problems("a", new SlotLayout(Lines: 4)), p => p.Contains("1 to 3", StringComparison.Ordinal));
        Assert.Contains(Problems("a", new SlotLayout(Width: 120)), p => p.Contains("of room", StringComparison.Ordinal));
        // two lines reach the next text's row
        Assert.Contains(Problems("a", new SlotLayout(Lines: 2)), p => p == "it may cover b if drawn at the same time");
        Assert.Contains(Problems("c", new SlotLayout(Lines: 2)), p => p == "it runs off the screen");
        // a text whose place no session recorded cannot be moved
        Assert.Contains(Problems("d", new SlotLayout(Y: 10)), p => p.Contains("cannot be moved", StringComparison.Ordinal));
        // a looser profile (an interface mod's, later) takes the same file with other limits
        var loose = new LayoutProfile("interface mod", null, 6, false);
        Assert.Empty(SlotLayouts.Validate(On("BarterScreen", "a", new SlotLayout(X: 20, Width: 250)), boxes, loose));
        Assert.True(new SlotLayout().IsOriginal);
    }

    /// <summary>The 1.2 slot kinds: an area (a hit rectangle) and a list (rows of equal height), read and written with their fields.</summary>
    [Fact]
    public void AreasAndListsAreWrittenAndReadBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-layouts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var area = new SlotLayout(X: 40, Y: 30, Width: 100, Kind: SlotKinds.Area, Height: 12);
            var list = new SlotLayout(X: 40, Y: 50, Width: 200, Kind: SlotKinds.List, Rows: 8, RowHeight: 10);
            var layouts = new Dictionary<string, Dictionary<string, SlotLayout>> { ["SettingsScreen"] = new() { ["close"] = area, ["rows"] = list } };
            SlotLayouts.Save(folder, layouts);
            var text = File.ReadAllText(Path.Combine(folder, "interface", "layout.json"));
            Assert.Contains("\"kind\": \"area\"", text, StringComparison.Ordinal);
            Assert.Contains("\"rowHeight\": 10", text, StringComparison.Ordinal);
            var read = SlotLayouts.Load(folder, out var problem);
            Assert.Null(problem);
            Assert.Equal(area, read["SettingsScreen"]["close"]);
            Assert.Equal(list, read["SettingsScreen"]["rows"]);
            // a declaration is never the original's, so saving keeps it
            Assert.False(area.IsOriginal);
            Assert.False(list.IsOriginal);
            Assert.False(area.IsText);
            Assert.True(new SlotLayout(X: 3).IsText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnAreaAndAListAreHitWhereTheyAreDeclared()
    {
        var area = new SlotLayout(X: 40, Y: 30, Width: 100, Kind: SlotKinds.Area, Height: 12);
        Assert.Equal((40, 30, 100, 12), area.Rectangle);
        Assert.True(area.Contains(40, 30));
        Assert.True(area.Contains(139, 41));
        Assert.False(area.Contains(140, 41));
        Assert.False(area.Contains(39, 30));
        Assert.Equal(-1, area.RowAt(50, 35));
        var list = new SlotLayout(X: 40, Y: 50, Width: 200, Kind: SlotKinds.List, Rows: 8, RowHeight: 10);
        Assert.Equal((40, 50, 200, 80), list.Rectangle);
        Assert.Equal(0, list.RowAt(40, 50));
        Assert.Equal(2, list.RowAt(100, 79));
        Assert.Equal(7, list.RowAt(239, 129));
        Assert.Equal(-1, list.RowAt(100, 130));
        Assert.Equal(-1, list.RowAt(240, 60));
        // a text has no rectangle of its own here (its box is the recorded one)
        Assert.Null(new SlotLayout(X: 1, Y: 2, Width: 3).Rectangle);
        Assert.False(new SlotLayout(X: 1, Y: 2, Width: 3).Contains(1, 2));
    }

    [Fact]
    public void AreasAndListsAreCheckedAndALanguagePackDeclaresNone()
    {
        var boxes = new Dictionary<string, TextBox>();
        List<string> Problems(SlotLayout layout, LayoutProfile profile) =>
            [.. SlotLayouts.Validate(On("SettingsScreen", "slot", layout), boxes, profile).Select(p => p.Problem)];
        var mod = LayoutProfile.InterfaceMod;
        Assert.Empty(Problems(new SlotLayout(X: 40, Y: 30, Width: 100, Kind: SlotKinds.Area, Height: 12), mod));
        Assert.Empty(Problems(new SlotLayout(X: 40, Y: 50, Width: 200, Kind: SlotKinds.List, Rows: 8, RowHeight: 10), mod));
        // a language pack lays out texts only
        Assert.Contains(Problems(new SlotLayout(X: 40, Y: 30, Width: 100, Kind: SlotKinds.Area, Height: 12), LayoutProfile.LanguagePack),
            p => p.Contains("not a language pack", StringComparison.Ordinal));
        // the fields each kind needs, and nothing of a text's
        Assert.Contains(Problems(new SlotLayout(X: 40, Y: 30, Kind: SlotKinds.Area, Height: 12), mod), p => p.Contains("x, y and width", StringComparison.Ordinal));
        Assert.Contains(Problems(new SlotLayout(X: 40, Y: 30, Width: 10, Kind: SlotKinds.Area), mod), p => p.Contains("needs a height", StringComparison.Ordinal));
        Assert.Contains(Problems(new SlotLayout(X: 40, Y: 30, Width: 10, Kind: SlotKinds.List, Rows: 3), mod), p => p.Contains("rows and a rowHeight", StringComparison.Ordinal));
        Assert.Contains(Problems(new SlotLayout(X: 40, Y: 30, Width: 10, Lines: 2, Kind: SlotKinds.Area, Height: 5), mod), p => p.Contains("a text's", StringComparison.Ordinal));
        Assert.Contains(Problems(new SlotLayout(X: 300, Y: 30, Width: 40, Kind: SlotKinds.Area, Height: 5), mod), p => p == "it runs off the screen");
        Assert.Contains(Problems(new SlotLayout(X: 0, Y: 150, Width: 40, Kind: SlotKinds.List, Rows: 6, RowHeight: 10), mod), p => p == "it runs off the screen");
        Assert.Contains(Problems(new SlotLayout(X: 0, Y: 0, Width: 4, Kind: "button", Height: 4), mod), p => p.Contains("text, area or list", StringComparison.Ordinal));
        // an area named like a text of the screen
        var withText = new Dictionary<string, TextBox> { ["exe:DS:1"] = new("exe:DS:1", 10, 50, false, 1, null) { Screens = ["SettingsScreen"], X = 5, Y = 5 } };
        Assert.Contains(SlotLayouts.Validate(On("SettingsScreen", "exe:DS:1", new SlotLayout(X: 0, Y: 0, Width: 4, Kind: SlotKinds.Area, Height: 4)), withText, mod),
            p => p.Problem.Contains("named like a text", StringComparison.Ordinal));
        // a text with an area's fields
        Assert.Contains(Problems(new SlotLayout(Width: 40, Height: 9), mod), p => p.Contains("a text has lines", StringComparison.Ordinal));
    }
}
