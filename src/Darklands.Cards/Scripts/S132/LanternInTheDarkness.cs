namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC08": in the halls of darkness lies a small lantern with a magic flame, one of the
/// Keys. The party takes it; the room counts as done (flags 0x808) and the card ends with the
/// courtyard fight.
/// </summary>
// orig: R132+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   other: R132+0x008C, R132+0x00C4
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card_text(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card_text R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
// DORMANT: the file's table set-up R132+0x008C and its "screwed up." default handler R132+0x00C4 are never called
//   (the card runs no menu); they would only clear the option table and the item/saint lists.
public sealed class LanternInTheDarkness : Card
{
    public override int Number => 352;

    private const int MagicLantern = 0x8D;

    public override int Run(CardContext c)
    {
        Show(c, 294);
        c.Next = Number;
        // No option menu: the card's item and saint lists are left as they are.
        c.Screen.Text(0); // card_text(k, 2): frame mode 2 drawn like mode 0 (card_text R043+0x0E40 treats 0 and 2 alike)
        c.AddItem(-1, MagicLantern);
        c.LastCastleFlags |= 0x808;
        LastCastle.Fight(c, LastCastle.CourtyardEnemy);
        return c.Next;
    }
}
