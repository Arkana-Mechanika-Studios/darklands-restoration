using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Graphics;
using Darklands.Platform;

namespace Darklands.Battles.Tests;

/// <summary>
/// The drawing of Canvas and SpriteDrawing as it was before the owner buffer (pixels and palette only), kept unchanged
/// as the reference <see cref="CanvasOwnershipTests"/> compares the real canvas with: the owner buffer must not change a pixel.
/// </summary>
internal sealed class PixelOnlyCanvas(Screen screen)
{
    private const int Width = Screen.Width;
    private const int Height = Screen.Height;

    private readonly byte[] _pixels = screen.Pixels;

    public Screen Screen => screen;

    public byte this[int x, int y]
    {
        get => (uint)x < Width && (uint)y < Height ? _pixels[y * Width + x] : (byte)0;
        set
        {
            if ((uint)x < Width && (uint)y < Height)
            {
                _pixels[y * Width + x] = value;
            }
        }
    }

    public void Clear(byte color) => Array.Fill(_pixels, color);

    public void Fill(int x, int y, int width, int height, byte color)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            var from = Math.Max(0, x);
            var to = Math.Min(Width, x + width);
            if (to > from)
            {
                _pixels.AsSpan(row * Width + from, to - from).Fill(color);
            }
        }
    }

    public void Outline(int x, int y, int width, int height, byte color)
    {
        Fill(x, y, width + 1, 1, color);
        Fill(x, y + height, width + 1, 1, color);
        Fill(x, y, 1, height + 1, color);
        Fill(x + width, y, 1, height + 1, color);
    }

    public void CopyFrom(byte[] page, int x, int y, int width, int height)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            var from = Math.Max(0, x);
            var to = Math.Min(Width, x + width);
            if (to > from)
            {
                Array.Copy(page, row * Width + from, _pixels, row * Width + from, to - from);
            }
        }
    }

    public void Draw(IndexedImage image, int x, int y, bool transparent = false) =>
        Draw(image.Pixels, image.Width, image.Height, x, y, transparent);

    public void Draw(ReadOnlySpan<byte> pixels, int width, int height, int x, int y, bool transparent = false)
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
                }
            }
        }
    }

    public void DrawMirrored(IndexedImage image, int x, int y, bool flipX, bool flipY)
    {
        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                var color = image.Pixels[(flipY ? image.Height - 1 - row : row) * image.Width + (flipX ? image.Width - 1 - column : column)];
                if (color != 0)
                {
                    this[x + column, y + row] = color;
                }
            }
        }
    }

    public void DrawPicture(Pic picture, int x = 0, int y = 0, bool transparent = false)
    {
        if (picture.EmbeddedPalette is { } chunk)
        {
            for (var i = chunk.StartIndex; i <= chunk.EndIndex && i < chunk.Entries.Count; i++)
            {
                if (chunk.Entries[i] is { } rgb)
                {
                    screen.Palette[i] = 0xFF000000u | (uint)rgb.R << 16 | (uint)rgb.G << 8 | rgb.B;
                }
            }
        }
        Draw(picture.Image, x, y, transparent);
    }

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

    public (int X, int Y, int Width, int Height, byte[] Pixels) Save(int x, int y, int width, int height)
    {
        var copy = new byte[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                copy[row * width + column] = this[x + column, y + row];
            }
        }
        return (x, y, width, height, copy);
    }

    public void Restore((int X, int Y, int Width, int Height, byte[] Pixels) region, bool transparent = false) =>
        Draw(region.Pixels, region.Width, region.Height, region.X, region.Y, transparent);

    public (byte[] Pixels, uint[] Palette) SaveScreen() => ((byte[])_pixels.Clone(), (uint[])screen.Palette.Clone());

    public void Restore((byte[] Pixels, uint[] Palette) saved)
    {
        saved.Pixels.CopyTo(_pixels, 0);
        saved.Palette.CopyTo(screen.Palette, 0);
    }

    public void DrawSprite(RowRunSprite sprite, int x, int y, (int Left, int Top, int Right, int Bottom) clip, byte[]? remap)
    {
        for (var row = 0; row < sprite.RowCount; row++)
        {
            var sy = y + row;
            if (sy < clip.Top || sy >= clip.Bottom)
            {
                continue;
            }
            var (start, pixels) = sprite.Row(row);
            for (var i = 0; i < pixels.Length; i++)
            {
                var sx = x + start + i;
                if (pixels[i] != 0 && sx >= clip.Left && sx < clip.Right)
                {
                    this[sx, sy] = remap is null ? pixels[i] : remap[pixels[i]];
                }
            }
        }
    }
}
