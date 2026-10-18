namespace Darklands.Core.World;

/// <summary>
/// What the original reads through a null far pointer: the real-mode interrupt vector table at
/// 0000:0000 (a vector = offset word, segment word). The tables of far pointers whose entry −1 the
/// original reads (<c>locations[-1]</c>, <c>events[-1]</c>) hold 0 there in play, so their fields
/// come from these words. Only the words some rule needs are listed.
/// </summary>
// UNCERTAIN: the values are those of the reference machine as far as they matter: a vector's segment word is the BIOS segment F000
// (DOSBox points the vectors the game does not hook into its F000 callback area; DOS's own handlers would be 0070, a hooked vector
// a program segment above 0100). Every rule here only needs |segment| > 100 or its remainder by 4; the offsets are not established.
public static class NullPage
{
    /// <summary>The segment word of INT 4 (0000:0012), read as <c>locations[-1]->rep</c> (+0x12).</summary>
    public const short Int4Segment = unchecked((short)0xF000);

    /// <summary>The vector of INT 1 (0000:0004), read as <c>*(long far *)(locations[-1] + 4)</c>: a place's map position (x = offset, y = segment).</summary>
    // UNCERTAIN: the offset is taken as FF53, the BIOS's default IRET handler that DOSBox points the unused vectors at; the segment is the BIOS's.
    public const short Int1Offset = unchecked((short)0xFF53);

    /// <summary>The segment word of INT 1 (0000:0006).</summary>
    public const short Int1Segment = unchecked((short)0xF000);

    /// <summary>The segment word of INT 0Ah (0000:002A), read as <c>events[-1]->f2a</c> (+0x2A, an event's first data word).</summary>
    // C: the rules need only its remainder by 4: 0 for the BIOS (F000) and DOS (0070) segments alike.
    public const short Int0ASegment = unchecked((short)0xF000);
}
