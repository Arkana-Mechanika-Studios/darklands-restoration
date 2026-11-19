using Darklands.Core.World;

namespace Darklands.Core.Rules;

/// <summary>How the party information screen ranks fame, standing, town sizes and directions (the screens word them).</summary>
public static class PartyStanding
{
    /// <summary>The number of fame ranks (entries of the fame-title table R136+0xA60B).</summary>
    public const int FameRanks = 11;

    // orig: R136+0xA60B (fame titles), read by R034+0x11DC with fame / 50
    // DIVERGENCE: negative fame indexes before the table in the original (fame / 50 < 0); kept at rank 0 (the first title) here.
    /// <summary>The fame rank 0..10, one per 50 points of fame (the last from 500 on): the entry of the fame-title table.</summary>
    public static int FameRank(int fame) => Math.Clamp(fame / 50, 0, FameRanks - 1);

    // orig: R136+0xA813 (standing words), indexed by place-query(location, 1)
    /// <summary>The party's standing band at a location (<see cref="LocationRules.ReputationBand"/>, 0..5): the entry of the standing words.</summary>
    public static int StandingBand(GameState s, int location) => LocationRules.Query(s, location, 1);

    // orig: the size switch of R034+0x0EE6 and R034+0x144C (matched); each reads its own copy of the three words
    /// <summary>The town's size class, the entry of the size words: 0 up to size 4, 1 for 5 and 6, else 2.</summary>
    public static int SizeClass(int size) => size switch
    {
        >= 0 and <= 4 => 0,
        5 or 6 => 1,
        _ => 2,
    };

    // orig: R030+0x29FA (1462:29FA) draft (called through thunk 09C0:1FBD; bytes match); tests: CampAndSheetRulesTests, TextAndOpeningTests
    // C: dx and dy are |difference| (dy divided by 3); a tie goes to the diagonal; "x2 <= x1" and "y2 <= y1" send equal coordinates west / north.
    //   The one copy: the Party Info screen, the cards' direction text variables (cases 57, 58, 69, 71, 72) and the developer console use it.
    /// <summary>
    /// The direction (0 north … 7 northwest) in which (x2, y2) lies from (x1, y1), y counted a
    /// third: straight when one axis is at most half the other.
    /// </summary>
    public static int Direction(int x1, int y1, int x2, int y2)
    {
        var dx = Math.Abs(x2 - x1);
        var dy = Math.Abs(y2 - y1) / 3;
        var north = y2 <= y1;
        if (dx / 2 >= dy)
        {
            return x2 <= x1 ? 6 : 2;
        }
        if (dy / 2 >= dx)
        {
            return north ? 0 : 4;
        }
        return x2 <= x1 ? (north ? 7 : 5) : (north ? 1 : 3);
    }

    // orig: R034+0x1A86 (1550:0BA6) matched; tests: CampAndSheetRulesTests
    /// <summary>
    /// The town nearest a world position (within 10, by the map distance) among the leading
    /// towns of the location list (it stops at the first non-town), or −1.
    /// </summary>
    public static int NearestTown(GameState s, int x, int y)
    {
        var best = -1;
        var bestDistance = 9999;
        for (var i = 0; i < s.Locations.Count && i < 414; i++)
        {
            Location place = s.Locations[i];
            if (place.Icon != 0)
            {
                break;
            }
            var d = LocationRules.Distance(x, y, place.X, place.Y);
            if (d < 10 && bestDistance > d)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }
}
