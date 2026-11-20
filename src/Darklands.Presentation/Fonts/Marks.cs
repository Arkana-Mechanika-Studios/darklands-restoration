namespace Darklands.Presentation.Fonts;

/// <summary>
/// The diacritics the font-pack maker puts on a base letter (PixelFontBuilder): each in two rows where the letter leaves room above it
/// (the x-height's letters of the 7 and 8 pixel fonts), else in one row; a letter with no room above (a capital) gives up a row of its
/// own first, as the game's Ä Ö Ü do. Marks below (cedilla, ogonek) take the descender's row.
/// </summary>
// Not in the original: the 1.1 font packs. Our drawings, in the game's sizes.
internal static class Marks
{
    private sealed record Mark(string[] Two, string[] One, bool Below, bool AtRight = false);

    private static readonly Dictionary<char, Mark> All = new()
    {
        ['̀'] = new(["#.", ".#"], ["#."], false),          // grave
        ['́'] = new([".#", "#."], [".#"], false),          // acute (also the Greek tonos)
        ['̂'] = new([".#.", "#.#"], ["###"], false),       // circumflex
        ['̃'] = new([".#.#", "#.#."], ["####"], false),    // tilde
        ['̄'] = new(["###"], ["###"], false),              // macron
        ['̆'] = new(["#..#", ".##."], ["##"], false),      // breve
        ['̇'] = new(["#"], ["#"], false),                  // dot
        ['̈'] = new(["#.#"], ["#.#"], false),              // diaeresis
        ['̊'] = new(["##", "##"], ["##"], false),          // ring
        ['̋'] = new([".#.#", "#.#."], [".#.#"], false),    // double acute
        ['̌'] = new(["#.#", ".#."], [".#."], false),       // caron
        ['̧'] = new([".#", "#."], ["#"], true),            // cedilla
        ['̨'] = new(["#.", ".#"], ["#"], true, AtRight: true), // ogonek
    };

    /// <summary><paramref name="glyph"/> with <paramref name="mark"/> (a combining character) on it; null for a mark not drawn here.</summary>
    public static PixelGlyph? Compose(PixelGlyph glyph, char mark, char baseLetter, PixelFontBuilder.Metrics m)
    {
        if (!All.TryGetValue(mark, out var shape))
        {
            return null;
        }
        var g = glyph.Copy();
        var (top, bottom) = g.InkRows();
        if (top < 0)
        {
            return null;
        }
        if (!shape.Below && baseLetter is 'i' or 'j')
        {
            // i and j: their own dot gives way to the accent
            for (var y = 0; y < m.XTop; y++)
            {
                for (var x = 0; x < g.Width; x++)
                {
                    g[x, y] = false;
                }
            }
            (top, bottom) = g.InkRows();
        }
        var (left, right) = g.InkColumns();
        if (shape.Below)
        {
            var room = g.Height - 1 - bottom;
            var rows = room >= 2 ? shape.Two : shape.One;
            var y0 = room >= 1 ? bottom + 1 : bottom;
            var anchor = shape.AtRight ? right - (rows[0].Length - 1) / 2 : (left + right + 1) / 2;
            return Stamp(g, rows, anchor, y0);
        }
        var free = top;
        string[] above;
        int start;
        if (free >= 2)
        {
            (above, start) = (shape.Two, top - 2);
        }
        else if (free >= 1)
        {
            (above, start) = (shape.One, top - 1);
        }
        else
        {
            // no room: the letter gives up a row (the one most like its neighbour), and the mark takes the top row
            var rows = Enumerable.Range(top, bottom - top + 1).Select(y => Enumerable.Range(0, g.Width).Select(x => g[x, y]).ToArray()).ToList();
            var squeezed = PixelFontBuilder.Squeeze(rows, rows.Count - 1);
            for (var y = top; y <= bottom; y++)
            {
                for (var x = 0; x < g.Width; x++)
                {
                    g[x, y] = false;
                }
            }
            for (var r = 0; r < squeezed.Count; r++)
            {
                for (var x = 0; x < g.Width; x++)
                {
                    g[x, top + 1 + r] = squeezed[r][x];
                }
            }
            (above, start) = (shape.One, top);
        }
        return Stamp(g, above, (left + right + 1) / 2, start);
    }

    // the mark's rows put down with their middle column on anchor (the glyph widened when the mark is wider than it)
    private static PixelGlyph Stamp(PixelGlyph g, string[] rows, int anchor, int y0)
    {
        var width = rows.Max(r => r.Length);
        var x0 = anchor - (width - 1) / 2;
        var shift = x0 < 0 ? -x0 : 0;
        var needed = Math.Max(g.Width + shift, x0 + shift + width);
        var target = needed > g.Width || shift > 0 ? g.Widened(needed, shift) : g;
        for (var r = 0; r < rows.Length; r++)
        {
            for (var x = 0; x < rows[r].Length; x++)
            {
                if (rows[r][x] == '#')
                {
                    target[x0 + shift + x, y0 + r] = true;
                }
            }
        }
        return target;
    }
}
