using Darklands.Platform;

namespace Darklands.Host.Sdl.Tests;

/// <summary>The developer's inspector over the window (I-2): its key, the pin, where the frame and the box go.</summary>
public sealed class InspectorOverlayTests
{
    private static SdlNative.SdlEvent Key(uint type, int scancode, int mod)
    {
        var e = new SdlNative.SdlEvent { Type = type };
        e.Keyboard.Type = type;
        e.Keyboard.Scancode = scancode;
        e.Keyboard.Mod = (ushort)mod;
        return e;
    }

    [Fact]
    public void F9PressedOrLetGoIsTheInspectorsKey()
    {
        Assert.True(SdlHost.IsInspectorKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeF9, 0)));
        Assert.True(SdlHost.IsInspectorKey(Key(SdlNative.EventKeyUp, SdlNative.ScancodeF9, 0)));
        Assert.True(SdlHost.IsInspectorKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeF9, 0x40)));   // Ctrl+F9 pins
        Assert.False(SdlHost.IsInspectorKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeF11, 0)));
    }

    [Fact]
    public void CtrlF9PinsThePixelAndGivesItsLinesThenLetsItGo()
    {
        var asked = new List<(int, int)>();
        var overlay = new InspectorOverlay((x, y) =>
        {
            asked.Add((x, y));
            return [$"pixel {x},{y}"];
        });
        Assert.False(overlay.Visible);
        Assert.Equal((5, 6), overlay.Target(5, 6));
        Assert.Equal(["pixel 10,20"], overlay.TogglePin(10, 20));
        Assert.True(overlay.Visible);
        Assert.Equal((10, 20), overlay.Target(5, 6));   // the pinned pixel, wherever the pointer goes
        Assert.Null(overlay.TogglePin(30, 40));
        Assert.False(overlay.Visible);
        Assert.Equal([(10, 20)], asked);
        overlay.Held = true;
        Assert.True(overlay.Visible);
    }

    [Fact]
    public void ThePixelsCellScalesWithThePicture()
    {
        var picture = new DisplayRectangle(40, 0, 1280, 800);   // 4x the 320x200 frame
        Assert.Equal(new DisplayRectangle(40, 0, 4, 4), InspectorOverlay.PixelCell(picture, 0, 0));
        Assert.Equal(new DisplayRectangle(40 + 319 * 4, 199 * 4, 4, 4), InspectorOverlay.PixelCell(picture, 319, 199));
        // a picture smaller than the frame still frames one drawable pixel
        Assert.Equal(1, InspectorOverlay.PixelCell(new DisplayRectangle(0, 0, 160, 100), 3, 3).Width);
    }

    [Fact]
    public void TheBoxGoesRightAndBelowElseTheOtherSideAndStaysInTheWindow()
    {
        var cell = new DisplayRectangle(100, 100, 4, 4);
        Assert.Equal((109, 109), InspectorOverlay.BoxPlace(cell, 200, 50, 1280, 800, 5));
        var corner = new DisplayRectangle(1270, 790, 4, 4);
        Assert.Equal((1270 - 5 - 200, 790 - 5 - 50), InspectorOverlay.BoxPlace(corner, 200, 50, 1280, 800, 5));
        // too wide for either side: kept inside the window
        Assert.Equal((0, 109), InspectorOverlay.BoxPlace(cell, 2000, 50, 1280, 800, 5));
    }
}
