using Darklands.Platform;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// input.mouseWheel: a notch away from the player is a tap of ↑, towards the player ↓ (a flipped report counts the other way), at most
/// five taps an event; each tap reaches the game as the key does: its make and break codes and the BIOS word.
/// </summary>
public sealed class SdlWheelTests
{
    [Fact]
    public void ANotchIsATapOfUpOrDown()
    {
        Assert.Equal(1, SdlHost.WheelTaps(1, 0));
        Assert.Equal(-2, SdlHost.WheelTaps(-2, 0));
        Assert.Equal(-1, SdlHost.WheelTaps(1, SdlNative.MouseWheelFlipped));
        Assert.Equal(5, SdlHost.WheelTaps(40, 0));
        Assert.Equal(0, SdlHost.WheelTaps(0, 0));
        Assert.Equal((Keys.Up, 0x48, 82), SdlHost.WheelKey(up: true));
        Assert.Equal((Keys.Down, 0x50, 81), SdlHost.WheelKey(up: false));
    }

    [Fact]
    public void ATapReachesTheGameAsTheKeyDoes()
    {
        static (List<int> Keys, List<(int, bool)> Codes) Tap(PcKeyboard keyboard)
        {
            var keys = new List<int>();
            var codes = new List<(int, bool)>();
            while (keyboard.TryReadKey(out var key))
            {
                keys.Add(key);
            }
            while (keyboard.TryReadTransition(out var code, out var released))
            {
                codes.Add((code, released));
            }
            return (keys, codes);
        }
        var (key, scanCode, id) = SdlHost.WheelKey(up: false);
        var wheel = new PcKeyboard();
        wheel.KeyDown(key, scanCode, id, 0);
        wheel.KeyUp(scanCode, id);
        // the keyboard's own ↓ (SDL scancode 81), pressed and let go
        var hand = new PcKeyboard();
        hand.KeyDown(Keys.Down, 0x50, 81, 0);
        hand.KeyUp(0x50, 81);
        var (wheelKeys, wheelCodes) = Tap(wheel);
        var (handKeys, handCodes) = Tap(hand);
        Assert.Equal([Keys.Down], wheelKeys);
        Assert.Equal(handKeys, wheelKeys);
        Assert.Equal(handCodes, wheelCodes);
        Assert.Equal([(0x50, false), (0x50, true)], wheelCodes);
    }
}
