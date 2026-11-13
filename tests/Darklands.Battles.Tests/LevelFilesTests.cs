using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The levels a battle file holds are the level files (level%d.flr / .atv), not the level in the buffer: level-change R024+0x059A sets
/// bit 31 of the hidden wall groups' cells after each load (R024+0x06D3), and the buffer is written back only while DS:D4E4 is set
/// (R024+0x05B4, R003+0x0132, R014+0x069A). Seen in the original (b35, lockstep dungeon-door-start: a write watch sets bit 31 of 279
/// cells at the battle's entry and again in the in-battle save's reload, the .BSV holding them clear).
/// </summary>
public sealed class LevelFilesTests
{
    private static readonly (int, int, int)[] NoGroups = [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)];

    private static int Hidden(byte[] floor) => Enumerable.Range(0, BattlefieldLevel.Size * BattlefieldLevel.Size).Count(c => (floor[4 * c + 3] & 0x80) != 0);

    /// <summary>The first battle of BattleSaveTests' battlefields whose shown level has wall groups still hidden.</summary>
    private static TacticalBattle WithHiddenGroups(GameFiles files)
    {
        var world = WorldData.Load(files);
        foreach (var (battlefield, seed) in new[] { (0x00, 1234), (0x05, 4321), (0x14, 863), (0x0A, 777), (0x29, 99), (0x5E, 5) })
        {
            var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
            var state = Simulation.NewGame.Quickstart(world, 7);
            var (_, tactics) = BattleSaveTests.StartBattle(ui, files, state, new BattleRequest(battlefield, seed, NoGroups, Persistent: true));
            if (Hidden(tactics.LevelShown.Image) > Hidden(tactics.Field.Store.FloorFiles![tactics.Battle.LevelShown]))
            {
                return tactics;
            }
        }
        Assert.Fail("no battlefield tried shows a level with a hidden wall group");
        return null!;
    }

    [Fact]
    public void AnUnchangedLevelIsWrittenAsItsFileWithoutTheHiddenGroupsMarks()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var tactics = WithHiddenGroups(files);
        var shown = tactics.Battle.LevelShown;
        Assert.False(tactics.LevelChanged);
        var file = new BattleFile();
        tactics.WriteBattleFile(file, inBattle: true, 0, 0);
        Assert.Equal(TacticalBattle.SavedFloor(tactics.Field.Store.FloorFiles![shown]), file.Levels[shown].Floor);
        Assert.True(Hidden(file.Levels[shown].Floor) < Hidden(tactics.LevelShown.Image));
    }

    [Fact]
    public void AChangedLevelIsWrittenFromTheBufferAndCountsAsSaved()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var tactics = WithHiddenGroups(files);
        var shown = tactics.Battle.LevelShown;
        tactics.LevelChanged = true;
        var file = new BattleFile();
        tactics.WriteBattleFile(file, inBattle: true, 0, 0);
        Assert.Equal(TacticalBattle.SavedFloor(tactics.LevelShown.Image), file.Levels[shown].Floor);
        Assert.Equal(tactics.LevelShown.Image, tactics.Field.Store.FloorFiles![shown]);
        Assert.False(tactics.LevelChanged);
    }
}
