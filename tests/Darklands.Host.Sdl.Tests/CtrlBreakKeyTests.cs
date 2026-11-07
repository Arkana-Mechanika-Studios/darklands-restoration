namespace Darklands.Host.Sdl.Tests;

/// <summary>The keys the host gives the game as Ctrl-Break: Ctrl+Pause (SDL scancode 72 with a Ctrl key) and SDL's Cancel (155).</summary>
public sealed class CtrlBreakKeyTests
{
    [Fact]
    public void CtrlPauseAndCancelAreCtrlBreak()
    {
        Assert.True(SdlHost.IsCtrlBreak(72, SdlNative.KeyModLeftCtrl));
        Assert.True(SdlHost.IsCtrlBreak(72, SdlNative.KeyModRightCtrl));
        Assert.True(SdlHost.IsCtrlBreak(155, 0));
        Assert.False(SdlHost.IsCtrlBreak(72, 0));            // plain Pause
        Assert.False(SdlHost.IsCtrlBreak(71, SdlNative.KeyModLeftCtrl));   // Ctrl+Scroll Lock (not Break on the enhanced keyboard)
    }
}
