using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>The loot file (R014 19CD 3204/3258/33C0): blocks appended, taken out with the later offsets moving down, equal items merged.</summary>
public sealed class LootStoreTests
{
    private static ItemStack I(int code, int quality, int count) => new(code, 1, quality, count, 1);

    [Fact]
    public void TakingABlockMovesTheLaterOffsetsAndMarksItsPiles()
    {
        var store = new LootStore();
        var a = store.Append([I(1, 20, 1), I(2, 30, 1)], 2);
        var b = store.Append([I(3, 25, 1)], 1);
        var pa = new LootPile { Level = 0, Offset = a, Count = 2 };
        var pb = new LootPile { Level = 0, Offset = b, Count = 1 };
        store.Piles.AddRange([pa, pb]);
        store.ChestOffsets[0] = b;
        Assert.Equal(0, a);
        Assert.Equal(12, b);
        var work = new List<ItemStack>();
        store.Take(work, 0, [2], [a], 1);
        Assert.Equal(2, work.Count);
        Assert.Equal(-1, pa.Offset);
        Assert.Equal(0, pb.Offset);
        Assert.Equal(0, store.ChestOffsets[0]);
        Assert.Single(store.Records);
    }

    [Fact]
    public void EqualItemsMergeAndTheCountIsAByte()
    {
        var work = new List<ItemStack> { I(5, 30, 200) };
        LootStore.Merge(work, [I(5, 30, 100), I(5, 31, 1), I(6, 30, 2)], 3);
        Assert.Equal(3, work.Count);
        Assert.Equal((200 + 100) & 0xFF, work[0].Count);
    }
}
