namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Host-neutral indexed-color image surface.
/// </summary>
public sealed class IndexedImage
{
    public const int OriginalVgaWidth = 320;

    public const int OriginalVgaHeight = 200;

    private readonly byte[] _pixels;

    public IndexedImage(int width, int height, IEnumerable<byte> pixels)
    {
        if (pixels is null)
        {
            throw new ArgumentNullException(nameof(pixels));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Frame height must be positive.");
        }

        _pixels = pixels.ToArray();
        if (_pixels.Length != checked(width * height))
        {
            throw new ArgumentException("Indexed frame pixel count must equal width multiplied by height.", nameof(pixels));
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<byte> Pixels => _pixels;

    public static IndexedImage Blank(int width = OriginalVgaWidth, int height = OriginalVgaHeight)
    {
        return new IndexedImage(width, height, new byte[checked(width * height)]);
    }
}

