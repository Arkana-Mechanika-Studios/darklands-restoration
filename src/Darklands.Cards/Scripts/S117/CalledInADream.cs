namespace Darklands.Cards.Scripts;

/// <summary>
/// "$wqSta02": in camp, the whole party dreams of a demon, a Templar fortress and the sevenfold
/// path, and a saint calls them to fight the witches' Sabbat and the Templars. Shown once
/// (until fact 1/1 exists); afterwards the card passes straight on to the map.
/// </summary>
// orig: R117+0x0896 (18C1:0006) matched; tests: CardSmokeTests
// DORMANT: the option flags are set but no menu follows; the file's 22 handlers are never called.
public sealed class CalledInADream : Card
{
    public override int Number => 294;

    public override int Run(CardContext c)
    {
        if (c.Events.FactAnywhere(1, 1))
        {
            return CardContext.MapCard;
        }
        c.PlayMusic(0x26);
        Show(c, 0);
        c.Next = Number;
        // DORMANT: the second test of fact 1/1 is always true here (it was tested above).
        if (!c.Events.FactAnywhere(1, 1))
        {
            Show(c, 289);
            c.Screen.Text(0);
            c.Screen.Text(1);
            c.PlayMusic(0x27);
            Show(c, 90);
            c.Screen.Text(2);
            Show(c, 88);
            c.Screen.Text(3);
            c.PassHours(c.HoursUntil(5));
        }
        c.LeaveForTheMap();
        c.Events.Remove(0x1C, 2, -1, -1, 1);
        return c.Next;
    }
}
