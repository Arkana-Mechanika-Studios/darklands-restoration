namespace Darklands.Presentation;

/// <summary>
/// The direction keys as the game's own keyboard interrupt keeps them: one direction key held at a time (a second one is
/// ignored until the first is let go), its direction as the signs of DS:0880 (x) and DS:0881 (y), cleared when it is let go.
/// The battle's cursor keys and its keys read them besides the BIOS keys.
/// </summary>
// orig: root 044A:020E (the INT 9 handler, asm, installed by root 044A:019C), its table DS:088A for the scan codes 0x29..0x51 (bits: 1 up,
// 2 down, 4 left, 8 right; 0x80 keypad 5, held without a direction): on a make code with no key held, DS:0884 = bits; the size
// 0x7F for the same key again within 5 BIOS ticks, else 0x5A; up/down set DS:0881 = −/+size, left/right DS:0880; on the break code of
// the held key DS:0884 = DS:0880 = DS:0881 = 0; the E0/E1 prefixes are stepped over (the grey keys give the keypad's codes); the key
// always goes on to the BIOS; tests: BattleUiTests
public sealed class KeyboardDirections
{
    private const double BiosTickMilliseconds = 54.925;

    private int _held;
    private int _lastBits;
    private long _lastMake = long.MinValue / 2;

    /// <summary>DS:0880: the held key's horizontal direction (negative left, positive right, 0 none).</summary>
    public int X { get; private set; }

    /// <summary>DS:0881: the held key's vertical direction (negative up, positive down, 0 none).</summary>
    public int Y { get; private set; }

    /// <summary>The bits of the scan code (DS:088A), 0 for the keys it does not know.</summary>
    public static int BitsOf(int scanCode) => scanCode switch
    {
        0x29 => 1,
        0x2B => 4,
        0x47 => 5,
        0x48 => 1,
        0x49 => 9,
        0x4A => 2,
        0x4B => 4,
        0x4C => 0x80,
        0x4D => 8,
        0x4E => 8,
        0x4F => 6,
        0x50 => 2,
        0x51 => 10,
        _ => 0,
    };

    /// <summary>A make (pressed) or break (let go) code of a key, at a time in milliseconds.</summary>
    public void Feed(int scanCode, bool released, long milliseconds)
    {
        var bits = BitsOf(scanCode & 0x7F);
        if (bits == 0)
        {
            return;
        }
        if (!released)
        {
            if (_held != 0)
            {
                return;
            }
            _held = bits;
            var ticks = (long)(milliseconds / BiosTickMilliseconds);
            var size = bits == _lastBits && ticks - _lastMake < 5 ? 0x7F : 0x5A;
            _lastBits = bits;
            if ((bits & 1) != 0)
            {
                Y = -size;
            }
            if ((bits & 2) != 0)
            {
                Y = size;
            }
            if ((bits & 4) != 0)
            {
                X = -size;
            }
            if ((bits & 8) != 0)
            {
                X = size;
            }
            _lastMake = ticks;
        }
        else if (_held == bits)
        {
            _held = 0;
            X = Y = 0;
        }
    }
}
