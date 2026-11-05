namespace Darklands.Core.World;

/// <summary>
/// What the original reads through a null far pointer: the real-mode interrupt vector table at
/// 0000:0000 (a vector = offset word, segment word). The tables of far pointers whose entry −1 the
/// original reads (<c>locations[-1]</c>, <c>events[-1]</c>) hold 0 there in play, so their fields
/// come from these words. Only the words some rule needs are listed.
/// </summary>
// C: the values are those of the reference machine (DOSBox-X, the machine the original is played and captured on), read from its
// source (tools/dosbox-x/source, src/ints/bios.cpp and src/gui/sdlmain.cpp): at boot vectors 00..5F get the default callback
// (CALLBACK_RealPointer(call_default): segment CB_SEG, F000 for a callback area in the ROM BIOS, (o >> 4) & 0xF000 in callback.cpp),
// 0A..0F the IRQ 2-7 handler F000:FF55, and the shell's start revectors 01 and 03 to BIOS_DEFAULT_HANDLER_LOCATION, F000:FF53 (the
// IRET at 0xFFF53, PhysToReal416). The game itself sets only INT 0, 8 and 9 (INT 21h AH=25h in root 047C:009C and root 06A1:026C, the
// writes of root 044A) and a sound driver its card's IRQ vector (0Ah only for a card on IRQ 2), so INT 1, 4 and 0Ah keep these. Every
// rule here only needs |segment| > 100 or its remainder by 4, and INT 1's offset.
public static class NullPage
{
    /// <summary>The segment word of INT 4 (0000:0012), read as <c>locations[-1]->rep</c> (+0x12).</summary>
    public const short Int4Segment = unchecked((short)0xF000);

    /// <summary>The vector of INT 1 (0000:0004), read as <c>*(long far *)(locations[-1] + 4)</c>: a place's map position (x = offset, y = segment).</summary>
    // C: F000:FF53, BIOS_DEFAULT_HANDLER_LOCATION, which DOSBox-X's shell start writes into vector 1 (sdlmain.cpp, "revector some
    //   dos-allocated interrupts": real_writed(0, 0x01*4, BIOS_DEFAULT_HANDLER_LOCATION)).
    public const short Int1Offset = unchecked((short)0xFF53);

    /// <summary>The segment word of INT 1 (0000:0006).</summary>
    public const short Int1Segment = unchecked((short)0xF000);

    /// <summary>The segment word of INT 0Ah (0000:002A), read as <c>events[-1]->f2a</c> (+0x2A, an event's first data word).</summary>
    // C: the rules need only its remainder by 4: 0 for the BIOS (F000) and DOS (0070) segments alike.
    public const short Int0ASegment = unchecked((short)0xF000);
}
