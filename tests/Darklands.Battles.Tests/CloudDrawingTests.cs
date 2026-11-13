using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// How a cloud is drawn (D-09fbc1): its puffs within the box of their points (25 wider, 8 higher) taken together with the last frame's
/// box, its edges sorted in with the combatants by depth.
/// </summary>
public sealed class CloudDrawingTests
{
    private sealed class Viewport : IBattleViewport
    {
        public int CameraX => 20 * 32;
        public int CameraY => 20 * 16;
        public bool CombatantInView(int i) => false;
    }

    private static EffectField Field()
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(77)) { Clock = 100, LevelShown = 0 };
        var level = new BattlefieldLevel();
        for (var x = 0; x < BattlefieldLevel.Size; x++)
        {
            for (var y = 0; y < BattlefieldLevel.Size; y++)
            {
                level[x, y] = new BattlefieldCell(0);
            }
        }
        var map = new BattleMap(battle, level, [], 0);
        var field = new EffectField(battle, map, _ => { }, (_, _, _) => { }, _ => "", () => false);
        field.CloudSlots[0] = new EffectCloud { X = 24 * 32 + 16, Y = 25 * 16 + 8, Size = 0x33, Kind = 0, Level = 0, Until = 1000 };
        field.SetCounts(1, 0, 0);
        return field;
    }

    private static (int X, int Y, int W, int H) PuffsBox(EffectField field, int n = 3)
    {
        int minX = 9999, minY = 9999, maxX = -9999, maxY = -9999;
        for (var j = 0; j < n * n; j++)
        {
            var (x, y) = field.CloudPuff(0, j);
            if (x != 0 && y != 0)
            {
                (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
            }
        }
        return (minX, minY, maxX - minX + 25, maxY - minY + 8);
    }

    [Fact]
    public void TheFirstFrameDrawsThePuffsWithinTheirOwnBox()
    {
        var field = Field();
        field.Drawn(new Viewport());
        Assert.Equal(1, field.PuffBoxCount);
        Assert.Equal(PuffsBox(field), field.PuffBox(0));
        Assert.Equal((0, 0, 0, 0), field.CloudBoxGrowth(0));
    }

    [Fact]
    public void ALaterFrameTakesInTheLastFramesBox()
    {
        var field = Field();
        var viewport = new Viewport();
        field.Drawn(viewport);
        var last = PuffsBox(field);
        var grew = false;
        for (var frame = 0; frame < 20; frame++)
        {
            field.Drawn(viewport);
            var now = PuffsBox(field);
            int left = Math.Min(last.X, now.X), top = Math.Min(last.Y, now.Y);
            int right = Math.Max(last.X + last.W, now.X + now.W), bottom = Math.Max(last.Y + last.H, now.Y + now.H);
            Assert.Equal((left, top, right - left, bottom - top), field.PuffBox(0));
            var g = field.CloudBoxGrowth(0);
            grew |= g != (0, 0, 0, 0);
            Assert.Equal((left, top, right - left, bottom - top), (now.X + g.X, now.Y + g.Y, now.W + g.W, now.H + g.H));
            last = now;
        }
        Assert.True(grew);
    }

    [Fact]
    public void AViewRepaintOrAGrowingCloudForgetsTheLastBox()
    {
        var field = Field();
        var viewport = new Viewport();
        field.Drawn(viewport);
        field.ForgetCloudBoxes();
        field.Drawn(viewport);
        Assert.Equal(PuffsBox(field), field.PuffBox(0));
        field.CloudSlots[0].Size = 0x32;
        field.Drawn(viewport);
        Assert.Equal(PuffsBox(field, 2), field.PuffBox(0));
    }

    [Fact]
    public void ABoxIsClippedToTheView()
    {
        Assert.Equal((0, 0, 5, 3), EffectField.ClipToView(-10, -4, 15, 7));
        Assert.Equal((0x100, 190, 4, 10), EffectField.ClipToView(0x100, 190, 20, 30));
        Assert.Equal(0, EffectField.ClipToView(0x104, 10, 20, 30).W);
        Assert.Equal(0, EffectField.ClipToView(10, 200, 20, 30).H);
        Assert.Equal(0, EffectField.ClipToView(-30, 10, 20, 30).W);
    }

    private static readonly DepthKey Sleeper = new(100, 80, 80);

    [Fact]
    public void ASouthEdgeBelowACombatantIsDrawnOverItAndOneAboveUnderIt()
    {
        // the edges are queued before the combatants; the walk back stops at the first entry that stays before
        var below = new DepthKey(90, 84, 84);
        var above = new DepthKey(90, 76, 76);
        var list = new List<DepthKey> { below, above, Sleeper };
        DepthOrder.Sort(list, k => k);
        Assert.Equal([above, Sleeper, below], list);
    }

    [Fact]
    public void AnEastEdgeOverlappingACombatantsRowIsDrawnOverItWhenEastOfIt()
    {
        var east = new DepthKey(110, 78, 82);   // x 110, the row 80 ± 2
        var west = new DepthKey(90, 78, 82);
        var list = new List<DepthKey> { east, west, Sleeper };
        DepthOrder.Sort(list, k => k);
        Assert.Equal([west, Sleeper, east], list);
    }

    [Fact]
    public void CombatantsAloneGoByRowThenColumnInQueueOrder()
    {
        var a = new DepthKey(50, 90, 90);
        var b = new DepthKey(40, 90, 90);
        var c = new DepthKey(70, 60, 60);
        var d = new DepthKey(40, 90, 90);
        var list = new List<DepthKey> { a, b, c, d };
        DepthOrder.Sort(list, k => k);
        Assert.Equal([c, b, d, a], list);
    }

    [Fact]
    public void TheOrderDependsOnTheQueueOrder()
    {
        // not a strict order: an entry the walk stops at keeps a later one from passing earlier entries
        var low = new DepthKey(5, 90, 90);
        var tall = new DepthKey(10, 0, 100);
        var high = new DepthKey(60, 20, 20);
        var list = new List<DepthKey> { low, tall, high };
        DepthOrder.Sort(list, k => k);
        Assert.Equal([low, tall, high], list);
        Assert.True(DepthOrder.MovesBehind(low, high));
    }
}
