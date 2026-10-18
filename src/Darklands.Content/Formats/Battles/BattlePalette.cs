using Darklands.Content.Formats.Graphics;

namespace Darklands.Content.Formats.Battles;

/// <summary>
/// The colours a battle sets on top of the current palette (<c>battle_run</c>): the common
/// battle colours (COMNCLRS.DAT) at 16..31, 120..163 and 243..254, the party's clothing
/// colours at 80..119 (eight per member), the battlefield's background colours (one of the
/// palettes of BKGNDPAL.DAT) at 164..234, and a grey at 13.
/// </summary>
// orig: battle_run R002+0x0000 (0E76:0000) draft (the palette block before combat_main)
// C: the original first reads the current palette, fills colours 16..255 with 0xFF ("leave unchanged" for the
// palette setter: 0xFF is outside the 6-bit DAC range) and then copies the parts below; Put skips such entries.
// DIVERGENCE: the original builds the palette only when DS:9069 == 0 (not in a re-entered battle); callers decide.
public static class BattlePalette
{
    public const int PartyColorsStart = 80;
    public const int BackgroundStart = 164;
    private const int BackgroundBytes = 0xD5;

    /// <summary>The colours to set: index → colour (other indexes keep their colour).</summary>
    public static IReadOnlyDictionary<int, Rgb24> Colors(byte[] commonColors, byte[] backgroundPalettes, int background,
        ReadOnlySpan<byte> partyColors)
    {
        var colors = new Dictionary<int, Rgb24> { [13] = new Rgb24(Expand(0x31), Expand(0x31), Expand(0x31)) };
        Put(colors, 16, commonColors.AsSpan(0, 0x30));
        Put(colors, 120, commonColors.AsSpan(0x30, 0x84));
        Put(colors, 243, commonColors.AsSpan(0xB4, 0x24));
        Put(colors, PartyColorsStart, partyColors[..Math.Min(0x78, partyColors.Length)]);
        var start = BackgroundBytes * background;
        if (start + BackgroundBytes <= backgroundPalettes.Length)
        {
            Put(colors, BackgroundStart, backgroundPalettes.AsSpan(start, BackgroundBytes));
        }
        return colors;
    }

    /// <summary>A 6-bit VGA DAC channel as an 8-bit one.</summary>
    public static byte Expand(byte value) => (byte)((value & 0x3F) << 2 | (value & 0x3F) >> 4);

    private static void Put(Dictionary<int, Rgb24> colors, int first, ReadOnlySpan<byte> rgb)
    {
        for (var i = 0; i + 2 < rgb.Length; i += 3)
        {
            if (rgb[i] <= 0x3F)
            {
                colors[first + i / 3] = new Rgb24(Expand(rgb[i]), Expand(rgb[i + 1]), Expand(rgb[i + 2]));
            }
        }
    }
}
