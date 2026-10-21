using System.Runtime.InteropServices;
using StbTrueTypeSharp;

namespace Darklands.Launcher.Drawing;

/// <summary>A TrueType font file, kept pinned for the rasteriser.</summary>
internal sealed unsafe class TrueTypeFont : IDisposable
{
    private readonly GCHandle _pin;

    public TrueTypeFont(byte[] file)
    {
        _pin = GCHandle.Alloc(file, GCHandleType.Pinned);
        var data = (byte*)_pin.AddrOfPinnedObject();
        Info = new StbTrueType.stbtt_fontinfo();
        if (StbTrueType.stbtt_InitFont(Info, data, StbTrueType.stbtt_GetFontOffsetForIndex(data, 0)) == 0)
        {
            _pin.Free();
            throw new InvalidDataException("not a TrueType font");
        }
    }

    public StbTrueType.stbtt_fontinfo Info { get; }

    public bool Has(int codepoint) => StbTrueType.stbtt_FindGlyphIndex(Info, codepoint) != 0;

    public void Dispose()
    {
        if (_pin.IsAllocated)
        {
            _pin.Free();
        }
    }
}

/// <summary>
/// A font at one size (in pixels per em, as CSS sizes fonts), with a fallback font for the characters it lacks.
/// Glyphs are rasterised once and kept.
/// </summary>
internal sealed unsafe class TextFace
{
    private readonly TrueTypeFont _font;
    private readonly TextFace? _fallback;
    private readonly float _scale;
    private readonly Dictionary<int, Glyph> _glyphs = [];

    private sealed record Glyph(int Advance, int OffsetX, int OffsetY, int Width, int Height, byte[] Coverage);

    public TextFace(TrueTypeFont font, float pixelsPerEm, TextFace? fallback)
    {
        _font = font;
        _fallback = fallback;
        _scale = StbTrueType.stbtt_ScaleForMappingEmToPixels(font.Info, pixelsPerEm);
        int ascent, descent, gap;
        StbTrueType.stbtt_GetFontVMetrics(font.Info, &ascent, &descent, &gap);
        Ascent = (int)Math.Round(ascent * _scale);
        LineHeight = (int)Math.Round((ascent - descent + gap) * _scale);
    }

    /// <summary>Pixels from a line's top to its baseline.</summary>
    public int Ascent { get; }

    public int LineHeight { get; }

    public int Measure(ReadOnlySpan<char> text)
    {
        var width = 0;
        for (var i = 0; i < text.Length; i++)
        {
            width += Advance(text, i);
        }
        return width;
    }

    /// <summary>Draws <paramref name="text"/> with its line's top at (x, y), with a drop shadow when <paramref name="shadow"/> is not 0; returns the x after it.</summary>
    public int Draw(Canvas canvas, ReadOnlySpan<char> text, int x, int y, uint colour, int shadow = 0, uint shadowColour = 0)
    {
        if (shadow > 0)
        {
            DrawRun(canvas, text, x + shadow, y + shadow, shadowColour);
        }
        return DrawRun(canvas, text, x, y, colour);
    }

    /// <summary>Splits <paramref name="text"/> into lines no wider than <paramref name="width"/> (at spaces).</summary>
    public IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(candidate) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    private int DrawRun(Canvas canvas, ReadOnlySpan<char> text, int x, int y, uint colour)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var (face, glyph) = Of(text[i]);
            var top = y + Ascent + glyph.OffsetY + (face == this ? 0 : Ascent - face.Ascent);
            for (var gy = 0; gy < glyph.Height; gy++)
            {
                for (var gx = 0; gx < glyph.Width; gx++)
                {
                    var coverage = glyph.Coverage[gy * glyph.Width + gx];
                    if (coverage != 0)
                    {
                        canvas.Blend(x + glyph.OffsetX + gx, top + gy, colour, coverage);
                    }
                }
            }
            x += Advance(text, i);
        }
        return x;
    }

    private int Advance(ReadOnlySpan<char> text, int i)
    {
        var (face, glyph) = Of(text[i]);
        var advance = glyph.Advance;
        if (face == this && i + 1 < text.Length && _font.Has(text[i + 1]))
        {
            advance += (int)Math.Round(StbTrueType.stbtt_GetCodepointKernAdvance(_font.Info, text[i], text[i + 1]) * _scale);
        }
        return advance;
    }

    private (TextFace Face, Glyph Glyph) Of(char c)
    {
        if (!_font.Has(c) && c != ' ' && _fallback is not null)
        {
            return _fallback.Of(c);
        }
        if (!_glyphs.TryGetValue(c, out var glyph))
        {
            glyph = _glyphs[c] = Rasterise(c);
        }
        return (this, glyph);
    }

    private Glyph Rasterise(int c)
    {
        int advance, bearing, w, h, xoff, yoff;
        StbTrueType.stbtt_GetCodepointHMetrics(_font.Info, c, &advance, &bearing);
        var bitmap = StbTrueType.stbtt_GetCodepointBitmap(_font.Info, _scale, _scale, c, &w, &h, &xoff, &yoff);
        var coverage = new byte[Math.Max(0, w * h)];
        if (bitmap != null)
        {
            new ReadOnlySpan<byte>(bitmap, coverage.Length).CopyTo(coverage);
            StbTrueType.stbtt_FreeBitmap(bitmap, null);
        }
        return new Glyph((int)Math.Round(advance * _scale), xoff, yoff, w, h, coverage);
    }
}
