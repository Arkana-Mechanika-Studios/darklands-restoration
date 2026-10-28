namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The cursor keys' state: program globals that no battle start clears (only the cursor keys handler, order mode, the group
/// and single-file toggles and the battle-state restore write them), so a battle begins with what the last one (or the last
/// load of a battle) left; the battle file keeps them.
/// </summary>
// C: DS:D728, DS:D73B, DS:D7FE, DS:D947 (bytes) and DS:D945 (word) are written only by R014+0x35FC, R014+0xB576 (D73B = 0),
// C: order_mode_enter R014+0xBC0C / order_mode_leave R014+0xBCA8 (D7FE), R016+0x0000 (D73B = 1), R016+0x0BD6 / R016+0x0CDC (D7FE)
// C: and the restore R003+0x0CE8; saved in that order by R003+0x00DC (battle file +0x156A..+0x156F)
// C: the original writes only 0 or 1 into the four bytes; a byte above 1 in a file read here counts as 1
public sealed class CursorKeyState
{
    /// <summary>DS:D728: a cursor key moved the pointer this pass (the hover acts as with the button held).</summary>
    public bool Hover { get; set; }

    /// <summary>DS:D73B: the keyboard's click (at the pointer), taken by the next pass's mouse handler.</summary>
    public bool Click { get; set; }

    /// <summary>DS:D7FE: the cursor keys move the pointer.</summary>
    public bool On { get; set; }

    /// <summary>DS:D947: letting the keys go is a click at the pointer.</summary>
    public bool ClickOnRelease { get; set; }

    /// <summary>DS:D945: the timer callbacks count (DS:07D6) the held keys' speed is measured from; 0 except from a file.</summary>
    public ushort ReleasedAt { get; set; }

    /// <summary>DS:07D6: the timer callbacks since the keys were last let go in a battle (a word that wraps); not in the battle file.</summary>
    // C: the timer's frame work root 047C:000A counts it at every callback of the program (the timer is installed at the start,
    // C: R000+0x06A5, and only taken off around the sound driver's reloads), in a battle or not; R014+0x38AE (the cursor keys let go)
    // C: is its only clear, so a battle begins with what the time since the last battle's let-go (or since the start) left
    public ushort HeldCallbacks { get; set; }

    /// <summary>The game timer's ticks when the last battle screen ended (the time outside the battles counts <see cref="HeldCallbacks"/> on).</summary>
    public long TimerTicksAtLeave { get; set; }

    /// <summary>The state as a battle file holds it (6 bytes).</summary>
    public void Write(Span<byte> to)
    {
        to[0] = (byte)(Hover ? 1 : 0);
        to[1] = (byte)(Click ? 1 : 0);
        to[2] = (byte)(On ? 1 : 0);
        to[3] = (byte)(ClickOnRelease ? 1 : 0);
        to[4] = (byte)ReleasedAt;
        to[5] = (byte)(ReleasedAt >> 8);
    }

    /// <summary>The state from a battle file's 6 bytes.</summary>
    public void Read(ReadOnlySpan<byte> from)
    {
        Hover = from[0] != 0;
        Click = from[1] != 0;
        On = from[2] != 0;
        ClickOnRelease = from[3] != 0;
        ReleasedAt = (ushort)(from[4] | from[5] << 8);
    }
}
