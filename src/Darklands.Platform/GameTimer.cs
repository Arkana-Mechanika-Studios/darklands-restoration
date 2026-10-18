namespace Darklands.Platform;

/// <summary>
/// The game's timer, one clock for the music and the pictures. The original runs the PC timer
/// at the VGA's frame rate (70.086 Hz); on six frames out of seven its interrupt calls the PAN
/// player's tick (root 030B:0508) and the sound driver's frame tick, so both count the same
/// 60 Hz ticks. A host's audio drives it (the sound is the clock); without sound the host's
/// real-time clock does.
/// </summary>
// orig: root 047C:000A (the frame work of the timer interrupt root 047C:01EA): `sub [7DAh],1 / jg` skips one frame in seven when the
//   multiplier [7E8h] is 4, then calls 030B:0508 (PAN tick) and 047C:0798 (the driver's frame tick). tests: GameTimerTests
public interface IGameTimer
{
    /// <summary>Ticks so far, as the player sees and hears them.</summary>
    long Ticks { get; }

    /// <summary>The first tick a sound request made now can be carried out at (the sound's output latency).</summary>
    long RequestTick { get; }

    /// <summary>From the frame after tick <paramref name="afterTick"/>, deliver the ticks of <paramref name="track"/> frame by frame; null stops a track.</summary>
    void Follow(TimerTrack? track, long afterTick);
}

/// <summary>
/// The ticks a measured run of the original delivered, frame by frame (0, 1, 2 ...): where its
/// timer interrupts were held up or lost (the PAN player reading its file), the music and the
/// animation slowed down together.
/// </summary>
public sealed class TimerTrack(string name, byte[] ticksPerFrame)
{
    public string Name { get; } = name;

    public IReadOnlyList<byte> TicksPerFrame { get; } = ticksPerFrame;

    /// <summary>Reads a track written as one digit per frame.</summary>
    public static TimerTrack FromDigits(string name, string digits) =>
        new(name, digits.Where(char.IsDigit).Select(c => (byte)(c - '0')).ToArray());

    public long Ticks => TicksPerFrame.Sum(t => (long)t);
}

/// <summary>
/// The frame-by-frame rule of the game's timer: one tick per frame, none on every seventh, or a
/// measured <see cref="TimerTrack"/> while one is followed. Pure: the caller says when a frame passes.
/// </summary>
public sealed class FrameTicker
{
    /// <summary>The PC timer's input clock.</summary>
    public const double PitClock = 1_193_182.0;

    /// <summary>The frame period the game measures from the VGA retrace, in timer counts (70.086 Hz).</summary>
    // orig: root 047C:02A2 measures the retrace period [7EAh] in timer counts. The value is the one of the shared-clock capture of the
    //   original (14.268048 ms per frame, 17024.5 counts).
    public const int FramePeriod = 0x4280;

    public static double FrameSeconds => FramePeriod / PitClock;

    private sbyte _skip = 7; // [7DAh]
    private TimerTrack? _track;
    private int _trackFrame;
    private TimerTrack? _pending;
    private long _pendingAfter;

    public long Ticks { get; private set; }

    public long Frames { get; private set; }

    /// <summary>True while a measured track is being followed.</summary>
    public bool Following => _track is not null;

    public void Follow(TimerTrack? track, long afterTick)
    {
        if (track is null)
        {
            _track = _pending = null;
            return;
        }
        _pending = track;
        _pendingAfter = afterTick;
    }

    /// <summary>One frame passes; returns the ticks it delivers.</summary>
    // orig: root 047C:000A - C: `sub byte [7DAh],1 / jg`: a signed byte, reloaded with 7 on the frame left out.
    public int NextFrame()
    {
        Frames++;
        if (_pending is not null && Ticks >= _pendingAfter)
        {
            _track = _pending;
            _trackFrame = 0;
            _pending = null;
        }
        int ticks;
        if (_track is not null)
        {
            ticks = _track.TicksPerFrame[_trackFrame++];
            if (_trackFrame >= _track.TicksPerFrame.Count)
            {
                _track = null;
                _skip = 7;
            }
        }
        else
        {
            _skip--;
            if (_skip <= 0)
            {
                _skip = 7;
                ticks = 0;
            }
            else
            {
                ticks = 1;
            }
        }
        Ticks += ticks;
        return ticks;
    }
}

/// <summary>The game timer on a real-time clock (hosts without sound).</summary>
public sealed class ClockGameTimer(IClock clock) : IGameTimer
{
    private readonly FrameTicker _ticker = new();
    private readonly object _lock = new();
    private readonly long _start = clock.Milliseconds;

    public long Ticks
    {
        get
        {
            lock (_lock)
            {
                var frames = (long)((clock.Milliseconds - _start) / 1000.0 / FrameTicker.FrameSeconds);
                while (_ticker.Frames < frames)
                {
                    _ticker.NextFrame();
                }
                return _ticker.Ticks;
            }
        }
    }

    public long RequestTick => Ticks;

    public void Follow(TimerTrack? track, long afterTick)
    {
        lock (_lock)
        {
            _ticker.Follow(track, afterTick);
        }
    }
}
