using System.IO.Compression;

namespace Darklands.Launcher.Drawing;

/// <summary>The launcher's built-in fonts, the project's badge and its art (embedded resources).</summary>
internal static class Assets
{
    /// <summary>The text font (EB Garamond; also the fallback for characters another font lacks).</summary>
    public static TrueTypeFont Text => LazyText.Value;

    /// <summary>Grenze Gotisch Regular: the display face's stand-in when its SemiBold is not embedded.</summary>
    public static TrueTypeFont Heading => LazyHeading.Value;

    public static Image Badge => LazyBadge.Value;

    private static readonly Lazy<TrueTypeFont> LazyText = new(() => new TrueTypeFont(Resource("EBGaramond12-Regular.ttf")!));
    private static readonly Lazy<TrueTypeFont> LazyHeading = new(() => FontOrText("GrenzeGotisch-Regular.ttf"));

    private static readonly Lazy<Image> LazyBadge = new(() =>
    {
        using var stream = new GZipStream(new MemoryStream(Resource("badge.argb.gz")!), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        stream.CopyTo(raw);
        var bytes = raw.ToArray();
        var width = BitConverter.ToInt32(bytes, 0);
        var height = BitConverter.ToInt32(bytes, 4);
        var argb = new uint[width * height];
        Buffer.BlockCopy(bytes, 8, argb, 0, argb.Length * 4);
        return new Image(width, height, argb);
    });

    /// <summary>EB Garamond Italic (captions, problem lines, the hint bar); EB Garamond when the file is not embedded.</summary>
    public static TrueTypeFont Italic => LazyItalic.Value;

    /// <summary>EB Garamond SemiBold (headings, the drop caps, the buttons); EB Garamond when the file is not embedded.</summary>
    public static TrueTypeFont Strong => LazyStrong.Value;

    /// <summary>Whether <see cref="Strong"/> is the real SemiBold (else the text is thickened when drawn).</summary>
    public static bool HasStrong => LazyStrong.Value != Text;

    /// <summary>The display face of "Darklands" and "Settings" (Grenze Gotisch SemiBold, else its Regular).</summary>
    public static TrueTypeFont Display => LazyDisplay.Value;

    private static readonly Lazy<TrueTypeFont> LazyItalic = new(() => FontOrText("EBGaramond-Italic.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyStrong = new(() => FontOrText("EBGaramond-SemiBold.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyDisplay = new(() => Resource("GrenzeGotisch-SemiBold.ttf") is { } file ? new TrueTypeFont(file) : Heading);

    private static readonly Dictionary<string, Image?> ArtCache = [];

    /// <summary>
    /// A picture of the launcher's art (Resources/art/&lt;name&gt;.png, put there by tools/launcher-art/Convert-LauncherArt.ps1;
    /// names and sizes in tools/launcher-art/ASSETS.md), or null: the launcher then draws its stand-in.
    /// </summary>
    public static Image? Art(string name)
    {
        lock (ArtCache)
        {
            if (!ArtCache.TryGetValue(name, out var image))
            {
                try
                {
                    image = Resource("art." + name + ".png") is { } file ? Png.Read(file) : null;
                }
                catch (InvalidDataException)
                {
                    image = null;
                }
                ArtCache[name] = image;
            }
            return image;
        }
    }

    /// <summary>
    /// A sliced picture's borders, in its pixels (Resources/art/&lt;name&gt;.slice.txt: "left top right bottom"): its corners and
    /// ends keep their shape, the rest stretches. Top and bottom 0: a plate in three slices. Null: not sliced.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom)? Slice(string name)
    {
        if (Resource("art." + name + ".slice.txt") is not { } file)
        {
            return null;
        }
        var parts = System.Text.Encoding.ASCII.GetString(file).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 && parts.All(p => int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            ? (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
               int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    private static TrueTypeFont FontOrText(string name) => Resource(name) is { } file ? new TrueTypeFont(file) : Text;

    private static byte[]? Resource(string name)
    {
        using var stream = typeof(Assets).Assembly.GetManifestResourceStream("Darklands.Launcher." + name);
        if (stream is null)
        {
            return null;
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
