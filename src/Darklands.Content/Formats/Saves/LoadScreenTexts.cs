using Darklands.Content.Formats.Executable;

namespace Darklands.Content.Formats.Saves;

/// <summary>
/// The three button texts of the load screen in DARKLAND.EXE ("Load Saved Game", "Delete Saved
/// Game", "Return to Main Menu", with the colour codes of their key letters).
/// </summary>
// orig: R136+0x8690.s[0..2], the far pointers at 290E:1DD3 (the struct the C declares as `{ char _p[0x1dd3]; char far *s[3]; } R136+0x8690`),
// drawn by R037+0x8B0E (1C7D:095E) matched; the table is the one CreationDataDecoder reads
// from 290E:1DDF on (three entries earlier); tests: LoadScreenTests
public static class LoadScreenTexts
{
    private const int Segment = 0x290E;
    private const int Table = 0x1DD3;

    public static IReadOnlyList<string> Decode(byte[] exe)
    {
        var texts = ProgramData.Load(exe, Segment).StringTable(Segment, Table, 3);
        if (!texts[0].Contains("oad Saved Game", StringComparison.Ordinal))
        {
            throw new InvalidDataException("DARKLAND.EXE is not version 483.07: the load screen's texts are not where expected.");
        }
        return texts;
    }
}
