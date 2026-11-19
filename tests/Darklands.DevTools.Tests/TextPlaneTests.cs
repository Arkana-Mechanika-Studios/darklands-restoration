using Darklands.Platform;

namespace Darklands.DevTools.Tests;

[CollectionDefinition("ScreenTextPlane", DisableParallelization = true)]
public sealed class ScreenTextPlaneCollection;

/// <summary>The text plane (I-3): a text drawn by key names its key under the inspector; nothing is kept while not recording.</summary>
[Collection("ScreenTextPlane")]
public sealed class TextPlaneTests
{
    [Fact]
    public void AKeyedTextNamesItsKeyArgumentsAndWidestDrawing()
    {
        ScreenTextPlane.Recording = true;
        try
        {
            var shown = ScreenTextPlane.Keyed("Ritter holds the purse of 3fl", "exe:DS:2F71", "Ritter, 3");
            Assert.Equal("Ritter holds the purse of 3fl", shown);
            var id = ScreenTextPlane.Drawn(shown, 90);
            Assert.Equal(id, ScreenTextPlane.Drawn(shown, 120));
            Assert.Equal(id, ScreenTextPlane.Drawn(shown, 60));
            var frame = new Screen();
            frame.Texels[5 * Screen.Width + 7] = id;   // a letter's pixel: no owner, the text's id
            var lines = Inspector.Describe(frame, 7, 5, new InspectedMoment(-1));
            Assert.Equal(
            [
                "pixel 7,5: palette index 0",
                "asset: none recorded",
                "text exe:DS:2F71 (Ritter, 3)",
                "  \"Ritter holds the purse of 3fl\", drawn 120 px wide at most",
            ], lines);
            Assert.Contains(ScreenTextPlane.KeyedTexts(), e => e.Key == "exe:DS:2F71" && e.WidestDrawn == 120);
        }
        finally
        {
            ScreenTextPlane.Recording = false;
        }
    }

    [Fact]
    public void AKeyedTextKeepsTheLeastRoomItWasDrawnInAndItsFont()
    {
        ScreenTextPlane.Recording = true;
        try
        {
            var shown = ScreenTextPlane.Keyed("Leave the plane test", "exe:DS:2FF2", null, [KeyValuePair.Create("arg0", "x")]);
            ScreenTextPlane.Drawn(shown, 40, 200, windowed: false, font: 1);
            ScreenTextPlane.Drawn(shown, 40, 64, windowed: true, font: 1);
            ScreenTextPlane.Drawn(shown, 40, 90, windowed: false, font: 1);
            var e = Assert.Single(ScreenTextPlane.KeyedTexts(), t => t.Text == shown);
            Assert.Equal((64, true, 1), (e.Room, e.Windowed, e.Font));
            Assert.Equal("x", Assert.Single(e.Values!).Value);
        }
        finally
        {
            ScreenTextPlane.Recording = false;
        }
    }

    [Fact]
    public void ALiteralSaysItHasNoKeyAndNothingIsKeptWhileNotRecording()
    {
        Assert.Equal(0, ScreenTextPlane.Drawn("not kept", 10));
        ScreenTextPlane.Recording = true;
        try
        {
            var id = ScreenTextPlane.Drawn("a literal of the inspector test", 33);
            var frame = new Screen();
            frame.Texels[0] = id;
            Assert.Equal(["text (no key)", "  \"a literal of the inspector test\", drawn 33 px wide at most"],
                Inspector.Describe(frame, 0, 0, new InspectedMoment(-1)).Skip(2));
        }
        finally
        {
            ScreenTextPlane.Recording = false;
        }
    }
}
