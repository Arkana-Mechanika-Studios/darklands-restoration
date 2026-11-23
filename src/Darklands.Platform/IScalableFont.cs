namespace Darklands.Platform;

/// <summary>
/// A font that can also be drawn at the window's resolution (a pack's TrueType / OpenType font, L-6b set 2). Its metrics and its
/// 1x glyphs (<see cref="IFont"/>) are what the game's 320x200 frame gets; <see cref="Raster"/> is what the host draws over the
/// scaled picture (<see cref="TextGlyphs"/>).
/// </summary>
// Not in the original: the 1.1 font packs.
public interface IScalableFont : IFont
{
    /// <summary>
    /// <paramref name="code"/>'s glyph at <paramref name="scaleX"/> by <paramref name="scaleY"/> output pixels per game pixel: its
    /// box (left and top from the 1x glyph's top-left corner, in output pixels) and its coverage, one byte (0..255) a pixel, row by row.
    /// </summary>
    (int Left, int Top, int Width, int Height, byte[] Coverage) Raster(int code, double scaleX, double scaleY);
}

/// <summary>
/// The glyphs of scalable fonts drawn on the game's frames: each drawing (font, code, 1x position) gets an id, which the glyph's 1x
/// pixels carry in <see cref="Screen.Texels"/> (owner 0) as <see cref="TexelOf"/>; so a glyph drawn over, cleared or copied with a
/// saved region is gone or back with its pixels, and the host draws at the window's resolution exactly the glyphs the frame shows.
/// </summary>
// Not in the original: the 1.1 font packs (L-6b set 2). It never changes a pixel of the frame.
public static class TextGlyphs
{
    /// <summary>A drawing: the font, the code, the 1x glyph's top-left corner, and the colour it was xor-ed in (0: drawn otherwise).</summary>
    public readonly record struct Glyph(IScalableFont Font, int Code, int X, int Y, int Xor = 0);

    private const int Capacity = 1 << 20;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<Glyph, int> Ids = [];
    private static readonly List<Glyph> Glyphs = [];

    /// <summary>The id of a drawing (<paramref name="xor"/>: the colour it is xor-ed in, 0 when it is drawn otherwise).</summary>
    public static int Id(IScalableFont font, int code, int x, int y, int xor = 0)
    {
        var glyph = new Glyph(font, code, x, y, xor);
        lock (Gate)
        {
            if (Ids.TryGetValue(glyph, out var id))
            {
                return id;
            }
            if (Glyphs.Count >= Capacity)
            {
                // a long session's table: begun again (the glyphs on the screen are drawn again with new ids)
                Ids.Clear();
                Glyphs.Clear();
            }
            id = Glyphs.Count;
            Glyphs.Add(glyph);
            Ids[glyph] = id;
            return id;
        }
    }

    /// <summary>The texel value of a glyph's 1x pixels (below <see cref="ScreenTexel.Unknown"/>: no asset's texel is negative).</summary>
    public static int TexelOf(int id) => -2 - id;

    /// <summary>The drawing a texel names (owner 0), or null.</summary>
    public static Glyph? Of(int texel)
    {
        if (texel > -2)
        {
            return null;
        }
        lock (Gate)
        {
            var id = -2 - texel;
            return id < Glyphs.Count ? Glyphs[id] : null;
        }
    }
}
