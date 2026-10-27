namespace Darklands.Platform.Tests;

/// <summary>DOSBox's typematic: 500 ms, then a repeat every 33 ms for the key held last.</summary>
public sealed class KeyTypematicTests
{
    private static int Repeats(KeyTypematic t, double from, double to)
    {
        var total = 0;
        for (var ms = from; ms <= to; ms += 1)
        {
            total += t.Due(ms, out _);
        }
        return total;
    }

    [Fact]
    public void NothingRepeatsBeforeTheDelay()
    {
        var t = new KeyTypematic();
        t.Press(Keys.Right, 1000);
        Assert.Equal(0, Repeats(t, 1000, 1499));
        Assert.Equal(1, Repeats(t, 1500, 1500));
    }

    [Theory]
    [InlineData(500, 1)]     // the first repeat comes with the delay
    [InlineData(1000, 16)]   // 1 + floor(500 / 33)
    [InlineData(2500, 61)]   // 1 + floor(2000 / 33)
    public void AKeyHeldForNMillisecondsRepeatsAtTheRate(int heldMs, int expected)
    {
        var t = new KeyTypematic();
        t.Press(Keys.Up, 0);
        Assert.Equal(expected, Repeats(t, 1, heldMs));
    }

    [Fact]
    public void ALateCallCatchesUpAndReportsTheKey()
    {
        var t = new KeyTypematic();
        t.Press(Keys.Left, 0);
        Assert.Equal(16, t.Due(1000, out var key));
        Assert.Equal(Keys.Left, key);
    }

    [Fact]
    public void ReleaseStopsTheRepeats()
    {
        var t = new KeyTypematic();
        t.Press(Keys.Right, 0);
        Assert.Equal(7, Repeats(t, 1, 700));
        t.Release(Keys.Right);
        Assert.False(t.IsHeld);
        Assert.Equal(0, Repeats(t, 701, 5000));
    }

    [Fact]
    public void ReleasingAnotherKeyKeepsTheLastPressedRepeatingAndANewPressRestartsTheDelay()
    {
        var t = new KeyTypematic();
        t.Press(Keys.Right, 0);
        t.Press(Keys.Up, 300);
        t.Release(Keys.Right);
        Assert.True(t.IsHeld);
        Assert.Equal(0, Repeats(t, 301, 799));
        Assert.Equal(1, t.Due(800, out var key));
        Assert.Equal(Keys.Up, key);
    }
}
