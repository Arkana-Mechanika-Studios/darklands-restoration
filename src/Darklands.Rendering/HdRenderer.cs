using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// Turns the game's screen into draw items for any host: the 320×200 frame as it is (the base layer), and over it, for
/// each drawing of an asset the pack replaces, the replacement picture masked to the pixels the asset still owns.
/// </summary>
/// <remarks>
/// <para>The screen says per pixel which asset drew it (<see cref="Screen.Owners"/>) and which of the asset's pixels it is
/// (<see cref="Screen.Texels"/>); the asset's size comes from <see cref="ScreenOwners.SizeOf"/>. Pixels of one asset
/// drawn at one place (and turned the same way) form one drawing: one item, its rectangle the asset's at that place.
/// A pixel without an owner, of an unknown place or of an asset without a replacement shows the frame's pixel.</para>
/// <para>Without a pack, or when nothing on the screen has a replacement, the frame is the base layer alone: exactly the
/// picture hosts showed before (<see cref="FrameCompositor"/>).</para>
/// <para>Not yet: palette effects. A replacement shows its own colours whatever the palette does (fades, colour cycling,
/// recoloured figures and markers keep their asset's picture); later work.</para>
/// New code (replacement graphics, not part of the game). Deterministic: the same screen and pack give the same items.
/// </remarks>
public sealed class HdRenderer(HdPack pack)
{
    private readonly RgbaImage _base = new(Screen.Width, Screen.Height);
    private readonly Dictionary<int, RgbaImage?> _replacements = [];
    private Dictionary<DrawingKey, (bool[] Owned, RgbaImage Texture)> _masked = [];

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
        var masked = new Dictionary<DrawingKey, (bool[] Owned, RgbaImage Texture)>();
        foreach (var (key, drawing) in drawings)
        {
            var replacement = _replacements[key.Owner]!;
            var texture = Texture(key, drawing, replacement);
            if (!ReferenceEquals(texture, replacement))
            {
                masked[key] = (drawing.Owned, texture);
            }
            items.Add(new DrawItem(texture, Destination(drawing, output), DrawFilter.Linear, drawing));
        }
        _masked = masked;
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

    /// <summary>The drawings of replaced assets on the screen, in the order their first pixel comes (row by row).</summary>
    private List<(DrawingKey Key, AssetPlacement Drawing)> FindDrawings(Screen screen)
    {
        var found = new Dictionary<DrawingKey, AssetPlacement>();
        var order = new List<(DrawingKey, AssetPlacement)>();
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                var i = y * Screen.Width + x;
                var owner = screen.Owners[i];
                var texel = screen.Texels[i];
                if (owner == 0 || texel == ScreenTexel.Unknown || Replacement(owner) is null)
                {
                    continue;
                }
                var (width, height) = ScreenOwners.SizeOf(owner);
                var (u, v, flipX, flipY) = ScreenTexel.Unpack(texel);
                if (u >= width || v >= height)
                {
                    continue;
                }
                var column = flipX ? width - 1 - u : u;
                var row = flipY ? height - 1 - v : v;
                var key = new DrawingKey(owner, x - column, y - row, flipX, flipY);
                if (!found.TryGetValue(key, out var drawing))
                {
                    drawing = new AssetPlacement(ScreenOwners.NameOf(owner)!, key.X, key.Y, width, height, flipX, flipY, new bool[width * height]);
                    found[key] = drawing;
                    order.Add((key, drawing));
                }
                drawing.Owned[row * width + column] = true;
            }
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

    /// <summary>
    /// The replacement turned as the drawing, transparent outside its owned pixels: the pack's picture itself when the
    /// drawing is whole and unturned, the last frame's when the drawing has not changed.
    /// </summary>
    private RgbaImage Texture(DrawingKey key, AssetPlacement drawing, RgbaImage replacement)
    {
        if (!drawing.FlipX && !drawing.FlipY && Array.TrueForAll(drawing.Owned, owned => owned))
        {
            return replacement;
        }
        if (_masked.TryGetValue(key, out var last) && last.Owned.AsSpan().SequenceEqual(drawing.Owned))
        {
            return last.Texture;
        }
        var (width, height) = (replacement.Width, replacement.Height);
        var texture = new RgbaImage(width, height);
        for (var py = 0; py < height; py++)
        {
            var row = (int)((long)py * drawing.Height / height);
            var sourceRow = drawing.FlipY ? height - 1 - py : py;
            for (var px = 0; px < width; px++)
            {
                var column = (int)((long)px * drawing.Width / width);
                if (drawing.Owns(column, row))
                {
                    texture.Argb[py * width + px] = replacement.Argb[sourceRow * width + (drawing.FlipX ? width - 1 - px : px)];
                }
            }
        }
        return texture;
    }

    private readonly record struct DrawingKey(int Owner, int X, int Y, bool FlipX, bool FlipY);
}
