namespace Darklands.Platform;

/// <summary>
/// The keyboard's typematic repeat for the one key held last: after the delay it sends the key's make code again every period
/// until that key (or none) is released. The host feeds each repeat to <see cref="BiosKeyBuffer"/> exactly as a press. The delay and
/// period are DOSBox's (500 ms, then every 33 ms), the machine the original is played on.
/// </summary>
// orig: root 044A:020E (the INT 9 handler) sees only what the keyboard sends; the original never programs the keyboard (no INT 16h function 3,
//   no command F3h to port 60h anywhere in DARKLAND.EXE), so the machine's own typematic applies
// C: the overland map walks one step per key it reads and stands (key 0x4C35, DS:8676 = 0, the standing marker) in a pass with none
//   (R030+0x0540..0x066A, R030+0x0E5E); a pass lasts one BIOS tick (54.9 ms, R030+0x3096 on clock()). A key held at the PC/AT power-on
//   rate (byte 2Ch: 500 ms, 10.9/s) leaves about 7 of the 18.2 passes a second without a key, so the original itself alternates
//   walking and standing; DOSBox's rate (keyboard.cpp: repeat.pause 500, repeat.rate 33) gives a key to every pass (both measured on the
//   emulated original's map with the keyboard's bytes through its INT 9 handler and DOS's time from the BIOS ticks)
public sealed class KeyTypematic(double delayMs = KeyTypematic.DelayMs, double periodMs = KeyTypematic.PeriodMs)
{
    /// <summary>The delay before the first repeat, in milliseconds (DOSBox's, the PC keyboard's power-on value too).</summary>
    public const double DelayMs = 500;

    /// <summary>The time between two repeats, in milliseconds (DOSBox's: 30.3 a second; the PC keyboard's power-on rate is 91.7 ms, 10.9 a second).</summary>
    public const double PeriodMs = 33;

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
        _nextMs = nowMs + delayMs;
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
            _nextMs += periodMs;
        }
        return count;
    }
}
