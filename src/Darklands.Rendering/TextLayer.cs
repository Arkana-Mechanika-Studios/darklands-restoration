using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// The text layer of a frame (L-6b set 2): the glyphs of scalable fonts the frame shows (their 1x pixels carry <see cref="TextGlyphs"/>
/// ids) drawn again at the output's resolution, as one picture over the scaled frame. Each output pixel of a glyph's box takes the
/// text's colour from the glyph's nearest 1x pixel (what the frame shows there, after the game's xor, recolours and fades) and its
/// background from the nearest pixel of the row that is not the glyph's, blended by the glyph's coverage; so the 1x pixels are hidden
/// under it. A frame without such glyphs gives no picture.
/// The 1x pixels are also taken out of the frame the host scales (<see cref="Erase"/>: each gets the background the layer gives its
/// cell), so no window pixel the layer leaves clear can show one: a graphics card stretches the frame by each window pixel's centre,
/// the layer's cells are cut by their edges, and the picture filters spread a pixel into its neighbours; at sizes that are not whole
/// multiples of 320x200, or with a filter, thin ticks of the 1x letters showed at the HD letters' edges.
/// A text's shadow (the same text drawn first a pixel away, as the saved-games list's and the party screen's lines: see
/// <see cref="ShadowOf"/>) is drawn as a light emboss: a quarter of a game pixel from the text (at least one output pixel) and half
/// as strong; the frame's own shadow, a whole game pixel away, read at the output's resolution as a second, blurred copy of the text.
/// </summary>
// Not in the original: the 1.1 font packs. It reads the frame only.
public sealed class TextLayer
{
    private RgbaImage? _image;
    private long _signature;
    private readonly List<(int At, uint Colour)> _erase = [];

