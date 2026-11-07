namespace Darklands.Platform.Tests;

/// <summary>Ctrl-Break as the BIOS keyboard interrupt delivers it: the buffer emptied, reported once (the game then finds 0000h, GameUi).</summary>
public sealed class PcKeyboardCtrlBreakTests
{
    [Fact]
    public void CtrlBreakEmptiesTheBufferAndIsReportedOnce()
    {
        var keyboard = new PcKeyboard();
        keyboard.KeyDown(Keys.Enter, 0x1C, 1, 0);
        keyboard.KeyUp(0x1C, 1);
        keyboard.KeyDown('a', 0, 2, 10);
        keyboard.KeyUp(0, 2);
        Assert.False(keyboard.TakeCtrlBreak());
        keyboard.CtrlBreak();
        Assert.False(keyboard.TryReadKey(out _));
        Assert.True(keyboard.TakeCtrlBreak());
        Assert.False(keyboard.TakeCtrlBreak());
        // a key typed after it waits as usual (behind the word 0000h the game keeps)
        keyboard.KeyDown('b', 0, 3, 20);
        Assert.True(keyboard.TryReadKey(out var key));
        Assert.Equal('b', key);
    }
}
