using Darklands.Platform;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The CPU reference of every picture filter on real frames, for a person to judge and tune the looks without a window; not a
/// check. With DARKLANDS_PICFILTER_PREVIEW set to "&lt;folder of 320x200 PNG frames&gt;;&lt;output folder&gt;" each frame is drawn
/// at 1728x1080 (a 1920x1080 window's picture) through each filter, saved as &lt;frame&gt;-&lt;filter&gt;.png, with
/// &lt;frame&gt;-sheet.png: the same 360x225 piece under every filter side by side. Without the variable it does nothing.
/// </summary>
public sealed class PictureFilterPreview
{
    [Fact]
    public void DrawTheReferenceOfEveryFilter()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_PICFILTER_PREVIEW") is not { Length: > 0 } setting)
        {
            return;
        }
        var parts = setting.Split(';');
        var output = Directory.CreateDirectory(parts[1]).FullName;
        const int width = 1728, height = 1080, pieceWidth = 360, pieceHeight = 225, gap = 6;
        foreach (var file in Directory.GetFiles(parts[0], "*.png").Order())
        {
            var frame = PngCodec.Read(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var filters = Enum.GetValues<PictureFilter>();
            var sheet = new uint[(pieceWidth + gap) * filters.Length * pieceHeight];
            var at = (X: width / 2 - pieceWidth / 2 + 120, Y: height / 3);
            Parallel.For(0, filters.Length, i =>
            {
                var shown = PictureFilterReference.Render(frame.Argb, frame.Width, frame.Height, filters[i], width, height);
                PngCodec.Write(new RgbaImage(width, height, shown), Path.Combine(output, $"{name}-{PictureFilters.Name(filters[i])}.png"));
                for (var y = 0; y < pieceHeight; y++)
                {
                    Array.Copy(shown, (at.Y + y) * width + at.X, sheet, y * (pieceWidth + gap) * filters.Length + i * (pieceWidth + gap), pieceWidth);
                }
            });
            PngCodec.Write(new RgbaImage((pieceWidth + gap) * filters.Length, pieceHeight, sheet), Path.Combine(output, $"{name}-sheet.png"));
        }
    }
}
