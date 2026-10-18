using Darklands.Platform;

namespace Darklands.Audio.Tests;

/// <summary>
/// The game timer: 70.086 Hz frames (the VGA's), a tick on six frames of seven (the 60 Hz clock the
/// music and the PAN player count), or the ticks of a measured track while one is followed.
/// </summary>
public sealed class GameTimerTests
{
    [Fact]
    public void SixOfSevenFramesTick()
    {
        var ticker = new FrameTicker();
        var ticks = Enumerable.Range(0, 70).Select(_ => ticker.NextFrame()).ToArray();
        Assert.Equal(60, ticker.Ticks);
        // [7DAh] counts 7 down from its first value: the seventh frame is left out
        Assert.Equal([1, 1, 1, 1, 1, 1, 0], ticks[..7]);
        Assert.Equal(70, ticker.Frames);
    }

    [Fact]
    public void TheFrameIsTheVgaRetraceTheCaptureMeasured()
    {
        Assert.Equal(0x4280, FrameTicker.FramePeriod);
        Assert.Equal(14.268, FrameTicker.FrameSeconds * 1000, 2);
        // six ticks (one cel of a PAN file) take seven frames: 99.88 ms
        Assert.Equal(99.88, 7 * FrameTicker.FrameSeconds * 1000, 1);
    }

    [Fact]
    public void ATrackStartsWithTheFrameAfterItsTick()
    {
        var ticker = new FrameTicker();
        ticker.NextFrame();
        ticker.NextFrame();                        // tick 2
        ticker.Follow(TimerTrack.FromDigits("t", "0 2 0 0 1"), afterTick: 2);
        Assert.False(ticker.Following);
        var delivered = Enumerable.Range(0, 5).Select(_ => ticker.NextFrame()).ToArray();
        Assert.Equal([0, 2, 0, 0, 1], delivered);
        Assert.False(ticker.Following);            // the track has ended: the usual pattern again
        Assert.Equal(5, ticker.Ticks);
        Assert.Equal(1, ticker.NextFrame());
    }

    [Fact]
    public void ATrackForALaterTickWaitsForIt()
    {
        var ticker = new FrameTicker();
        ticker.Follow(TimerTrack.FromDigits("t", "000"), afterTick: 3);
        Assert.Equal([1, 1, 1, 0, 0, 0, 1], Enumerable.Range(0, 7).Select(_ => ticker.NextFrame()).ToArray());
    }

    [Fact]
    public void FollowingNothingStopsATrack()
    {
        var ticker = new FrameTicker();
        ticker.Follow(TimerTrack.FromDigits("t", "0000000000"), afterTick: 0);
        ticker.NextFrame();
        Assert.True(ticker.Following);
        ticker.Follow(null, 0);
        Assert.False(ticker.Following);
        Assert.Equal(1, ticker.NextFrame());
    }

    [Fact]
    public void WithoutSoundTheHostsClockDrivesTheTimer()
    {
        var clock = new ManualClock();
        var timer = new ClockGameTimer(clock);
        Assert.Equal(0, timer.Ticks);
        clock.Now = 1000;                           // 70 frames
        Assert.Equal(60, timer.Ticks);
        Assert.Equal(timer.Ticks, timer.RequestTick);
    }

    private sealed class ManualClock : IClock
    {
        public long Now { get; set; }

        public long Milliseconds => Now;

        public void Sleep(int milliseconds) => Now += milliseconds;
    }
}
