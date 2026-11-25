using Darklands.Platform;
using Darklands.Presentation.Views;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The true pause in a real SDL window (1.2, the view registry): a registry view opened by the host runs inside Pump; while it is open
/// the game's clock stands still and the sound device is paused, and the game's frame comes back when it closes. Uses the SDL3
/// library the host ships; skipped without one.
/// </summary>
[Collection("SDL")]
public sealed class TruePauseTests
{
    [Fact]
    public void AViewRunsInsidePumpWithTheClockAndTheSoundStill()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        using var host = new SdlHost("pause test");
        var sound = new List<bool>();
        host.AudioPaused = sound.Add;
        var frame = new Screen();
        frame.Pixels[0] = 5;
        host.Present(frame);
        host.Pump();
        var view = new EmptyView(20);
        host.OpenView(view);
        var before = host.Clock.Milliseconds;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        host.Pump();
        var took = watch.ElapsedMilliseconds;
        var after = host.Clock.Milliseconds;
        Assert.Equal(20, view.Shown);
        // 20 frames of the view took real time (a frame's wait is 15 ms); the game's clock saw almost none of it: only Pump's own work
        //   before and after the view, which under a loaded machine (the whole test run) takes some tens of ms; a clock that ran on
        //   would have moved all of it
        Assert.True(took >= 200, $"the view was open {took} ms");
        Assert.True(after - before < Math.Max(20, took / 5), $"the game's clock moved {after - before} ms while the view was open {took} ms");
        Assert.Equal([true, false], sound);
        Assert.False(host.ViewOpen);
        Assert.False(host.QuitRequested);
        Assert.Same(frame, host.LastFrame);
        // and the clock runs again
        Thread.Sleep(50);
        Assert.True(host.Clock.Milliseconds - after >= 40);
    }
}
