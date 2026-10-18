namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC09": inside a weird stump the demons guarded, the party finds a seed the size of a
/// man's head, one of the Keys. Fact 1/0x18 is set for good, the room counts as done (flags
/// 0x101) and the card ends with the courtyard fight.
/// </summary>
// orig: R132+0x00FC (1847:000C) matched; tests: CardSmokeTests
//   other: R132+0x01A8, R132+0x01E0
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
// DORMANT: the file's table set-up R132+0x01A8 and its "screwed up." default handler R132+0x01E0 are never called
//   (the card runs no menu); they would only clear the option table and the item/saint lists.
public sealed class StumpWithTheSeed : Card
{
    public override int Number => 353;

    private const int GiantSeed = 0x8F;

    public override int Run(CardContext c)
    {
        Show(c, 309);
        c.Next = Number;
        // No option menu: the card's item and saint lists are left as they are.
        c.Screen.Text(0); // UNCERTAIN: card_text(k, 2): frame mode 2 read as drawing like mode 0
        c.AddItem(-1, GiantSeed);
        c.Events.Create(-2, c.Here, 0, 0x1C, 0x18, 0, 0, Core.Events.EventLog.Forever, 0, 1);
        c.LastCastleFlags |= 0x101;
        LastCastle.Fight(c, LastCastle.CourtyardEnemy);
        return c.Next;
    }
}
