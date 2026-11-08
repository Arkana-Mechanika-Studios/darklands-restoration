using System.Diagnostics;
using Darklands.Platform;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The launcher's filter previews made by the host: from the player's game folder (a card's picture, VILLAGE4.PIC) or, without one,
/// a pattern; asked for and looked at without waiting; every filter made on a worker thread, each one its own picture.
/// </summary>
public sealed class FilterPreviewsTests
{
    private static string? GameFolder() =>
        Environment.GetEnvironmentVariable("DARKLANDS_DIR") is { Length: > 0 } dir && Darklands.Content.GameFiles.IsGameFolder(dir) ? dir : null;

    [Fact]
    public void WithoutAGameFolderThePatternIsShown()
    {
        var (argb, fromGame) = FilterPreviews.Source(null);
        Assert.False(fromGame);
        Assert.Equal(FilterPreviews.Pattern(), argb);
        Assert.False(FilterPreviews.Source(Path.GetTempPath()).FromGame);
        // the pattern has dithering (a pixel unlike both its neighbours across) and more than a few colours
        Assert.True(argb.Distinct().Count() > 6);
    }

    [Fact]
    public void WithTheGameACardsPictureIsShown()
    {
        if (GameFolder() is not { } folder)
        {
            return;
        }
        var (argb, fromGame) = FilterPreviews.Source(folder);
        Assert.True(fromGame);
        Assert.Equal(FilterPreviews.CropWidth * FilterPreviews.CropHeight, argb.Length);
        Assert.True(argb.Distinct().Count() > 8);
    }

    /// <summary>
    /// Asking and looking return at once (the launcher's frames do nothing else); every filter's preview is made within a few
    /// seconds on a worker thread (the time is in the log of the test run), and no two look the same.
    /// </summary>
    [Fact]
    public async Task ThePreviewsAreMadeAwayFromTheAskingThread()
    {
        var previews = new FilterPreviews();
        var clock = Stopwatch.StartNew();
        previews.Prepare(GameFolder(), 360, 225, curvature: false);
        var asked = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        foreach (var filter in Enum.GetValues<PictureFilter>())
        {
            _ = previews.Get(filter);
        }
        var looked = clock.Elapsed.TotalMilliseconds;
        Assert.True(asked < 20, $"Prepare took {asked:F1} ms");
        Assert.True(looked < 5, $"Get took {looked:F1} ms");
        // asking again for the same is nothing
        var work = previews.Work;
        previews.Prepare(GameFolder(), 360, 225, curvature: false);
        Assert.Same(work, previews.Work);
        await previews.Work.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(previews.ReadyAfterMilliseconds);
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "filter-previews-timing.txt"),
            $"{DateTime.Now:s} all previews at 360x225 made in {previews.ReadyAfterMilliseconds:F0} ms (game: {GameFolder() is not null})\n");
        var made = Enum.GetValues<PictureFilter>().Select(f => previews.Get(f)!).ToList();
        Assert.All(made, p => Assert.Equal((360, 225, 360 * 225), (p.Width, p.Height, p.Argb.Length)));
        // at three times the piece's size, a whole multiple, Even is Sharp (as in the game window); every other one is its own
        Assert.Equal(made[(int)PictureFilter.Sharp].Argb, made[(int)PictureFilter.Even].Argb);
        var others = made.Where((_, i) => i != (int)PictureFilter.Even).ToList();
        Assert.Equal(others.Count, others.Select(p => string.Join(',', p.Argb)).Distinct().Count());
    }

    [Fact]
    public async Task ANewSizeKeepsTheLastPreviewsOnShowUntilItsOwnAreMade()
    {
        var previews = new FilterPreviews();
        previews.Prepare(null, 120, 75, curvature: false);
        await previews.Work.WaitAsync(TimeSpan.FromSeconds(30));
        previews.Prepare(null, 240, 150, curvature: true);
        // at once: still the last ones (never nothing once something was made)
        Assert.NotNull(previews.Get(PictureFilter.Crt));
        await previews.Work.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(240, previews.Get(PictureFilter.Crt)!.Width);
        // curved: the corners of the CRT kinds are black, the others' not
        Assert.Equal(0xFF000000u, previews.Get(PictureFilter.CrtPlus)!.Argb[0]);
        Assert.NotEqual(0xFF000000u, previews.Get(PictureFilter.Even)!.Argb[240 * 75 + 120]);
    }
}
