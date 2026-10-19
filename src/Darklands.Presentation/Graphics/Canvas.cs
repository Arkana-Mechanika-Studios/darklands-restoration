using Darklands.Content.Formats.Graphics;
using Darklands.Platform;

namespace Darklands.Presentation.Graphics;

/// <summary>
/// Drawing on the game screen: pictures, filled rectangles, saved regions and the palette.
/// All coordinates are screen pixels; drawing is clipped to the screen.
/// </summary>
/// <remarks>
/// Beside the pixels the canvas keeps <see cref="Screen.Owners"/>, which asset drew each pixel (not in the original;
/// it never changes a pixel): copying an image's pixels records the image's <see cref="IndexedImage.AssetName"/> for
/// the pixels written, and in <see cref="Screen.Texels"/> which of the image's pixels each one is; fills, outlines and
/// single pixels record no owner; saved regions and screens carry their owners back; a recolour keeps them.
/// </remarks>
public sealed class Canvas(Screen screen)
{
    public const int Width = Screen.Width;
    public const int Height = Screen.Height;

    private readonly byte[] _pixels = screen.Pixels;
    private readonly int[] _owners = screen.Owners;
    private readonly int[] _texels = screen.Texels;

    public Screen Screen => screen;

    public byte this[int x, int y]
    {
        get => (uint)x < Width && (uint)y < Height ? _pixels[y * Width + x] : (byte)0;
        set => Plot(x, y, value, 0);
    }

    /// <summary>
    /// Sets one pixel, the <see cref="ScreenOwners"/> id of the asset it belongs to and which of the asset's pixels it is
    /// (<see cref="ScreenTexel"/>), clipped to the screen.
    /// </summary>
    public void Plot(int x, int y, byte color, int owner, int texel = ScreenTexel.Unknown)
    {
        if ((uint)x < Width && (uint)y < Height)
        {
            _pixels[y * Width + x] = color;
            _owners[y * Width + x] = owner;
            _texels[y * Width + x] = owner == 0 ? 0 : texel;
        }
    }

    public void Clear(byte color)
    {
        Array.Fill(_pixels, color);
        Array.Clear(_owners);
        Array.Clear(_texels);
    }

