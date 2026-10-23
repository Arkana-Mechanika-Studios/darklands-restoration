namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC05": a chunk of wormwood sits in the floor; inside it the party finds a bottle of
/// magic honey, one of the Keys. The room counts as done (flags 0x404) and the card ends with
/// the courtyard fight.
/// </summary>
// orig: R131+0x0B8C (18F0:000C) matched; tests: CardSmokeTests
//   other: R131+0x0C1A, R131+0x0C52
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card_text(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card_text R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
// DORMANT: the file's table set-up R131+0x0C1A and its "screwed up." default handler R131+0x0C52 are never called
//   (the card runs no menu); they would only clear the option table and the item/saint lists.
public sealed class WormwoodHoney : Card
{
    public override int Number => 349;

    private const int BottleOfHoney = 0x8E;

    public override int Run(CardContext c)
    {
        Show(c, 312);
        c.Next = Number;
        // No option menu: the card's item and saint lists are left as they are.
        c.AddItem(-1, BottleOfHoney);
        c.Screen.Text(0); // card_text(k, 2): frame mode 2 drawn like mode 0 (card_text R043+0x0E40 treats 0 and 2 alike)
        c.LastCastleFlags |= 0x404;
        LastCastle.Fight(c, LastCastle.CourtyardEnemy);
        return c.Next;
    }
}
