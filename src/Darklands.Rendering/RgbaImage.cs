namespace Darklands.Rendering;

/// <summary>
/// A true-colour picture: one 0xAARRGGBB value per pixel, row by row (the format of <see cref="Platform.Screen.Palette"/>).
/// </summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height, uint[]? argb = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (argb is not null && argb.Length != width * height)
        {
            throw new ArgumentException($"{width}x{height} needs {width * height} pixels, not {argb.Length}.", nameof(argb));
        }
        Width = width;
        Height = height;
        Argb = argb ?? new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Argb { get; }

    /// <summary>
    /// Counts the changes of <see cref="Argb"/> by its maker: a host that keeps the picture on the graphics card uploads it
    /// again when the version moved. The same object with the same version holds the same pixels.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Marks the pixels as changed (<see cref="Version"/>).</summary>
    public void Changed() => Version++;
}
