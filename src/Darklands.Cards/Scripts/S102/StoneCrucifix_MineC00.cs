using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineC00": the chest holds a great stone crucifix. Everybody gains 30 points of Divine
/// Favor, and then the mine's foes attack (battle −3). The card has no menu.
/// </summary>
// orig: R102+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: the file also holds ten option handlers (R102+0x0076 … 02B6, each
// ending the game, 0x62), their hints, a reset (R102+0x02F6) and pop-up hints,
// none of them used: the card returns before any menu.
public sealed class StoneCrucifix_MineC00 : Card
{
    private const int Picture = 0;

    public override int Number => 0xE8;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 0x1E);
        c.Screen.Text(0, TextFrame.BlankPage);
        c.BattleMode = 1;
        return c.Next = -3;
    }
}
