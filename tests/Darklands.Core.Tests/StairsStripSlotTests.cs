using System.Reflection;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Randomness;

namespace Darklands.Core.Tests;

/// <summary>
/// The stairs of a level above the ground floor (R006+0x34B8): a stair whose new room fails before the strip search copies the other
/// level's room and cuts it from strip slotn, which nothing has set yet. The original's slotn is then the word the planner's call left
/// ([bp-4] = AX at thunk 09C0:1BE9 = layout-room-list[level] - 1, R005+0x0300..0x032A), not 0.
/// </summary>
public sealed class StairsStripSlotTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Level 2 above level 1. Level 1: the keep room 0 (5..8, 5..8) and room 1 (11..13, 11..13), diagonal to it (Direction 6: the placement
    /// fails at once), holding a stair up at (12, 12) on side 0. Level 2: two rooms (0, 1; the last of the keep 1) and three strips that all
    /// overlap room 1's copy. The stair is the level's first, so slotn is still the call's word: 1 (two rooms).
    /// </summary>
    internal static CastleWork Craft(CastleGenerator g)
    {
        var w = (CastleWork)typeof(CastleGenerator).GetField("_w", Any)!.GetValue(g)!;
        void Set(string field, params int[] values)
        {
            var a = (int[])typeof(CastleGenerator).GetField(field, Any)!.GetValue(g)!;
            for (var i = 0; i < values.Length; i++)
            {
                a[i] = values[i];
            }
        }
        typeof(CastleGenerator).GetField("_level", Any)!.SetValue(g, 2);
        Set("_roomCount", 0, 2, 2);
        Set("_lastKeepRoom", 0, 0, 1);
        Set("_stripCount", 0, 0, 3);
        Set("_stairCount", 0, 1, 0);
        Set("_bounds", 5, 13, 5, 13);
        int[][] rooms1 = [[5, 8, 5, 8], [11, 13, 11, 13]];
        int[][] rooms2 = [[5, 8, 5, 8], [20, 22, 20, 22]];
        for (var r = 0; r < 2; r++)
        {
            for (var f = 0; f < 4; f++)
            {
                w.SetRoom(1, r, f, rooms1[r][f]);
                w.SetRoom(2, r, f, rooms2[r][f]);
            }
            w.SetRoomKind(1, r, 3);
            w.SetRoomKind(2, r, 3);
        }
        int[][] strips = [[9, 30, 10, 14, 0], [10, 30, 9, 15, 0], [8, 30, 11, 13, 0]];
        for (var s = 0; s < 3; s++)
        {
            for (var f = 0; f < 5; f++)
            {
                w.SetStrip(2, s, f, strips[s][f]);
            }
        }
        // the stair: x, y, room, link, kind (1 up), side
        int[] stair = [12, 12, 1, -1, 1, 0];
        for (var f = 0; f < 6; f++)
        {
            w.SetStairWord(1, 0, f, stair[f]);
        }
        return w;
    }

    internal static CastleGenerator NewGenerator(uint seed = 1)
    {
        var random = new MscRandom(seed);
        var context = new BattlefieldContext { Enemies = null!, Items = [], Tables = null!, Maps = null!, Random = random };
        return new CastleGenerator(new GenerationState(context, new BattlefieldForces(null!, [], null!, random)));
    }

    [Fact]
    public void AStairThatFailsBeforeTheStripSearchCutsTheStripOfTheLastRoomsIndex()
    {
        var g = NewGenerator();
        var w = Craft(g);
        int[] Strip(int s) => Enumerable.Range(0, 5).Select(f => w.Strip(2, s, f)).ToArray();
        var strip0 = Strip(0);
        var strip1 = Strip(1);

        typeof(CastleGenerator).GetMethod("ContinueStairs", Any)!.Invoke(g, []);

        // the copy of level 1's room 1 is level 2's room 2, cut from strip 1 (slotn = 2 rooms - 1), strip 0 untouched
        Assert.Equal(13, w.Room(2, 2, 1));
        Assert.Equal(strip0, Strip(0));
        Assert.NotEqual(strip1, Strip(1));
    }
}
