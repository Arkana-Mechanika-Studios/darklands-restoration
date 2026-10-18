using Darklands.Core.Economy;
using Darklands.Core.Randomness;
using Darklands.Core.Time;

namespace Darklands.Core.Tests;

/// <summary>Readable examples of the Core value rules (the oracle tests check them exhaustively).</summary>
public sealed class CoreValueRulesTests
{
    [Fact]
    public void MoneyCarriesUpwardsAndBorrowsDownwards()
    {
        Assert.Equal(new Money(1, 1, 1), Money.From(0, 20, 13));
        Assert.Equal(new Money(0, 19, 11), new Money(1, 0, 0).Subtract(0, 0, 1));
        Assert.Equal(253, Money.From(1, 1, 1).TotalPfennigs);
    }

    [Fact]
    public void MoneyIsFormattedLikeTheGame()
    {
        Assert.Equal("3 florins, 2 groschen and 1 pfennig", new Money(3, 2, 1).ToString());
        Assert.Equal("1 florin and 5 groschen", new Money(1, 5, 0).ToString());
        Assert.Equal("1,200 florins", new Money(1200, 0, 0).ToString());
    }

    [Fact]
    public void DaysRollOverOnlyPastTheMonthLength()
    {
        // 31 January is a valid date; the next day is 1 February.
        Assert.Equal(new GameDate(0, 31, 0, 1400), new GameDate(23, 30, 0, 1400).AddHours(1));
        Assert.Equal(new GameDate(0, 1, 1, 1400), new GameDate(23, 31, 0, 1400).AddHours(1));
    }

    [Fact]
    public void AdvancingTheClockReportsEachNewMonthBeforeTheYearRolls()
    {
        var months = new List<int>();
        var end = Calendar.AdvanceHours(new GameDate(12, 31, 11, 1400), 24, months.Add);

        Assert.Equal(new GameDate(12, 1, 0, 1401), end);
        Assert.Equal([12], months);   // the monthly update sees month 12 for January
    }

    [Theory]
    [InlineData(2, 7)]
    [InlineData(3, 0)]
    [InlineData(8, 1)]
    [InlineData(20, 5)]
    [InlineData(23, 6)]
    public void HoursFallIntoCanonicalBands(int hour, int band) => Assert.Equal(band, TimeOfDay.Band(hour));

    [Fact]
    public void DaylightIsFiveToEighteen()
    {
        Assert.False(TimeOfDay.IsDaylight(4));
        Assert.True(TimeOfDay.IsDaylight(5));
        Assert.True(TimeOfDay.IsDaylight(18));
        Assert.False(TimeOfDay.IsDaylight(19));
    }

    [Fact]
    public void RandomBelowTreatsSmallRangesAsTwo()
    {
        var a = new MscRandom(42);
        var b = new MscRandom(42);
        Assert.Equal(a.Below(2), b.Below(0));
    }
}
