namespace Darklands.Platform.Tests;

public sealed class DosClockTests
{
    // orig: the BIOS counter (0040:006C) runs from midnight at 1193182 / 65536 ticks a second and its low word is the party seed (R037+0x9DB2)
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 18206)]
    [InlineData(3600, 65535 & (3600 * 1193182L / 65536))]
    [InlineData(86399, 65535 & (86399 * 1193182L / 65536))]
    public void TheTickWordIsTheLowWordOfTheCountSinceMidnight(int seconds, long expected)
    {
        Assert.Equal((ushort)expected, DosClock.TickWordOfDay(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ADayHoldsTheBiosCountsOneDay()
    {
        // 0x1800B0 ticks a day on a real BIOS (the count wraps there); 86400 s at 18.2065 Hz is that, within the rounding of the divisor
        Assert.InRange(DosClock.BiosTicks(86_400_000), 0x1800B0 - 3, 0x1800B0 + 3);
    }
}
