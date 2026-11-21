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
}
