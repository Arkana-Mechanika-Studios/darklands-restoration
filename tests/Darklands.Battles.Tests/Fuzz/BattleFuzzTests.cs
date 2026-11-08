namespace Darklands.Battles.Tests.Fuzz;

/// <summary>The fight fuzzer's mutations (BattleFuzz): battle_oracle's 12 argument words stay a fight both programs can be given.</summary>
public sealed class BattleFuzzTests
{
    [Fact]
    public void MutantsStayBattleRequests()
    {
        var parent = new[] { 0, 0x11, 1234, 12, 2, 3, 2, 2, 2, -1, 0, 0 };
        var a = new Random(3);
        var b = new Random(3);
        for (var i = 0; i < 500; i++)
        {
            var c = BattleFuzz.Mutate(parent, a, 0x46);
            Assert.Equal(c, BattleFuzz.Mutate(parent, b, 0x46));
            Assert.Equal(12, c.Length);
            Assert.Equal(0, c[0]);
            Assert.InRange(c[1], 0, 0x89);
            Assert.InRange(c[2], 0, 0x7FFF);
            Assert.True(c[3] >= 0, "the first group is never taken away");
            for (var g = 3; g < 12; g += 3)
            {
                if (c[g] < 0)
                {
                    Assert.Equal((-1, 0, 0), (c[g], c[g + 1], c[g + 2]));
                    continue;
                }
                Assert.InRange(c[g], 0, 0x45);
                Assert.InRange(c[g + 1], 0, 6);
                Assert.InRange(c[g + 2], 1, 8);
            }
        }
        Assert.Equal(new[] { 0, 0x11, 1234, 12, 2, 3, 2, 2, 2, -1, 0, 0 }, parent);
    }

    [Fact]
    public void TheCommittedBuildHasNoEdgeMap()
    {
        Assert.Null(EdgeMap.Attach());
        Assert.Equal(7, EdgeMap.Bucket(200));
    }
}
