using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>How a host scales an item's picture to its destination.</summary>
public enum DrawFilter
{
    /// <summary>Each output pixel takes the nearest picture pixel (the game's own frame: hard pixels, as today).</summary>
    Nearest,

    /// <summary>Smooth (bilinear where the device has it): replacement pictures.</summary>
    Linear,
}

/// <summary>
/// Where an asset's drawing lies on the game screen and which of its pixels are still seen there.
/// </summary>
/// <param name="Asset">The asset's name (<c>IndexedImage.AssetName</c>).</param>
/// <param name="X">The drawing's left edge on the 320×200 screen (may be off the screen when it was clipped).</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Width">The asset's width in its own pixels.</param>
/// <param name="Height">Its height.</param>
/// <param name="FlipX">The asset was drawn turned over left to right.</param>
/// <param name="FlipY">Top to bottom.</param>
/// <param name="Owned">
/// Width × Height, row by row in screen orientation: whether the screen pixel at (X + column, Y + row) still shows this
/// drawing (not transparent there, not drawn over since, on the screen).
/// </param>
public sealed record AssetPlacement(string Asset, int X, int Y, int Width, int Height, bool FlipX, bool FlipY, bool[] Owned)
{
    public bool Owns(int column, int row) => Owned[row * Width + column];
}

/// <summary>
/// One picture for a host to draw: <see cref="Texture"/> stretched over <see cref="Destination"/> (output pixels), blended
/// by its alpha over what is already there.
/// </summary>
/// <param name="Texture">
/// The picture, ready to draw: already turned over as the asset was, transparent outside the pixels the asset owns.
/// The object and its <see cref="RgbaImage.Version"/> stay the same while its pixels do, so a host may keep it on the
/// graphics card; it is valid until the next <see cref="HdRenderer.Render"/>.
/// </param>
/// <param name="Destination">Where it goes, in output pixels (may reach outside the output; the parts outside are transparent).</param>
/// <param name="Filter">How to scale it.</param>
/// <param name="Placement">The asset drawing it replaces; null for the base layer (the game's own frame).</param>
public sealed record DrawItem(RgbaImage Texture, DisplayRectangle Destination, DrawFilter Filter, AssetPlacement? Placement);

/// <summary>A frame to show: the items in drawing order, the first being the game's frame over the whole output rectangle.</summary>
public sealed record HdFrame(DisplayRectangle Output, IReadOnlyList<DrawItem> Items)
{
    /// <summary>The game's own 320×200 frame (palette applied), scaled to <see cref="Output"/>.</summary>
    public DrawItem Base => Items[0];
}
