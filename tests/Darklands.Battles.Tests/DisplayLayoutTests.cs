using Darklands.Platform;

namespace Darklands.Battles.Tests;

/// <summary>The host window: 720×400 base, the text screen at its own size, the graphics screen doubled and centred.</summary>
public sealed class DisplayLayoutTests
{
    [Fact]
    public void TheWindowIsTheTextScreensSizeUnlessScaled()
    {
        Assert.Equal((720, 400), DisplayLayout.WindowSize(null, null));
        Assert.Equal((1440, 800), DisplayLayout.WindowSize(2, null));
        Assert.Equal((1920, 1080), DisplayLayout.WindowSize(null, (1920, 1080)));
        Assert.Throws<ArgumentException>(() => DisplayLayout.WindowSize(2, (800, 600)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayLayout.WindowSize(0, null));
        Assert.Equal((1440, 800), DisplayLayout.ParseResolution("1440x800"));
        Assert.Throws<FormatException>(() => DisplayLayout.ParseResolution("1440"));
        Assert.Throws<FormatException>(() => DisplayLayout.ParseResolution("-1x5"));
    }

    [Fact]
    public void TheTextScreenFillsTheBaseWindowPixelForPixel()
    {
        Assert.Equal(new DisplayRectangle(0, 0, 720, 400), DisplayLayout.Text(720, 400));
        Assert.Equal(new DisplayRectangle(0, 0, 2160, 1200), DisplayLayout.Text(2160, 1200));
    }

    [Fact]
    public void TheGraphicsScreenIsDoubledAndCentred()
    {
        Assert.Equal(new DisplayRectangle(40, 0, 640, 400), DisplayLayout.Graphics(720, 400));
        Assert.Equal(new DisplayRectangle(80, 0, 1280, 800), DisplayLayout.Graphics(1440, 800));
        // a 4:3 window: the 16:10 picture keeps its aspect, black above and below
        Assert.Equal(new DisplayRectangle(0, 50, 800, 500), DisplayLayout.Graphics(800, 600));
    }

    [Fact]
    public void WindowPixelsMapBackToTheGameScreen()
    {
        var graphics = DisplayLayout.Graphics(720, 400);
        Assert.Equal((0, 0), DisplayLayout.ToScreen(graphics, 40, 0));
        Assert.Equal((0, 0), DisplayLayout.ToScreen(graphics, 10, 0)); // the black border clamps
        Assert.Equal((160, 100), DisplayLayout.ToScreen(graphics, 360, 200));
        Assert.Equal((319, 199), DisplayLayout.ToScreen(graphics, 719, 399));
    }

    [Fact]
    public void SdlsSoftwareRendererIsFollowedInItsTruncatedFixedPoint()
    {
        // 1366x768: the picture is 1228x768 at x 69; window column 364 is picture column 295, whose centre is in game
        // pixel 77 but which the software renderer fills from 76 (as CI's Linux runner showed), and row 564 likewise
        var graphics = DisplayLayout.Graphics(1366, 768);
        Assert.Equal((77, 147), DisplayLayout.ToScreen(graphics, 364, 564));
        Assert.Equal((76, 146), DisplayLayout.ToScreen(graphics, 364, 564, PictureSampling.SdlSoftware));
        Assert.Equal((0, 0), DisplayLayout.ToScreen(graphics, 10, 0, PictureSampling.SdlSoftware)); // the black border clamps
        Assert.Equal((319, 199), DisplayLayout.ToScreen(graphics, 1365, 767, PictureSampling.SdlSoftware));

        // the closed form is SDL's loop (SDL_stretch.c: posx = incx / 2, then posx += incx per window pixel)
        foreach (var shown in new[] { 320, 480, 640, 913, 1228, 1280, 1920, 2304 })
        {
            var inc = (320L << 16) / shown;
            var pos = inc / 2;
            for (var n = 0; n < shown; n++, pos += inc)
            {
                var r = new DisplayRectangle(0, 0, shown, 200);
                Assert.Equal((int)(pos >> 16), DisplayLayout.ToScreen(r, n, 0, PictureSampling.SdlSoftware).X);
            }
        }
    }
}
