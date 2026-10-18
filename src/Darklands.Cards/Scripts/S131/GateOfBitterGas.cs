namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC06": the third gate. While the Third Seal holds (no fact 1/0x16) it stays shut and
/// the party turns away; once broken, bitter gas rushes out over a glowing floor and the card
/// ends with that hall's fight (enemy 0x5A).
/// </summary>
// orig: R131+0x0C8A (1900:000A) matched; tests: CardSmokeTests
//   other: R131+0x0D50, R131+0x0D88
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// DORMANT: the file's table set-up R131+0x0D50 and its "screwed up." default handler R131+0x0D88 are never called
//   (the card runs no menu); they would only clear the option table and the item/saint lists.
public sealed class GateOfBitterGas : Card
{
    public override int Number => 350;

    public override int Run(CardContext c)
    {
        // No option menu: the card's item and saint lists are left as they are.
        if (c.Events.FactAnywhere(1, 0x16))
        {
            Show(c, 333);
            c.Screen.Text(1);
            LastCastle.Fight(c, 0x5A);
        }
        else
        {
            Show(c, 231);
            c.Screen.Text(0);
            LastCastle.TurnBack(c);
        }
        return -2;
    }
}
