using System.Runtime.InteropServices;
using Darklands.Platform;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// '+' and '-' reach the game as the original's words on every keyboard layout. SDL3's key events give the layout's unshifted key
/// (SDL_KEYCODE_OPTIONS "none"), so with Shift the host looks the shifted character up in the layout: AZERTY's '=' key gives '+'
/// (it gave '=' before, and '+' never arrived), its '&amp;' key '1'; the keypad's minus and plus give 4A2Dh and 4E2Bh (they gave nothing).
/// The table holds what SDL3 reports for each layout (measured: the French, German and US layouts loaded, SDL_GetKeyFromScancode).
/// </summary>
[Collection("SDL")] // SDL is not thread-safe: its tests run one at a time
public sealed class SdlKeyLayoutTests
{
    private const int Shift = SdlNative.KeyModLeftShift;

    // (layout, SDL scancode, the event's key, the layout's key with Shift, Shift held, the game key)
    public static TheoryData<string, int, int, int, bool, int> Presses() => new()
    {
        // French AZERTY: '+' is Shift with the '=' key (the US '=' place), '-' the '6' key, the digits Shift with the top row
        { "fr", 46, '=', '+', true, 0x0D2B },
        { "fr", 46, '=', '+', false, 0x0D3D },
        { "fr", 35, '-', '6', false, 0x0C2D },
        { "fr", 35, '-', '6', true, 0x0736 },
        { "fr", 30, '&', '1', true, 0x0231 },
        { "fr", 30, '&', '1', false, '&' },
        { "fr", 4, 'q', 'Q', true, 0x1051 },
        // German QWERTZ: '+' its own key (the US ']' place), '-' the US '/' place
        { "de", 48, '+', '*', false, 0x0D2B },
        { "de", 56, '-', '_', false, 0x0C2D },
        { "de", 30, '1', '!', true, 0x0231 },
        // US QWERTY: '+' is Shift with '=', '-' its own key; Shift with '-' is '_' as under DOS
        { "us", 46, '=', '+', true, 0x0D2B },
        { "us", 46, '=', '+', false, 0x0D3D },
        { "us", 45, '-', '_', false, 0x0C2D },
        { "us", 45, '-', '_', true, '_' },
        { "us", 4, 'a', 'A', true, 0x1E41 },
        { "us", 4, 'a', 'A', false, 0x1E61 },
        // the keypad's minus and plus on every layout (SDL's keypad keycodes)
        { "fr", 86, 0x40000056, 0x40000056, false, 0x4A2D },
        { "fr", 87, 0x40000057, 0x40000057, false, 0x4E2B },
        { "us", 87, 0x40000057, 0x40000057, true, 0x4E2B },
    };

    [Theory]
    [MemberData(nameof(Presses))]
    public void APressGivesTheGameTheLayoutsCharacter(string layout, int scancode, int key, int shifted, bool shift, int expected)
    {
        Assert.False(string.IsNullOrEmpty(layout));
        Assert.Equal(expected, SdlKeyMap.ToGameKey(scancode, key, shift ? Shift : 0, shift ? shifted : 0));
    }

    [Fact]
    public void AltWithTheKeypadsPlusOrMinusCarriesNoCharacter()
    {
        Assert.Equal(0x4E00, SdlKeyMap.ToGameKey(87, 0x40000057, 0x0100, 0));
        Assert.Equal(0x4A00, SdlKeyMap.ToGameKey(86, 0x40000056, 0x0100, 0));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string id, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    /// <summary>
    /// The real SDL3 keymap of each layout (Windows: the layout loaded for this thread only, the thread's own layout put back after): the
    /// '+' and '-' keys of the layout, through the host's lookup, give the original's words.
    /// </summary>
    [Theory]
    [InlineData("0000040C", 46, true, 35, false)]   // French AZERTY: Shift+'=' key, the '6' key
    [InlineData("00000407", 48, false, 56, false)]  // German QWERTZ: the '+' key, the '-' key
    [InlineData("00000409", 46, true, 45, false)]   // US QWERTY: Shift+'=' key, the '-' key
    public void TheLayoutsPlusAndMinusKeysGiveTheOriginalsWords(string layout, int plusKey, bool plusShift, int minusKey, bool minusShift)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        var library = File.Exists(bundled) ? bundled : LocalPaths.Get("sdl", "DARKLANDS_SDL3_PATH");
        if (!OperatingSystem.IsWindows() || library is null || !File.Exists(library))
        {
            return;
        }
        var before = GetKeyboardLayout(0);
        var hkl = LoadKeyboardLayout(layout, 0);
        if (hkl == IntPtr.Zero)
        {
            return;
        }
        ActivateKeyboardLayout(hkl, 0);
        try
        {
            SdlNative.ConfigureLibrary(library);
            SdlNative.SDL_SetHint(SdlNative.HintKeycodeOptions, "none");
            Assert.True(SdlNative.SDL_Init(SdlNative.InitVideo));
            try
            {
                int Press(int scancode, bool shift)
                {
                    var mod = shift ? Shift : 0;
                    var key = SdlNative.SDL_GetKeyFromScancode(scancode, (ushort)mod, true);
                    var shifted = shift ? SdlNative.SDL_GetKeyFromScancode(scancode, Shift, false) : 0;
                    return SdlKeyMap.ToGameKey(scancode, key, mod, shifted);
                }
                Assert.Equal('+', Keys.CharacterOf(Press(plusKey, plusShift)));
                Assert.Equal('-', Keys.CharacterOf(Press(minusKey, minusShift)));
                Assert.Equal(0x0C2D, Press(minusKey, minusShift));
                Assert.Equal(0x4E2B, Press(87, false));
                Assert.Equal(0x4A2D, Press(86, false));
            }
            finally
            {
                SdlNative.SDL_Quit();
            }
        }
        finally
        {
            ActivateKeyboardLayout(before, 0);
        }
    }
}
