using Darklands.Cards;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Randomness;
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

    /// <summary>
    /// The party's random spots (R014+0x4350..0x43AB) are one run of clip tests: each line test reads the point the previous one left
    /// (DL 0036), the collision tests between them not reaching it. The original's own tests of a direct call of R014+0x4188 (round battlelead,
    /// a battle-call job at (699, 168) facing 7; a read-only hook at R014+0xF284): the lines from the start to its random spots against the
    /// one wall box there (east of cell (21, 10)), the run beginning with the leftover (1, 85), and what each returned. One shared run gives
    /// every one of them; a fresh run per test frees lines the original found blocked.
    /// </summary>
    [Fact]
    public void ThePartysRandomSpotsAreOneRunOfClipTests()
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1)) { FloorShown = 0 };
        var level = new BattlefieldLevel(new byte[BattlefieldLevel.ImageSize]);
        level[21, 10] = new BattlefieldCell(0x0A);
        var map = new BattleMap(battle, level, [], 0);
        (int X, int Y, int Blocked)[] tests =
        [
            (731, 185, 1), (718, 181, 1), (738, 182, 1), (733, 187, 1), (741, 190, 1), (720, 185, 1), (707, 181, 1), (703, 187, 1), (700, 191, 0),
            (708, 184, 1), (742, 189, 1), (720, 188, 1), (703, 183, 1), (706, 182, 1), (727, 191, 1), (745, 188, 1), (742, 184, 1), (736, 181, 1),
            (712, 191, 1), (745, 184, 1),
        ];
        var from = new MapPoint(699, 168);
        var run = new BattleMap.ClipResidue { Point = (1, 85) };
        Assert.Equal(tests.Select(t => t.Blocked), tests.Select(t => map.LineBlocked(from, new MapPoint(t.X, t.Y), residue: run)));
        Assert.Contains(tests, t => t.Blocked != map.LineBlocked(from, new MapPoint(t.X, t.Y), residue: new BattleMap.ClipResidue()));
    }
}
