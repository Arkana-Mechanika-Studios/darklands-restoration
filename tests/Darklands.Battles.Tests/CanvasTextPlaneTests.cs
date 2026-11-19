using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

[CollectionDefinition("ScreenTextPlane", DisableParallelization = true)]
public sealed class ScreenTextPlaneCollection;

/// <summary>
/// The developer's text plane (I-3, Screen.Texels where Owners is 0): the letters of a text drawn while recording carry the text's
/// id, the pixels are the same as without it, a saved region keeps the ids, and a later drawing over a letter clears it.
/// </summary>
[Collection("ScreenTextPlane")]
public sealed class CanvasTextPlaneTests
{
    private static int Count(Screen s, int id) => s.Texels.Where((t, i) => s.Owners[i] == 0 && t == id).Count();

    [Fact]
    public void TheLettersCarryTheTextsIdAndThePixelsDoNotChange()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var painter = new TextPainter(new GameAssets(files).Font(1));
        var plain = new Canvas(new Screen());
        painter.Draw(plain, 10, 20, "Leave", 15);
        Assert.All(plain.Screen.Texels, t => Assert.Equal(0, t));
        ScreenTextPlane.Recording = true;
        try
        {
            var shown = ScreenTextPlane.Keyed("Leave", "exe:DS:2FF2");
            var canvas = new Canvas(new Screen());
            painter.Draw(canvas, 10, 20, shown, 15);
            Assert.Equal(plain.Screen.Pixels, canvas.Screen.Pixels);
            var id = ScreenTextPlane.Drawn("Leave", 0);
            Assert.Equal("exe:DS:2FF2", ScreenTextPlane.Of(id)!.Value.Key);
            var letters = plain.Screen.Pixels.Count(p => p == 15);
            Assert.Equal(letters, Count(canvas.Screen, id));

            // a saved region keeps the plane and puts it back
            var saved = canvas.Save(0, 0, 100, 40);
            canvas.Fill(0, 0, 100, 40, 0);
            Assert.Equal(0, Count(canvas.Screen, id));
            canvas.Restore(saved);
            Assert.Equal(letters, Count(canvas.Screen, id));

            // a pixel drawn over a letter is no longer the text's
            var at = Array.IndexOf(canvas.Screen.Texels, id);
            canvas[at % Canvas.Width, at / Canvas.Width] = 3;
            Assert.Equal(letters - 1, Count(canvas.Screen, id));
        }
        finally
        {
            ScreenTextPlane.Recording = false;
        }
    }
}
