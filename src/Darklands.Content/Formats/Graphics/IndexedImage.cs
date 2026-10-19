namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Host-neutral indexed-color image surface.
/// </summary>
public sealed class IndexedImage
{
    public const int OriginalVgaWidth = 320;

    public const int OriginalVgaHeight = 200;

    private readonly byte[] _pixels;

    public IndexedImage(int width, int height, IEnumerable<byte> pixels, string? assetName = null)
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
        AssetName = assetName;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>
    /// Where the image came from, for replacement graphics (the screen's owner buffer); null for an image made at run time.
    /// </summary>
    /// <remarks>
    /// Not in the original. One scheme for every asset name (also <c>RowRunSprite.AssetName</c>), upper case:
    /// <list type="bullet">
    /// <item>a file of the game folder: its path there, <c>\</c> between folders: <c>PICS\STARTSCR.PIC</c>, <c>COMMONSP.IMG</c>;</item>
    /// <item>a member of a CAT archive: archive <c>/</c> member: <c>EINFO.CAT/E02SWST.PIC</c>, <c>BC/CAVE.FLC</c>;</item>
    /// <item>one image of several in a file or member: <c>#</c> its index, <c>#frame.facing</c> for an animation's
    /// frames, <c>@</c> its hex offset where the file finds its images by offset: <c>COMMONSP.IMG#12</c>,
    /// <c>INTRO.PAN#3</c>, <c>F01C.CAT/F01WKSW.IMC#3.5</c>, <c>BC/CAVE.FLC@1A40</c>;</item>
    /// <item>an image cut from another at run time: <c>kind:key</c>: <c>dropcap:D</c>.</item>
    /// </list>
    /// </remarks>
    public string? AssetName { get; }

    public static IndexedImage Blank(int width = OriginalVgaWidth, int height = OriginalVgaHeight)
    {
        return new IndexedImage(width, height, new byte[checked(width * height)]);
    }
}

