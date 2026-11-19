namespace Darklands.Platform;

/// <summary>
/// A font for the Unicode text path (L-6b): metrics and glyphs by code (a Unicode code point; control and colour codes have none).
/// The game's own fonts on the byte path do not go through it; a pack's font (<see cref="BitmapFont"/>) and the original fonts drawing
/// a translation do.
/// </summary>
// Not in the original: the 1.1 translations and font packs (docs/LOCALISATION.md).
public interface IFont
{
    /// <summary>The glyphs' height in pixels.</summary>
    int Height { get; }

    /// <summary>A line's height: the glyphs' height and the line spacing.</summary>
    int LineHeight { get; }

    /// <summary>Whether the font has a glyph for <paramref name="code"/>.</summary>
    bool Has(int code);

    /// <summary>How far <paramref name="code"/> moves the pen (its width and the spacing; 0 for a code the font has no glyph for).</summary>
    int Advance(int code);

    /// <summary>Whether pixel (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="code"/>'s glyph is set.</summary>
    bool Pixel(int code, int x, int y);
}
