using System.IO.Compression;

namespace Darklands.Launcher.Drawing;

/// <summary>The launcher's built-in fonts and the project's badge (embedded resources).</summary>
internal static class Assets
{
    /// <summary>The text font (EB Garamond; also the fallback for characters another font lacks).</summary>
    public static TrueTypeFont Text => LazyText.Value;

    /// <summary>Titles, the illuminated capitals and Play (Pirata One).</summary>
    public static TrueTypeFont Title => LazyTitle.Value;

    /// <summary>Section headings (Grenze Gotisch).</summary>
    public static TrueTypeFont Heading => LazyHeading.Value;

    /// <summary>The settings panel's labels and values (MedievalSharp).</summary>
    public static TrueTypeFont Panel => LazyPanel.Value;

    /// <summary>
    /// The folder fields' font (VT323, a terminal's), its size in the 1080x600 design, and the step its sizes keep to: a pixel
    /// font is sharp only at whole multiples of its pixel grid, VT323's being 12.5 pixels per em.
    /// </summary>
    public static (TrueTypeFont Font, double Size, double Step) Path => (LazyPath.Value, 25, 12.5);

    private static readonly Lazy<TrueTypeFont> LazyPath = new(() => FontOrText("VT323-Regular.ttf"));

    public static Image Badge => LazyBadge.Value;

    private static readonly Lazy<TrueTypeFont> LazyText = new(() => new TrueTypeFont(Resource("EBGaramond12-Regular.ttf")!));
    private static readonly Lazy<TrueTypeFont> LazyTitle = new(() => FontOrText("PirataOne-Regular.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyHeading = new(() => FontOrText("GrenzeGotisch-Regular.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyPanel = new(() => FontOrText("MedievalSharp-Regular.ttf"));

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
