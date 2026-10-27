using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Persistence;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The cursor keys' state DS:D728, DS:D73B, DS:D7FE, DS:D947, DS:D945: the battle-state save writes it as it stands (R003+0x00DC at
/// 0x02DC..0x0315, battle file +0x156A..+0x156F), the restore puts it back (R003+0x0CE8 at 0x0EDB..0x0F06), and no battle start clears it
/// (round B3, lockstep session b3-save-fight: a save in order mode right after the start wrote D7FE = 1 in the original, 0 here).
/// </summary>
public sealed class CursorKeyStateTests
{
    private static readonly (int, int, int)[] NoGroups = [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)];

    [Fact]
    public void TheBattleFileHoldsTheFiveValuesInTheOriginalsOrder()
    {
        var state = new CursorKeyState { Hover = true, Click = false, On = true, ClickOnRelease = true, ReleasedAt = 0x1234 };
        var bytes = new byte[6];
        state.Write(bytes);
        Assert.Equal(new byte[] { 1, 0, 1, 1, 0x34, 0x12 }, bytes);
        var back = new CursorKeyState();
        back.Read(bytes);
        Assert.True(back.Hover);
        Assert.False(back.Click);
        Assert.True(back.On);
        Assert.True(back.ClickOnRelease);
        Assert.Equal(0x1234, back.ReleasedAt);
    }

    [Fact]
    public void ASaveInOrderModeWritesTheCursorKeysOn()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var (_, tactics) = BattleSaveTests.StartBattle(ui, files, state, new BattleRequest(0x14, 863, NoGroups, Persistent: true));
        tactics.EnterOrderMode(tactics.Battle.Leader);
        Assert.True(tactics.CursorKeysOn);
        var file = new BattleFile();
        tactics.WriteBattleFile(file, inBattle: true, 0, 0);
        Assert.Equal(1, file.Variables[BattleVariables.CursorKeys + 2]);

        tactics.LeaveOrderMode();
        tactics.WriteBattleFile(file, inBattle: true, 0, 0);
        Assert.Equal(0, file.Variables[BattleVariables.CursorKeys + 2]);
    }

    [Fact]
    public void ABattleTakenUpHasTheSavedStateAndTheProgramsBattlesShareIt()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(world, 7);
        var (_, tactics) = BattleSaveTests.StartBattle(ui, files, state, new BattleRequest(0x14, 863, NoGroups, Persistent: true));
        tactics.CursorKeys.Hover = true;
        tactics.CursorKeys.Click = true;
        tactics.CursorKeys.On = true;
        tactics.CursorKeys.ClickOnRelease = true;
        var (sav, bsv) = BattleSaveTests.SaveInBattle(state, tactics);
        Assert.Equal(new byte[] { 1, 1, 1, 1, 0, 0 }, state.BattleFile!.Variables[BattleVariables.CursorKeys..(BattleVariables.CursorKeys + 6)]);

        var loaded = OriginalSaves.Read(sav, bsv, "dksave1.sav", world).State;
        var screens = new BattleScreens(ui, () => loaded);
        Assert.False(screens.CursorKeys.On);
        var screen = new BattleScreen(ui, new BattleAssets(files), loaded, screens);
        screen.Start(new BattleRequest(0x14, 863, NoGroups, Persistent: true, Resume: true));
        var resumed = screen.Tactics!;
        Assert.Same(screens.CursorKeys, resumed.CursorKeys);
        Assert.True(resumed.CursorKeys.Hover);
        Assert.True(resumed.CursorKeys.Click);
        Assert.True(resumed.CursorKeys.ClickOnRelease);

        // the next battle of the program begins with what this one left (R014+0x0000 clears none of them)
        var next = new BattleScreen(ui, new BattleAssets(files), loaded, screens);
        next.Start(new BattleRequest(0x05, 4321, NoGroups, Persistent: true));
        Assert.Same(resumed.CursorKeys, next.Tactics!.CursorKeys);
    }
}
