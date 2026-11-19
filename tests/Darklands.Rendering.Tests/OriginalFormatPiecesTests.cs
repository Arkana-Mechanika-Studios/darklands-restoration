using Darklands.Content.Formats.Text.Strings;

namespace Darklands.Rendering.Tests;

/// <summary>A format's printed pieces (a translation's $arg0 ...) and its conversions' places (the template's placeables), L-7w.</summary>
public sealed class OriginalFormatPiecesTests
{
    [Fact]
    public void EachConversionGivesItsPrintedTextWithItsWidth()
    {
        const string format = "%s holds %3d fl, %02dg and 100%% (%-4s)";
        object[] args = ["Ritter", 7, 5, "ab"];
        Assert.Equal(["Ritter", "  7", "05", "ab  "], OriginalFormat.Pieces(format, args));
        Assert.Equal("Ritter holds   7 fl, 05g and 100% (ab  )", OriginalFormat.Format(format, args));
    }

    [Fact]
    public void TheConversionsArePlacedAndAPercentSignIsNone()
    {
        const string format = "%Fs of %ld, 50%%";
        Assert.Equal([(0, 3), (7, 3)], OriginalFormat.Conversions(format));
    }
}
