namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityR01": watching the moonlight on the city roofs from a grove of trees at night. An
/// unfinished card (see <see cref="UnfinishedPauseScene"/>): every line leads to the "option
/// isn't working yet" card.
/// </summary>
// orig: R069+0x0928 (18CA:0008) matched; tests: CardSmokeTests
// orig: lines 0..9: R069+0x0B6A, 0296, 02e2, 032e, 0378, 03c4, 040e, 045a, 04a6, 04f2 (hints 025c .. 0504);
// handler setup R069+0x0E5E; unused-line handler R069+0x0F00 ("screwed up."); standard item/saint pop-ups
public sealed class CityRoofsByMoonlight : UnfinishedPauseScene
{
    public override int Number => 0x65;
}
