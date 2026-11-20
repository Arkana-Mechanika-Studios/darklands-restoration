using System.Globalization;
using System.Text;
using Darklands.Content.Formats.Graphics;

namespace Darklands.Presentation.Fonts;

/// <summary>A pixel glyph: its width, height and pixels (row by row).</summary>
public sealed class PixelGlyph
{
    public PixelGlyph(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new bool[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public bool[] Pixels { get; }

    public bool this[int x, int y]
    {
        get => x >= 0 && x < Width && y >= 0 && y < Height && Pixels[y * Width + x];
        set
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                Pixels[y * Width + x] = value;
            }
        }
    }

    public PixelGlyph Copy()
    {
        var copy = new PixelGlyph(Width, Height);
        Pixels.CopyTo(copy.Pixels, 0);
        return copy;
    }

    /// <summary>The first and last rows with ink (-1, -1: none).</summary>
    public (int Top, int Bottom) InkRows()
    {
        var (top, bottom) = (-1, -1);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (this[x, y])
                {
                    top = top < 0 ? y : top;
                    bottom = y;
                    break;
                }
            }
        }
        return (top, bottom);
    }

    /// <summary>The first and last columns with ink (-1, -1: none).</summary>
    public (int Left, int Right) InkColumns()
    {
        var (left, right) = (-1, -1);
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                if (this[x, y])
                {
                    left = left < 0 ? x : left;
                    right = x;
                    break;
                }
            }
        }
        return (left, right);
    }

    /// <summary>The glyph widened to <paramref name="width"/>, its pixels moved right by <paramref name="shift"/>.</summary>
    public PixelGlyph Widened(int width, int shift)
    {
        var wide = new PixelGlyph(width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                wide[x + shift, y] = this[x, y];
            }
        }
        return wide;
    }
}

/// <summary>Which scripts a made font pack covers beyond the game's own letters.</summary>
[Flags]
public enum FontScripts
{
    Latin = 1,
    Greek = 2,
    Cyrillic = 4,
}

/// <summary>
/// Makes a pixel font in the game's own style for other languages (docs/LOCALISATION.md, font packs): every glyph of one of the
/// player's game fonts kept, accented Latin letters (Latin-1 and Latin Extended-A) made by putting the diacritic onto the font's own
/// base letter, the Greek and Cyrillic letters the font's Latin glyphs already draw taken from them (А = A, Ο = O ...), and the others
/// drawn from the project's own pixel designs (Resources/PixelGlyphs.txt) fitted to the font's rows. Where a capital has no room above
/// it for an accent, the capital gives up a row, as the game's own Ä Ö Ü do.
/// </summary>
// Not in the original: the 1.1 font packs. The game's glyphs are read from the player's files here and never stored in the program.
public static class PixelFontBuilder
{
    /// <summary>The glyphs of <paramref name="font"/> and the made letters of <paramref name="scripts"/>, by code point.</summary>
    public static IReadOnlyDictionary<int, PixelGlyph> Build(FntFont font, FontScripts scripts)
    {
        ArgumentNullException.ThrowIfNull(font);
        var glyphs = new Dictionary<int, PixelGlyph>();
        // the game's own glyphs, as the letters GameTextCodec gives their bytes (0x5B is Ä, 0x7B is ö ...)
        for (var b = 0x1F; b < 0x80; b++)
        {
            if (!font.Contains((byte)b) || (b < 0x20 && b != 0x1F))
            {
                continue;
            }
            var c = Darklands.Core.GameTextCodec.ToChar((byte)b);
            glyphs[c] = Read(font, (byte)b);
        }
        var metrics = Metrics.Of(glyphs, font.CharacterHeight);
        if (scripts.HasFlag(FontScripts.Greek) || scripts.HasFlag(FontScripts.Cyrillic))
        {
            foreach (var (letter, latin) in LookAlikes)
            {
                var isGreek = letter is >= 'Ͱ' and <= 'Ͽ';
                if (((isGreek && scripts.HasFlag(FontScripts.Greek)) || (!isGreek && scripts.HasFlag(FontScripts.Cyrillic))) && glyphs.TryGetValue(latin, out var g))
                {
                    glyphs.TryAdd(letter, g.Copy());
                }
            }
            if (scripts.HasFlag(FontScripts.Greek) && Dotless(glyphs.GetValueOrDefault('i'), metrics) is { } iota)
            {
                // ι is the dotless i of the font
                glyphs.TryAdd('ι', iota);
            }
            foreach (var (letter, design) in Designs.Value)
            {
                var isGreek = letter is >= 'Ͱ' and <= 'Ͽ';
                var isCyrillic = letter is >= 'Ѐ' and <= 'ӿ';
                if ((isGreek && scripts.HasFlag(FontScripts.Greek)) || (isCyrillic && scripts.HasFlag(FontScripts.Cyrillic)))
                {
                    glyphs.TryAdd(letter, Fit(design, metrics));
                }
            }
        }
        if (scripts.HasFlag(FontScripts.Latin))
        {
            foreach (var (letter, design) in Designs.Value.Where(d => d.Key < 0x0370))
            {
                glyphs.TryAdd(letter, Fit(design, metrics));
            }
            AddSpecials(glyphs, metrics);
        }
        // the accented letters: the base letter and its marks (Unicode decomposition)
        foreach (var range in Ranges(scripts))
        {
            for (var code = range.First; code <= range.Last; code++)
            {
                if (glyphs.ContainsKey(code) || code is >= 0xD800 and <= 0xDFFF)
                {
                    continue;
                }
                var decomposed = ((char)code).ToString().Normalize(NormalizationForm.FormD);
                if (decomposed.Length < 2 || !glyphs.TryGetValue(decomposed[0], out var baseGlyph))
                {
                    continue;
                }
                var glyph = baseGlyph.Copy();
                var ok = true;
                foreach (var mark in decomposed[1..])
                {
                    if (Marks.Compose(glyph, mark, decomposed[0], metrics) is { } marked)
                    {
                        glyph = marked;
                    }
                    else
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    glyphs[code] = glyph;
                }
            }
        }
        return glyphs;
    }

