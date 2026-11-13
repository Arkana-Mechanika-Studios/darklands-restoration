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

    // C: the party's place tables (doors DS:C64C / DS:C678 / DS:C6EF, stairs DS:C77E, chest DS:C784, dissolve slot DS:C7CD, exit zone
    //   DS:C7FE, stairs back DS:C8B2) carry into a new battle (b35: the original's second battle of a run starts with member 1's door 1 /
    //   side 1 from the first; markers written at the fight's entry survive the start for slots 0..4); a fresh program's are zeros
    [Fact]
    public void ANewBattleStartsFromThePartysPlaceTablesAsTheyWere()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, kept) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]),
            s =>
            {
                var file = new BattleFile();
                file.Variables[BattleVariables.MemberDoor + 1] = 1;
                file.Variables[BattleVariables.MemberDoorSide + 1] = 1;
                file.Variables[BattleVariables.MemberDoorOpens + 1] = 1;
                file.Variables[BattleVariables.MemberStairs + 3] = 2;
                file.Variables[BattleVariables.MemberChest + 3] = 4;
                file.Variables[BattleVariables.MemberDissolveSlot + 3] = 5;
                file.Variables[BattleVariables.MemberExitZone + 3] = 0xFF;
                file.Variables[BattleVariables.MemberStairsBack + 3] = 1;
                s.BattleFile = file;
            });
        Assert.Equal((1, 1, true), (kept.Battle[1].Door, kept.Battle[1].DoorSide, kept.Battle[1].DoorOpens));
        var m3 = kept.Battle[3];
        Assert.Equal((2, 4, 5, -1, 1), (m3.StairsToTake, m3.Chest, m3.DissolveSlot, m3.ExitZone, m3.StairsBack));
        var (_, fresh) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        Assert.Equal((0, 0, false), (fresh.Battle[1].Door, fresh.Battle[1].DoorSide, fresh.Battle[1].DoorOpens));
        var f3 = fresh.Battle[3];
        Assert.Equal((0, 0, 0, 0, 0), (f3.StairsToTake, f3.Chest, f3.DissolveSlot, f3.ExitZone, f3.StairsBack));
    }

    // C: DS:C7A4 (the chests' loot file offsets) is not cleared for a new battlefield: a chest it does not have keeps the program's value
    //   (b35: a read watch in the original, the loot take R014+0xE7C8 reads them from the fight's entry); a fresh program's are zeros
    [Fact]
    public void ANewBattlefieldKeepsTheChestOffsetsItHasNoChestFor()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, kept) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]),
            s =>
            {
                var file = new BattleFile();
                file.Variables[BattleVariables.ChestOffsets + 2 * 19] = 0xFF;
                file.Variables[BattleVariables.ChestOffsets + 2 * 19 + 1] = 0xFF;
                s.BattleFile = file;
            });
        Assert.Equal(-1, kept.Field.Store.Loot.ChestOffsets[19]);
        var (_, fresh) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        Assert.Equal(0, fresh.Field.Store.Loot.ChestOffsets[19]);
    }
}
