namespace Darklands.Presentation;

/// <summary>The world map screen's globals that live as long as the program: no map start, load or battle resets them.</summary>
public sealed class OverlandMemory
{
    /// <summary>
    /// DS:290E is 1: the last pass with a vertical key was the first of a pair, so the next one moves the marker a pixel up or down.
    /// </summary>
    // orig: written only by R030+0x0C44 (`if (DS:290E++)` in the direction cases, `if (DS:290E > 1) DS:290E = 0` at +0x461); world-map-start
    //   R030+0x00A8 does not reset it: a write watch over gen-carry-06 (a walk, a battle taken up from its save, an Alt-L from it, the
    //   walk again) saw only 0c44's writes, and the second walk's first diagonal pass took the vertical pixel at once, the first walk's
    //   having ended on an odd pass
    public bool VerticalStep { get; set; }

    /// <summary>
    /// DS:8676: the direction of the last pass's step (1 north .. 8 north-west) while it walked on, else 0. A pass whose key is none of
    /// the map's direction keys (key 0, a letter, Enter, Alt-Q, after the rest or the camp) steps again in place while it is not 0.
    /// </summary>
    // orig: written only by R030+0x0C44: each direction case sets it (0x0D0F .. 0x0E54), case 0x4C35 sets 0 (0x0E5E), and a step that did not
    //   walk on (1300's result 0 or more: blocked, arrived, an encounter) sets 0 (0x10D6); the block at 0x10AB steps (`if (DS:8676)
    //   1300(DS:2866, DS:2868)`) whatever the case was; world-map-start does not reset it (every write in the EXE is one of these)
    public int StepDirection { get; set; }
}
