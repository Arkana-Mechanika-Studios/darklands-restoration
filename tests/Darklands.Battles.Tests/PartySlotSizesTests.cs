using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// R014+0x0254..0x027E: a battle's start copies the party's animation slot sizes (DS:1182 / DS:118E, as the last battle's loads left them)
/// into the collision boxes (DS:C80F / DS:C831) before it loads its own sets. Seen in the original (lockstep dungeon-door-back, a hook at
/// R014+0x05B4): member 2's set is 7 wide, so in the second battle of a run its box is 7, not the fresh slot's 5.
/// </summary>
public sealed class PartySlotSizesTests
{
    private static (BattleScreens Screens, BattleScreen Screen) Start(GameFiles files, (int, int)[]? kept)
    {
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var screens = new BattleScreens(ui, () => state);
        screens.GenerationMemory.PartySlotSizes = kept;
        var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
        screen.Start(new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        return (screens, screen);
    }

    [Fact]
    public void TheBattleStartTakesThePartysBoxesFromTheSlotSizesTheLastBattleLeft()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, screen) = Start(GameFolder.Files, [(5, 2), (5, 2), (7, 2), (5, 2), (6, 3)]);
        var b = screen.Tactics!.Battle;
        Assert.Equal((7, 2), (b[2].HalfWidth, b[2].HalfHeight));
        Assert.Equal((6, 3), (b[4].HalfWidth, b[4].HalfHeight));
        Assert.Equal((5, 2), (b[0].HalfWidth, b[0].HalfHeight));
    }

    [Fact]
    public void AFreshProgramUsesTheSlotsOwnSizes()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, screen) = Start(GameFolder.Files, null);
        var t = screen.Tactics!;
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(5, t.Battle[i].HalfWidth);
            Assert.Equal(2, t.Battle[i].HalfHeight);
        }
    }
}
