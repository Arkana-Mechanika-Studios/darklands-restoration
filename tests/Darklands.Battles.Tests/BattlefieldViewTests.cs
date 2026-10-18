using Darklands.Content.Formats.Exe;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

public sealed class BattlefieldViewTests
{
    [Fact]
    public void SlotOffsetsAreTheProgramsTables()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var exe = DarklandsExecutable.Load(files);
        var columns = exe.DataSegment(0x1FC6, 140);
        var rows = exe.DataSegment(0x2052, 140);
        for (var row = 0; row < BattlefieldView.Rows; row++)
        {
            for (var column = 0; column < BattlefieldView.Columns; column++)
            {
                var k = row * BattlefieldView.Columns + column;
                Assert.Equal(((sbyte)columns[k], (sbyte)rows[k]), BattlefieldView.SlotOffset(row, column));
            }
        }
    }

    [Fact]
    public void MapAndScreenPositionsAreInverse()
    {
        var view = new BattlefieldView { CentreColumn = 12, CentreRow = 20 };
        var (sx, sy) = view.ToScreen(400, 333);
        Assert.Equal((400, 333), view.ToMap(sx, sy));
        Assert.Equal((BattlefieldView.Left + 0x88, 0x69), view.ToScreen(12 * 32, 20 * 16));
    }

    /// <summary>
    /// The slot tables R000+0x0114 fills (R136+0x415E floor y, R136+0x4B4A floor x): entry 75 (row 7, column 5) is the origin the
    /// mouse handler subtracts (R136+0x41F4 = 0x69, R136+0x4BE0 = 0x78).
    /// </summary>
    [Fact]
    public void TheMouseOriginIsSlotSeventyFiveOfTheProgramsTables()
    {
        var rowY = new int[140];
        var columnX = new int[140];
        for (int i = 0, k = 0, v = -7; v < 0xD9 && i < 140; k++, v += 16)
        {
            var c = -((k & 1) * 16 + 24);
            for (var j = 0; j < 10; j++, c += 32)
            {
                rowY[i + j] = v;
                columnX[i + j] = c;
            }
            i += 10;
        }
        Assert.Equal(0x69, rowY[(0x41F4 - 0x415E) / 2]);
        Assert.Equal(0x78, columnX[(0x4BE0 - 0x4B4A) / 2]);
        Assert.Equal((0x78, 0x69), BattlefieldView.SlotFloor(7, 5));
    }

    /// <summary>
    /// Every pixel of the view: the ground point is the original's formula (h = y − 0x69, x = h − 0x78 + (x − 60) − 16 + origin x,
    /// y = h + origin y), and projecting it back gives the pixel again, for several view origins.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 20)]
    [InlineData(19, 23)]
    [InlineData(39, 39)]
    public void TheGroundPointUnderThePointerRoundTrips(int column, int row)
    {
        var view = new BattlefieldView { CentreColumn = column, CentreRow = row };
        for (var y = 0; y < BattlefieldView.Height; y++)
        {
            for (var x = BattlefieldView.Left; x < BattlefieldView.Left + BattlefieldView.Width; x++)
            {
                var h = y - 0x69;
                var expected = (h - 0x78 + (x - 60) - 16 + column * 32, h + row * 16);
                var ground = view.ToMap(x, y);
                Assert.Equal(expected, ground);
                Assert.Equal((x, y), view.ToScreen(ground.X, ground.Y));
            }
        }
    }

    /// <summary>The ground cursor (the ghost of a move order) stands on the clicked pixel: centred on its x, its bottom on its y.</summary>
    [Theory]
    [InlineData(250, 180)]
    [InlineData(61, 3)]
    [InlineData(190, 100)]
    [InlineData(322, 199)]
    public void TheGroundCursorIsDrawnUnderTheClick(int x, int y)
    {
        var view = new BattlefieldView { CentreColumn = 19, CentreRow = 14 };
        var (gx, gy) = view.ToMap(x, y);
        foreach (var (width, height) in new[] { (0x0B, 0x1A), (0x0B, 0x0F), (0x16, 0x19), (0x0E, 0x1C) })
        {
            var (left, top) = view.CursorTopLeft(gx, gy, width, height);
            Assert.Equal(x - width / 2, left);
            Assert.Equal(y, top + height);
        }
    }
}
