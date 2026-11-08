using Darklands.Platform;

namespace Darklands.Launcher.Tests;

/// <summary>The picture filter on the launcher's model (display.filter): shown as the file has it, changed in steps, saved only when changed.</summary>
public sealed class LauncherFilterTests
{
    private const string Game = @"C:\GOG Games\Darklands";

    private static LauncherModel Model(GameSettings settings) => new(new LauncherContext(
        settings,
        new Dictionary<string, string>(),
        "1.0",
        LauncherReason.Asked,
        null,
        folder => folder,
        () => [],
        folder => new RomStatus(folder ?? "MT32_ROMS", true, false),
        null,
        "",
        _ => { }), null);

    [Fact]
    public void TheFilterIsSharpUnlessTheFileSaysOtherwise()
    {
        Assert.Equal("sharp", Model(new GameSettings { GameFolder = Game }).Filter);
        Assert.Equal("Sharp", Model(new GameSettings { GameFolder = Game }).FilterLabel);
        Assert.Equal("crt", Model(new GameSettings { GameFolder = Game, Filter = "crt" }).Filter);
        Assert.Equal("sharp", Model(new GameSettings { GameFolder = Game, Filter = "nonsense" }).Filter);
    }

    [Fact]
    public void StepsGoRoundTheFiltersAndAChangeIsSaved()
    {
        var model = Model(new GameSettings { GameFolder = Game });
        Assert.False(model.Changes().ContainsKey("display.filter"));
        model.StepFilter(1);
        Assert.Equal("smooth", model.Filter);
        Assert.Equal("Smooth pixels", model.FilterLabel);
        Assert.Equal("smooth", (string)model.Changes()["display.filter"]!);
        Assert.Equal("smooth", model.Settings().Filter);
        model.StepFilter(1);
        model.StepFilter(1);
        Assert.Equal("sharp", model.Filter);
        Assert.False(model.Changes().ContainsKey("display.filter"));
        model.StepFilter(-1);
        Assert.Equal("crt", model.Filter);
        Assert.Equal(LauncherModel.Filters.Select(f => f.Value), PictureFilters.Names);
    }
}
