using Darklands.Cards;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// Mode 0 places the party before the level is shown (R014+0x0017..0x0058: R024+0x0000 places it, then level-change R024+0x059A hides
/// the unopened doors' wall groups). In these two mine fights the start lies by such a group: the original puts members 0 and 1 on the
/// formation's first two spots, which level-change makes void a moment later (battle_oracle fights of round battlefuzz, the original's
/// records at the first pass; a read-only hook at R014+0x4188's collision calls returned 0 for every slot).
/// </summary>
public sealed class PlacementOrderTests
{
    [Theory]
    [InlineData(0x0E, 4712, 50, 3, 39, 2, 34, 2, new[] { 1000, 360, 1016, 360, 992, 368, 1024, 368 })]
    [InlineData(0x0A, 24351, 48, 4, 46, 2, -1, 0, new[] { 720, 141, 720, 131, 736, 146, 736, 126 })]
    public void ANewBattlefieldsPartyIsPlacedBeforeTheLevelHidesItsGroups(int battlefield, int seed, int a, int na, int b, int nb, int c, int nc, int[] places)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var folder = Path.Combine(LocalPaths.Get("game", "DARKLANDS_DIR")!, "SAVES");
        var sav = File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.SAV"));
        var state = Darklands.Persistence.OriginalSaves.Read(sav, [], "dksave0.sav", WorldData.Load(files)).State;
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var screen = new BattleScreen(ui, new BattleAssets(files), state, new BattleScreens(ui, () => state));
        screen.Start(new BattleRequest(battlefield, seed, [(a, 2, na), (b, 2, nb), (c, c < 0 ? 0 : 2, nc)]));
        var battle = screen.Tactics!.Battle;
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal((places[2 * i], places[2 * i + 1]), (battle[i].Position.X, battle[i].Position.Y));
        }
        // the start's group is hidden once the level is shown: the first spot is void now, as in the original's level at the first pass
        Assert.True(screen.Tactics.Level[places[0] >> 5, places[1] >> 4].IsVoid);
    }
}
