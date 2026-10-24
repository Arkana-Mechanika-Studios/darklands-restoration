namespace Darklands.Platform.Tests;

/// <summary>The keyboard buffer with the game's keyboard interrupt in front of it (root 044A:02EE): a held key never stacks up.</summary>
public sealed class BiosKeyBufferTests
{
    [Fact]
    public void AHeldKeysRepeatsLeaveAtMostTwoPresses()
    {
        var buffer = new BiosKeyBuffer();
        for (var i = 0; i < 40; i++)
        {
            buffer.Press(Keys.Right);   // the press, then the typematic repeats, none read
        }
        Assert.Equal(2, buffer.Count);
        buffer.ScanCode();              // the release: its break code runs the handler too
        Assert.Equal(1, buffer.Count);
        Assert.True(buffer.TryRead(out var key));
        Assert.Equal(Keys.Right, key);
        Assert.False(buffer.TryRead(out _));
    }

    [Fact]
    public void OnlyTheCopiesOfTheOldestKeyAreDropped()
    {
        var buffer = new BiosKeyBuffer();
        buffer.Press('a');
        buffer.Press(Keys.Up);
        buffer.Press(Keys.Up);
        buffer.Press(Keys.Up);
        Assert.Equal(4, buffer.Count);
        var read = new List<int>();
        while (buffer.TryRead(out var k))
        {
            read.Add(k);
        }
        Assert.Equal(['a', Keys.Up, Keys.Up, Keys.Up], read);
    }

    [Fact]
    public void AFullBufferLosesTheNewKey()
    {
        var buffer = new BiosKeyBuffer();
        for (var i = 0; i < 20; i++)
        {
            buffer.Press(0x1000 + i);
        }
        Assert.Equal(BiosKeyBuffer.Capacity, buffer.Count);
        for (var i = 0; i < BiosKeyBuffer.Capacity; i++)
        {
            Assert.True(buffer.TryRead(out var k));
            Assert.Equal(0x1000 + i, k);
        }
    }
}
