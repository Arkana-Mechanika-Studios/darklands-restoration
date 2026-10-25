using Darklands.Cards;
using Darklands.Core.Rules;

namespace Darklands.Cards.Tests;

/// <summary>
/// A card's battle reloads the world (cards_startup R043+0x078E, world_load R043+0x0100): the city record is DARKLAND.CTY's again, so a
/// loc_set into it is lost. The original's shutdown and startup run directly on a Pressburg state after loc_set(85, 3, 0x55) gave
/// city_rec[0x63] = 0x1F, the file's value, and the whole record equal to the file's (local/batch/nmB).
/// </summary>
public sealed class CityReloadTests
{
    private const int Pressburg = 85;

    [Fact]
    public void ACardBattleForgetsWhatLocSetWroteIntoTheCity()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: Pressburg);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0)) { Battle = (_, _, _) => 0 };
        var file = state.Data.LoadCity(Pressburg).ToArray();
        Assert.Equal(file, state.City.ToArray());
        LocationRules.Set(state, Pressburg, 3, 0x55);
        Assert.Equal(0x55, state.City.Byte(0x63));
        c.RunBattle(0, 0);
        Assert.Equal(0x1F, state.City.Byte(0x63));
        Assert.Equal(file, state.City.ToArray());
        Assert.Equal(0x1F, LocationRules.Query(state, Pressburg, 3));
    }

    [Fact]
    public void InTheWildernessTheRecordIsTheFilesFirstBytes()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: Pressburg);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0)) { Battle = (_, _, _) => 0 };
        state.City.SetWord(0x40, 0x1234);
        state.CurrentLocation = Core.GameState.Wilderness;
        c.RunBattle(0, 0);
        var expected = state.Data.CityFileBytes(0, Core.World.CityRecord.Size);
        Assert.Equal(expected, state.City.ToArray());
    }
}
