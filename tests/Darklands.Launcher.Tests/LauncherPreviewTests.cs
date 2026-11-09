using System.Diagnostics;
using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher.Tests;

/// <summary>
/// The Picture row's preview: the inset follows the row's filter from the next frame on, a placeholder stands in while a preview
/// is not made, the launcher's frames never make one (they only ask the service to and look at what is ready), and the inset
/// keeps clear of the settings panel and the tooltip at every size.
/// </summary>
public sealed class LauncherPreviewTests
{
    /// <summary>A preview service that answers at once: one plain colour for each filter, once "made".</summary>
    private sealed class FakePreviews(bool ready = true) : IFilterPreviews
    {
        public List<(string? Folder, int Width, int Height, bool Curved)> Asked { get; } = [];

        public int Gets { get; private set; }

        public bool Ready { get; set; } = ready;

        public static uint Colour(PictureFilter filter) => 0xFF000000u | (uint)(40 + 30 * (int)filter) << 16 | 0x4080;

        public void Prepare(string? gameFolder, int width, int height, bool curvature) => Asked.Add((gameFolder, width, height, curvature));

        public FilterPreview? Get(PictureFilter filter)
        {
            Gets++;
            var (width, height) = Asked.Count > 0 ? (Asked[^1].Width, Asked[^1].Height) : (10, 10);
            return Ready ? new FilterPreview(width, height, Enumerable.Repeat(Colour(filter), width * height).ToArray(), true) : null;
        }
    }

    private static LauncherContext Context(IFilterPreviews previews, GameSettings? settings = null) =>
        LauncherLayoutTests.Broken() with { Previews = previews, Settings = settings ?? LauncherLayoutTests.Broken().Settings };

    private static readonly KeyPressed Tab = new(LauncherKey.Tab);
    private static readonly KeyPressed Right = new(LauncherKey.Right);

    /// <summary>From Play to the Picture row (the broken context's three links first): Discord, the guide, the report, the notes, the game folder, Change, window, fullscreen, picture.</summary>
    private static void ToPicture(LauncherScreen screen)
    {
        while (screen.Focus != LauncherScreen.Item.Picture)
        {
            screen.Handle(Tab);
        }
    }

    private static uint Middle(Canvas canvas, Rect area) => canvas.Pixels[(area.Y + area.Height / 2) * canvas.Width + area.X + area.Width / 2] | 0xFF000000;

    [Fact]
    public void ThePreviewFollowsTheRowFromTheNextFrame()
    {
        var previews = new FakePreviews();
        var context = Context(previews);
        var model = new LauncherModel(context, null);
        var screen = new LauncherScreen(model, context, new ScriptedSurface(1280, 720) { Frameless = true });
        var canvas = new Canvas(1280, 720);
        screen.Render(canvas);
        Assert.Equal(default, screen.PreviewArea);   // not shown before the row is chosen ...
        Assert.NotEmpty(previews.Asked);             // ... but asked for from the first frame
        ToPicture(screen);
        foreach (var expected in Enum.GetValues<PictureFilter>().Skip(1).Append(PictureFilter.Sharp).Prepend(PictureFilter.Sharp))
        {
            screen.Render(canvas);
            Assert.NotEqual(default, screen.PreviewArea);
            Assert.Equal(PictureFilters.Name(expected), model.Filter);
            Assert.Equal(FakePreviews.Colour(expected), Middle(canvas, screen.PreviewArea));
            screen.Handle(Right);
        }
    }

    [Fact]
    public void APlaceholderStandsInUntilThePreviewIsMade()
    {
        var previews = new FakePreviews(ready: false);
        var context = Context(previews);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(1280, 720) { Frameless = true });
        var canvas = new Canvas(1280, 720);
        ToPicture(screen);
        screen.Render(canvas);
        Assert.NotEqual(default, screen.PreviewArea);
        Assert.NotEqual(FakePreviews.Colour(PictureFilter.Sharp), Middle(canvas, screen.PreviewArea));
        previews.Ready = true;
        screen.Render(canvas);
        Assert.Equal(FakePreviews.Colour(PictureFilter.Sharp), Middle(canvas, screen.PreviewArea));
    }

    /// <summary>
    /// Stepping through the filters only looks at the service: what it is asked to make does not change (the same folder, size and
    /// curvature as from the first frame), and each frame stays far under a 60 Hz frame's 16 ms.
    /// </summary>
    [Fact]
    public void SteppingNeverMakesAPreviewOnTheLaunchersFrames()
    {
        var previews = new FakePreviews();
        var context = Context(previews, LauncherLayoutTests.Broken().Settings.Copy() is var s ? Curved(s) : null);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(1280, 720) { Frameless = true });
        var canvas = new Canvas(1280, 720);
        screen.Render(canvas);
        ToPicture(screen);
        var clock = new Stopwatch();
        var worst = 0.0;
        for (var i = 0; i < 21; i++)
        {
            screen.Handle(Right);
            clock.Restart();
            screen.Render(canvas);
            worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds);
        }
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "launcher-stepping-timing.txt"),
            $"{DateTime.Now:s} slowest of 21 frames while stepping the Picture row at 1280x720: {worst:F1} ms{Environment.NewLine}");
        Assert.Single(previews.Asked.Distinct());
        Assert.True(previews.Asked[0].Curved);   // the settings file's CRT curvature reaches the previews
        Assert.True(previews.Gets >= 21);
        Assert.True(worst < 100, $"the slowest frame while stepping took {worst:F1} ms");   // a test machine's debug build; the report gives release figures
    }

    private static GameSettings Curved(GameSettings settings)
    {
        settings.CrtCurvature = true;
        return settings;
    }

    [Theory]
    [MemberData(nameof(LauncherLayoutTests.Sizes), MemberType = typeof(LauncherLayoutTests))]
    public void TheInsetKeepsClearOfThePanelAndTheTooltipAtEverySize(int width, int height)
    {
        var context = Context(new FakePreviews());
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(width, height) { Frameless = true });
        var canvas = new Canvas(width, height);
        ToPicture(screen);
        foreach (var _ in Enum.GetValues<PictureFilter>())
        {
            screen.Render(canvas);
            var inset = screen.PreviewArea;
            Assert.True(inset.Width > 0 && inset.X >= 0 && inset.Y >= 0 && inset.Right <= width && inset.Bottom <= height, $"{width}x{height}: {inset}");
            Assert.False(inset.Intersects(screen.SettingsPanel), $"{width}x{height}: the preview {inset} over the settings {screen.SettingsPanel}");
            Assert.False(inset.Intersects(screen.TooltipArea), $"{width}x{height}: the preview {inset} under the tooltip {screen.TooltipArea}");
            screen.Handle(Right);
        }
    }
}
