namespace Darklands.Platform.Tests;

/// <summary>The game's clock that a host stops while a registry view is open (1.2, the true pause).</summary>
public sealed class PausableClockTests
{
    [Fact]
    public void PausedTheClockStandsStillAndGoesOnWhereItStopped()
    {
        var clock = new PausableClock();
        Thread.Sleep(30);
        clock.Pause();
        Assert.True(clock.Paused);
        var stopped = clock.Milliseconds;
        Thread.Sleep(80);
        Assert.Equal(stopped, clock.Milliseconds);
        clock.Resume();
        Assert.False(clock.Paused);
        Thread.Sleep(30);
        var now = clock.Milliseconds;
        Assert.True(now - stopped >= 25 && now - stopped < 80, $"{now - stopped} ms after the pause");
    }

    [Fact]
    public void PausesNest()
    {
        var clock = new PausableClock();
        clock.Pause();
        clock.Pause();
        clock.Resume();
        Assert.True(clock.Paused);
        var stopped = clock.Milliseconds;
        Thread.Sleep(40);
        Assert.Equal(stopped, clock.Milliseconds);
        clock.Resume();
        Assert.False(clock.Paused);
        clock.Resume();   // one too many: nothing
        Assert.False(clock.Paused);
    }
}
