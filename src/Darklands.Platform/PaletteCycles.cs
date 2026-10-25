namespace Darklands.Platform;

/// <summary>
/// The palette cycles of the game's timer: up to eight ranges of the VGA DAC (at most 16 colours each) that the timer's frame work turns by
/// one colour every few frames while the cycles run, from the colours the DAC held when each range was taken. The DAC is 768 bytes of
/// 6-bit values (three per colour).
/// </summary>
// orig: root 047C:0362 (take), 047C:03DA (start), 047C:040B (stop), 047C:042A (one step of all slots); the slots are 0x66-byte records in
//   segment 28C0 listed at 28C0:0002 (eight pointers, then -1): +0 the direction (3 or -3), +2 the position, +4 the range's bytes (3n),
//   +6 the first colour, +7 the reload, +8 the counter, +9 the colours (3n bytes) followed by the first 3n - 3 again
// C: the timer interrupt's frame work root 047C:000A calls 042A first on EVERY frame (70.086 Hz) while DS:07D8 != 0, before the
//   one-in-seven skip of the 60 Hz tick (FrameTicker): the cycles count frames, not ticks.
public sealed class PaletteCycles
{
    public const int Slots = 8;

    private readonly Slot[] _slots = Enumerable.Range(0, Slots).Select(_ => new Slot()).ToArray();

    /// <summary>DS:07D8: the frame work runs the cycles.</summary>
    public bool Running { get; set; }

    /// <summary>Whether slot <paramref name="slot"/> (1..8) has a range taken and a counter running.</summary>
    public bool Turning(int slot) => (uint)(slot - 1) < Slots && _slots[slot - 1].Counter != 0;

    /// <summary>Takes the colours <paramref name="first"/> .. <paramref name="last"/> (at most 16) of the DAC into slot <paramref name="slot"/>
    /// (1..8), to turn one colour every <paramref name="reload"/> frames once started; the slot stands until then.</summary>
    // orig: root 047C:0362(slot, reload, first, last): slot - 1 >= 8 (unsigned): nothing; last - first <= 0 (unsigned): nothing; n = last - first + 1
    //   capped at 16; the 3n bytes read from the DAC (port 3C7 = first, then 3C9) with the interrupts off, the first 3n - 3 copied after them;
    //   +7 = reload (its low byte), +8 = 0, +2 = 0; the direction (+0) stays what it was
    public void Take(int slot, int reload, int first, int last, ReadOnlySpan<byte> dac)
    {
        if ((uint)((slot - 1) & 0xFFFF) >= Slots)
        {
            return;
        }
        var s = _slots[slot - 1];
        var span = (last & 0xFFFF) - (first & 0xFFFF);
        if (span <= 0)
        {
            return;
        }
        var bytes = Math.Min(span + 1, 16) * 3;
        s.Bytes = bytes;
        s.First = first & 0xFF;
        for (var i = 0; i < bytes; i++)
        {
            s.Colours[i] = dac[(s.First * 3 + i) % dac.Length];
        }
        for (var i = 0; i < bytes - 3; i++)
        {
            s.Colours[bytes + i] = s.Colours[i];
        }
        s.Reload = reload & 0xFF;
        s.Counter = 0;
        s.Position = 0;
    }

    /// <summary>Starts slot |<paramref name="slot"/>| (1..8): forwards for a positive number, backwards for a negative one.</summary>
    // orig: root 047C:03DA(slot): |slot| - 1 >= 8 (unsigned): nothing; +0 = slot < 0 ? -3 : 3; +8 = +7 (the counter reloaded)
    public void Start(int slot)
    {
        var index = Math.Abs((short)slot) - 1;
        if ((uint)index >= Slots)
        {
            return;
        }
        var s = _slots[index];
        s.Direction = (short)slot < 0 ? -3 : 3;
        s.Counter = s.Reload;
    }

    /// <summary>Stops slot <paramref name="slot"/> (1..8): its colours stay as the last step left them.</summary>
    // orig: root 047C:040B(slot): slot - 1 >= 8 (unsigned): nothing; +8 = 0
    public void Stop(int slot)
    {
        if ((uint)((slot - 1) & 0xFFFF) >= Slots)
        {
            return;
        }
        _slots[slot - 1].Counter = 0;
    }

    /// <summary>One frame of the timer: each running slot counts down and, at 0, turns its range by one colour into the DAC.</summary>
    // orig: root 047C:042A: for each slot of the list with +8 != 0: if (--(+8) == 0) { +8 = +7; si = +2 - +0; if (si < 0) si += +4;
    //   if (si >= +4) si = 0; +2 = si; port 3C8 = +6; the +4 bytes from +9 + si to port 3C9 }
    // C: the frame work calls it only while DS:07D8 != 0 (Running); the caller passes the frames.
    public void Frame(Span<byte> dac)
    {
        foreach (var s in _slots)
        {
            if (s.Counter == 0 || --s.Counter != 0)
            {
                continue;
            }
            s.Counter = s.Reload;
            var position = s.Position - s.Direction;
            if (position < 0)
            {
                position += s.Bytes;
            }
            if (position >= s.Bytes)
            {
                position = 0;
            }
            s.Position = position;
            for (var i = 0; i < s.Bytes; i++)
            {
                dac[(s.First * 3 + i) % dac.Length] = s.Colours[position + i];
            }
            s.Written = true;
        }
    }

    /// <summary>The colours slot <paramref name="slot"/> (1..8) last wrote into the DAC (first colour, 6-bit bytes), or none before its first step.</summary>
    public (int First, ReadOnlyMemory<byte> Colours)? Written(int slot)
    {
        var s = _slots[slot - 1];
        return s.Written ? (s.First, s.Colours.AsMemory(s.Position, s.Bytes)) : null;
    }

    /// <summary>Forgets what the slots wrote (the palette was set again: the DAC shows the palette until each slot's next step).</summary>
    public void PaletteSet()
    {
        foreach (var s in _slots)
        {
            s.Written = false;
        }
    }

    private sealed class Slot
    {
        public int Direction;
        public int Position;
        public int Bytes;
        public int First;
        public int Reload;
        public int Counter;
        public bool Written;
        public readonly byte[] Colours = new byte[16 * 3 * 2 - 3];
    }
}
