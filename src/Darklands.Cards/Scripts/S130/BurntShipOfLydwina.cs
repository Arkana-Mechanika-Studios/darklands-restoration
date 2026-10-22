namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC02": the scorched wreck of a boat. The first time (no fact 1/0x17) the party frees
/// Lydwina from its hold; she tells of the Templars and Baphomet, gives them the first Key (the
/// globe with the trout) and breaks four Seals of the Apocalypse (facts 1/0x17 and 1/0x16).
/// Later visits find the globe again if it was lost, or nothing. Either way the room counts as
/// done (flags 0x202) and the card ends with the courtyard fight.
/// </summary>
// orig: R130+0x1674 (199F:0004) matched; tests: CardSmokeTests
//   other: R130+0x183C, R130+0x1874
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card_text(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card_text R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class BurntShipOfLydwina : Card
{
    public override int Number => 346;

    private const int GlobeWithTheTrout = 0x8C;

    public override int Run(CardContext c)
    {
        Show(c, 283);
        c.Next = Number;
        c.Variables.Named[0] = "Lydwina";
        // No option menu: the card's item and saint lists are left as they are.
        // DORMANT: the file's table set-up R130+0x183C (and its "screwed up." default handler
        //   R130+0x1874) is never called; it would only clear the option table and the item/saint lists.
        if (!c.Events.FactAnywhere(1, 0x17))
        {
            // card_text(k, 2): frame mode 2 drawn like mode 0 (card_text R043+0x0E40 treats 0 and 2 alike).
            c.Screen.Text(0);
            c.Screen.Text(1);
            c.Screen.Text(2);
            c.AddItem(-1, GlobeWithTheTrout);
            // C: the picture changes go through card_load (a full reload of the card), not card_load_near.
            ShowPicture(c, 322);
            c.Screen.Text(3);
            c.Events.Create(-2, c.Here, 0, 0x1C, 0x17, 0, 0, Core.Events.EventLog.Forever, 0, 1);
            c.Events.Create(-2, c.Here, 0, 0x1C, 0x16, 0, 0, Core.Events.EventLog.Forever, 0, 1);
        }
        else if (c.CountItems(-2, GlobeWithTheTrout) == 0)
        {
            ShowPicture(c, 303);
            c.Screen.Text(4);
            c.AddItem(-1, GlobeWithTheTrout);
        }
        else
        {
            ShowPicture(c, 304);
            c.Screen.Text(5);
        }
        c.LastCastleFlags |= 0x202;
        LastCastle.Fight(c, LastCastle.CourtyardEnemy);
        return c.Next;
    }
}
