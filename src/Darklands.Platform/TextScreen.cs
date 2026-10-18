namespace Darklands.Platform;

/// <summary>
/// The PC's 80×25 colour text screen (video mode 3): one character (code page 437) and one
/// attribute byte per cell, and the blinking hardware cursor. The game shows its start-up banner
/// and its start-up messages on it before it switches to the 320×200 graphics screen.
/// </summary>
// orig: BIOS video mode 3 (int 10h, AX = 3), set by the start-up overlay at R000+0x0114 before the banner is written.
public sealed class TextScreen
{
    public const int Columns = 80;
    public const int Rows = 25;

    /// <summary>The attribute a mode set leaves in every cell: light grey on black.</summary>
    public const byte DefaultAttribute = 0x07;

    public TextScreen() => Clear();

    /// <summary>Code page 437 character codes, row by row.</summary>
    public byte[] Characters { get; } = new byte[Columns * Rows];

    /// <summary>Attributes, row by row: background in bits 4..6 (bit 7 blinks), foreground in bits 0..3.</summary>
    public byte[] Attributes { get; } = new byte[Columns * Rows];

    public int CursorRow { get; set; }

    public int CursorColumn { get; set; }

    /// <summary>The underline cursor is shown (it blinks; the host times the blinking).</summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>What a mode set does: blank cells in light grey on black, the cursor at the top left.</summary>
    public void Clear()
    {
        Array.Fill(Characters, (byte)' ');
        Array.Fill(Attributes, DefaultAttribute);
        CursorRow = 0;
        CursorColumn = 0;
    }

    public void Put(int row, int column, byte character, byte attribute)
    {
        Characters[row * Columns + column] = character;
        Attributes[row * Columns + column] = attribute;
    }

    public void CopyTo(TextScreen other)
    {
        Characters.CopyTo(other.Characters, 0);
        Attributes.CopyTo(other.Attributes, 0);
        other.CursorRow = CursorRow;
        other.CursorColumn = CursorColumn;
        other.CursorVisible = CursorVisible;
    }
}
