using Darklands.Content.Formats.Graphics;

namespace Darklands.Content.Formats.Battles;

/// <summary>
/// The colours a battle sets on top of the current palette (<c>battle_run</c>): the common
/// battle colours (COMNCLRS.DAT) at 16..31, 120..163 and 243..254, the party's clothing
/// colours at 80..119 (eight per member), the battlefield's background colours (one of the
/// palettes of BKGNDPAL.DAT) at 164..234, a grey at 13, and white at every other colour from 16 up
/// (32..79, 235..242, 255).
/// </summary>
// orig: battle_run R002+0x0000 (0E76:0000) draft (the palette block before combat_main)
// orig: R002+0x03AC root 0410:031E reads the DAC into the 0x300-byte buffer, R002+0x03B4..0x03C6 fills bytes 0x30..0x2FF (colours
//   16..255) with 0xFF, R002+0x03CE..0x03DE writes 0x31 into bytes 0x27..0x29 (colour 13), R002+0x03F9..0x04AA copies the parts below;
//   R002+0x04BA set_palette root 0410:0364 writes all 0x300 bytes to port 3C9 as they are (root 0410:0397..0x0399 `lodsb; out dx, al`):
//   the DAC keeps the low six bits, so a byte 0xFF left by the fill is 0x3F (white). checked against asm and the original's DAC (vis10
//   sessions D-09fbc1, D-b797f7: colours 32..47, 235..242 and 255 white through the battle; local/batch/batpal)
// DIVERGENCE: the original builds the palette only when DS:9069 == 0 (not in a re-entered battle); callers decide.
public static class BattlePalette
{
    public const int PartyColorsStart = 80;
    public const int BackgroundStart = 164;
    private const int BackgroundBytes = 0xD5;

    /// <summary>The colours to set: colour 13 and every colour 16..255 (0..12, 14 and 15 keep their colour).</summary>
    public static IReadOnlyDictionary<int, Rgb24> Colors(byte[] commonColors, byte[] backgroundPalettes, int background,
        ReadOnlySpan<byte> partyColors)
    {
        var dac = new byte[0x300];
        dac.AsSpan(0x30).Fill(0xFF);
        commonColors.AsSpan(0, 0x30).CopyTo(dac.AsSpan(0x30));
        commonColors.AsSpan(0x30, 0x84).CopyTo(dac.AsSpan(3 * 120));
        commonColors.AsSpan(0xB4, 0x24).CopyTo(dac.AsSpan(3 * 243));
        partyColors[..Math.Min(0x78, partyColors.Length)].CopyTo(dac.AsSpan(3 * PartyColorsStart));
        var start = BackgroundBytes * background;
        if (start + BackgroundBytes <= backgroundPalettes.Length)
        {
            backgroundPalettes.AsSpan(start, BackgroundBytes).CopyTo(dac.AsSpan(3 * BackgroundStart));
        }
        var colors = new Dictionary<int, Rgb24> { [13] = new Rgb24(Expand(0x31), Expand(0x31), Expand(0x31)) };
        for (var i = 16; i < 256; i++)
        {
            colors[i] = new Rgb24(Expand(dac[3 * i]), Expand(dac[3 * i + 1]), Expand(dac[3 * i + 2]));
        }
        return colors;
    }

    /// <summary>A 6-bit VGA DAC channel as an 8-bit one (the DAC keeps the low six bits of a byte written to it).</summary>
    public static byte Expand(byte value) => (byte)((value & 0x3F) << 2 | (value & 0x3F) >> 4);
}