    private static IEnumerable<(int First, int Last)> Ranges(FontScripts scripts)
    {
        if (scripts.HasFlag(FontScripts.Latin))
        {
            yield return (0x00C0, 0x017F);
        }
        if (scripts.HasFlag(FontScripts.Greek))
        {
            yield return (0x0386, 0x03CE);
        }
        if (scripts.HasFlag(FontScripts.Cyrillic))
        {
            yield return (0x0400, 0x045F);
        }
    }

    private static PixelGlyph Read(FntFont font, byte b)
    {
        var width = Math.Max(1, (int)(font.FixedWidth != 0 ? font.FixedWidth : font.GetCharacterWidth(b)));
        var glyph = new PixelGlyph(width, font.CharacterHeight);
        for (var y = 0; y < font.CharacterHeight; y++)
        {
            var row = font.GetGlyphRow(b, y);
            var bits = row[0] << 8 | (row.Length > 1 ? row[1] : 0);
            for (var x = 0; x < width && x < 16; x++)
            {
                glyph[x, y] = (bits & (0x8000 >> x)) != 0;
            }
        }
        return glyph;
    }

    /// <summary>Where a font's letters sit: the capitals' top and baseline, the x-height's top, the descender's last row.</summary>
    internal readonly record struct Metrics(int Height, int CapTop, int Baseline, int XTop, int Descender)
    {
        public static Metrics Of(IReadOnlyDictionary<int, PixelGlyph> glyphs, int height)
        {
            int Top(char c, int fallback) => glyphs.TryGetValue(c, out var g) && g.InkRows().Top >= 0 ? g.InkRows().Top : fallback;
            int Bottom(char c, int fallback) => glyphs.TryGetValue(c, out var g) && g.InkRows().Bottom >= 0 ? g.InkRows().Bottom : fallback;
            var baseline = Bottom('H', height - 2);
            var descender = new[] { 'g', 'j', 'p', 'q', 'y' }.Max(c => Bottom(c, baseline));
            return new Metrics(height, Top('H', 0), baseline, Top('x', 1), Math.Max(baseline, descender));
        }

        public int CapRows => Baseline - CapTop + 1;

        public int XRows => Baseline - XTop + 1;
    }

    // a design (6 rows lower case, 7 rows a capital) fitted to the font's rows
    private static PixelGlyph Fit(Design design, Metrics m)
    {
        var glyph = new PixelGlyph(design.Width, m.Height);
        if (design.Upper)
        {
            var rows = Squeeze(design.Rows[..6], m.CapRows);
            for (var r = 0; r < rows.Count; r++)
            {
                Copy(rows[r], glyph, m.CapTop + r);
            }
            Outward(design.Rows[6], design.Rows[5], glyph, m.Baseline + 1, m.Descender, down: true);
        }
        else
        {
            var x = Squeeze(design.Rows[1..5], m.XRows);
            for (var r = 0; r < x.Count; r++)
            {
                Copy(x[r], glyph, m.XTop + r);
            }
            Outward(design.Rows[0], x[0], glyph, m.XTop - 1, 0, down: false);
            Outward(design.Rows[5], x[^1], glyph, m.Baseline + 1, m.Descender, down: true);
        }
        return glyph;
    }

