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
