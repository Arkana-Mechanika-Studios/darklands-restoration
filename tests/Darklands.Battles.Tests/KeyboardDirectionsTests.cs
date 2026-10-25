using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>The direction keys as the game's keyboard interrupt keeps them: one at a time, cleared when it is let go.</summary>
public sealed class KeyboardDirectionsTests
{
    [Fact]
    public void OneDirectionKeyAtATimeAndItsBreakClearsIt()
    {
        var d = new KeyboardDirections();
        d.Feed(0x48, released: false, 0);
        Assert.Equal((0, -0x5A), (d.X, d.Y));
        // a second key while the first is held is ignored, and so is its break
        d.Feed(0x4D, released: false, 10);
        d.Feed(0x4D, released: true, 20);
        Assert.Equal((0, -0x5A), (d.X, d.Y));
        d.Feed(0x48, released: true, 30);
        Assert.Equal((0, 0), (d.X, d.Y));
    }

    [Fact]
    public void TheCornerKeysGiveBothDirectionsAndAQuickRepeatTheLargerSize()
    {
        var d = new KeyboardDirections();
        d.Feed(0x49, released: false, 0);       // PgUp: up and right
        Assert.Equal((0x5A, -0x5A), (d.X, d.Y));
        d.Feed(0x49, released: true, 50);
        d.Feed(0x49, released: false, 100);     // again within 5 BIOS ticks
        Assert.Equal((0x7F, -0x7F), (d.X, d.Y));
        d.Feed(0x49, released: true, 120);
        d.Feed(0x4C, released: false, 2000);    // keypad 5: held, no direction
        d.Feed(0x50, released: false, 2010);
        Assert.Equal((0, 0), (d.X, d.Y));
    }

    [Fact]
    public void EnterIsKeptApartAndTheMapsClearingLeavesTheKeyHeld()
    {
        var d = new KeyboardDirections();
        d.Feed(0x48, released: false, 0);
        d.Feed(0x1C, released: false, 10);      // DS:0882 = 0xFF, the direction stays
        Assert.Equal((0, -0x5A, true), (d.X, d.Y, d.Enter));
        d.Feed(0x1C, released: true, 20);       // DS:0882 = 0
        Assert.False(d.Enter);
        d.Feed(0x1C, released: false, 30);
        d.ClearForTheMap();                     // R030+0x0662: the three bytes, not DS:0884
        Assert.Equal((0, 0, false), (d.X, d.Y, d.Enter));
        d.Feed(0x48, released: false, 40);      // a repeat of the held key is still ignored
        Assert.Equal((0, 0), (d.X, d.Y));
        d.Feed(0x48, released: true, 50);
        d.Feed(0x48, released: false, 2000);
        Assert.Equal((0, -0x5A), (d.X, d.Y));
    }
}