    public void Fill(int x, int y, int width, int height, byte color)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            var from = Math.Max(0, x);
            var to = Math.Min(Width, x + width);
            if (to > from)
            {
                _pixels.AsSpan(row * Width + from, to - from).Fill(color);
                _owners.AsSpan(row * Width + from, to - from).Clear();
                _texels.AsSpan(row * Width + from, to - from).Clear();
            }
        }
    }

    /// <summary>The four lines of a frame from (x, y) to (x + width, y + height), both corners included.</summary>
    public void Outline(int x, int y, int width, int height, byte color)
    {
        Fill(x, y, width + 1, 1, color);
        Fill(x, y + height, width + 1, 1, color);
        Fill(x, y, 1, height + 1, color);
        Fill(x + width, y, 1, height + 1, color);
    }

    /// <summary>Copies a rectangle of a saved screen (the game's back page) onto the screen.</summary>
    public void CopyFrom(SavedScreen page, int x, int y, int width, int height)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            var from = Math.Max(0, x);
            var to = Math.Min(Width, x + width);
            if (to > from)
            {
                Array.Copy(page.Pixels, row * Width + from, _pixels, row * Width + from, to - from);
                if (page.Owners is { } owners)
                {
                    Array.Copy(owners, row * Width + from, _owners, row * Width + from, to - from);
                    CopyTexels(page.Texels, row * Width + from, to - from);
                }
                else
                {
                    _owners.AsSpan(row * Width + from, to - from).Clear();
                    _texels.AsSpan(row * Width + from, to - from).Clear();
                }
            }
        }
    }

    /// <summary>Draws an image; colour 0 is transparent when <paramref name="transparent"/> is set.</summary>
    public void Draw(IndexedImage image, int x, int y, bool transparent = false) =>
        Copy(image.Pixels, default, default, ScreenOwners.IdOf(image.AssetName, image.Width, image.Height), image.Width, image.Height, x, y, transparent);

    /// <summary>Draws raw pixels; they have no owner.</summary>
    public void Draw(ReadOnlySpan<byte> pixels, int width, int height, int x, int y, bool transparent = false) =>
        Copy(pixels, default, default, 0, width, height, x, y, transparent);

    /// <summary>
    /// Copies pixels to the screen; each pixel written gets its entry of <paramref name="owners"/> and
    /// <paramref name="texels"/>, or <paramref name="owner"/> and its own column and row when there are none.
    /// </summary>
    private void Copy(ReadOnlySpan<byte> pixels, ReadOnlySpan<int> owners, ReadOnlySpan<int> texels, int owner, int width, int height, int x, int y, bool transparent)
    {
        for (var row = 0; row < height; row++)
        {
            var sy = y + row;
            if ((uint)sy >= Height)
            {
                continue;
            }
            for (var column = 0; column < width; column++)
            {
                var sx = x + column;
                var color = pixels[row * width + column];
                if ((uint)sx < Width && !(transparent && color == 0))
                {
                    _pixels[sy * Width + sx] = color;
                    var pixelOwner = owners.IsEmpty ? owner : owners[row * width + column];
                    _owners[sy * Width + sx] = pixelOwner;
                    _texels[sy * Width + sx] = pixelOwner == 0 ? 0
                        : owners.IsEmpty ? ScreenTexel.Pack(column, row)
                        : texels.IsEmpty ? ScreenTexel.Unknown : texels[row * width + column];
                }
            }
        }
    }

    /// <summary>Draws an image turned over left to right and/or top to bottom; colour 0 is transparent.</summary>
    public void DrawMirrored(IndexedImage image, int x, int y, bool flipX, bool flipY)
    {
        var owner = ScreenOwners.IdOf(image.AssetName, image.Width, image.Height);
        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                var (sourceColumn, sourceRow) = (flipX ? image.Width - 1 - column : column, flipY ? image.Height - 1 - row : row);
                var color = image.Pixels[sourceRow * image.Width + sourceColumn];
                if (color != 0)
                {
                    Plot(x + column, y + row, color, owner, ScreenTexel.Pack(sourceColumn, sourceRow, flipX, flipY));
                }
            }
        }
    }

    /// <summary>Draws a picture and takes over its palette entries (the colours it defines).</summary>
    public void DrawPicture(Pic picture, int x = 0, int y = 0, bool transparent = false)
    {
        UsePalette(picture);
        Draw(picture.Image, x, y, transparent);
    }

    /// <summary>Copies the colours a picture defines into the current palette.</summary>
    public void UsePalette(Pic picture)
    {
        if (picture.EmbeddedPalette is { } chunk)
        {
            for (var i = chunk.StartIndex; i <= chunk.EndIndex && i < chunk.Entries.Count; i++)
            {
                if (chunk.Entries[i] is { } rgb)
                {
                    SetColor(i, rgb);
                }
            }
        }
    }

    public void UsePalette(Palette256 palette)
    {
        for (var i = 0; i < Palette256.ColorCount; i++)
        {
            SetColor(i, palette[i]);
        }
    }

    public void SetColor(int index, Rgb24 rgb) =>
        screen.Palette[index] = 0xFF000000u | (uint)rgb.R << 16 | (uint)rgb.G << 8 | rgb.B;

    /// <summary>Replaces one colour by another inside a rectangle (used to grey or highlight text); the owners stay.</summary>
    public void Recolor(int x, int y, int width, int height, byte from, byte to)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                ref var p = ref _pixels[row * Width + column];
                if (p == from)
                {
                    p = to;
                }
            }
        }
    }

    /// <summary>A copy of a screen region (with its owners), to restore later.</summary>
    public SavedRegion Save(int x, int y, int width, int height)
    {
        var copy = new byte[width * height];
        var owners = new int[width * height];
        var texels = new int[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                copy[row * width + column] = this[x + column, y + row];
                owners[row * width + column] = OwnerAt(x + column, y + row);
                texels[row * width + column] = owners[row * width + column] == 0 ? 0 : _texels[(y + row) * Width + x + column];
            }
        }
        return new SavedRegion(x, y, width, height, copy, owners, texels);
    }

    public void Restore(SavedRegion region) =>
        Copy(region.Pixels, region.Owners, region.Texels, 0, region.Width, region.Height, region.X, region.Y, transparent: false);

    /// <summary>Puts back a rectangle the way the driver draws a saved picture: its colour-0 pixels are transparent.</summary>
    // orig: gfx_save_rect drv+0x0BB7 (root 047C:04D5) keeps a rectangle as rows of [count, skip, pixels] without the zeros at the ends of
    //   each row (drv+0x0B15); gfx_draw_pic drv+0x0D7D / drv+0x0D9E (root 047C:05A5) draws such a picture skipping every colour-0 pixel;
    //   the callers that save under a box recolour 0 to 5 around the save to keep their black. checked against asm
    public void RestoreTransparent(SavedRegion region) =>
        Copy(region.Pixels, region.Owners, region.Texels, 0, region.Width, region.Height, region.X, region.Y, transparent: true);

    /// <summary>A copy of the whole screen, palette and owners.</summary>
    public SavedScreen SaveScreen() => new((byte[])_pixels.Clone(), (uint[])screen.Palette.Clone(), (int[])_owners.Clone(), (int[])_texels.Clone());

    public void Restore(SavedScreen saved)
    {
        saved.Pixels.CopyTo(_pixels, 0);
        saved.Palette.CopyTo(screen.Palette, 0);
        if (saved.Owners is { } owners)
        {
            owners.CopyTo(_owners, 0);
            CopyTexels(saved.Texels, 0, _texels.Length);
        }
        else
        {
            Array.Clear(_owners);
            Array.Clear(_texels);
        }
    }

    /// <summary>Saved texels into the screen's from <paramref name="start"/> (none saved: the owned pixels' places are unknown).</summary>
    private void CopyTexels(int[]? texels, int start, int length)
    {
        if (texels is not null)
        {
            Array.Copy(texels, start, _texels, start, length);
            return;
        }
        for (var i = start; i < start + length; i++)
        {
            _texels[i] = _owners[i] == 0 ? 0 : ScreenTexel.Unknown;
        }
    }

    private int OwnerAt(int x, int y) => (uint)x < Width && (uint)y < Height ? _owners[y * Width + x] : 0;
}

/// <summary>
/// A saved rectangle of the screen; <paramref name="Owners"/> and <paramref name="Texels"/>: its <see cref="Screen.Owners"/>
/// and <see cref="Screen.Texels"/> (null: none; owners without texels: places unknown).
/// </summary>
public sealed record SavedRegion(int X, int Y, int Width, int Height, byte[] Pixels, int[]? Owners = null, int[]? Texels = null);

/// <summary>
/// A saved screen and palette; <paramref name="Owners"/> and <paramref name="Texels"/>: its <see cref="Screen.Owners"/> and
/// <see cref="Screen.Texels"/> (null: none; owners without texels: places unknown).
/// </summary>
public sealed record SavedScreen(byte[] Pixels, uint[] Palette, int[]? Owners = null, int[]? Texels = null);
