using Darklands.Content.Formats.Graphics;

namespace Darklands.Presentation.Graphics;

/// <summary>Palette entries the game uses for fixed colours.</summary>
public static class Palette
{
    public const byte Black = 0;

    /// <summary>Colour 5: black like colour 0 (see <see cref="StartupColors"/>), the colour pictures and sprites use for black that must not be transparent.</summary>
    public const byte OpaqueBlack = 5;

    public const byte Highlight = 9;
    public const byte White = 15;
    public const byte Grey = 7;
    public const byte Blank = 0xFF;

    /// <summary>
    /// Colours 0..15 as the game sets them at start-up and keeps them (pictures define 16..255 only):
    /// the graphics driver's 16 EGA colours taken through the index list {0, 1, 2, 3, 4, 0, 6, 7, ..., 15},
    /// so colour 5 is black (not magenta) and colour 6 brown.
    /// </summary>
    // orig: root 03A1:000A (the program's start) calls the driver's EGA palette entry, root thunk 047C:0584 (driver slot 27,
    //   drv+0x010C, asm), with the 17-byte list at DS:01C0 = {0, 1, 2, 3, 4, 0, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15; border 0}:
    //   each byte picks an entry of the driver's 64-colour EGA table (drv data +0x50, 6-bit DAC values) and DAC entries 0..15
    //   are written in order. The battles set the same list again (BattleScreen). checked against asm (driver and DS bytes);
    //   tests: tools/visuals/compare.py (0 differing pixels on the Quickstart captures)
    // C: the DAC holds 6-bit values; a host shows v as (v << 2) | (v >> 4), as DOSBox does (42 -> 170, 21 -> 85, 63 -> 255).
    public static IReadOnlyList<Rgb24> StartupColors { get; } =
    [
        Dac(0, 0, 0), Dac(0, 0, 42), Dac(0, 42, 0), Dac(0, 42, 42),
        Dac(42, 0, 0), Dac(0, 0, 0), Dac(42, 21, 0), Dac(42, 42, 42),
        Dac(21, 21, 21), Dac(21, 21, 63), Dac(21, 63, 21), Dac(21, 63, 63),
        Dac(63, 21, 21), Dac(63, 21, 63), Dac(63, 63, 21), Dac(63, 63, 63),
    ];

    /// <summary>A colour from 6-bit VGA DAC values, widened to 8 bits.</summary>
    public static Rgb24 Dac(int r, int g, int b) => new(Widen(r), Widen(g), Widen(b));

    private static byte Widen(int v) => (byte)((v << 2) | (v >> 4));
}
