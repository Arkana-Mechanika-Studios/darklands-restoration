using System.Runtime.InteropServices;
using System.Text.Json;
using Darklands.Platform;
using StbTrueTypeSharp;

namespace Darklands.Rendering;

/// <summary>
/// A font pack's TrueType / OpenType font (docs/LOCALISATION.md, font packs; L-6b set 2), <c>fonts/slotN.json</c>:
/// <code>{ "ttf": "MyFont.ttf", "size": 8, "spacing": 0, "lineSpacing": 1 }</code>
/// <c>size</c> is the line's height (ascent to descent) in the game's 320-wide pixels. Its metrics are whole game pixels (an advance
/// rounded), so the game's layout is the same loop; the game's frame gets each glyph at 1x (its coverage at half or more), the host
/// draws it again at the window's scale (<see cref="Raster"/>, <see cref="TextGlyphs"/>). No hinting and no complex-script shaping
/// (stb_truetype): Latin, Greek and Cyrillic draw well; Arabic or Indic scripts would need shaping.
/// </summary>
// Not in the original: the 1.1 font packs.
public sealed unsafe class TtfFont : IScalableFont
{
    private readonly StbTrueType.stbtt_fontinfo _info = new();
    private readonly GCHandle _data;
    private readonly float _size;
    private readonly int _spacing;
    private readonly int _ascent;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, (int Advance, int Width, bool[] Mask)?> _glyphs = [];
    private readonly Dictionary<(int Code, double ScaleX, double ScaleY), (int, int, int, int, byte[])> _rasters = [];

    private TtfFont(byte[] file, float size, int spacing, int lineSpacing)
    {
        _data = GCHandle.Alloc(file, GCHandleType.Pinned);
        var data = (byte*)_data.AddrOfPinnedObject();
        if (StbTrueType.stbtt_InitFont(_info, data, StbTrueType.stbtt_GetFontOffsetForIndex(data, 0)) == 0)
        {
            _data.Free();
            throw new InvalidDataException("not a TrueType or OpenType font");
        }
        _size = size;
        _spacing = spacing;
        var scale = StbTrueType.stbtt_ScaleForPixelHeight(_info, size);
        int ascent, descent, lineGap;
        StbTrueType.stbtt_GetFontVMetrics(_info, &ascent, &descent, &lineGap);
        _ascent = (int)Math.Round(ascent * scale);
        Height = Math.Max(1, (int)Math.Ceiling((ascent - descent) * scale));
        LineHeight = Height + lineSpacing;
    }

    public int Height { get; }

    public int LineHeight { get; }

    public bool Has(int code) => code == ' ' || (code > ' ' && Glyph(code) is not null);

    public int Advance(int code) => code == ' ' ? SpaceAdvance() : Glyph(code)?.Advance ?? 0;

    public bool Pixel(int code, int x, int y) =>
        Glyph(code) is { } g && x >= 0 && x < g.Width && y >= 0 && y < Height && g.Mask[y * g.Width + x];

    public (int Left, int Top, int Width, int Height, byte[] Coverage) Raster(int code, double scaleX, double scaleY)
    {
        lock (_gate)
        {
            if (_rasters.TryGetValue((code, scaleX, scaleY), out var kept))
            {
                return kept;
            }
            var s = StbTrueType.stbtt_ScaleForPixelHeight(_info, _size);
            var (fx, fy) = ((float)(s * scaleX), (float)(s * scaleY));
            int w, h, xoff, yoff;
            var bitmap = StbTrueType.stbtt_GetCodepointBitmap(_info, fx, fy, code, &w, &h, &xoff, &yoff);
            var coverage = new byte[Math.Max(0, w * h)];
            if (bitmap != null)
            {
                new ReadOnlySpan<byte>(bitmap, coverage.Length).CopyTo(coverage);
                StbTrueType.stbtt_FreeBitmap(bitmap, null);
            }
            // the box from the 1x glyph's top-left corner: the baseline is the 1x ascent, scaled
            var raster = (xoff, (int)Math.Round(_ascent * scaleY) + yoff, w, h, coverage);
            if (_rasters.Count > 4096)
            {
                _rasters.Clear();
            }
            _rasters[(code, scaleX, scaleY)] = raster;
            return raster;
        }
    }

    private int SpaceAdvance()
    {
        int advance, bearing;
        StbTrueType.stbtt_GetCodepointHMetrics(_info, ' ', &advance, &bearing);
        return Math.Max(1, (int)Math.Round(advance * StbTrueType.stbtt_ScaleForPixelHeight(_info, _size))) + _spacing;
    }

    // a code's 1x glyph: its advance in game pixels and its mask (coverage at half or more; at least one pixel for a glyph that has ink)
    private (int Advance, int Width, bool[] Mask)? Glyph(int code)
    {
        lock (_gate)
        {
            if (_glyphs.TryGetValue(code, out var kept))
            {
                return kept;
            }
            (int, int, bool[])? glyph = null;
            if (code > ' ' && code <= 0x10FFFF && StbTrueType.stbtt_FindGlyphIndex(_info, code) != 0)
            {
                var scale = StbTrueType.stbtt_ScaleForPixelHeight(_info, _size);
                int advance, bearing, w, h, xoff, yoff;
                StbTrueType.stbtt_GetCodepointHMetrics(_info, code, &advance, &bearing);
                var pen = Math.Max(1, (int)Math.Round(advance * scale));
                var bitmap = StbTrueType.stbtt_GetCodepointBitmap(_info, scale, scale, code, &w, &h, &xoff, &yoff);
                var width = Math.Max(pen, Math.Max(0, xoff) + w);
                var mask = new bool[width * Height];
                var (best, bestAt) = (0, -1);
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var (mx, my) = (x + Math.Max(0, xoff), y + _ascent + yoff);
                        if (mx < 0 || mx >= width || my < 0 || my >= Height)
                        {
                            continue;
                        }
                        var c = bitmap[y * w + x];
                        mask[my * width + mx] = c >= 128;
                        if (c > best)
                        {
                            (best, bestAt) = (c, my * width + mx);
                        }
                    }
                }
                if (bestAt >= 0 && !mask.Contains(true))
                {
                    mask[bestAt] = true;
                }
                if (bitmap != null)
                {
                    StbTrueType.stbtt_FreeBitmap(bitmap, null);
                }
                glyph = (pen + _spacing, width, mask);
            }
            _glyphs[code] = glyph;
            return glyph;
        }
    }

    /// <summary>
    /// The font of <paramref name="json"/> (a <c>slotN.json</c> naming <c>"ttf"</c>), its file read from beside it; null with the reason
    /// in <paramref name="problem"/> when it cannot be used.
    /// </summary>
    public static TtfFont? Load(string json, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(json), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (!root.TryGetProperty("ttf", out var name) || name.GetString() is not { Length: > 0 } file)
            {
                problem = $"{json}: no \"ttf\"";
                return null;
            }
            var size = root.TryGetProperty("size", out var s) && s.TryGetDouble(out var v) ? v : 0;
            if (size is < 3 or > 64)
            {
                problem = $"{json}: \"size\" must be 3 to 64 game pixels";
                return null;
            }
            int Number(string key, int fallback) => root.TryGetProperty(key, out var n) && n.TryGetInt32(out var i) ? i : fallback;
            problem = null;
            return new TtfFont(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(json) ?? "", file)), (float)size, Number("spacing", 0), Number("lineSpacing", 1));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException)
        {
            problem = $"{json}: {e.Message}";
            return null;
        }
    }
}
