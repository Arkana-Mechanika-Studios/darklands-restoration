namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityR00": leaning against an old wall, enjoying the busy city by day. An unfinished card
/// (see <see cref="UnfinishedPauseScene"/>): every line leads to the "option isn't working yet" card.
/// </summary>
// orig: R069+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: lines 0..9: R069+0x0242, 028e, 02da, 0326, 0370, 03bc, 0406, 0452, 049e, 04ea (hints 0254 .. 04fc);
// handler setup R069+0x0536; unused-line handler 05d8 ("screwed up."); standard item/saint pop-ups
public sealed class CityWallInTheDay : UnfinishedPauseScene
{
    public override int Number => 0x64;
}
