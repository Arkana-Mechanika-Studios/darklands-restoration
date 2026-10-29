using Darklands.Launcher;
using Darklands.Platform;

namespace Darklands.Host.Sdl.Tests;

/// <summary>The SDL launcher's typing without text events, and the game folder given at the start (found on Linux, 2026-10-29).</summary>
public sealed class LinuxLauncherTests
{
    private const int ShiftLeft = SdlNative.KeyModLeftShift;

    private static IEnumerable<LauncherInput> Key(TypedKeys typing, char key, int modifiers = 0, long now = 0) =>
        typing.Take(SdlNative.EventKeyDown, key, modifiers, null, now);

    private static IEnumerable<LauncherInput> Text(TypedKeys typing, string text, long now = 0) =>
        typing.Take(SdlNative.EventTextInput, 0, 0, new TextTyped(text), now);

    [Fact]
    public void WithoutTextEventsThePrintableKeysAreTyped()
    {
        var typing = new TypedKeys { On = true };
        var typed = "~/Games/DARKLAND".SelectMany(c =>
        {
            var shifted = c is '~' or >= 'A' and <= 'Z';
            var key = c switch { '~' => '`', >= 'A' and <= 'Z' => char.ToLowerInvariant(c), _ => c };
            return Key(typing, key, shifted ? ShiftLeft : 0);
        });
        Assert.Equal("~/Games/DARKLAND", string.Concat(typed.OfType<TextTyped>().Select(t => t.Text)));
        // Ctrl+V and the keys the launcher reads itself are not text; Caps Lock turns letters upper case
        Assert.Empty(Key(typing, 'v', SdlNative.KeyModLeftCtrl));
        Assert.Equal("D", Assert.IsType<TextTyped>(Assert.Single(Key(typing, 'd', SdlNative.KeyModCaps))).Text);
        Assert.Empty(typing.Take(SdlNative.EventKeyDown, SdlNative.KeyReturn, 0, null, 0));
        Assert.Empty(new TypedKeys { On = false }.Take(SdlNative.EventKeyDown, 'a', 0, null, 0));
    }

    [Fact]
    public void WhenTextEventsComeTheyAloneCountAndAreNotTypedTwice()
    {
        var typing = new TypedKeys { On = true };
        Assert.Equal("a", Assert.IsType<TextTyped>(Assert.Single(Key(typing, 'a', now: 100))).Text);
        // SDL's own text for the same key, a moment later: dropped (already typed)
        Assert.Empty(Text(typing, "a", now: 120));
        // from now on only SDL's text: the key gives nothing, its text event the character
        Assert.Empty(Key(typing, 'b', now: 200));
        Assert.Equal("b", Assert.IsType<TextTyped>(Assert.Single(Text(typing, "b", now: 210))).Text);
        // the launcher's other input passes unchanged
        var press = new KeyPressed(LauncherKey.Enter);
        Assert.Equal(press, Assert.Single(typing.Take(SdlNative.EventKeyDown, SdlNative.KeyReturn, 0, press, 300)));
    }

    [Fact]
    public void AFolderGivenAtTheStartIsFixedOnlyWhenItHoldsTheGame()
    {
        var environment = (Func<string, string?>)(_ => null);
        var good = LauncherHost.GivenGameFolder(new CommandLineOptions { GameFolder = "/home/p/Games/DARKLAND" }, environment, _ => true);
        Assert.Equal("/home/p/Games/DARKLAND", good.Given);
        Assert.Equal("the command line", good.FixedKeys["gameFolder"]);

        var wrong = LauncherHost.GivenGameFolder(new CommandLineOptions { GameFolder = "/home/p/nothing" }, environment, _ => false);
        Assert.Equal("/home/p/nothing", wrong.Given);
        Assert.False(wrong.FixedKeys.ContainsKey("gameFolder"));

        var variable = LauncherHost.GivenGameFolder(new CommandLineOptions(), name => name == "DARKLANDS_DIR" ? "/opt/darklands" : null, _ => true);
        Assert.Equal("/opt/darklands", variable.Given);
        Assert.Equal("DARKLANDS_DIR", variable.FixedKeys["gameFolder"]);

        Assert.Null(LauncherHost.GivenGameFolder(new CommandLineOptions(), environment, _ => true).Given);
    }
}
