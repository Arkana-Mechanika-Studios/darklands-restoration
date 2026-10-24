namespace Darklands.Platform;

/// <summary>
/// The keyboard's typematic repeat for the one key held last: after the delay it sends the key's make code again at the repeat
/// rate until that key (or none) is released. The host feeds each repeat to <see cref="BiosKeyBuffer"/> exactly as a press.
/// </summary>
// orig: root 044A:020E (the INT 9 handler) sees only what the keyboard sends; the original never programs the keyboard (no INT 16h function 3,
//   no command F3h to port 60h anywhere in DARKLAND.EXE), so the PC/AT power-on typematic applies (default byte 2Ch: 500 ms, 10.9 cps)
//   (the divergence from SDL's operating-system repeat is registered at the host member that uses this)
public sealed class KeyTypematic
{
    /// <summary>The keyboard's power-on delay before the first repeat, in milliseconds.</summary>
    public const double DelayMs = 500;

    /// <summary>The keyboard's power-on repeat rate, characters per second.</summary>
    public const double RateCps = 10.9;

    private int _key;
    private int _id;
    private bool _held;
    private double _nextMs;

    public bool IsHeld => _held;

    /// <summary>A key goes down (an operating-system auto-repeat is not a press): it takes over the repeating.</summary>
    public void Press(int key, double nowMs) => Press(key, nowMs, key);

    /// <summary>The same, the physical key (its scan code) apart from the character it produced, which can change with the shift state.</summary>
    public void Press(int key, double nowMs, int id)
    {
        _key = key;
        _id = id;
        _held = true;
        _nextMs = nowMs + DelayMs;
    }

    /// <summary>A key comes up: when it is the one repeating, the repeats stop.</summary>
    public void Release(int id)
    {
        if (_held && _id == id)
        {
            _held = false;
        }
    }

    /// <summary>Stops the repeating whatever the key (the focus or the console took the keyboard).</summary>
    public void Stop() => _held = false;

    /// <summary>The repeats of the held key that have come due by <paramref name="nowMs"/>; each is a repeat of the returned key.</summary>
    public int Due(double nowMs, out int key)
    {
        key = _key;
        var count = 0;
        while (_held && nowMs >= _nextMs)
        {
            count++;
            _nextMs += 1000.0 / RateCps;
        }
        return count;
    }
}
