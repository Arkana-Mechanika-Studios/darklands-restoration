namespace Darklands.Platform;

/// <summary>
/// Game key codes: the PC keyboard scan code in the high byte and the character in the low
/// byte, as the original reads them from the BIOS (Enter = 0x1C0D, F1 = 0x3B00, Alt-Q = 0x1000).
/// </summary>
public static class Keys
{
    public const int None = 0;
    public const int Escape = 0x011B;
    public const int Backspace = 0x0E08;
    public const int Tab = 0x0F09;
    public const int Enter = 0x1C0D;
    public const int Space = 0x3920;

    public const int Up = 0x4800;
    public const int Down = 0x5000;
    public const int Left = 0x4B00;
    public const int Right = 0x4D00;
    public const int Home = 0x4700;
    public const int End = 0x4F00;
    public const int PageUp = 0x4900;
    public const int PageDown = 0x5100;
    public const int Center = 0x4C35;

    public const int F1 = 0x3B00;
    public const int F2 = 0x3C00;
    public const int F3 = 0x3D00;
    public const int F4 = 0x3E00;
    public const int F5 = 0x3F00;
    public const int F6 = 0x4000;
    public const int F7 = 0x4100;
    public const int F8 = 0x4200;
    public const int F9 = 0x4300;
    public const int F10 = 0x4400;
    public const int F11 = 0x8500;
    public const int F12 = 0x8600;

    /// <summary>Ctrl-F1..F5 (scan codes 0x5E..0x62): make that party member the leader.</summary>
    // orig: cases 0x5e00 .. 0x6200 of card-hotkeys R043+0x33C6 and R030+0x0C44 (the C comments call them Shift-F keys; Shift-F1 is 0x5400)
    public const int CtrlF1 = 0x5E00;
    public const int CtrlF5 = 0x6200;

    // orig: the scan code of each Alt key in hotkey-table root 0000:0CAA: Q 0x1000, O 0x1800, P 0x1900, S 0x1F00, D 0x2000, F 0x2100, L 0x2600, C 0x2E00, M 0x3200
    public const int AltO = 0x1800;
    public const int AltQ = 0x1000;
    public const int AltS = 0x1F00;
    public const int AltL = 0x2600;
    public const int AltP = 0x1900;
    public const int AltD = 0x2000;
    public const int AltF = 0x2100;
    public const int AltC = 0x2E00;
    public const int AltM = 0x3200;

    /// <summary>Scan codes of the letter keys A..Z (PC set 1).</summary>
    private static readonly byte[] LetterScanCodes =
    [
        0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
        0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C,
    ];

    /// <summary>The game code of a letter, digit or punctuation key typed as <paramref name="c"/>.</summary>
    public static int ForCharacter(char c)
    {
        if (c is >= 'a' and <= 'z')
        {
            return LetterScanCodes[c - 'a'] << 8 | c;
        }
        if (c is >= 'A' and <= 'Z')
        {
            return LetterScanCodes[c - 'A'] << 8 | c;
        }
        if (c is >= '1' and <= '9')
        {
            return (c - '1' + 2) << 8 | c;
        }
        return c switch
        {
            '0' => 0x0B30,
            ' ' => Space,
            '-' => 0x0C2D,
            '\'' => 0x2827,
            '.' => 0x342E,
            ',' => 0x332C,
            _ => c,
        };
    }

    /// <summary>The character of a key code, or '\0' for keys without one.</summary>
    public static char CharacterOf(int key) => (char)(key & 0xFF);

    public static bool IsLetter(int key, char letter) => char.ToLowerInvariant(CharacterOf(key)) == char.ToLowerInvariant(letter);
}
