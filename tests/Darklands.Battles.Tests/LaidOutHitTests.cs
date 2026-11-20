using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// A laid-out clickable text of the store, barter and loot screens (a translation pack's interface/layout.json) answers the pointer
/// exactly where it is drawn: every point of its drawn box hits its line or row, the place it left hits nothing, and with nothing laid
/// out every point is the original's.
/// </summary>
public sealed class LaidOutHitTests
{
    private static IEnumerable<(int X, int Y)> Points(LaidOutAreas.Box box)
    {
        for (var y = box.Y; y < box.Y + box.Height; y++)
        {
            for (var x = box.X; x < box.X + box.Width; x++)
            {
                yield return (x, y);
            }
        }
    }

    [Fact]
    public void ALaidOutMenuLineAnswersWhereItIsDrawn()
    {
        // menu line 3 ("Barter for another person"), moved up a row and wrapped onto two lines, 200 px wide
        var original = new LaidOutAreas.Box(BuySellPage.MenuX, BuySellPage.MenuY + 3 * 8, 120, BuySellPage.RowHeight);
        var drawn = new LaidOutAreas.Box(BuySellPage.MenuX, BuySellPage.MenuY + 2 * 8, 200, 18);
        var laid = new LaidOutAreas();
        laid.Laid(original, drawn);
        foreach (var (x, y) in Points(drawn))
        {
            var (mx, my) = laid.Map(x, y);
            Assert.Equal(3, BuySellPage.MenuLineAt(mx, my));
        }
        // the place it left (outside its drawn box) answers nothing
        foreach (var (x, y) in Points(original).Where(p => !drawn.Contains(p.X, p.Y)))
        {
            var (mx, my) = laid.Map(x, y);
            Assert.Null(BuySellPage.MenuLineAt(mx, my));
        }
        // a highlight of its line lights its drawn box
        Assert.Equal(drawn, Assert.Single(laid.DrawnIn(new LaidOutAreas.Box(BuySellPage.MenuX, BuySellPage.MenuY + 3 * 8, 0x96, 8))));
    }

    [Fact]
    public void ALaidOutListRowAnswersWhereItIsDrawn()
    {
        // the upper list's row 1, given 10 more pixels to the left and two lines
        var original = new LaidOutAreas.Box(BuySellPage.UpperX, BuySellPage.UpperY + 8, 150, BuySellPage.RowHeight);
        var drawn = new LaidOutAreas.Box(BuySellPage.UpperX - 2, BuySellPage.UpperY + 8, 170, 17);
        var laid = new LaidOutAreas();
        laid.Laid(original, drawn);
        foreach (var (x, y) in Points(drawn))
        {
            var (mx, my) = laid.Map(x, y);
            Assert.Equal(1, BuySellPage.UpperRowAt(mx, my));
        }
    }

    [Fact]
    public void WithNothingLaidOutEveryPointIsTheOriginals()
    {
        var laid = new LaidOutAreas();
        for (var y = 0; y < 200; y += 3)
        {
            for (var x = 0; x < 320; x += 3)
            {
                Assert.Equal((x, y), laid.Map(x, y));
            }
        }
        Assert.Empty(laid.DrawnIn(new LaidOutAreas.Box(0, 0, 320, 200)));
    }
}