    // an ascender (or descender) row next to the letter, and where the font has more such rows, only its strokes that go on from the
    //   letter's own row continue into them (a stem lengthens; a bar stays one row)
    private static void Outward(bool[] row, bool[] letter, PixelGlyph glyph, int first, int last, bool down)
    {
        if (down ? first > last : first < last)
        {
            return;
        }
        Copy(row, glyph, first);
        var stems = row.Select((on, x) => on && x < letter.Length && letter[x]).ToArray();
        for (var y = down ? first + 1 : first - 1; down ? y <= last : y >= last; y += down ? 1 : -1)
        {
            Copy(stems, glyph, y);
        }
    }

    private static void Copy(bool[] row, PixelGlyph glyph, int y)
    {
        for (var x = 0; x < row.Length; x++)
        {
            glyph[x, y] = row[x];
        }
    }

    /// <summary>
    /// <paramref name="rows"/> made <paramref name="count"/> rows: a row most like its neighbour given up while there are too many, the
    /// middle row doubled while too few.
    /// </summary>
    internal static List<bool[]> Squeeze(IReadOnlyList<bool[]> rows, int count)
    {
        var list = rows.ToList();
        while (list.Count > count && list.Count > 1)
        {
            var (best, cost) = (list.Count / 2, int.MaxValue);
            for (var r = 1; r < list.Count - 1; r++)
            {
                var d = Math.Min(Distance(list[r], list[r - 1]), Distance(list[r], list[r + 1]));
                if (d < cost)
                {
                    (best, cost) = (r, d);
                }
            }
            list.RemoveAt(best);
        }
        while (list.Count < count)
        {
            list.Insert(list.Count / 2, (bool[])list[list.Count / 2].Clone());
        }
        return list;
    }

    private static int Distance(bool[] a, bool[] b) => a.Zip(b).Count(p => p.First != p.Second);

    // ---------------------------------------------------------------- the letters made from others
    private static void AddSpecials(Dictionary<int, PixelGlyph> glyphs, Metrics m)
    {
        PixelGlyph? Get(char c) => glyphs.GetValueOrDefault(c);
        void Add(int code, PixelGlyph? glyph)
        {
            if (glyph is not null)
            {
                glyphs.TryAdd(code, glyph);
            }
        }
        // ligatures: the two letters side by side, the second over the first's last column
        Add('Æ', Join(Get('A'), Get('E')));
        Add('æ', Join(Get('a'), Get('e')));
        Add('Œ', Join(Get('O'), Get('E')));
        Add('œ', Join(Get('o'), Get('e')));
        // strokes
        Add('ł', Stroke(Get('l'), m, diagonal: true));
        Add('Ł', Stroke(Get('L'), m, diagonal: true));
        Add('ø', Slash(Get('o')));
        Add('Ø', Slash(Get('O')));
        Add('đ', Bar(Get('d'), m.XTop - 1, right: true));
        Add('Đ', Bar(Get('D'), (m.CapTop + m.Baseline) / 2, right: false));
        Add('Ð', glyphs.GetValueOrDefault('Đ')?.Copy());
        Add('ð', glyphs.GetValueOrDefault('đ')?.Copy());
        Add('ħ', Bar(Get('h'), m.XTop - 1, right: false));
        Add('Ħ', Bar(Get('H'), m.CapTop + 1, right: false));
        Add('ı', Dotless(Get('i'), m));
        // punctuation of the languages: mirrored, doubled or widened from the font's own
        Add('¿', Turned(Get('?'), m));
        Add('¡', Turned(Get('!'), m));
        Add('«', Join(Get('<'), Get('<'), overlap: 2));
        Add('»', Join(Get('>'), Get('>'), overlap: 2));
        Add('…', Join(Join(Get('.'), Get('.'), overlap: 0), Get('.'), overlap: 0));
        Add('–', Get('-')?.Copy());
        Add('—', Join(Get('-'), Get('-'), overlap: 1));
        Add('‘', Get('\'')?.Copy());
        Add('’', Get('\'')?.Copy());
        Add('‚', Get(',')?.Copy());
        Add('“', Get('"')?.Copy());
        Add('”', Get('"')?.Copy());
        Add('„', Join(Get(','), Get(','), overlap: 0));
        Add('·', Middle(Get('.'), m));
        Add('°', Degree(m));
        // the rest of Latin Extended-A: IJ, L with a middle dot, T with a stroke
        Add('Ĳ', Join(Get('I'), Get('J'), overlap: 0));
        Add('ĳ', Join(Get('i'), Get('j'), overlap: 0));
        Add('Ŀ', Join(Get('L'), glyphs.GetValueOrDefault('·'), overlap: 0));
        Add('ŀ', Join(Get('l'), glyphs.GetValueOrDefault('·'), overlap: 0));
        Add('Ŧ', Bar(Get('T'), (m.CapTop + m.Baseline) / 2, right: false));
        Add('ŧ', Bar(Get('t'), Math.Min(m.Baseline - 1, m.XTop + 1), right: false));
        Add(' ', Get(' ')?.Copy());
    }

