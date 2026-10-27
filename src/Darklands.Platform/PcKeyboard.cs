namespace Darklands.Platform;

/// <summary>
/// The keyboard as the original's machine delivers it: a key's make code, its typematic repeats (<see cref="KeyTypematic"/>) and its
/// break code each reach the game's keyboard interrupt (the make and break codes also its held-key bytes, through
/// <see cref="TryReadTransition"/>), and the BIOS stores a press or repeat in its buffer (<see cref="BiosKeyBuffer"/>).
/// A host turns its own key events into <see cref="KeyDown"/> / <see cref="KeyUp"/> and calls <see cref="Tick"/> once a frame.
/// </summary>
// orig: root 044A:020E (the INT 9 handler, run for every byte the keyboard sends) and the BIOS buffer it collapses (BiosKeyBuffer)
public sealed class PcKeyboard(KeyTypematic? typematic = null)
{
    private readonly BiosKeyBuffer _keys = new();
    private readonly KeyTypematic _typematic = typematic ?? new();
    private readonly Queue<(int ScanCode, bool Released)> _transitions = new();

    /// <summary>A key goes down (not an operating-system repeat): its make code (0: none the game knows), then the BIOS key (<see cref="Keys.None"/>: none).</summary>
    /// <param name="id">The physical key, which its release names (the character can change with the shift state meanwhile).</param>
    public void KeyDown(int key, int scanCode, int id, double nowMs)
    {
        if (scanCode != 0)
        {
            _transitions.Enqueue((scanCode, false));
        }
        _typematic.Press(key, nowMs, id);
        Press(key);
    }

    /// <summary>A key comes up: its repeats stop, its break code reaches the keyboard interrupt.</summary>
    public void KeyUp(int scanCode, int id)
    {
        _typematic.Release(id);
        _keys.ScanCode();
        if (scanCode != 0)
        {
            _transitions.Enqueue((scanCode, true));
        }
    }

    /// <summary>The typematic repeats of the key held last that came due by <paramref name="nowMs"/>, each pressed as the keyboard sends it.</summary>
    public void Tick(double nowMs)
    {
        var due = _typematic.Due(nowMs, out var held);
        for (var i = 0; i < due; i++)
        {
            Press(held);
        }
    }

    /// <summary>Stops the repeating whatever the key (the developer console took the keyboard).</summary>
    public void StopRepeating() => _typematic.Stop();

    /// <summary>Takes the oldest key of the BIOS buffer.</summary>
    public bool TryReadKey(out int key) => _keys.TryRead(out key);

    /// <summary>The next make or break code for the game's held-key bytes.</summary>
    public bool TryReadTransition(out int scanCode, out bool released)
    {
        if (_transitions.TryDequeue(out var t))
        {
            (scanCode, released) = t;
            return true;
        }
        (scanCode, released) = (0, false);
        return false;
    }

    /// <summary>A make code or typematic repeat of a key reaches the keyboard interrupt: a game key is stored, any other only runs the handler.</summary>
    private void Press(int key)
    {
        if (key != Keys.None)
        {
            _keys.Press(key);
        }
        else
        {
            _keys.ScanCode();
        }
    }
}
