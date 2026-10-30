using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// Output to the text screen the way the program start writes it: the BIOS cursor, the game's
/// coloured string writer, and DOS/C-library printing (teletype: the cells keep their colour).
/// </summary>
public sealed class TextConsole(TextScreen screen)
{
    /// <summary>The colour of the game's string writer; it keeps the last FF xx escape for the next call.</summary>
    // orig: root 044A:00A8, the byte in front of the writer (asm): initially 0Fh, rewritten by every FF xx escape
    private byte _attribute = 0x0F;

    public TextScreen Screen => screen;

    /// <summary>Mode 3 is set: the screen is blank and the cursor at the top left.</summary>
    // orig: int 10h AX = 3
    public void SetMode() => screen.Clear();

    // orig: text-cursor-position-call R000+0x0092 (0E76:0092) draft, bytes matched: int 10h AH = 2, page 0
    public void SetCursor(int row, int column)
    {
        screen.CursorRow = row;
        screen.CursorColumn = column;
    }

    /// <summary>Writes a string at a position; FF xx changes the colour of what follows (and of later calls).</summary>
    // orig: root 044A:00A9 (asm): int 10h AH = 2 unless row and column are both FFh; then per byte: FFh xx sets the
    // colour (an FFh 00 pair ends the string), a NUL ends it, any other byte is written as int 10h AH = 9 (a space in
    // the colour, CX = 1) followed by AH = 0Eh (teletype: the character, the cursor advances)
    public void Write(int row, int column, ReadOnlySpan<byte> text)
    {
        if (row != 0xFF || column != 0xFF)
        {
            SetCursor(row, column);
        }
        for (var i = 0; i < text.Length && text[i] != 0; i++)
        {
            if (text[i] == 0xFF)
            {
                if (++i >= text.Length || text[i] == 0)
                {
                    return;
                }
                _attribute = text[i];
                continue;
            }
            screen.Put(screen.CursorRow, screen.CursorColumn, (byte)' ', _attribute);
            Teletype(text[i]);
        }
    }

    /// <summary>DOS function 9: prints up to the '$'.</summary>
    // orig: dos-text-print root 044A:00F4 (asm): int 21h AH = 9
    public void DosPrint(ReadOnlySpan<byte> text)
    {
        foreach (var c in text)
        {
            if (c == (byte)'$')
            {
                return;
            }
            Teletype(c);
        }
    }

    /// <summary>The C library's printf to the screen: text-mode output turns "\n" into CR LF.</summary>
    public void Printf(string text)
    {
        foreach (var c in text)
        {
            if (c == '\n')
            {
                Teletype((byte)'\r');
            }
            Teletype(c < 0x100 ? (byte)c : (byte)'?');
        }
    }

    /// <summary>The BIOS teletype in text mode: the character goes into the cell, its colour stays.</summary>
    // orig: int 10h AH = 0Eh: BEL rings, BS steps back, CR returns, LF goes down (scrolling the screen at the bottom
    // with the colour of the cell under the cursor), others are written and the cursor wraps after column 79
    public void Teletype(byte c)
    {
        switch (c)
        {
            case 0x07:
                return;
            case 0x08:
                screen.CursorColumn = Math.Max(0, screen.CursorColumn - 1);
                return;
            case 0x0D:
                screen.CursorColumn = 0;
                return;
            case 0x0A:
                LineFeed();
                return;
        }
        screen.Characters[screen.CursorRow * TextScreen.Columns + screen.CursorColumn] = c;
        if (++screen.CursorColumn == TextScreen.Columns)
        {
            screen.CursorColumn = 0;
            LineFeed();
        }
    }

    private void LineFeed()
    {
        if (screen.CursorRow < TextScreen.Rows - 1)
        {
            screen.CursorRow++;
            return;
        }
        var fill = screen.Attributes[screen.CursorRow * TextScreen.Columns + screen.CursorColumn];
        Array.Copy(screen.Characters, TextScreen.Columns, screen.Characters, 0, TextScreen.Columns * (TextScreen.Rows - 1));
        Array.Copy(screen.Attributes, TextScreen.Columns, screen.Attributes, 0, TextScreen.Columns * (TextScreen.Rows - 1));
        for (var x = 0; x < TextScreen.Columns; x++)
        {
            screen.Put(TextScreen.Rows - 1, x, (byte)' ', fill);
        }
    }
}
