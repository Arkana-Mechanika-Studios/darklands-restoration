using StbTrueTypeSharp;

namespace Darklands.Host.Sdl;

/// <summary>
/// A TrueType font rasterised for the developer console at one pixel height: the printable ASCII and Latin-1
/// characters in one texture (white, the glyph's coverage as alpha; the colour comes from the texture's colour mod).
/// </summary>
internal sealed unsafe class ConsoleFont : IDisposable
{
    private const int FirstCharacter = 32;
    private const int LastCharacter = 255;
    private const int AtlasColumns = 16;

    private readonly IntPtr _texture;
    private readonly Glyph[] _glyphs = new Glyph[LastCharacter + 1];
    private readonly int _cellWidth;
    private readonly int _cellHeight;

    private readonly record struct Glyph(int Advance, int OffsetX, int OffsetY, int Width, int Height, int AtlasX, int AtlasY, bool Present);

    private ConsoleFont(IntPtr renderer, byte[] file, int pixelHeight)
    {
        PixelHeight = pixelHeight;
        var info = new StbTrueType.stbtt_fontinfo();
        fixed (byte* data = file)
        {
            if (StbTrueType.stbtt_InitFont(info, data, StbTrueType.stbtt_GetFontOffsetForIndex(data, 0)) == 0)
            {
                throw new InvalidDataException("not a TrueType font");
            }
            var scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelHeight);
            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);
            Ascent = (int)Math.Ceiling(ascent * scale);
            LineHeight = (int)Math.Ceiling((ascent - descent + lineGap) * scale);
            _cellWidth = pixelHeight * 2;
            _cellHeight = LineHeight + 2;
            var rows = (LastCharacter - FirstCharacter + AtlasColumns) / AtlasColumns;
            var atlasWidth = _cellWidth * AtlasColumns;
            var atlasHeight = _cellHeight * rows;
            var argb = new int[atlasWidth * atlasHeight];
            for (var c = FirstCharacter; c <= LastCharacter; c++)
            {
                if (c is >= 127 and < 160 || StbTrueType.stbtt_FindGlyphIndex(info, c) == 0)
                {
                    continue;
                }
                int advance, bearing, w, h, xoff, yoff;
                StbTrueType.stbtt_GetCodepointHMetrics(info, c, &advance, &bearing);
                var bitmap = StbTrueType.stbtt_GetCodepointBitmap(info, scale, scale, c, &w, &h, &xoff, &yoff);
                var slot = c - FirstCharacter;
                var (ax, ay) = (slot % AtlasColumns * _cellWidth, slot / AtlasColumns * _cellHeight);
                var pitch = w;
                w = Math.Min(w, _cellWidth);
                h = Math.Min(h, _cellHeight);
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        argb[(ay + y) * atlasWidth + ax + x] = bitmap[y * pitch + x] << 24 | 0xFFFFFF;
                    }
                }
                if (bitmap != null)
                {
                    StbTrueType.stbtt_FreeBitmap(bitmap, null);
                }
                _glyphs[c] = new Glyph((int)Math.Round(advance * scale), xoff, yoff, w, h, ax, ay, true);
            }
            _texture = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessStatic, atlasWidth, atlasHeight);
            SdlNative.SDL_UpdateTexture(_texture, IntPtr.Zero, argb, atlasWidth * 4);
            SdlNative.SDL_SetTextureBlendMode(_texture, SdlNative.BlendModeBlend);
        }
        CharacterWidth = _glyphs['M'].Present ? _glyphs['M'].Advance : pixelHeight / 2;
    }

    public int PixelHeight { get; }

    /// <summary>Pixels from the top of a line to the baseline.</summary>
    public int Ascent { get; }

    public int LineHeight { get; }

    /// <summary>The width of 'M' (every character's, in a monospaced font).</summary>
    public int CharacterWidth { get; }

    /// <summary>The bundled DejaVu Sans Mono, or <paramref name="file"/>; falls back to the bundled font when the file cannot be used.</summary>
    public static ConsoleFont Load(IntPtr renderer, string? file, int pixelHeight, Action<string> report)
    {
        if (file is not null)
        {
            try
            {
                return new ConsoleFont(renderer, File.ReadAllBytes(file), pixelHeight);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or IndexOutOfRangeException)
            {
                report($"Console: cannot use the font {file} ({e.Message}); the bundled font is used.");
            }
        }
        return new ConsoleFont(renderer, Bundled.Value, pixelHeight);
    }

    private static readonly Lazy<byte[]> Bundled = new(() =>
    {
        using var stream = typeof(ConsoleFont).Assembly.GetManifestResourceStream("Darklands.Host.Sdl.DejaVuSansMono.ttf")
            ?? throw new InvalidOperationException("the bundled console font is missing");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    });

    /// <summary>The width of <paramref name="text"/> in pixels.</summary>
    public int Measure(ReadOnlySpan<char> text)
    {
        var width = 0;
        foreach (var c in text)
        {
            width += Of(c).Advance;
        }
        return width;
    }

    /// <summary>Draws <paramref name="text"/> with its line's top at (x, y); returns the x after it.</summary>
    public int Draw(IntPtr renderer, ReadOnlySpan<char> text, int x, int y, (byte R, byte G, byte B) colour)
    {
        SdlNative.SDL_SetTextureColorMod(_texture, colour.R, colour.G, colour.B);
        foreach (var c in text)
        {
            var g = Of(c);
            if (g.Width > 0 && g.Height > 0)
            {
                var source = new SdlNative.SdlRect { X = g.AtlasX, Y = g.AtlasY, Width = g.Width, Height = g.Height };
                var target = new SdlNative.SdlRect { X = x + g.OffsetX, Y = y + Ascent + g.OffsetY, Width = g.Width, Height = g.Height };
                SdlNative.SDL_RenderCopyPart(renderer, _texture, ref source, ref target);
            }
            x += g.Advance;
        }
        return x;
    }

    private Glyph Of(char c) => c <= LastCharacter && _glyphs[c].Present ? _glyphs[c] : _glyphs['?'];

    public void Dispose() => SdlNative.SDL_DestroyTexture(_texture);
}
