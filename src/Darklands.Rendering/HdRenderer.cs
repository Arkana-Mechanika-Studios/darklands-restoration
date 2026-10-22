using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// Turns the game's screen into draw items for any host: the 320×200 frame as it is (the base layer), for each drawing
/// of an asset the pack replaces its replacement picture, and over them the frame again where no replacement shows
/// (the overlay), so whatever was drawn over a replaced asset (text, the pointer, a box) stays on top.
/// </summary>
/// <remarks>
/// <para>The screen says per pixel which asset drew it (<see cref="Screen.Owners"/>) and which of the asset's pixels it is
/// (<see cref="Screen.Texels"/>); the asset's size comes from <see cref="ScreenOwners.SizeOf"/>. Pixels of one asset
/// drawn at one place (and turned the same way) form one drawing: one item, its rectangle the asset's at that place.
/// A pixel without an owner, of an unknown place or of an asset without a replacement shows the frame's pixel.</para>
/// <para>A drawing whose rectangle holds no pixel of another replaced drawing is drawn whole and the overlay cuts it to
/// its pixels: its picture changes only when its colours do, so a pointer moving over it costs nothing. Drawings
/// overlapping another replaced drawing are masked to their own pixels instead (rebuilt when those change).</para>
/// <para>Without a pack, or when nothing on the screen has a replacement, the frame is the base layer alone: exactly the
/// picture hosts showed before (<see cref="FrameCompositor"/>).</para>
/// <para>Palette effects: for an asset whose own colours are known (<see cref="ScreenOwners.OwnColorsOf"/>: pictures), each
/// pixel's own colour is compared with the colour shown; where the palette (a fade) or a recolour changed it, the
/// replacement goes through the same change (<see cref="PaletteEffect"/>). Assets without known colours (sprites:
/// recoloured figures, markers) keep their replacement's colours; later work.</para>
/// New code (replacement graphics, not part of the game). Deterministic: the same screen and pack give the same items.
/// </remarks>
public sealed class HdRenderer(HdPack pack)
{
    private readonly RgbaImage _base = new(Screen.Width, Screen.Height);
    private readonly RgbaImage _overlay = new(Screen.Width, Screen.Height);
    private readonly Dictionary<int, RgbaImage?> _replacements = [];
    private readonly int[] _drawingAt = new int[Screen.Width * Screen.Height];
    private Dictionary<DrawingKey, Kept> _kept = [];

    public HdPack Pack => pack;

    /// <summary>The draw items of the screen for an output rectangle (the host's window area for the 320×200 picture).</summary>
    public HdFrame Render(Screen screen, DisplayRectangle output)
    {
        for (var i = 0; i < _base.Argb.Length; i++)
        {
            _base.Argb[i] = screen.Palette[screen.Pixels[i]] | 0xFF000000u;
        }
        _base.Changed();
        var items = new List<DrawItem> { new(_base, output, DrawFilter.Nearest, null) };
        if (pack.IsEmpty)
        {
            return new HdFrame(output, items);
        }
        var drawings = FindDrawings(screen);
        if (drawings.Count == 0)
        {
            _kept = [];
            return new HdFrame(output, items);
        }
        var kept = new Dictionary<DrawingKey, Kept>();
        for (var d = 0; d < drawings.Count; d++)
        {
            var drawing = drawings[d];
            var keep = _kept.GetValueOrDefault(drawing.Key) ?? new Kept();
            kept[drawing.Key] = keep;
            var effect = Effect(screen, drawing, keep);
            var placement = drawing.Placement with { Effect = effect };
            var texture = Exclusive(d, drawing.Placement)
                ? Whole(placement, drawing.Replacement, keep)
                : Masked(placement, drawing.Replacement, keep);
            items.Add(new DrawItem(texture, Destination(placement, output), DrawFilter.Linear, placement));
        }
        _kept = kept;
        for (var i = 0; i < _overlay.Argb.Length; i++)
        {
            _overlay.Argb[i] = _drawingAt[i] >= 0 ? 0 : _base.Argb[i];
        }
        _overlay.Changed();
        items.Add(new DrawItem(_overlay, output, DrawFilter.Nearest, null));
        return new HdFrame(output, items);
    }

