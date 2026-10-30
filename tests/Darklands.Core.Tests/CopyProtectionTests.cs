using Darklands.Core.Randomness;
using Darklands.Core.World;

namespace Darklands.Core.Tests;

/// <summary>
/// The copy-protection question's draws (R041+0x0000): the expected values come from the original's bytes run with its own roll-below and
/// _rand from the given generator state (the symbols as the _itoa arguments of the picture names, the named one from the substance
/// table index, the state after the call).
/// </summary>
public sealed class CopyProtectionTests
{
    [Theory]
    [InlineData(1u, 6, 7, 12, 19, 7, 3403800452u)]
    [InlineData(0x1234u, 1, 9, 7, 14, 1, 269751579u)]
    [InlineData(0xDEADBEEFu, 1, 0, 9, 19, 0, 1059273626u)]
    [InlineData(0x7FFFFFFFu, 0, 19, 11, 14, 14, 4199332202u)]
    [InlineData(42u, 5, 0, 12, 17, 17, 1054043289u)]
    [InlineData(123456u, 0, 18, 8, 16, 18, 505055671u)]
    [InlineData(0xFFFFFFFFu, 0, 19, 11, 14, 14, 2051848554u)]
    [InlineData(0x13579BDFu, 5, 18, 7, 17, 5, 3540075466u)]
    [InlineData(2024u, 6, 2, 8, 15, 6, 808577855u)]
    [InlineData(0x80000000u, 0, 19, 8, 14, 8, 580340855u)]
    public void TheQuestionDrawsAsTheOriginal(uint seed, int topLeft, int bottomLeft, int topRight, int bottomRight, int named, uint after)
    {
        var random = new MscRandom(seed);
        var q = CopyProtection.Draw(random);
        Assert.Equal((topLeft, bottomLeft, topRight, bottomRight), (q.TopLeft, q.BottomLeft, q.TopRight, q.BottomRight));
        Assert.Equal(named, q.Named);
        Assert.Equal(after, random.State);
    }

    [Fact]
    public void ExactlyFiveDrawsAreMade()
    {
        var random = new MscRandom(77);
        var reference = new MscRandom(77);
        CopyProtection.Draw(random);
        for (var i = 0; i < 5; i++)
        {
            reference.NextRaw();
        }
        Assert.Equal(reference.State, random.State);
    }

    [Fact]
    public void TheFourSymbolsAreDifferentAndInTheirRanges()
    {
        var random = new MscRandom(5);
        for (var i = 0; i < 2000; i++)
        {
            var q = CopyProtection.Draw(random);
            Assert.InRange(q.TopLeft, 0, 6);
            Assert.InRange(q.BottomLeft, 0, 19);
            Assert.InRange(q.TopRight, 7, 13);
            Assert.InRange(q.BottomRight, 14, 19);
            Assert.Equal(4, new[] { q.TopLeft, q.BottomLeft, q.TopRight, q.BottomRight }.Distinct().Count());
            Assert.InRange(q.Asked, 0, 3);
        }
    }
}
