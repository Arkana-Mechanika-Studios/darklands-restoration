namespace Darklands.Platform;

/// <summary>
/// The PC's keyboard buffer as the original sees it with its own keyboard interrupt installed: the BIOS ring of 16 words at
/// 0040:001E (head 0040:001A, tail 0040:001C) holds at most 15 keys, a key typed into a full buffer is lost; and before every
/// scan code reaches the BIOS, the game's handler drops the copies of the oldest key that follow it, so a held key (its
/// typematic repeats) never stacks up more than two presses.
/// </summary>
// orig: root 044A:020E (the INT 9 handler installed once by root 044A:019C at R000+0x06C0; root 044A:01F4 removes it only on the exit paths, e.g. R030+0x307C), its tail
//   root 044A:02EE..0x031D, run for every byte of port 60h (make, break, the E0/E1 prefixes) before it jumps to the BIOS handler
//   (044A:0325): `bx = head; if (bx != tail) { ax = word[bx]; for (;;) { bx += 2 (wrapping at 0040:0082 to 0040:0080); if (bx == tail
//   || word[bx] != ax) break; head = bx; } }`; the BIOS then stores the new key at the tail (dropped when the buffer is full)
public sealed class BiosKeyBuffer
{
    /// <summary>Keys the BIOS buffer holds at most (16 words, one always free).</summary>
    public const int Capacity = 15;

    private readonly LinkedList<int> _keys = new();

    public int Count => _keys.Count;

    /// <summary>A scan code (a press, a typematic repeat or a release) reaches the game's keyboard interrupt: the copies of the oldest key are dropped.</summary>
    public void ScanCode()
    {
        while (_keys.First is { Next: { } next } first && next.Value == first.Value)
        {
            _keys.RemoveFirst();
        }
    }

    /// <summary>A press or repeat of a key the BIOS stores: the game's handler first, then the BIOS at the tail (lost when full).</summary>
    public void Press(int key)
    {
        ScanCode();
        if (_keys.Count < Capacity)
        {
            _keys.AddLast(key);
        }
    }

    /// <summary>Takes the oldest key (INT 16h function 0 without the wait).</summary>
    public bool TryRead(out int key)
    {
        if (_keys.First is { } first)
        {
            key = first.Value;
            _keys.RemoveFirst();
            return true;
        }
        key = 0;
        return false;
    }

    public void Clear() => _keys.Clear();
}
