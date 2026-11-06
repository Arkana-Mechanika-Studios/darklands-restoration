namespace Darklands.Presentation.Battles;

/// <summary>A draw-list entry's depth: its map x, and its map y less and plus its height (+0x0E, +0x10, +0x12 of the entry).</summary>
public readonly record struct DepthKey(int X, int Top, int Bottom);

/// <summary>The order the battle's sprite list (list C) is drawn in: the entries as queued, insertion-sorted by depth.</summary>
public static class DepthOrder
{
    /// <summary>
    /// Sorts <paramref name="list"/> in place: each entry from the second on moves back past every earlier entry that lies beyond it,
    /// and stops at the first that does not. An earlier entry stays before a later one when its top is not below the later one's
    /// bottom, and it either ends above the later one's top or is not east of it. The test is not a strict order (entries of
    /// height overlap), so the result depends on the queue order, as in the original.
    /// </summary>
    // orig: R014+0x97A6 (14F7:2F96) checked against the asm: entry 0 is the sentinel (given the entry's keys); the walk back stops at
    // C: prev.top <= cur.bottom && (prev.bottom < cur.top || prev.x <= cur.x) (R014+0x9801..0x9819)
    // DIVERGENCE: the walk back has no lower bound in the original: the sentinel (entry 0 given cur's keys) stops it only when cur's top is
    //   not below its bottom. An entry with top > bottom walks past entry 0 through the words below the list (20A5:3474.., the floor-object
    //   table, then the member colours 20A5:33FC.. and the door states), each shifted up by one word and read as an entry pointer, until a
    //   word happens to pass the test: the member colours keep that shift after the battle and a save stores it (the players' "neon hair":
    //   the colours of real saves are the stock ones moved 2 bytes up, 00 00 first; MicroProse's README says a battle character's colours
    //   change in battle). Here the walk stops at entry 0 (j > 0: the same order whenever cur's top <= bottom). No inverted entry was found:
    //   0 in about 6,500 watched sorts of the 37 oracle fights, 0 of 2,575,015 furniture cells of 20,700 built battlefields; bodies, missiles,
    //   cursors and walls have fixed or ordered rows. The trigger is unknown, so what the walk reads is the memory below the list.
    //   C: every store of the list's count DS:E162 read (round coverage): the six writers of an entry's rows are the two cursors (R014+0x6DCE,
    //   +0x6E76: an entry's rows + 1 each), the copy of the entry before (R014+0x7306..0x73A6: its rows + 1), the sprite entry R014+0x7BBE (y - h,
    //   y + h with h 0 or 2), the wall pieces R014+0x827A (both rows the same value) and R014+0x8724 (rows given: the fixed pairs, and the
    //   furniture's max(dy - hh + row, row + 1) .. min(row + dy + hh, row + 14), inverted only for a .frc record with dy - hh > 14 or dy + hh < 1,
    //   which no record the shipped battlefields place has). So with the shipped data no entry has its top below its bottom and no play reaches the
    //   walk; the shifted colours of the real saves (started in an earlier version, 483.04) come from a state these writers do not make.
    public static void Sort<T>(IList<T> list, Func<T, DepthKey> key)
    {
        for (var k = 1; k < list.Count; k++)
        {
            var cur = list[k];
            var c = key(cur);
            var j = k;
            while (j > 0 && MovesBehind(key(list[j - 1]), c))
            {
                list[j] = list[j - 1];
                j--;
            }
            list[j] = cur;
        }
    }

    /// <summary>The earlier entry <paramref name="prev"/> must come after <paramref name="cur"/>.</summary>
    public static bool MovesBehind(DepthKey prev, DepthKey cur) =>
        prev.Top > cur.Bottom || (prev.Bottom >= cur.Top && prev.X > cur.X);
}