    /// <summary>The same items placed for another output rectangle (the window changed size; the screen did not).</summary>
    public static HdFrame Relayout(HdFrame frame, DisplayRectangle output) =>
        new(output, [.. frame.Items.Select(item => item with { Destination = item.Placement is { } p ? Destination(p, output) : output })]);

    /// <summary>A drawing's rectangle in output pixels: its screen rectangle with edges where the frame's pixels have theirs.</summary>
    public static DisplayRectangle Destination(AssetPlacement drawing, DisplayRectangle output)
    {
        var left = Edge(drawing.X, output.X, output.Width, Screen.Width);
        var top = Edge(drawing.Y, output.Y, output.Height, Screen.Height);
        return new DisplayRectangle(left, top,
            Edge(drawing.X + drawing.Width, output.X, output.Width, Screen.Width) - left,
            Edge(drawing.Y + drawing.Height, output.Y, output.Height, Screen.Height) - top);
    }

    /// <summary>The output coordinate of a screen coordinate (rounded down, as nearest-pixel scaling places pixel edges).</summary>
    private static int Edge(int screen, int outputStart, int outputSize, int screenSize) =>
        outputStart + (int)FrameCompositor.FloorDiv((long)screen * outputSize, screenSize);

    /// <summary>
    /// The drawings of replaced assets on the screen, in the order their first pixel comes (row by row); fills
    /// <see cref="_drawingAt"/> with each pixel's drawing (-1: none).
    /// </summary>
    private List<Drawing> FindDrawings(Screen screen)
    {
        var found = new Dictionary<DrawingKey, int>();
        var order = new List<Drawing>();
        var owners = new Dictionary<int, Asset?>();
        Array.Fill(_drawingAt, -1);
        var (lastOwner, lastAsset) = (0, (Asset?)null);
        var (lastKey, lastIndex) = (default(DrawingKey), -1);
        for (var i = 0; i < _drawingAt.Length; i++)
        {
            var owner = screen.Owners[i];
            var texel = screen.Texels[i];
            if (owner == 0 || texel == ScreenTexel.Unknown)
            {
                continue;
            }
            Asset? asset;
            if (owner == lastOwner)
            {
                asset = lastAsset;
            }
            else if (!owners.TryGetValue(owner, out asset))
            {
                asset = Replacement(owner) is { } replacement && ScreenOwners.SizeOf(owner) is var (w, h)
                    ? new Asset(ScreenOwners.NameOf(owner)!, w, h, replacement, ScreenOwners.OwnColorsOf(owner) is { } c && c.Pixels.Length == w * h ? c : null)
                    : null;
                owners[owner] = asset;
            }
            (lastOwner, lastAsset) = (owner, asset);
            if (asset is null)
            {
                continue;
            }
            var (u, v, flipX, flipY) = ScreenTexel.Unpack(texel);
            if (u >= asset.Width || v >= asset.Height)
            {
                continue;
            }
            var column = flipX ? asset.Width - 1 - u : u;
            var row = flipY ? asset.Height - 1 - v : v;
            var key = new DrawingKey(owner, i % Screen.Width - column, i / Screen.Width - row, flipX, flipY);
            var index = key == lastKey ? lastIndex : -1;
            if (index < 0 && !found.TryGetValue(key, out index))
            {
                index = order.Count;
                found[key] = index;
                var placement = new AssetPlacement(asset.Name, key.X, key.Y, asset.Width, asset.Height, flipX, flipY, new bool[asset.Width * asset.Height]);
                var screenAt = new int[asset.Width * asset.Height];
                Array.Fill(screenAt, -1);
                order.Add(new Drawing(key, placement, asset.Replacement, asset.OwnColors, screenAt));
            }
            (lastKey, lastIndex) = (key, index);
            var t = row * asset.Width + column;
            order[index].Placement.Owned[t] = true;
            order[index].ScreenAt[t] = i;
            _drawingAt[i] = index;
        }
        return order;
    }

