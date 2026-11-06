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
}
