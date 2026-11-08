namespace Darklands.Host.Sdl.Tests;

/// <summary>The frameless launcher window: what a point of it does (SDL's hit test), from its edges and the launcher's controls.</summary>
public sealed class LauncherHitTestTests
{
    // a 1280 x 720 window shown at twice its pixels (high DPI), with two controls
    private static readonly (int X, int Y, int Width, int Height)[] Controls = [(2000, 1200, 400, 100), (2500, 10, 50, 40)];

    private static int At(int x, int y, bool fills = false) => SdlLauncherSurface.Region(x, y, 1280, 720, 2560, 1440, Controls, fills);

    [Theory]
    [InlineData(2, 2, SdlNative.HitTestResizeTopLeft)]
    [InlineData(640, 1, SdlNative.HitTestResizeTop)]
    [InlineData(1278, 3, SdlNative.HitTestResizeTopRight)]
    [InlineData(1279, 360, SdlNative.HitTestResizeRight)]
    [InlineData(1275, 716, SdlNative.HitTestResizeBottomRight)]
    [InlineData(640, 719, SdlNative.HitTestResizeBottom)]
    [InlineData(0, 715, SdlNative.HitTestResizeBottomLeft)]
    [InlineData(5, 360, SdlNative.HitTestResizeLeft)]
    public void TheEdgesResizeTheWindow(int x, int y, int expected) => Assert.Equal(expected, At(x, y));

    [Fact]
    public void TheControlsAreClickedAndTheRestDragsTheWindow()
    {
        // the first control is at window (1000..1200, 600..650), the second (the close button) at (1250..1275, 5..25)
        Assert.Equal(SdlNative.HitTestNormal, At(1100, 620));
        Assert.Equal(SdlNative.HitTestNormal, At(1260, 15));
        Assert.Equal(SdlNative.HitTestDraggable, At(999, 620));
        Assert.Equal(SdlNative.HitTestDraggable, At(1100, 650));
        Assert.Equal(SdlNative.HitTestDraggable, At(640, 360));
        Assert.Equal(SdlNative.HitTestDraggable, At(SdlLauncherSurface.ResizeEdge, SdlLauncherSurface.ResizeEdge));
    }

    [Fact]
    public void TheFramelessWindowIsActivatedWithoutPaintingAFrame()
    {
        // WM_NCACTIVATE goes on to SDL told not to paint the frame (lParam -1); WM_NCPAINT stops; the rest passes as it is
        Assert.Equal((true, (IntPtr)(-1)), FramelessWindow.Filter(0x0086, 1));
        Assert.Equal((false, IntPtr.Zero), FramelessWindow.Filter(0x0085, 1));
        Assert.Equal((true, (IntPtr)1234), FramelessWindow.Filter(0x0201, 1234));
    }

    [Fact]
    public void AWindowThatFillsTheScreenIsNeitherMovedNorResized()
    {
        Assert.Equal(SdlNative.HitTestNormal, At(1, 1, fills: true));
        Assert.Equal(SdlNative.HitTestNormal, At(640, 360, fills: true));
    }
}
