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
        Assert.Equal("even", model.Filter);
        Assert.Equal("Even", model.FilterLabel);
        Assert.Equal("even", (string)model.Changes()["display.filter"]!);
        Assert.Equal("even", model.Settings().Filter);
        for (var i = 0; i < 6; i++)
        {
            model.StepFilter(1);
        }
        Assert.Equal("sharp", model.Filter);
        Assert.False(model.Changes().ContainsKey("display.filter"));
        model.StepFilter(-1);
        Assert.Equal("crtplus", model.Filter);
        Assert.Equal("CRT+", model.FilterLabel);
        // a file of before 1.0's last round says "smooth": shown as Even, and not written back unless changed
        var old = Model(new GameSettings { GameFolder = Game, Filter = "smooth" });
        Assert.Equal("even", old.Filter);
        Assert.Equal(LauncherModel.Filters.Select(f => f.Value), PictureFilters.Names);
    }

    /// <summary>Play runs with the whole file: the keys the launcher has no row for (cleanDrawing, mods ...) are kept, its rows over them.</summary>
    [Fact]
    public void ThePlayedSettingsAreTheWholeFileWithTheLaunchersRows()
    {
        var file = new GameSettings
        {
            GameFolder = Game,
            CleanDrawing = true,
            Mods = ["one", "two"],
            CrtCurvature = true,
            ConsoleEnabled = false,
            ConsoleFont = "C:/Fonts/x.ttf",
            ScreenshotsFolder = "D:/shots",
            Filter = "smooth",
        };
        var model = Model(file);
        model.Fullscreen = true;
        model.StepFilter(1);
        var played = model.Settings();
        Assert.True(played.CleanDrawing);
        Assert.Equal(["one", "two"], played.Mods);
        Assert.True(played.CrtCurvature);
        Assert.False(played.ConsoleEnabled);
        Assert.Equal("C:/Fonts/x.ttf", played.ConsoleFont);
        Assert.Equal("D:/shots", played.ScreenshotsFolder);
        Assert.True(played.Fullscreen);
        Assert.Equal("soft", played.Filter);   // "smooth" (Even) stepped on once
        // a copy: the file's own settings are left as they were
        played.Mods!.Add("three");
        Assert.Equal(2, file.Mods.Count);
        Assert.Null(file.Fullscreen);
        Assert.Equal("smooth", file.Filter);
    }

    private static LauncherContext Context(IReadOnlyDictionary<string, string>? fixedKeys = null) => new(
        new GameSettings { GameFolder = Game },
        fixedKeys ?? new Dictionary<string, string>(),
        "1.0",
        LauncherReason.Asked,
        null,
        folder => folder,
        () => [],
        folder => new RomStatus(folder ?? "MT32_ROMS", true, false),
        null,
        "",
        _ => { });

    /// <summary>The Picture row (General, beside Fullscreen): the keyboard and a click on its › step the filter; --filter fixes it.</summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(960, 540)]
    public void ThePictureRowStepsTheFilter(int width, int height)
    {
        var context = Context();
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(width, height) { Frameless = true });
        screen.Render(new Darklands.Launcher.Drawing.Canvas(width, height));
        Assert.Contains(screen.Blocks, b => b.What == "label Picture");
        Assert.Contains(screen.Blocks, b => b.What == "value Picture");
        var fullscreen = screen.Area(LauncherScreen.Item.Fullscreen);
        var picture = screen.Area(LauncherScreen.Item.Picture);
        Assert.Equal(fullscreen.Y, picture.Y);   // on one row
        Assert.False(fullscreen.Intersects(picture));
        var next = screen.Area(LauncherScreen.Item.Picture, LauncherScreen.Part.Next);
        screen.Handle(new PointerPressed(next.X + next.Width / 2, next.Y + next.Height / 2));
        screen.Handle(new PointerReleased(next.X + next.Width / 2, next.Y + next.Height / 2));
        screen.Render(new Darklands.Launcher.Drawing.Canvas(width, height));
        Assert.Equal(LauncherScreen.Item.Picture, screen.Focus);
        screen.Handle(new KeyPressed(LauncherKey.Right));
        Assert.Equal(LauncherScreen.Item.Picture, screen.Focus);

        // from Play: Discord, the game folder, Change, window, fullscreen, then the picture; Right twice: Soft
        var keyed = new Launcher().Run(Context(), new ScriptedSurface(width, height,
            [new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab),
             new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Right), new KeyPressed(LauncherKey.Right),
             new KeyPressed(LauncherKey.Escape)]));
        Assert.Equal("soft", (string)keyed.Changes["display.filter"]!);
        Assert.Equal("soft", keyed.Settings.Filter);

        // set by --filter: shown, not changed
        var fixedRun = new Launcher().Run(Context(new Dictionary<string, string> { ["display.filter"] = "--filter" }), new ScriptedSurface(width, height,
            [new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab),
             new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Right), new KeyPressed(LauncherKey.Escape)]));
        Assert.False(fixedRun.Changes.ContainsKey("display.filter"));
    }
}
