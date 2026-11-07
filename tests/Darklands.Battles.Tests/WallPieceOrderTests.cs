using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>The battle's wall pieces sorted in among the sprites (R014+0x88E6 / R014+0x7BBE / R014+0x97A6): the slots a sprite touches,
/// the pieces' depth keys and their order against a combatant.</summary>
public sealed class WallPieceOrderTests
{
    [Fact]
    public void TheSlotTablesAreTheProgramsTables()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var exe = DarklandsExecutable.Load(files);
        void Same(int at, sbyte[] table) => Assert.Equal(table, exe.DataSegment(at, table.Length).ToArray().Select(b => (sbyte)b).ToArray());
        Same(0x1A5D + BattlefieldDirtyBoxes.FirstRow, BattlefieldDirtyBoxes.RowCorrection);
        Same(0x1A72, BattlefieldDirtyBoxes.Round);
        Same(0x1A8A, BattlefieldDirtyBoxes.RoundRows);
        Same(0x1AA2, BattlefieldDirtyBoxes.Wide);
        Same(0x1AAC, BattlefieldDirtyBoxes.WideRows);
        Same(0x1AB6, BattlefieldDirtyBoxes.Big);
        Same(0x1ABE, BattlefieldDirtyBoxes.BigRows);
        Same(0x1AC6, BattlefieldDirtyBoxes.Varying);
        Same(0x1ACA, BattlefieldDirtyBoxes.VaryingRows);
        Same(0x1ACE, BattlefieldDirtyBoxes.Piece);
        Same(0x1ADC, BattlefieldDirtyBoxes.PieceRows);
    }

    [Fact]
    public void TheRowCorrectionMakesTheSlotOfTheViewCell()
    {
        // inside the view the base slot of a map point is the slot showing its cell (SlotOffset's inverse)
        for (var row = 0; row < BattlefieldView.Rows; row++)
        {
            for (var column = 0; column < BattlefieldView.Columns; column++)
            {
                var (dx, dy) = BattlefieldView.SlotOffset(row, column);
                var boxes = new BattlefieldDirtyBoxes();
                boxes.MarkSprite(20 * 32 + dx * 32 + 5, 20 * 16 + dy * 16 + 3, 20 * 32, 20 * 16, (10, 10, 4, 4), -1, false);
                Assert.NotNull(boxes.Box(row * 10 + column));
            }
        }
    }

    [Fact]
    public void ASpriteMarksTheSlotsRoundItsOwnWithItsBox()
    {
        var boxes = new BattlefieldDirtyBoxes();
        // slot 75 (row 7, column 5) is the centre cell
        boxes.MarkSprite(20 * 32 + 8, 20 * 16 + 8, 20 * 32, 20 * 16, (100, 60, 20, 30), -1, false);
        boxes.MarkSprite(20 * 32 + 8, 20 * 16 + 8, 20 * 32, 20 * 16, (90, 70, 5, 5), -1, false);
        var marked = Enumerable.Range(0, BattlefieldDirtyBoxes.Slots).Where(s => boxes.Box(s) is not null).ToArray();
        Assert.Equal(BattlefieldDirtyBoxes.Round.Select(o => 75 + o).Order(), marked);
        Assert.Equal((90, 60, 30, 30), boxes.Box(75));
        // the sprites' own boxes are the places the redraw reaches the screen
        Assert.Equal([(100, 60, 20, 30), (90, 70, 5, 5)], boxes.Rects);
        boxes.MarkSprite(20 * 32 + 8, 20 * 16 + 8, 20 * 32, 20 * 16, (90, 70, 0, 5), -1, false);
        Assert.Equal(2, boxes.Rects.Count);
        // a combatant's set with size class 1 and 4 bits, and varying furniture: more slots
        var wide = new BattlefieldDirtyBoxes();
        wide.MarkSprite(20 * 32 + 8, 20 * 16 + 8, 20 * 32, 20 * 16, (100, 60, 20, 30), 1, true);
        Assert.NotNull(wide.Box(75 - 22));
        Assert.NotNull(wide.Box(75 + 31));
        Assert.Null(boxes.Box(75 + 31));
        // a slot that wraps to another row is not marked: at column 0, offset −1 is the row before's last slot
        var edge = new BattlefieldDirtyBoxes();
        var (dx, _) = BattlefieldView.SlotOffset(7, 0);
        edge.MarkSprite(20 * 32 + dx * 32 + 8, 20 * 16 + 8, 20 * 32, 20 * 16, (0, 60, 20, 30), -1, false);
        Assert.NotNull(edge.Box(70));
        Assert.Null(edge.Box(69));
        // clamped to the view
        var big = new BattlefieldDirtyBoxes();
        big.MarkSprite(20 * 32 + 8, 20 * 16 + 8, 20 * 32, 20 * 16, (-10, -5, 400, 300), -1, false);
        Assert.Equal((0, 0, 0x104, 0xC8), big.Box(75));
    }

    [Fact]
    public void TheWallPiecesOfASlotHaveTheOriginalsKeysAndOrder()
    {
        var level = new BattlefieldLevel();
        for (var x = 0; x < BattlefieldLevel.Size; x++)
        {
            for (var y = 0; y < BattlefieldLevel.Size; y++)
            {
                level[x, y] = new BattlefieldCell(0);
            }
        }
        // cell (20, 20): east wall 1, furniture 2 in orientation 1, south wall 1
        level[20, 20] = new BattlefieldCell(1u | 2u << 4 | 1u << 24 | 1u << 28);
        var tiles = new BattlefieldTiles();
        var piece = RowRunSprite.Empty;
        tiles.WestWalls[1] = new WallTile(piece, piece, piece, piece);
        tiles.NorthWalls[1] = new WallTile(piece, piece, piece, piece);
        tiles.Furniture[2] = new FurnitureTile(piece, [0, 7, 0, 0], [0, 6, 0, 0], [0, -3, 0, 0], [0, -20, 0, 0], 1, 9, false);
        var view = new BattlefieldView { CentreColumn = 20, CentreRow = 20 };
        var pieces = view.WallPieces(level, tiles, new TerrainDescription(), 7, 5);
        int c = 20 * 32, r = 20 * 16;
        Assert.Equal([0x7D05, 0x7D0A, 0x7D14], pieces.Select(p => p.Type));
        Assert.Equal(new DepthKey(c + 0x1F, r + 9, r + 0xF), pieces[0].Key);
        // furniture: x + BoxX, rows max(r + BoxY − height / 2, r + 1) .. min(r + BoxY + height / 2, r + 14)
        Assert.Equal(new DepthKey(c + 7, r + 2, r + 10), pieces[1].Key);
        Assert.Equal(new DepthKey(c + 0x10, r + 0xF, r + 0xF), pieces[2].Key);
        var (fx, fy) = BattlefieldView.SlotFloor(7, 5);
        Assert.Equal((fx + 32, fy - 25), (pieces[0].X, pieces[0].Y));
        Assert.Equal((fx - 3, fy - 20), (pieces[1].X, pieces[1].Y));
        Assert.Equal((fx, fy - 9), (pieces[2].X, pieces[2].Y));
    }

    [Fact]
    public void AWallInFrontIsDrawnAfterTheFigureBehindIt()
    {
        // the south wall (railing) of cell (20, 20) and a combatant standing in the cell behind it, queued first as in the original
        int c = 20 * 32, r = 20 * 16;
        var figure = new DepthKey(c + 12, r + 6, r + 6);
        var rail = new DepthKey(c + 0x10, r + 0xF, r + 0xF);
        var list = new List<(string Name, DepthKey Key)> { ("figure", figure), ("rail", rail) };
        DepthOrder.Sort(list, e => e.Key);
        Assert.Equal(["figure", "rail"], list.Select(e => e.Name));
        // a combatant in front of it (the next row south) is drawn after the rail, though queued before it
        var front = new List<(string Name, DepthKey Key)> { ("figure", new DepthKey(c + 12, r + 20, r + 20)), ("rail", rail) };
        DepthOrder.Sort(front, e => e.Key);
        Assert.Equal(["rail", "figure"], front.Select(e => e.Name));
    }
}