    private static PixelGlyph? Join(PixelGlyph? a, PixelGlyph? b, int overlap = 1)
    {
        if (a is null || b is null)
        {
            return null;
        }
        var (_, aRight) = a.InkColumns();
        var (bLeft, _) = b.InkColumns();
        var start = Math.Max(0, aRight + 1 - overlap) - Math.Max(0, bLeft);
        var joined = new PixelGlyph(Math.Max(a.Width, start + b.Width), a.Height);
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                joined[x, y] |= a[x, y];
            }
            for (var x = 0; x < b.Width; x++)
            {
                joined[start + x, y] |= b[x, y];
            }
        }
        return joined;
    }

    private static PixelGlyph? Stroke(PixelGlyph? letter, Metrics m, bool diagonal)
    {
        if (letter?.Copy() is not { } g)
        {
            return null;
        }
        // the stem: the column with the most ink
        var stem = Enumerable.Range(0, g.Width).OrderByDescending(x => Enumerable.Range(0, g.Height).Count(y => g[x, y])).First();
        var middle = (Math.Max(0, g.InkRows().Top) + g.InkRows().Bottom) / 2;
        var wide = stem == 0 ? g.Widened(g.Width + 1, 1) : g;
        stem += stem == 0 ? 1 : 0;
        wide[stem - 1, middle + (diagonal ? 1 : 0)] = true;
        wide[stem + 1, middle - (diagonal ? 1 : 0)] = true;
        return wide.Width < stem + 2 ? wide.Widened(stem + 2, 0) : wide;
    }

    private static PixelGlyph? Slash(PixelGlyph? letter)
    {
        if (letter?.Copy() is not { } g)
        {
            return null;
        }
        var (top, bottom) = g.InkRows();
        var (left, right) = g.InkColumns();
        for (var y = top; y <= bottom; y++)
        {
            var x = left + (right - left) * (bottom - y) / Math.Max(1, bottom - top);
            g[x, y] = true;
        }
        return g;
    }

    private static PixelGlyph? Bar(PixelGlyph? letter, int row, bool right)
    {
        if (letter?.Copy() is not { } g)
        {
            return null;
        }
        var stem = Enumerable.Range(0, g.Width).OrderByDescending(x => Enumerable.Range(0, g.Height).Count(y => g[x, y])).First();
        if (right)
        {
            var wide = stem + 1 >= g.Width ? g.Widened(g.Width + 1, 0) : g;
            wide[stem - 1, row] = wide[stem, row] = wide[stem + 1, row] = true;
            return wide;
        }
        var shifted = stem == 0 ? g.Widened(g.Width + 1, 1) : g;
        stem += stem == 0 ? 1 : 0;
        shifted[stem - 1, row] = shifted[stem, row] = shifted[stem + 1, row] = true;
        return shifted;
    }

    private static PixelGlyph? Dotless(PixelGlyph? i, Metrics m)
    {
        if (i?.Copy() is not { } g)
        {
            return null;
        }
        for (var y = 0; y < m.XTop; y++)
        {
            for (var x = 0; x < g.Width; x++)
            {
                g[x, y] = false;
            }
        }
        return g;
    }

    // a glyph turned half round (¿ from ?, ¡ from !), its ink moved down to end on the descender's row
    private static PixelGlyph? Turned(PixelGlyph? mark, Metrics m)
    {
        if (mark is null)
        {
            return null;
        }
        var turned = new PixelGlyph(mark.Width, mark.Height);
        for (var y = 0; y < mark.Height; y++)
        {
            for (var x = 0; x < mark.Width; x++)
            {
                turned[mark.Width - 1 - x, mark.Height - 1 - y] = mark[x, y];
            }
        }
        var (_, bottom) = turned.InkRows();
        var shift = m.Descender - bottom;
        var moved = new PixelGlyph(mark.Width, mark.Height);
        for (var y = 0; y < mark.Height; y++)
        {
            for (var x = 0; x < mark.Width; x++)
            {
                moved[x, y + shift] = turned[x, y];
            }
        }
        return moved;
    }

    private static PixelGlyph? Middle(PixelGlyph? dot, Metrics m)
    {
        if (dot is null)
        {
            return null;
        }
        var g = new PixelGlyph(dot.Width, dot.Height);
        g[Math.Max(0, dot.InkColumns().Left), (m.XTop + m.Baseline) / 2] = true;
        return g;
    }

    private static PixelGlyph Degree(Metrics m)
    {
        var g = new PixelGlyph(3, m.Height);
        foreach (var (x, y) in new[] { (1, 0), (0, 1), (2, 1), (1, 2) })
        {
            g[x, m.CapTop + y] = true;
        }
        return g;
    }

    // ---------------------------------------------------------------- Greek and Cyrillic letters the Latin glyphs draw
    private static readonly (int Letter, char Latin)[] LookAlikes =
    [
        ('А', 'A'), ('В', 'B'), ('Е', 'E'), ('К', 'K'), ('М', 'M'), ('Н', 'H'), ('О', 'O'), ('Р', 'P'), ('С', 'C'), ('Т', 'T'), ('Х', 'X'),
        ('Ѕ', 'S'), ('І', 'I'), ('Ј', 'J'),
        ('а', 'a'), ('е', 'e'), ('о', 'o'), ('р', 'p'), ('с', 'c'), ('у', 'y'), ('х', 'x'), ('ѕ', 's'), ('і', 'i'), ('ј', 'j'),
        ('Α', 'A'), ('Β', 'B'), ('Ε', 'E'), ('Ζ', 'Z'), ('Η', 'H'), ('Ι', 'I'), ('Κ', 'K'), ('Μ', 'M'), ('Ν', 'N'), ('Ο', 'O'), ('Ρ', 'P'),
        ('Τ', 'T'), ('Υ', 'Y'), ('Χ', 'X'),
        ('ο', 'o'),
    ];

    /// <summary>The Latin capital whose decorated picture a card text's first letter may take (A–Z: itself; É: E; В: B; none: 0).</summary>
    public static byte DropCapOf(int code)
    {
        if (code is >= 'A' and <= 'Z')
        {
            return (byte)code;
        }
        if (code is < 0 or > 0xFFFF)
        {
            return 0;
        }
        var first = ((char)code).ToString().Normalize(NormalizationForm.FormD)[0];
        if (first is >= 'A' and <= 'Z')
        {
            return (byte)first;
        }
        foreach (var (letter, latin) in LookAlikes)
        {
            if (letter == first && latin is >= 'A' and <= 'Z')
            {
                return (byte)latin;
            }
        }
        return 0;
    }

    // ---------------------------------------------------------------- our own designs
    private sealed record Design(bool Upper, bool[][] Rows)
    {
        public int Width => Rows[0].Length;
    }

    private static readonly Lazy<Dictionary<int, Design>> Designs = new(() =>
    {
        using var stream = typeof(PixelFontBuilder).Assembly.GetManifestResourceStream("Darklands.Presentation.Resources.PixelGlyphs.txt")
            ?? throw new InvalidOperationException("the pixel glyphs are not embedded");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var designs = new Dictionary<int, Design>();
        string? line;
        (int Letter, bool Upper)? current = null;
        var rows = new List<bool[]>();
        void End()
        {
            if (current is { } c)
            {
                if (rows.Count != (c.Upper ? 7 : 6) || rows.Any(r => r.Length != rows[0].Length))
                {
                    throw new InvalidDataException($"pixel glyph {char.ConvertFromUtf32(c.Letter)}: {rows.Count} rows of unequal or wrong size");
                }
                designs[c.Letter] = new Design(c.Upper, [.. rows]);
            }
            current = null;
            rows.Clear();
        }
        while ((line = reader.ReadLine()) is not null)
        {
            // a comment is "# ..." (a glyph's row has no space)
            if (line.StartsWith("# ", StringComparison.Ordinal) || line.Trim().Length == 0)
            {
                continue;
            }
            if (line.StartsWith("@ ", StringComparison.Ordinal))
            {
                End();
                var parts = line.Split(' ');
                current = (char.ConvertToUtf32(parts[1], 0), parts[2] == "upper");
                continue;
            }
            rows.Add([.. line.Trim().Select(c => c == '#')]);
        }
        End();
        return designs;
    });
}
