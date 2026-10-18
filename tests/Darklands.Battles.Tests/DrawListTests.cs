using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Battles.Tests;

/// <summary>
/// The renderer's draw list stops at 157 entries: a hit flash past it is not drawn and draws no random number, a body past it
/// gets no hot spot; the animated pieces of the view each draw their frame by lot.
/// </summary>
public sealed class DrawListTests
{
    private sealed class Viewport(int logged) : IBattleViewport
    {
        public int CameraX => 20 * 32;
        public int CameraY => 20 * 16;
        public bool CombatantInView(int i) => i == 0;
        public int LoggedSprites => logged;
    }

    private static (EffectField Field, Battle Battle, MscRandom Random) Scene(BattlefieldLevel? level = null, TerrainDescription? terrain = null)
    {
        var random = new MscRandom(1234);
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), random) { Clock = 100, FloorShown = 0 };
        var c = battle[0];
        c.Status = CharacterStatus.Able;
        c.Floor = 0;
        c.Flash[0] = 0x50;
        c.Flash[1] = 0x51;
        c.Flash[2] = 0x52;
        c.FlashUntil = 200;
        var map = new BattleMap(battle, level ?? new BattlefieldLevel(), [], 0);
        var field = new EffectField(battle, map, _ => { }, (_, _, _) => { }, _ => "", () => false) { Terrain = terrain };
        return (field, battle, random);
    }

    [Fact]
    public void AFlashWithRoomInTheListIsDrawnByLot()
    {
        var (field, _, random) = Scene();
        var before = random.State;
        field.Drawn(new Viewport(0));
        Assert.InRange(field.FlashSpriteOf(0), 0x50, 0x52);
        Assert.True(field.BodyQueued(0));
        Assert.NotEqual(before, random.State);
        Assert.Equal(3, field.DrawListCount);   // the sentinel, the body, the flash
    }

    [Fact]
    public void PastTheLimitTheFlashIsNotDrawnAndTakesNoRandomNumber()
    {
        // the last frame logged 155 sprites: the body takes place 156 (count 157), and the flash finds the list full
        var (field, _, random) = Scene();
        var before = random.State;
        field.Drawn(new Viewport(155));
        Assert.True(field.BodyQueued(0));
        Assert.Equal(-1, field.FlashSpriteOf(0));
        Assert.Equal(before, random.State);
        Assert.Equal(EffectField.DrawListLimit, field.DrawListCount);
    }

    [Fact]
    public void ABodyPastTheLimitHasNoPlace()
    {
        var (field, _, _) = Scene();
        field.Drawn(new Viewport(156));
        Assert.False(field.BodyQueued(0));
        Assert.Equal(-1, field.FlashSpriteOf(0));
    }

    [Fact]
    public void EachAnimatedPieceInViewDrawsItsFrame()
    {
        // two north-wall decorations below the animation limit (tile 1) in the view, one at the limit (not animated)
        var level = new BattlefieldLevel();
        for (var x = 0; x < BattlefieldLevel.Size; x++)
        {
            for (var y = 0; y < BattlefieldLevel.Size; y++)
            {
                level[x, y] = new BattlefieldCell(0);
            }
        }
        level[20, 20] = new BattlefieldCell(1u << 20);
        level[21, 18] = new BattlefieldCell(1u << 20);
        level[19, 19] = new BattlefieldCell(2u << 20);
        var terrain = new TerrainDescription();
        terrain.SetAnimatedBelow(TileKind.NorthWallDecoration, 2);
        terrain.SetAnimatedBelow(TileKind.FloorDecoration, 0);
        terrain.SetAnimatedBelow(TileKind.WestWallDecoration, 0);
        var (field, _, random) = Scene(level, terrain);
        var expected = new MscRandom(1234);
        field.Drawn(new Viewport(0));
        Assert.InRange(field.PieceFrame(0, 20, 20), 0, 1);
        Assert.InRange(field.PieceFrame(0, 21, 18), 0, 1);
        Assert.Equal(-1, field.PieceFrame(0, 19, 19));
        // two pieces and the flash: three numbers drawn
        for (var k = 0; k < 3; k++)
        {
            expected.NextRaw();
        }
        Assert.Equal(expected.State, random.State);
    }
}
