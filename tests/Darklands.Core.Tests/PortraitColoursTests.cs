using Darklands.Core.Characters.Creation;

namespace Darklands.Core.Tests;

/// <summary>The figure colour keys 1, 2, 3 of the "Create a new party" menu (R037 1462 0444).</summary>
public sealed class PortraitColoursTests
{
    [Fact]
    public void KeyOneOnF01StepsThroughSixSetsAndCopiesThemToTheMember()
    {
        var colours = new PortraitColours();
        var member = new byte[24];
        colours.Cycle(1, "F01", member);
        Assert.Equal(1, colours.FirstStep);
        Assert.Equal([0x1A, 0x1C, 0x1C, 0x1C, 0x1A, 0x1A], member.Take(6));
        for (var k = 0; k < 5; k++)
        {
            colours.Cycle(1, "F01", member);
        }
        Assert.Equal(0, colours.FirstStep);
        Assert.Equal([0x39, 0x36, 0x00, 0x3D, 0x38, 0x15], member.Take(6));
        Assert.All(member.Skip(6), b => Assert.Equal(0, b));
    }

    [Fact]
    public void TheCountersAreSharedBetweenMembers()
    {
        var colours = new PortraitColours();
        colours.Cycle(1, "F01", new byte[24]);
        var second = new byte[24];
        colours.Cycle(1, "C00", second);
        Assert.Equal(2, colours.FirstStep);
    }

    [Fact]
    public void KeyTwoOnA00UsesTheFirstA00TestNotTheDormantSecond()
    {
        var colours = new PortraitColours();
        colours.Cycle(2, "A00", new byte[24]);
        Assert.Equal(1, colours.SecondStep);
        Assert.Equal(0, colours.ThirdStep);
    }

    [Fact]
    public void AnUnknownFigureChangesNothing()
    {
        var colours = new PortraitColours();
        var member = Enumerable.Repeat((byte)7, 24).ToArray();
        colours.Cycle(3, "Z99", member);
        Assert.All(member, b => Assert.Equal(7, b));
        Assert.Equal(0, colours.ThirdStep);
    }
}
