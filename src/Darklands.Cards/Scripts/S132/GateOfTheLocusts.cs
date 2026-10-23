namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC10": the fifth gate. While the Fifth Seal holds (no fact 1/0x16) it stays closed and
/// the party turns back; once broken, it opens on a banquet hall full of buzzing locusts with
/// women's heads and scorpions' stings, and the card ends with their fight (enemy 0x5C).
/// </summary>
// orig: R132+0x0218 (1859:0008) matched; tests: CardSmokeTests
//   other: R132+0x02DE, R132+0x0316
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// DORMANT: the file's table set-up R132+0x02DE and its "screwed up." default handler R132+0x0316 are never called
//   (the card runs no menu); they would only clear the option table and the item/saint lists.
public sealed class GateOfTheLocusts : Card
{
    public override int Number => 354;

    public override int Run(CardContext c)
    {
        // No option menu: the card's item and saint lists are left as they are.
        if (c.Events.FactAnywhere(1, 0x16))
        {
            Show(c, 336);
            c.Screen.Text(1);
            LastCastle.Fight(c, 0x5C);
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
