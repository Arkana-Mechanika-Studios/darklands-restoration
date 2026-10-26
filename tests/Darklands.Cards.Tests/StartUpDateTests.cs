using Darklands.Core.Time;
using Darklands.Simulation;

namespace Darklands.Cards.Tests;

/// <summary>
/// The program's date before a game sets one: the original's hour, day, month, year words DS:00E0..00E7 captured at the "Create
/// characters" menu (local/batch/evid1o creation-menu.steps: 09 00 1A 00 08 00 78 05), which the menu's load of SAVES\DEFAULT does not
/// change; a prayer from the menu's character sheet runs on that date at the start-up city.
/// </summary>
public sealed class StartUpDateTests
{
    [Fact]
    public void TheProgramStartsAtNineOnDayTwentySixOfMonthEightIn1400() =>
        Assert.Equal(new GameDate(9, 26, 8, 1400), NewGame.StartUpDate);
}
