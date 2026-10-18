namespace Darklands.Platform;

/// <summary>Everything the game needs from the machine it runs on.</summary>
public interface IHost
{
    IVideo Video { get; }

    IInput Input { get; }

    IAudio Audio { get; }

    IClock Clock { get; }

    /// <summary>The game's timer (the music's and the animations' clock); by default the real-time clock's.</summary>
    IGameTimer Timer => DefaultTimers.For(Clock);
}

/// <summary>One real-time game timer per clock, for hosts that do not provide their own.</summary>
internal static class DefaultTimers
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IClock, ClockGameTimer> Timers = new();

    public static IGameTimer For(IClock clock) => Timers.GetValue(clock, c => new ClockGameTimer(c));
}

/// <summary>Shows the game screen.</summary>
public interface IVideo
{
    void Present(Screen screen);

    /// <summary>Shows the 80×25 text screen (the program start before the graphics screen); hosts without one ignore it.</summary>
    void PresentText(TextScreen screen)
    {
    }
}

/// <summary>Keyboard and mouse, in game terms.</summary>
public interface IInput
{
    /// <summary>Processes pending host events. Call often.</summary>
    void Pump();

    /// <summary>The player closed the window or asked the host to quit.</summary>
    bool QuitRequested { get; }

    /// <summary>Takes the next key press, as a game key code (<see cref="Keys"/>).</summary>
    bool TryReadKey(out int key);

    /// <summary>Mouse position in screen pixels (0..319, 0..199) and buttons held.</summary>
    MouseState Mouse { get; }

    /// <summary>True while a Shift key is held (the game shows chances while Shift is down).</summary>
    bool ShiftHeld { get; }

    /// <summary>True while a Ctrl key is held (the battle's Ctrl-click); hosts that cannot tell say no.</summary>
    bool CtrlHeld => false;

    /// <summary>
    /// The next make (pressed) or break (let go) of a key, as its PC scan code (set 1, without the E0 prefix), for the game's own
    /// keyboard interrupt (the held direction keys); hosts that cannot tell give none.
    /// </summary>
    bool TryReadKeyTransition(out int scanCode, out bool released)
    {
        scanCode = 0;
        released = false;
        return false;
    }

    /// <summary>Puts the mouse pointer at a screen pixel (the mouse driver's function 4, as the game's cursor keys do); hosts that cannot move it ignore it.</summary>
    void MovePointer(int x, int y)
    {
    }
}

[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
}

public readonly record struct MouseState(int X, int Y, MouseButtons Buttons)
{
    public bool Left => (Buttons & MouseButtons.Left) != 0;

    public bool Right => (Buttons & MouseButtons.Right) != 0;
}

/// <summary>Music and sound effects, by the game's tune and effect numbers.</summary>
public interface IAudio
{
    void PlayMusic(int tune);

    /// <summary>Plays a music number at a tick of the game timer (no sooner than its RequestTick): the animations' cues.</summary>
    void PlayMusicAt(int tune, long tick) => PlayMusic(tune);

    void StopMusic();

    void PlaySound(int effect);

    /// <summary>
    /// Carries out a towns-and-map driver number now, after the requests made before it, and returns
    /// the AX the driver leaves (the game's music callers read it); −1 without a driver.
    /// </summary>
    // orig: root 047C:078A asm - the play slot returns in AX; R043+0x09AE (1462:09AE) leaves that AX to its callers
    int PlayMusicNow(int tune)
    {
        PlayMusic(tune);
        return -1;
    }

    /// <summary>Driver command 5: the number last played (the driver's word [CA60h] in ASOUND.DC); −1 without a driver.</summary>
    // orig: root 047C:078A asm - number 5 is not stored: it returns the last number stored (ASOUND.DC image 20D0h..20D8h)
    int RequestCurrentPiece() => PlayMusicNow(5);
}

/// <summary>Real time.</summary>
public interface IClock
{
    long Milliseconds { get; }

    void Sleep(int milliseconds);
}

/// <summary>Audio that does nothing (no sound device, or sound not implemented by a host).</summary>
public sealed class SilentAudio : IAudio
{
    public void PlayMusic(int tune) { }

    public void StopMusic() { }

    public void PlaySound(int effect) { }
}

/// <summary>A clock on the system timer.</summary>
public sealed class SystemClock : IClock
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

    public long Milliseconds => _watch.ElapsedMilliseconds;

    public void Sleep(int milliseconds) => Thread.Sleep(Math.Max(0, milliseconds));
}
