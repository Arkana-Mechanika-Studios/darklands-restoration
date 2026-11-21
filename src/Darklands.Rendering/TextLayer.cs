using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// The text layer of a frame (L-6b set 2): the glyphs of scalable fonts the frame shows (their 1x pixels carry <see cref="TextGlyphs"/>
/// ids) drawn again at the output's resolution, as one picture over the scaled frame. Each output pixel of a glyph's box takes the
/// text's colour from the glyph's nearest 1x pixel (what the frame shows there, after the game's xor, recolours and fades) and its
/// background from the nearest pixel of the row that is not the glyph's, blended by the glyph's coverage; so the 1x pixels are hidden
/// under it. A frame without such glyphs gives no picture.
/// </summary>
// Not in the original: the 1.1 font packs. It reads the frame only.
public sealed class TextLayer
{
    private RgbaImage? _image;
    private long _signature;

    /// <summary>The layer's draw item for <paramref name="screen"/> at <paramref name="output"/>, or null when the frame shows no such glyph.</summary>
    public DrawItem? Item(Screen screen, DisplayRectangle output)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var glyphs = new Dictionary<int, List<int>>();
        var signature = 17L * 31 + output.Width * 7919 + output.Height;
        for (var i = 0; i < screen.Texels.Length; i++)
        {
            var texel = screen.Texels[i];
            if (texel <= -2 && screen.Owners[i] == 0)
            {
                if (!glyphs.TryGetValue(texel, out var pixels))
                {
                    glyphs[texel] = pixels = [];
                }
                pixels.Add(i);
                signature = signature * 31 + texel * 131 + screen.Palette[screen.Pixels[i]];
            }
        }
        if (glyphs.Count == 0 || output.Width <= 0 || output.Height <= 0)
        {
            _image = null;
            return null;
        }
        // the backgrounds the glyphs are drawn over count too (a glyph's box takes them)
        foreach (var (texel, pixels) in glyphs)
        {
            if (TextGlyphs.Of(texel) is { } g)
            {
                for (var y = g.Y; y < g.Y + g.Font.Height; y++)
                {
                    for (var x = g.X - 1; x <= g.X + g.Font.Advance(g.Code); x++)
                    {
                        if ((uint)x < Screen.Width && (uint)y < Screen.Height)
                        {
                            signature = signature * 31 + screen.Palette[screen.Pixels[y * Screen.Width + x]];
                        }
                    }
                }
            }
        }
        if (_image is not null && _image.Width == output.Width && _image.Height == output.Height && signature == _signature)
        {
            return new DrawItem(_image, output, DrawFilter.Nearest, Whole);
        }
        _signature = signature;
        var image = _image is { } kept && kept.Width == output.Width && kept.Height == output.Height ? kept : new RgbaImage(output.Width, output.Height);
        Array.Clear(image.Argb);
        var (sx, sy) = ((double)output.Width / Screen.Width, (double)output.Height / Screen.Height);
        // in the order they were first drawn (a glyph's id is its first drawing's): an embossed heading's face, drawn after its shadow a
        // pixel away, stays over it (the order the frame is read in would put the shadow over the face, a row lower)
        foreach (var (texel, pixels) in glyphs.OrderByDescending(g => g.Key))
        {
            if (TextGlyphs.Of(texel) is { } g && !Hidden(screen, pixels))
            {
                Draw(image, screen, g, texel, pixels, sx, sy);
            }
        }
        image.Changed();
        _image = image;
        return new DrawItem(image, output, DrawFilter.Nearest, Whole);
    }

    // a placement for the whole screen: the host draws the layer as a picture (no filter), not as the game's frame
    private static readonly AssetPlacement Whole = new("text layer", 0, 0, Screen.Width, Screen.Height, false, false, []);

    private static void Draw(RgbaImage image, Screen screen, TextGlyphs.Glyph g, int texel, List<int> pixels, double sx, double sy)
    {
        var (left, top, width, height, coverage) = g.Font.Raster(g.Code, sx, sy);
        var mine = new HashSet<int>(pixels);
        uint Colour(int at) => screen.Palette[screen.Pixels[at]] | 0xFF000000u;
        // the text's colour at a 1x cell: its nearest glyph pixel's
        uint Ink(int cx, int cy)
        {
            var (best, distance) = (pixels[0], int.MaxValue);
            foreach (var at in pixels)
            {
                var d = Math.Abs(at % Screen.Width - cx) + Math.Abs(at / Screen.Width - cy);
                if (d < distance)
                {
                    (best, distance) = (at, d);
                }
            }
            return Colour(best);
        }
        // the background at a 1x cell: the cell itself when it is not the glyph's, else the nearest such pixel of its row (then above / below)
        uint Paper(int cx, int cy)
        {
            for (var reach = 0; reach < 16; reach++)
            {
                foreach (var (x, y) in new[] { (cx - reach, cy), (cx + reach, cy), (cx, cy - reach), (cx, cy + reach) })
                {
                    if ((uint)x < Screen.Width && (uint)y < Screen.Height && !mine.Contains(y * Screen.Width + x) && !IsGlyph(screen, y * Screen.Width + x))
                    {
                        return Colour(y * Screen.Width + x);
                    }
                }
            }
            return 0xFF000000u;
        }
        // the output pixels of the glyph's 1x cells and of its raster's box
        var ox = (int)Math.Floor(g.X * sx);
        var oy = (int)Math.Floor(g.Y * sy);
        var cells = pixels.Select(at => (X: at % Screen.Width, Y: at / Screen.Width)).ToList();
        var x0 = Math.Min(ox + left, cells.Min(c => (int)Math.Floor(c.X * sx)));
        var y0 = Math.Min(oy + top, cells.Min(c => (int)Math.Floor(c.Y * sy)));
        var x1 = Math.Max(ox + left + width, cells.Max(c => (int)Math.Ceiling((c.X + 1) * sx)));
        var y1 = Math.Max(oy + top + height, cells.Max(c => (int)Math.Ceiling((c.Y + 1) * sy)));
        for (var py = Math.Max(0, y0); py < Math.Min(image.Height, y1); py++)
        {
            for (var px = Math.Max(0, x0); px < Math.Min(image.Width, x1); px++)
            {
                var (cx, cy) = ((int)(px / sx), (int)(py / sy));
                var (rx, ry) = (px - ox - left, py - oy - top);
                var cover = rx >= 0 && rx < width && ry >= 0 && ry < height ? coverage[ry * width + rx] : 0;
                var onGlyphCell = mine.Contains(cy * Screen.Width + cx);
                if (cover == 0 && !onGlyphCell)
                {
                    continue;
                }
                var paper = Paper(cx, cy);
                var colour = cover == 0 ? paper : Blend(Ink(cx, cy), paper, cover);
                ref var target = ref image.Argb[py * image.Width + px];
                // where two glyphs' boxes meet, the more inked pixel stays
                if (target == 0 || cover > 0)
                {
                    target = colour;
                }
            }
        }
    }

    // a glyph drawn by xor that does not show (a card's option lines before the picture fades: each pixel's colour is the one its
    //   index without the xor bit has, so the 1x frame shows nothing there): it is not drawn again either
    private static bool Hidden(Screen screen, List<int> pixels) =>
        pixels.All(at => screen.Pixels[at] is var index && index >= 0x80 && screen.Palette[index] == screen.Palette[index ^ 0x80]);

    private static bool IsGlyph(Screen screen, int at) => screen.Texels[at] <= -2 && screen.Owners[at] == 0;

    private static uint Blend(uint ink, uint paper, int cover)
    {
        uint Channel(int shift) => (uint)(((ink >> shift & 0xFF) * cover + (paper >> shift & 0xFF) * (255 - cover) + 127) / 255) << shift;
        return 0xFF000000u | Channel(16) | Channel(8) | Channel(0);
    }
}
