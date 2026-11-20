namespace Darklands.Localization.Tests;

/// <summary>A pack's screen layouts (layout.json): read and written, and their limits checked.</summary>
public sealed class SlotLayoutsTests
{
    [Fact]
    public void LayoutsAreWrittenAndReadBackWithoutTheOriginalOnes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-layouts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Empty(SlotLayouts.Load(folder, out var none));
            Assert.Null(none);
            SlotLayouts.Save(folder, new Dictionary<string, SlotLayout>
            {
                ["exe:DS:2FF2"] = new(Dy: -8, Width: 200, Lines: 2),
                ["exe:DS:2F71"] = new(),
            });
            var read = SlotLayouts.Load(folder, out var problem);
            Assert.Null(problem);
            Assert.Equal(new SlotLayout(Dy: -8, Width: 200, Lines: 2), Assert.Single(read).Value);
            SlotLayouts.Save(folder, new Dictionary<string, SlotLayout>());
            Assert.False(File.Exists(Path.Combine(folder, SlotLayouts.FileName)));
            File.WriteAllText(Path.Combine(folder, SlotLayouts.FileName), "{ not json");
            Assert.Empty(SlotLayouts.Load(folder, out problem));
            Assert.NotNull(problem);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ALayoutKeepsToItsLimits()
    {
        var boxes = new Dictionary<string, TextBox>
        {
            ["a"] = new("a", 60, 100, false, 1, null) { Screens = ["BarterScreen"], X = 80, Y = 40 },
            ["b"] = new("b", 60, 100, false, 1, null) { Screens = ["BarterScreen"], X = 80, Y = 49 },
            ["c"] = new("c", 60, 100, false, 1, null) { Screens = ["BarterScreen"], X = 80, Y = 195 },
        };
        string[] Problems(string key, SlotLayout layout) =>
            [.. SlotLayouts.Validate(new Dictionary<string, SlotLayout> { [key] = layout }, boxes).Select(p => p.Problem)];
        Assert.Empty(Problems("a", new SlotLayout(Dx: 4, Width: 90)));
        Assert.Contains(Problems("a", new SlotLayout(Dx: 17)), p => p.Contains("at most 16", StringComparison.Ordinal));
        Assert.Contains(Problems("a", new SlotLayout(Lines: 4)), p => p.Contains("1 to 3", StringComparison.Ordinal));
        Assert.Contains(Problems("a", new SlotLayout(Width: 120)), p => p.Contains("of room", StringComparison.Ordinal));
        // two lines reach the next text's row
        Assert.Contains(Problems("a", new SlotLayout(Lines: 2)), p => p == "it overlaps b");
        Assert.Contains(Problems("c", new SlotLayout(Lines: 2)), p => p == "it runs off the screen");
        Assert.True(new SlotLayout().IsOriginal);
    }
}
