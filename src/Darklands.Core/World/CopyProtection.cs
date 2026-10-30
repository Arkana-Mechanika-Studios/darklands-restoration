using Darklands.Core.Randomness;

namespace Darklands.Core.World;

/// <summary>
/// The copy-protection question the first visit to the map asks: four of the twenty alchemical symbols are shown (one from 0..6, one from
/// 0..19, one from 7..13, one from 14..19, never two the same) and the player picks the one for a named substance. Only the choice is
/// kept here: the restoration does not ask the question (see DarklandsGame.Travel), but the choice's five random draws are made, so the
/// generator goes on as the original's does.
/// </summary>
// orig: copy-protection-check R041+0x0000 (1462:0000) matched; tests: CopyProtectionTests (vectors from the original's bytes: roll-below and _rand run, local/batch/nmB)
// C: the five roll-below calls (4, 20, 7, 7, 6) come first, before the window is set up; the loops that move a symbol off the second one draw nothing.
//   Every callee after them (picture loads, the graphics driver, the party panel, the key and mouse reads) draws nothing: with roll-below
//   counted over the whole call tree of a correct answer there are exactly these five (local/batch/nmB/rec).
// C: the window leaves DS:2812 (the back window's text colour) at 14 and DS:E7A2 / DS:E7A0 holding the two fonts it saved; every later
//   user of these three words sets them before reading them, so nothing of it is modelled.
// C: an answer: a click on a picture or the keys 1..4 (top row or keypad); a wrong one ends the program (R041+0x0594). The key 5 (0x0635 or
//   keypad 0x4C35) is taken as right whatever was asked (R041+0x050E), and Alt-Q quits.
public static class CopyProtection
{
    /// <summary>The four symbols shown (top left, bottom left, top right, bottom right) and which of them is asked for (0..3).</summary>
    public readonly record struct Question(int Asked, int TopLeft, int BottomLeft, int TopRight, int BottomRight)
    {
        /// <summary>The symbol (0..19) whose substance the question names (the table at 290E:1D73).</summary>
        public int Named => Asked switch { 0 => TopLeft, 1 => BottomLeft, 2 => TopRight, _ => BottomRight };
    }

    /// <summary>Draws the question as the original does (five draws).</summary>
    public static Question Draw(IRandomSource random)
    {
        var asked = random.Below(4);
        var bottomLeft = random.Below(20);
        var topLeft = random.Below(7);
        while (topLeft == bottomLeft)
        {
            topLeft = (topLeft + 1) % 7;
        }
        var topRight = random.Below(7) + 7;
        while (topRight == bottomLeft)
        {
            topRight++;
            if (topRight >= 14)
            {
                topRight = 7;
            }
        }
        var bottomRight = random.Below(6) + 14;
        while (bottomRight == bottomLeft)
        {
            bottomRight++;
            if (bottomRight >= 20)
            {
                bottomRight = 14;
            }
        }
        return new Question(asked, topLeft, bottomLeft, topRight, bottomRight);
    }
}