    private RgbaImage? Replacement(int owner)
    {
        if (!_replacements.TryGetValue(owner, out var image))
        {
            image = ScreenOwners.NameOf(owner) is { } name && ScreenOwners.SizeOf(owner) is ( > 0, > 0) ? pack.Find(name) : null;
            if (image is { Width: 0 } or { Height: 0 })
            {
                image = null;
            }
            _replacements[owner] = image;
        }
        return image;
    }

    /// <summary>Whether the drawing's rectangle on the screen holds no pixel of another replaced drawing.</summary>
    private bool Exclusive(int index, AssetPlacement drawing)
    {
        for (var y = Math.Max(0, drawing.Y); y < Math.Min(Screen.Height, drawing.Y + drawing.Height); y++)
        {
            for (var x = Math.Max(0, drawing.X); x < Math.Min(Screen.Width, drawing.X + drawing.Width); x++)
            {
                var at = _drawingAt[y * Screen.Width + x];
                if (at >= 0 && at != index)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// The drawing's palette effect (null: none, or its own colours are unknown). Each pixel compares its own colour with
    /// the screen's where the drawing owns the pixel, else with the current palette's colour of its own index; the lines
    /// are fitted again only when the set of changed pairs changes.
    /// </summary>
    private static PaletteEffect? Effect(Screen screen, Drawing drawing, Kept keep)
    {
        if (drawing.OwnColors is not { } colors)
        {
            return null;
        }
        var placement = drawing.Placement;
        var (width, height) = (placement.Width, placement.Height);
        if (keep.OwnIndex is null || keep.OwnColors is not { } kept || kept.Pixels != colors.Pixels || kept.Palette != colors.Palette)
        {
            // the asset's own colour index per pixel (screen orientation): the same every frame
            (keep.OwnIndex, keep.OwnColors) = (new byte[width * height], colors);
            for (var row = 0; row < height; row++)
            {
                var sourceRow = placement.FlipY ? height - 1 - row : row;
                for (var column = 0; column < width; column++)
                {
                    keep.OwnIndex[row * width + column] = colors.Pixels[sourceRow * width + (placement.FlipX ? width - 1 - column : column)];
                }
            }
            keep.ShownIndex = null;
        }
        // per pixel the palette index shown: the screen's where the drawing owns the pixel, else its own index
        var shownIndex = new byte[width * height];
        for (var t = 0; t < shownIndex.Length; t++)
        {
            var at = drawing.ScreenAt[t];
            shownIndex[t] = at >= 0 ? screen.Pixels[at] : keep.OwnIndex[t];
        }
        if (keep.ShownIndex is not null && keep.ShownPalette is not null
            && keep.ShownIndex.AsSpan().SequenceEqual(shownIndex) && keep.ShownPalette.AsSpan().SequenceEqual(screen.Palette))
        {
            return keep.Effect;
        }
        (keep.ShownIndex, keep.ShownPalette) = (shownIndex, (uint[])screen.Palette.Clone());
        // pixels counted per (own index, shown index): 65536 counters, then merged by colour
        var counts = keep.Counts ??= new int[65536];
        Array.Clear(counts);
        var ownPalette = colors.Palette;
        var changed = false;
        for (var t = 0; t < shownIndex.Length; t++)
        {
            var (o, i) = (keep.OwnIndex[t], shownIndex[t]);
            if (((ownPalette[o] ^ screen.Palette[i]) & 0xFFFFFF) != 0)
            {
                counts[o << 8 | i]++;
                changed = true;
            }
        }
        if (!changed)
        {
            keep.Effect = null;
            return null;
        }
        var byColor = new SortedDictionary<ulong, int>();
        for (var k = 0; k < counts.Length; k++)
        {
            if (counts[k] > 0)
            {
                var pair = Pair(ownPalette[k >> 8], screen.Palette[k & 0xFF]);
                byColor[pair] = byColor.GetValueOrDefault(pair) + counts[k];
            }
        }
        var keys = byColor.Keys.ToArray();
        if (keep.Lines is null || keep.Pairs is null || !keep.Pairs.AsSpan().SequenceEqual(keys))
        {
            keep.Lines = ColorLines.Fit([.. byColor.Select(p => (((uint)(p.Key >> 32), (uint)p.Key), p.Value))], width * height);
            keep.Pairs = keys;
        }
        var lines = keep.Lines;
        var lineAt = new int[65536];
        for (var k = 0; k < counts.Length; k++)
        {
            if (counts[k] > 0)
            {
                lineAt[k] = lines.LineOf(ownPalette[k >> 8] | 0xFF000000u, screen.Palette[k & 0xFF] | 0xFF000000u);
            }
        }
        var lineOf = new int[width * height];
        for (var t = 0; t < lineOf.Length; t++)
        {
            var k = keep.OwnIndex[t] << 8 | shownIndex[t];
            lineOf[t] = counts[k] > 0 ? lineAt[k] : lines.Unchanged;
        }
        keep.Effect = new PaletteEffect(lines, lineOf);
        return keep.Effect;
    }

    /// <summary>
    /// The whole replacement, turned as drawn, through the palette effect: the pack's picture itself without either; else
    /// kept from the last frame, repainted only where a pixel's line changed (and marked changed only there).
    /// </summary>
    private static RgbaImage Whole(AssetPlacement drawing, RgbaImage replacement, Kept keep)
    {
        if (!drawing.FlipX && !drawing.FlipY && drawing.Effect is null)
        {
            keep.Texture = null;
            return replacement;
        }
        var lines = drawing.Effect?.Lines;
        var lineOf = drawing.Effect?.LineOf;
        if (keep.Texture is { } texture && keep.Mask is null && ReferenceEquals(keep.TextureLines, lines))
        {
            if (lineOf is null || keep.LineOf is null || ReferenceEquals(lineOf, keep.LineOf))
            {
                return texture;
            }
            // only the pixels whose line changed (the hidden lines coming out, a box over the drawing)
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (var t = 0; t < lineOf.Length; t++)
            {
                if (lineOf[t] != keep.LineOf[t])
                {
                    var (column, row) = (t % drawing.Width, t / drawing.Width);
                    (left, top, right, bottom) = (Math.Min(left, column), Math.Min(top, row), Math.Max(right, column), Math.Max(bottom, row));
                }
            }
            if (right >= 0)
            {
                var area = Paint(texture, drawing, replacement, null, left, top, right + 1, bottom + 1);
                texture.Changed(area.X, area.Y, area.Width, area.Height);
                keep.LineOf = lineOf;
            }
            return texture;
        }
        texture = new RgbaImage(replacement.Width, replacement.Height);
        Paint(texture, drawing, replacement, null, 0, 0, drawing.Width, drawing.Height);
        (keep.Texture, keep.Mask, keep.TextureLines, keep.LineOf) = (texture, null, lines, lineOf);
        return texture;
    }

    /// <summary>The replacement transparent outside the drawing's own pixels (kept while the pixels and colours stay).</summary>
    private static RgbaImage Masked(AssetPlacement drawing, RgbaImage replacement, Kept keep)
    {
        var lines = drawing.Effect?.Lines;
        var lineOf = drawing.Effect?.LineOf;
        if (keep.Texture is { } kept && keep.Mask is { } mask && mask.AsSpan().SequenceEqual(drawing.Owned)
            && ReferenceEquals(keep.TextureLines, lines) && (lineOf is null || lineOf.AsSpan().SequenceEqual(keep.LineOf)))
        {
            return kept;
        }
        var texture = new RgbaImage(replacement.Width, replacement.Height);
        Paint(texture, drawing, replacement, drawing.Owned, 0, 0, drawing.Width, drawing.Height);
        (keep.Texture, keep.Mask, keep.TextureLines, keep.LineOf) = (texture, (bool[])drawing.Owned.Clone(), lines, lineOf);
        return texture;
    }

    /// <summary>
    /// Paints the replacement pixels over asset pixels [left, right) × [top, bottom) into the texture (turned, through the
    /// effect, transparent where <paramref name="mask"/> says the drawing does not own the pixel); returns the texture area.
    /// </summary>
    private static (int X, int Y, int Width, int Height) Paint(RgbaImage texture, AssetPlacement drawing, RgbaImage replacement, bool[]? mask,
        int left, int top, int right, int bottom)
    {
        var (width, height) = (texture.Width, texture.Height);
        var (x0, x1) = (Start(left, width, drawing.Width), Start(right, width, drawing.Width));
        var (y0, y1) = (Start(top, height, drawing.Height), Start(bottom, height, drawing.Height));
        var tables = drawing.Effect?.Lines.Tables;
        var lineOf = drawing.Effect?.LineOf;
        var columns = new int[x1 - x0];
        for (var px = x0; px < x1; px++)
        {
            columns[px - x0] = (int)((long)px * drawing.Width / width);
        }
        void PaintRow(int py)
        {
            var row = (int)((long)py * drawing.Height / height);
            var source = (drawing.FlipY ? height - 1 - py : py) * width;
            for (var px = x0; px < x1; px++)
            {
                var t = row * drawing.Width + columns[px - x0];
                var argb = mask is null || mask[t] ? replacement.Argb[source + (drawing.FlipX ? width - 1 - px : px)] : 0;
                texture.Argb[py * width + px] = argb != 0 && lineOf is not null ? PaletteEffect.Apply(tables!, lineOf[t], argb) : argb;
            }
        }
        // rows are independent: a large area (a whole picture in a fade) is painted on several cores, the same pixels either way
        if ((long)(x1 - x0) * (y1 - y0) >= 1 << 18)
        {
            Parallel.For(y0, y1, PaintRow);
        }
        else
        {
            for (var py = y0; py < y1; py++)
            {
                PaintRow(py);
            }
        }
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>The first texture pixel of asset pixel <paramref name="texel"/> (the inverse of px × size / textureSize).</summary>
    private static int Start(int texel, int textureSize, int size) => (int)(((long)texel * textureSize + size - 1) / size);

    /// <summary>A pair (own colour, shown colour) as one sortable number, both opaque.</summary>
    private static ulong Pair(uint own, uint shown) => (ulong)(own | 0xFF000000u) << 32 | (shown | 0xFF000000u);

    private sealed record Asset(string Name, int Width, int Height, RgbaImage Replacement, (byte[] Pixels, uint[] Palette)? OwnColors);

    /// <summary>A drawing found on the screen; <paramref name="ScreenAt"/>: per asset pixel, its screen pixel (-1: not owned).</summary>
    private sealed record Drawing(DrawingKey Key, AssetPlacement Placement, RgbaImage Replacement, (byte[] Pixels, uint[] Palette)? OwnColors, int[] ScreenAt);

    /// <summary>What a drawing keeps from one frame to the next: its fitted lines and the texture made last.</summary>
    private sealed class Kept
    {
        public (byte[] Pixels, uint[] Palette)? OwnColors;
        public byte[]? OwnIndex;
        public byte[]? ShownIndex;
        public uint[]? ShownPalette;
        public int[]? Counts;
        public PaletteEffect? Effect;
        public ulong[]? Pairs;
        public ColorLines? Lines;
        public RgbaImage? Texture;
        public bool[]? Mask;
        public ColorLines? TextureLines;
        public int[]? LineOf;
    }

    private readonly record struct DrawingKey(int Owner, int X, int Y, bool FlipX, bool FlipY);
}
