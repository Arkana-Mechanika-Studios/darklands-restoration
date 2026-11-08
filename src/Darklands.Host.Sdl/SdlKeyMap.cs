using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>Translates SDL keyboard events (USB scancodes) into game key codes.</summary>
internal static class SdlKeyMap
{
    private const int ModShift = SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift;
    private const int ModCtrl = SdlNative.KeyModLeftCtrl | SdlNative.KeyModRightCtrl;
    private const int ModAlt = 0x0100 | 0x0200;

    // SDL scancode → PC scan code for keys without a character.
    private static readonly Dictionary<int, int> Special = new()
    {
        [40] = Keys.Enter, [41] = Keys.Escape, [42] = Keys.Backspace, [43] = Keys.Tab, [44] = Keys.Space,
        [82] = Keys.Up, [81] = Keys.Down, [80] = Keys.Left, [79] = Keys.Right,
        [74] = Keys.Home, [77] = Keys.End, [75] = Keys.PageUp, [78] = Keys.PageDown,
        [88] = Keys.Enter,                                  // keypad Enter
        [96] = Keys.Up, [90] = Keys.Down, [92] = Keys.Left, [94] = Keys.Right,
        [95] = Keys.Home, [89] = Keys.End, [97] = Keys.PageUp, [91] = Keys.PageDown, [93] = Keys.Center,
    };

    /// <summary>
    /// The game key of a key press: <paramref name="sym"/> is the layout's unshifted key (the key events' symbol), <paramref name="shifted"/>
    /// the layout's key with Shift (0 when Shift is not held). With Shift a symbol key gives the layout's shifted character, as the DOS
    /// keyboard driver gives it (AZERTY's '=' key: '+', its '&amp;' key: '1'; US QWERTY's '=' key: '+'); a letter gives its capital and a
    /// digit itself, as before. The keypad's minus and plus give the BIOS's words (4A2Dh, 4E2Bh).
    /// </summary>
    // orig: root 047C:0744 (read-key: the BIOS word of INT 16h, the scan code high, the character low)
    // DIVERGENCE: host: SDL3's key events give the unshifted key whatever the layout (SDL_KEYCODE_OPTIONS "none"), so the shifted
    //   character is looked up from the layout (SdlHost.ShiftedKey); under DOS the BIOS with the layout's driver gives the shifted
    //   character with the US key's scan code for the US characters (Keys.ForCharacter), whatever key the layout has it on.
    public static int ToGameKey(int scancode, int sym, int mod, int shifted = 0)
    {
        if (scancode is 86 or 87)                         // keypad minus, keypad plus
        {
            var word = scancode == 86 ? 0x4A2D : 0x4E2B;
            return (mod & ModAlt) != 0 ? word & 0xFF00 : word;
        }
        if (scancode is >= 58 and <= 67)                  // F1..F10
        {
            var n = scancode - 58;
            if ((mod & ModCtrl) != 0 && n < 5)
            {
                return 0x5E00 + (n << 8);                  // Ctrl-F1..F5 make a member the leader
            }
            return Keys.F1 + (n << 8);
        }
        if (scancode is 68 or 69)                         // F11, F12
        {
            return scancode == 68 ? Keys.F11 : Keys.F12;
        }
        if (Special.TryGetValue(scancode, out var special))
        {
            return special;
        }
        if (sym is < 32 or > 126)
        {
            return Keys.None;
        }
        var c = (char)sym;
        if ((mod & ModAlt) != 0)
        {
            return Keys.ForCharacter(c) & 0xFF00;         // Alt keys carry no character
        }
        if ((mod & ModShift) != 0)
        {
            c = char.IsAsciiLetterOrDigit(c) || shifted is < 32 or > 126 ? char.ToUpperInvariant(c) : (char)shifted;
        }
        return Keys.ForCharacter(c);
    }
}
