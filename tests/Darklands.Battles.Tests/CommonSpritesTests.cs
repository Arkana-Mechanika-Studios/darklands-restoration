using Darklands.Content.Formats.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>COMMONSP.IMG decodes into the pieces the screens draw, each with the size its use needs.</summary>
public sealed class CommonSpritesTests
{
    [Fact]
    public void ThePanelFramesBoxCornersAndBackgroundsHaveTheirSizes()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var s = CommonSprites.Decode(files.Read("COMMONSP.IMG"));
        // a row is 0x28 high: the edges cover it, the lines above and below span the 0x37 between them
        Assert.All(new[] { s.PanelSide, s.PanelSideLit }, p => Assert.Equal((4, 40), (p.Width, p.Height)));
        Assert.All(new[] { s.PanelTop, s.PanelBottom, s.PanelTopLit, s.PanelBottomLit }, p => Assert.Equal((0x37, 1), (p.Width, p.Height)));
        // framed-box-paint puts the corners at x1 - 3 / y1 - 3: four rows each
        Assert.All(new[] { s.CornerTopLeft, s.CornerTopRight, s.CornerBottomLeft, s.CornerBottomRight }, p => Assert.Equal(4, p.Height));
        Assert.Equal((0x37, 0x26), (s.RowBackground.Width, s.RowBackground.Height));
        Assert.Equal((0x37, 0x0B), (s.ColumnBackground.Width, s.ColumnBackground.Height));
        Assert.False(s.Pointer.IsEmpty);
        Assert.False(s.DeadShield.IsEmpty);
    }
}
