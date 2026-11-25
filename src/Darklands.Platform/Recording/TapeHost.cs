namespace Darklands.Platform.Recording;

/// <summary>
/// A host that plays a tape back (<see cref="RecordingHost"/>): the game gets the same values in the same order as when it was
/// recorded, so it does the same, to the byte; nothing waits (a sleep takes no time). A value of another kind than the tape's next one
/// stops the replay (<see cref="TapeMismatchException"/>); the tape's end stops it too (<see cref="TapeEndedException"/>). Frames and
/// sound go nowhere, or to <see cref="Frames"/>.
/// </summary>
// Not in the original: R-1, the input recording.
public sealed class TapeHost : IHost, IVideo, IInput, IClock, IAudio, IGameTimer
{
    private readonly TapeReader _tape;

    public TapeHost(TapeReader tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        _tape = tape;
    }

    /// <summary>A note met right after a pump (a setting changed, a console command): the replay carries it out there.</summary>
    public Action<string>? Note { get; set; }

    /// <summary>Called at the pump the recording was taken in, before the replay stops there (the game as it was then).</summary>
    public Action? Ended { get; set; }

    /// <summary>Each frame the game shows (null: none kept).</summary>
    public Action<Screen>? Frames { get; set; }

    /// <summary>The last frame the game showed.</summary>
    public Screen? LastFrame { get; private set; }

    /// <summary>The tape's position (entries read).</summary>
    public long Position => _tape.Position;

    public IVideo Video => this;

    public IInput Input => this;

    public IAudio Audio => this;

    public IClock Clock => this;

    public IGameTimer Timer => this;

    public void Present(Screen screen)
    {
        LastFrame = screen;
        Frames?.Invoke(screen);
    }

    public void Pump()
    {
        _tape.Value(TapeKind.Pump);
        while (_tape.TakeNote() is { } note)
        {
            Note?.Invoke(note);
        }
        if (_tape.AtEnd)
        {
            // the recording was taken inside this pump (a problem report): the game is where it was then (Ended looks at it before the
            //   game unwinds)
            Ended?.Invoke();
            throw new TapeEndedException();
        }
    }

    public bool QuitRequested => _tape.Value(TapeKind.QuitRequested) != 0;

    public bool TryReadKey(out int key)
    {
        var value = _tape.Value(TapeKind.Key);
        key = value == 0 ? 0 : (int)(value - 1);
        return value != 0;
    }

    public MouseState Mouse => TapeValues.Mouse(_tape.Value(TapeKind.Mouse));

    public bool ShiftHeld => _tape.Value(TapeKind.Shift) != 0;

    public bool CtrlHeld => _tape.Value(TapeKind.Ctrl) != 0;

    public bool TryReadKeyTransition(out int scanCode, out bool released)
    {
        var value = _tape.Value(TapeKind.KeyTransition);
        scanCode = value == 0 ? 0 : (int)((value - 1) >> 1);
        released = value != 0 && ((value - 1) & 1) != 0;
        return value != 0;
    }

    public bool TakeCtrlBreak() => _tape.Value(TapeKind.CtrlBreak) != 0;

    public void MovePointer(int x, int y)
    {
    }

    public bool PointerInWindow => _tape.Value(TapeKind.PointerInWindow) != 0;

    public bool SelectionListActive { get; set; }

    public long Milliseconds => _tape.Value(TapeKind.ClockMilliseconds);

    public void Sleep(int milliseconds)
    {
    }

    public long Ticks => _tape.Value(TapeKind.TimerTicks);

    long IGameTimer.Frames => _tape.Value(TapeKind.TimerFrames);

    public long RequestTick => _tape.Value(TapeKind.RequestTick);

    public void Follow(TimerTrack? track, long afterTick)
    {
    }

    public void PlayMusic(int tune)
    {
    }

    public void PlayMusicAt(int tune, long tick)
    {
    }

    public void StopMusic()
    {
    }

    public void PlaySound(int effect)
    {
    }

    public int PlayMusicNow(int tune) => (int)_tape.Value(TapeKind.PlayMusicNow);

    public int RequestCurrentPiece() => (int)_tape.Value(TapeKind.RequestCurrentPiece);

    public void CardShown(int card)
    {
    }

    /// <summary>A seed the game asks for (as recorded).</summary>
    public uint Seed() => (uint)_tape.Value(TapeKind.Seed);

    /// <summary>A tick word of the day the game asks for (as recorded).</summary>
    public ushort PartyTicks() => (ushort)_tape.Value(TapeKind.PartyTicks);
}
