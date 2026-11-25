using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The preview's size (1.1.3): every pixel of its picture is drawn as the same whole number of the screen's pixels across and down,
/// at any display scaling, the largest that fits; a TrueType font's picture is never shrunk.
/// </summary>
public sealed class PreviewScaleTests
{
    private static PreviewResult Shown(int width, int scale = 1) => new(width, 50, [], 0, 0, 0, null, true) { Scale = scale };

    [Theory]
    [InlineData(162, 1, 520.0, 2, 1.0, 2)]      // the prayer screen's column beside a card's pane: twice
    [InlineData(162, 1, 520.0, 2, 1.25, 2)]     // at 125 %: two screen pixels a game pixel, not 2.5
    [InlineData(162, 1, 520.0, 2, 1.5, 3)]      // at 150 %: three
    [InlineData(260, 1, 520.0, 4, 1.0, 2)]      // a card text's window
    [InlineData(260, 1, 520.0, 4, 1.25, 2)]
    [InlineData(100, 1, 1000.0, 4, 1.5, 6)]     // a short screen text, at its most: four layout units, six screen pixels
    [InlineData(100, 1, 50.0, 4, 1.0, 1)]       // too wide for its room: at least its own pixels
    [InlineData(100, 4, 300.0, 4, 1.0, 4)]      // a TrueType picture (4 pixels a game pixel): shown at its own pixels, never 3 of 4
    [InlineData(60, 4, 1000.0, 4, 2.0, 8)]      // at 200 %: twice its own pixels
    public void ThePictureIsAWholeNumberOfScreenPixelsAPixel(int width, int pictureScale, double room, int most, double scaling, int screenPixels)
    {
        var scale = TextEditorView.Scale(Shown(width, pictureScale), room, most, scaling);
        // screen pixels a game pixel
        Assert.Equal(screenPixels, scale * scaling, 6);
        // and a whole number of them a picture pixel
        var perPicturePixel = scale * scaling / pictureScale;
        Assert.Equal(Math.Round(perPicturePixel), perPicturePixel, 6);
        Assert.True(perPicturePixel >= 1);
    }
}
