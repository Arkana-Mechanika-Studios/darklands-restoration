namespace Darklands.Simulation.Overland;

/// <summary>
/// The season of the travel map: its background colour and the palette changes that turn the
/// trees and fields autumn-coloured or snowy as the months pass.
/// </summary>
// orig: R030+0x2DD6 (1462:2DD6) draft (bytes match; 16 of the file's 22 functions do); tests: OverlandTests
public static class SeasonPalette
{
    /// <summary>A palette seen as the original's 768 bytes (red, green and blue of colour 0, 1, ... in 6-bit units).</summary>
    public interface IPaletteBytes
    {
        int this[int offset] { get; set; }
    }

    /// <summary>The map's background colour (DS:2860) and whether the palette changed in a way that needs a redraw.</summary>
    public readonly record struct Result(int Background, bool Changed);

    /// <summary>
    /// Sets the season for <paramref name="month"/> (0 January) and <paramref name="day"/> of the month: winter in
    /// December to March (snow colour 15), a slow change through autumn from September, a green map (0x68) in spring
    /// and summer. The palette bytes are those of MAPICONS.PIC; the changes are written over them.
    /// </summary>
    // C: the cases are in the source's order; the offsets are byte offsets into the palette (colour = offset / 3, 0 red, 1 green, 2 blue) and the values 6-bit.
    // C: `changed` is set when the first sentinel byte of a block still has the pre-season value, so the block was not applied before; the caller redraws the map when `changed` or the background differs from the last call (old != v || changed).
    // C: month 3 (April) writes its colours and then falls out of the switch to the green background; month 8 (September) never sets `changed`, it only moves the background (6 after day 6); month 10 (November) has three stages by day (> 2, > 3, > 4).
    // C: months 4..7 (May to August) change nothing and give 0x68: the palette is restored only when the picture is loaded again (the caller loads MAPICONS.PIC before every call after a sheet or a start).
    public static Result Apply(IPaletteBytes p, int month, int day)
    {
        var changed = false;
        int v;
        switch (month)
        {
            case 0 or 1 or 2 or 11:
                if (p[0xDE] != 0x2E)
                {
                    changed = true;
                }
                Winter(p);
                v = 15;
                break;
            case 3:
                if (p[0x135] != 0x3F)
                {
                    changed = true;
                }
                Spring(p);
                v = 0x68;
                break;
            case 8:
                v = 0x68;
                if (day > 2)
                {
                    p[0x135] = 0x3F;
                }
                p[0x136] = 0x23;
                p[0x137] = 9;
                if (day > 4)
                {
                    p[0x144] = 0x38;
                }
                p[0x145] = 0x15;
                p[0x146] = 0;
                if (day > 6)
                {
                    v = 6;
                }
                break;
            case 9:
                if (p[0x135] != 0x3F)
                {
                    changed = true;
                }
                Spring(p);
                v = 6;
                break;
            case 10:
                v = 6;
                Set(p, 0xDE, 0x2E, 0xE0, 0x37, 0xE7, 0x37, 0xDF, 0x34, 0xE8, 0x34, 0xE9, 0x3C, 0xEA, 0x32, 0xEB, 0x30, 0xEC, 0x38);
                if (day > 2)
                {
                    if (p[0xED] != 0x2E)
                    {
                        changed = true;
                    }
                    Set(p, 0xEF, 0x34, 0xF0, 0x2A, 0xF1, 0x28, 0xF2, 0x31, 0xED, 0x2E, 0x10B, 0x2E, 0xEE, 0x2C, 0x10C, 0x2C,
                        0x10D, 0x32, 0x111, 0x38, 0x112, 0x33, 0x113, 0x3F);
                }
                if (day > 3)
                {
                    if (p[0x13B] != 0x38)
                    {
                        changed = true;
                    }
                    Set(p, 0x142, 0x3C, 0x144, 0x3C, 0x14A, 0x3C, 0x13B, 0x38, 0x145, 0x38, 0x14B, 0x38, 0x1F2, 0x38, 0x1F3, 0x38,
                        0x216, 0x38, 0x13C, 0x33, 0x217, 0x33, 0x141, 0x3B, 0x22B, 0x3B, 0x146, 0x3A, 0x14C, 0x3A, 0x22C, 0x3A,
                        0x13D, 0x3F, 0x143, 0x3F, 0x1F4, 0x3F, 0x218, 0x3F, 0x22D, 0x3F, 0x2BE, 0x25, 0x2BF, 0x2F, 0x2C0, 0x37);
                }
                if (day > 4)
                {
                    v = 15;
                }
                break;
            default:
                v = 0x68;
                break;
        }
        return new Result(v, changed);
    }

    // orig: the 44 stores of the first case of 2dd6, in the C's order
    private static void Winter(IPaletteBytes p) =>
        Set(p, 0xEB, 0x30, 0xDF, 0x34, 0xE8, 0x34, 0xEF, 0x34, 0xF0, 0x2A, 0xF1, 0x28, 0xF2, 0x31, 0xDE, 0x2E, 0xED, 0x2E,
            0x10B, 0x2E, 0xEE, 0x2C, 0x10C, 0x2C, 0xEA, 0x32, 0x10D, 0x32, 0xE9, 0x3C, 0x142, 0x3C, 0x144, 0x3C, 0x14A, 0x3C,
            0xEC, 0x38, 0x111, 0x38, 0x13B, 0x38, 0x145, 0x38, 0x14B, 0x38, 0x1F2, 0x38, 0x1F3, 0x38, 0x216, 0x38,
            0x112, 0x33, 0x13C, 0x33, 0x217, 0x33, 0x141, 0x3B, 0x22B, 0x3B, 0x146, 0x3A, 0x14C, 0x3A, 0x22C, 0x3A,
            0x113, 0x3F, 0x13D, 0x3F, 0x143, 0x3F, 0x1F4, 0x3F, 0x218, 0x3F, 0x22D, 0x3F, 0x2BE, 0x25, 0x2BF, 0x2F,
            0xE0, 0x37, 0xE7, 0x37, 0x2C0, 0x37);

    // orig: the six stores shared by April (3) and October (9): the first autumn/spring tints
    private static void Spring(IPaletteBytes p) => Set(p, 0x135, 0x3F, 0x136, 0x23, 0x137, 9, 0x144, 0x38, 0x145, 0x15, 0x146, 0);

    private static void Set(IPaletteBytes p, params int[] offsetValuePairs)
    {
        for (var i = 0; i < offsetValuePairs.Length; i += 2)
        {
            p[offsetValuePairs[i]] = offsetValuePairs[i + 1];
        }
    }
}