    /// <summary>
    /// Takes the glyphs the last <see cref="Item"/> drew out of a 320x200 picture of the frame: each 1x pixel gets the background the
    /// layer gives its cell (glyphs that do not show, <see cref="Unseen"/>, keep theirs).
    /// </summary>
    public void Erase(RgbaImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        foreach (var (at, colour) in _erase)
        {
            frame.Argb[at] = colour;
        }
    }

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
            _erase.Clear();
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
        _erase.Clear();
        var image = _image is { } kept && kept.Width == output.Width && kept.Height == output.Height ? kept : new RgbaImage(output.Width, output.Height);
        Array.Clear(image.Argb);
        var (sx, sy) = ((double)output.Width / Screen.Width, (double)output.Height / Screen.Height);
        // in the order they were first drawn (a glyph's id is its first drawing's): an embossed heading's face, drawn after its shadow a
        // pixel away, stays over it (the order the frame is read in would put the shadow over the face, a row lower)
        var drawn = new Dictionary<TextGlyphs.Glyph, int>();
        foreach (var key in glyphs.Keys)
        {
            if (TextGlyphs.Of(key) is { } shown)
            {
                drawn.TryAdd(shown, key);
            }
        }
        foreach (var (texel, pixels) in glyphs.OrderByDescending(g => g.Key))
        {
            if (TextGlyphs.Of(texel) is { } g && !pixels.All(at => Unseen(screen, g, at)))
            {
                Draw(image, screen, g, texel, pixels, sx, sy, _erase, ShadowOf(g, texel, drawn));
            }
        }
        image.Changed();
        _image = image;
        return new DrawItem(image, output, DrawFilter.Nearest, Whole);
    }

    // a placement for the whole screen: the host draws the layer as a picture (no filter), not as the game's frame
    private static readonly AssetPlacement Whole = new("text layer", 0, 0, Screen.Width, Screen.Height, false, false, []);

    // a glyph that is a text's shadow: the same character of the same font drawn a pixel away (across, down or both) before the
    //   text's own glyph (ids are given in the order glyphs are first drawn, so the later drawing has the lower texel); the step
    //   from the text to the shadow, or null
    private static (int Dx, int Dy)? ShadowOf(TextGlyphs.Glyph g, int texel, Dictionary<TextGlyphs.Glyph, int> drawn)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx, dy) != (0, 0) && drawn.TryGetValue(g with { X = g.X - dx, Y = g.Y - dy }, out var face) && face < texel)
                {
                    return (dx, dy);
                }
            }
        }
        return null;
    }

    private static void Draw(RgbaImage image, Screen screen, TextGlyphs.Glyph g, int texel, List<int> pixels, double sx, double sy,
        List<(int At, uint Colour)> erase, (int Dx, int Dy)? shadow = null)
    {
        var (left, top, width, height, coverage) = g.Font.Raster(g.Code, sx, sy);
        var mine = new HashSet<int>(pixels);
        uint Colour(int at) => screen.Palette[screen.Pixels[at]] | 0xFF000000u;
        // the text's colour at a 1x cell: its nearest glyph pixel's; none when that pixel does not show (Unseen), so the layer shows the
        //   frame there as the frame does
        uint? Ink(int cx, int cy)
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
            return Unseen(screen, g, best) ? null : Colour(best);
        }
        // the background at a 1x cell: the cell itself when it is not the glyph's (or is, but does not show), else the nearest such pixel of
        //   its row (then above / below)
        //   (a pixel an odd step away is of a dither's other phase: the next one along, of the cell's phase, is taken when it is of a
        //   near colour, so a dithered background keeps its pattern instead of a flat patch of one of its two colours beside the
        //   letters; a border or another colour there is not taken)
        uint Paper(int cx, int cy)
        {
            if (mine.Contains(cy * Screen.Width + cx) && Unseen(screen, g, cy * Screen.Width + cx))
            {
                return Colour(cy * Screen.Width + cx);
            }
            bool Free(int x, int y) => (uint)x < Screen.Width && (uint)y < Screen.Height && !mine.Contains(y * Screen.Width + x) && !IsGlyph(screen, y * Screen.Width + x);
            for (var reach = 0; reach < 16; reach++)
            {
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    var (x, y) = (cx + dx * reach, cy + dy * reach);
                    if (Free(x, y))
                    {
                        var nearest = Colour(y * Screen.Width + x);
                        return reach % 2 == 1 && Free(x + dx, y + dy) && Colour((y + dy) * Screen.Width + x + dx) is var phase && Near(phase, nearest)
                            ? phase
                            : nearest;
                    }
                }
            }
            return 0xFF000000u;
        }
        // the output pixels of the glyph's 1x cells and of its raster's box
        var ox = (int)Math.Floor(g.X * sx);
        var oy = (int)Math.Floor(g.Y * sy);
        if (shadow is var (dx, dy))
        {
            // a shadow: a quarter of a game pixel from its text (at least one output pixel), not a whole one
            ox = (int)Math.Floor((g.X - dx) * sx) + dx * Math.Max(1, (int)Math.Round(sx / 4));
            oy = (int)Math.Floor((g.Y - dy) * sy) + dy * Math.Max(1, (int)Math.Round(sy / 4));
        }
        // a shadow's ink half as strong: a light emboss, not a second copy of the text
        var strength = shadow is null ? 255 : 128;
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
                ref var target = ref image.Argb[py * image.Width + px];
                // over what is drawn already (a neighbour's box, a shadow under the text) the ink is blended with that, not with the paper
                var under = target == 0 ? paper : target;
                var colour = cover == 0 || Ink(cx, cy) is not { } ink ? paper : Blend(ink, under, cover * strength / 255);
                // where two glyphs' boxes meet, the more inked pixel stays
                if (target == 0 || cover > 0)
                {
                    target = colour;
                }
            }
        }
        // the frame under the layer without the glyph: its 1x pixels in the background the layer gives their cells
        foreach (var at in pixels)
        {
            if (!Unseen(screen, g, at))
            {
                erase.Add((at, Paper(at % Screen.Width, at / Screen.Width)));
            }
        }
    }

    // a pixel of a glyph drawn by xor that does not show: its colour is the one its index had before the xor (a card's option lines
    //   before the picture fades: the picture repeats colours 0x20..0x7F in 0xA0..0xFF, so a line xor-ed by 0x80 over either half
    //   changes no colour there; over the picture's own 0x00..0x1F it shows). A glyph none of whose pixels show is not drawn again;
    //   one partly over such colours is drawn where it shows only (drawing all of it in the frame's colours left faint outlines of the
    //   option lines over a varied picture before the fade)
    private static bool Unseen(Screen screen, TextGlyphs.Glyph g, int at) =>
        g.Xor != 0 && screen.Palette[screen.Pixels[at]] == screen.Palette[screen.Pixels[at] ^ g.Xor];

    // two colours of one dither: no channel more than 0x30 apart
    private static bool Near(uint a, uint b) =>
        Math.Abs((int)(a >> 16 & 0xFF) - (int)(b >> 16 & 0xFF)) <= 0x30 && Math.Abs((int)(a >> 8 & 0xFF) - (int)(b >> 8 & 0xFF)) <= 0x30
        && Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) <= 0x30;

    private static bool IsGlyph(Screen screen, int at) => screen.Texels[at] <= -2 && screen.Owners[at] == 0;

    private static uint Blend(uint ink, uint paper, int cover)
    {
        uint Channel(int shift) => (uint)(((ink >> shift & 0xFF) * cover + (paper >> shift & 0xFF) * (255 - cover) + 127) / 255) << shift;
        return 0xFF000000u | Channel(16) | Channel(8) | Channel(0);
    }
}
