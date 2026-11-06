using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Randomness;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The event table and the location records are the program's: a prayer on the creation's member sheet (its events, its standing
/// changes) is still there in the begun game (lockstep session carry-create-pray: the original's begun game holds the prayers' events).
/// </summary>
public sealed class CreationSheetWorldTests
{
    [Fact]
    public void TheBegunGameKeepsTheSheetsEventsAndLocationRecords()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var sheet = new GameState(new MscRandom(3)) { Data = world, CurrentLocation = 5, Date = Simulation.NewGame.StartUpDate };
        sheet.Locations.AddRange(world.CreateLocations());
        var slot = sheet.Events.Create(-1, 0, 0x63, 0x4B, 0, 0, 0, 24, 1, 0);
        sheet.Locations[5].Reputation = 42;
        var game = Simulation.NewGame.Quickstart(world, 7);
        var before = game.Events.All().Count();

        CharacterCreationScreen.CarryOverSheetWorld(sheet, game);

        Assert.Equal(before + 1, game.Events.All().Count());
        Assert.Equal(0x4B, game.Events[slot]!.Kind);
        Assert.Equal(42, game.Locations[5].Reputation);
    }
}
