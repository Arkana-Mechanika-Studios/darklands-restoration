using Darklands.Content;
using Darklands.Content.Formats.Graphics;

namespace Darklands.Presentation.Graphics;

/// <summary>The illuminated capitals that open card texts: 26 letters of 20×20 cut from ILLMCAPS.PIC.</summary>
// orig: root 03A1:000A matched: pics\Illmcaps.pic is drawn to page 1 (root 0617:0230), then for i in 0..25 `DS:A6E0[i] = gfx-rect-save(1, i % 15 * 21, i / 15 * 21, 20, 20)` (the letters sit on a 21-pixel grid, 15 to a row); text-paint-wrapped root 0265:0134 draws DS:A65E[letter] for a first letter 'A'..'Z'; tests: none (needs the game files)
// DIVERGENCE: the original cuts the 26 pictures once at start-up into DOS memory; here they are cut on first use. A letter outside A..Z, or a missing ILLMCAPS.PIC, draws the plain character (the original has no such case: a first byte outside '@'..'[' is not a drop cap).
public static class DropCaps
{
    private const int Size = 20;
    private const int Pitch = 21;
    private const int PerRow = 15;

    private static IndexedImage[]? _letters;

    public static IndexedImage? Get(GameAssets assets, byte letter)
    {
        if (letter is < (byte)'A' or > (byte)'Z' || !assets.PictureExists("Illmcaps"))
        {
            return null;
        }
        _letters ??= Cut(assets.Picture("Illmcaps").Image);
        return _letters[letter - 'A'];
    }

    private static IndexedImage[] Cut(IndexedImage sheet)
    {
        var letters = new IndexedImage[26];
        for (var i = 0; i < letters.Length; i++)
        {
            var x0 = i % PerRow * Pitch;
            var y0 = i / PerRow * Pitch;
            var pixels = new byte[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var sx = x0 + x;
                    var sy = y0 + y;
                    pixels[y * Size + x] = sx < sheet.Width && sy < sheet.Height ? sheet.Pixels[sy * sheet.Width + sx] : (byte)0;
                }
            }
            letters[i] = new IndexedImage(Size, Size, pixels, $"dropcap:{(char)('A' + i)}");
        }
        return letters;
    }
}
