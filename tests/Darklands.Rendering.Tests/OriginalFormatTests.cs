using Darklands.Content.Formats.Text.Strings;

namespace Darklands.Rendering.Tests;

/// <summary>The original's sprintf conversions the screens' format strings use (MSC 6 run-time semantics).</summary>
public sealed class OriginalFormatTests
{
    [Theory]
    [InlineData("%d", 42, "42")]
    [InlineData("%d", -7, "-7")]
    [InlineData("%5d", 42, "   42")]
    [InlineData("%-5d|", 42, "42   |")]
    [InlineData("%05d", 42, "00042")]
    [InlineData("%05d", -42, "-0042")]
    [InlineData("%+d", 3, "+3")]
    [InlineData("% d", 3, " 3")]
    [InlineData("%.3d", 7, "007")]
    [InlineData("%.0d", 0, "")]
    [InlineData("%u", -1, "65535")]
    [InlineData("%x", 255, "ff")]
    [InlineData("%X", 255, "FF")]
    [InlineData("%#x", 255, "0xff")]
    [InlineData("%o", 8, "10")]
    [InlineData("%d", 40000, "-25536")]
    [InlineData("%ld", 40000, "40000")]
    [InlineData("%lu", -1, "4294967295")]
    [InlineData("%3d%%", 50, " 50%")]
    public void NumbersPrintAsTheRunTimePrintsThem(string format, int value, string expected) =>
        Assert.Equal(expected, OriginalFormat.Format(format, [value]));

    [Fact]
    public void StringsAndCharacters()
    {
        Assert.Equal("Ann holds the purse of 3fl, 4gr, 5pf (1000pf).",
            OriginalFormat.Format("%s holds the purse of %dfl, %dgr, %dpf (%lupf).", ["Ann", 3, 4, 5, 1000]));
        Assert.Equal("ab   |  cd|xy", OriginalFormat.Format("%-5s|%4s|%.2s", ["ab", "cd", "xyz"]));
        Assert.Equal("A!", OriginalFormat.Format("%c%c", ['A', 33]));
        Assert.Equal("  7", OriginalFormat.Format("%*d", [3, 7]));
    }

    [Fact]
    public void TooFewArgumentsAreAnError() =>
        Assert.Throws<FormatException>(() => OriginalFormat.Format("%d %d", [1]));
}
