namespace Darklands.Platform.Recording;

/// <summary>
/// A host that hands the game everything its inner host gives (keys, mouse, clock, timer, the sound driver's answers) and writes each
/// value to a tape as the game asks for it. What the game sends out (frames, music, pointer moves) goes to the inner host untouched;
/// the player's window, sound and settings are the inner host's. Nothing of the game's state is kept: inputs only.
/// </summary>
// Not in the original: R-1, the input recording of a problem report (docs/REPORTING.md).
public sealed class RecordingHost : IHost
{
    private readonly IHost _inner;
    private readonly TapeWriter _tape;

    public RecordingHost(IHost inner, TapeWriter tape)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tape);
        (_inner, _tape) = (inner, tape);
        // each member is the inner host's as it is at the call (a host may put another audio in place after the start)
        Input = new RecordedInput(() => inner.Input, tape, () => AfterPump?.Invoke());
        Clock = new RecordedClock(() => inner.Clock, tape);
        Audio = new RecordedAudio(() => inner.Audio, tape);
        Timer = new RecordedTimer(() => inner.Timer, tape);
    }

    public IVideo Video => _inner.Video;

    public IInput Input { get; }

    public IAudio Audio { get; }

    public IClock Clock { get; }

    public IGameTimer Timer { get; }

    /// <summary>Called after each pump of the inner host (what the game plays by may have changed inside it: a paused view's settings).</summary>
    public Action? AfterPump { get; set; }

    /// <summary>The tape the values go to.</summary>
    public TapeWriter Tape => _tape;

    private sealed class RecordedInput(Func<IInput> host, TapeWriter tape, Action afterPump) : IInput
    {
        private IInput inner => host();

        public void Pump()
        {
            // the pump's mark first: what happens inside it (a setting changed in a paused view) is noted after it
            tape.Value(TapeKind.Pump, 0);
            inner.Pump();
            afterPump();
        }

        public bool QuitRequested
        {
            get
            {
                var quit = inner.QuitRequested;
                tape.Value(TapeKind.QuitRequested, quit ? 1 : 0);
                return quit;
            }
        }

        public bool TryReadKey(out int key)
        {
            var got = inner.TryReadKey(out key);
            tape.Value(TapeKind.Key, got ? key + 1L : 0);
            return got;
        }

        public MouseState Mouse
        {
            get
            {
                var mouse = inner.Mouse;
                tape.Value(TapeKind.Mouse, TapeValues.Mouse(mouse));
                return mouse;
            }
        }

        public bool ShiftHeld
        {
            get
            {
                var held = inner.ShiftHeld;
                tape.Value(TapeKind.Shift, held ? 1 : 0);
                return held;
            }
        }

        public bool CtrlHeld
        {
            get
            {
                var held = inner.CtrlHeld;
                tape.Value(TapeKind.Ctrl, held ? 1 : 0);
                return held;
            }
        }

        public bool TryReadKeyTransition(out int scanCode, out bool released)
        {
            var got = inner.TryReadKeyTransition(out scanCode, out released);
            tape.Value(TapeKind.KeyTransition, got ? ((long)scanCode << 1 | (released ? 1L : 0)) + 1 : 0);
            return got;
        }

        public bool TakeCtrlBreak()
        {
            var taken = inner.TakeCtrlBreak();
            tape.Value(TapeKind.CtrlBreak, taken ? 1 : 0);
            return taken;
        }

        public void MovePointer(int x, int y) => inner.MovePointer(x, y);

        public bool PointerInWindow
        {
            get
            {
                var inWindow = inner.PointerInWindow;
                tape.Value(TapeKind.PointerInWindow, inWindow ? 1 : 0);
                return inWindow;
            }
        }

        public bool SelectionListActive
        {
            get => inner.SelectionListActive;
            set => inner.SelectionListActive = value;
        }
    }

    private sealed class RecordedClock(Func<IClock> host, TapeWriter tape) : IClock
    {
        private IClock inner => host();

        public long Milliseconds
        {
            get
            {
                var now = inner.Milliseconds;
                tape.Value(TapeKind.ClockMilliseconds, now);
                return now;
            }
        }

        public void Sleep(int milliseconds) => inner.Sleep(milliseconds);
    }

    private sealed class RecordedTimer(Func<IGameTimer> host, TapeWriter tape) : IGameTimer
    {
        private IGameTimer inner => host();

        public long Ticks
        {
            get
            {
                var ticks = inner.Ticks;
                tape.Value(TapeKind.TimerTicks, ticks);
                return ticks;
            }
        }

        public long Frames
        {
            get
            {
                var frames = inner.Frames;
                tape.Value(TapeKind.TimerFrames, frames);
                return frames;
            }
        }

        public long RequestTick
        {
            get
            {
                var tick = inner.RequestTick;
                tape.Value(TapeKind.RequestTick, tick);
                return tick;
            }
        }

        public void Follow(TimerTrack? track, long afterTick) => inner.Follow(track, afterTick);
    }

    private sealed class RecordedAudio(Func<IAudio> host, TapeWriter tape) : IAudio
    {
        private IAudio inner => host();

        public void PlayMusic(int tune) => inner.PlayMusic(tune);

        public void PlayMusicAt(int tune, long tick) => inner.PlayMusicAt(tune, tick);

        public void StopMusic() => inner.StopMusic();

        public void PlaySound(int effect) => inner.PlaySound(effect);

        public int PlayMusicNow(int tune)
        {
            var ax = inner.PlayMusicNow(tune);
            tape.Value(TapeKind.PlayMusicNow, ax);
            return ax;
        }

        public int RequestCurrentPiece()
        {
            var ax = inner.RequestCurrentPiece();
            tape.Value(TapeKind.RequestCurrentPiece, ax);
            return ax;
        }

        public void CardShown(int card) => inner.CardShown(card);
    }
}

/// <summary>How the tape keeps the values that are not numbers.</summary>
public static class TapeValues
{
    /// <summary>A mouse state as one number.</summary>
    public static long Mouse(MouseState mouse) => (long)(ushort)mouse.X | (long)(ushort)mouse.Y << 16 | (long)mouse.Buttons << 32;

    /// <summary>The mouse state of a number <see cref="Mouse(MouseState)"/> made.</summary>
    public static MouseState Mouse(long value) => new((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF), (MouseButtons)(value >> 32));
}
