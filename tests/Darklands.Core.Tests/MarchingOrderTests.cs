using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>"Change Marching Order" (R022+0x051C, R022+0x07AA): the prompts, the picks, the members left over, the length byte.</summary>
public sealed class MarchingOrderTests
{
    [Fact]
    public void TheAbleMembersAreMarkedAndOnePromptFewerIsAsked()
    {
        var order = new byte[6];
        var choice = new MarchingOrderChoice(order, i => i is 0 or 2 or 3);
        Assert.Equal(3, choice.Count);
        Assert.Equal(2, choice.Prompts);
        Assert.Equal([true, false, true, true, false], choice.Selected);
        choice.Take(3);
        choice.Take(0);
        Assert.Equal([2], choice.Finish());
        Assert.Equal([3, 0, 2, 0, 0, 3], order);
    }

    [Fact]
    public void AClickNeedsAnAbleMarkedMember()
    {
        var choice = new MarchingOrderChoice(new byte[6], i => i < 2);
        Assert.True(choice.CanClick(1, ableNow: true));
        Assert.False(choice.CanClick(1, ableNow: false));
        Assert.False(choice.CanClick(2, ableNow: true));
        choice.Take(1);
        Assert.False(choice.CanClick(1, ableNow: true));
    }

    [Fact]
    public void FKeysPickWithoutATestAndASixthEntryOverwritesTheLength()
    {
        var order = new byte[6];
        var choice = new MarchingOrderChoice(order, i => i < 4);
        choice.Take(2);
        choice.Take(2);                                         // F3 again: a second entry for slot 2
        choice.Take(4);                                         // F5: nobody able there
        Assert.Equal([0, 1, 3], choice.Finish());
        // the last of the three left over is written at index 5, the length's own byte: 3, then + 1
        Assert.Equal([2, 2, 4, 0, 1, 4], order);
    }

    [Fact]
    public void NoPromptForASingleMember()
    {
        var order = new byte[] { 9, 9, 9, 9, 9, 9 };
        var choice = new MarchingOrderChoice(order, i => i == 3);
        Assert.Equal(0, choice.Prompts);
        Assert.Equal([3], choice.Finish());
        Assert.Equal([3, 9, 9, 9, 9, 1], order);                // the old entries after the length stay
    }
}
