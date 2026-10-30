namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubi01": back with the patron after the robber knight's end: thanks, fame and the
/// promised reward, depending on who gave the task. The quest's record is removed.
/// </summary>
// orig: R080+0x038C (1870:000C) matched; tests: CardSmokeTests
// orig: R080+0x066C (1870:02EC) matched; R080+0x06A4 (1870:0324) matched; tests: CardSmokeTests
public sealed class RobberKnightTaskRewarded : Card
{
    public const int CardNumber = 0x91;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 94);
        var patron = c.State.SavedCard;
        var quest = c.Events.Find(EventKindsResolvedQuest, -1, c.Here, c.State.SavedCard2, 3, -1);
        // DIVERGENCE: without the resolved quest (slot -1) the original reads through a stray pointer; 0 here.
        c.Variables.Location = c.Events[quest]?.Data2 ?? 0;
        c.RandomName(c.EventValue(quest), 0, 0);
        c.RandomName(c.Variables.Location + 0x44C, 0, 1);
        c.Events.Free(quest);
        // C: the script keeps place-query(location-now, 0) in reputation-now; nothing reads it.
        switch (patron)
        {
            case 0x12:
            case 0x19:
            case 0x1B:
            case 0x1C:
            case 0x2B:
            case 0x84:
                c.AdjustReputation(c.Here, 0x28, 0x32);
                c.Screen.Text(0);
                break;
            case 0x15:
                ShowPicture(c, 356);
                c.Screen.Text(2);
                break;
            case 0x32:
            case 0x33:
                break;
            case 0x3F:
                ShowPicture(c, 194);
                c.Screen.Text(3);
                break;
            case 0x47:
                ShowPicture(c, 194);
                // BUG: text 6 (the Hanse guide leaves) or 7 is chosen by a coin toss, not by whether the guide came along; nobody is removed.
                c.Screen.Text(c.Random(2) != 0 ? 6 : 7);
                break;
            case 0x59:
                ShowPicture(c, 197);
                c.Screen.Text(4);
                break;
            case 0x86:
                ShowPicture(c, 345);
                // BUG: text 1 says the Schulz departs from the party whether or not he joined it (RobberKnightTaskOffered); no member is
                // removed here: one who joined stays until his 90 days are up.
                c.Screen.Text(1);
                break;
            default:
                // Also the alchemist's guild (0x43).
                ShowPicture(c, 356);
                c.Screen.Text(c.Random(4) != 0 ? 8 : 5);
                break;
        }
        c.Next = patron;
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        // DORMANT: only a patron card 0x91 would keep the card: twenty lines of the "screwed up."
        // default, none of which leaves (the original loops for ever).
        var nothing = Enumerable.Range(0, CardMenu.MaxLines)
            .Select(_ => new MenuLine(() => { }, OptionHint.Saying(CardTexts.ScrewedUp))).ToArray();
        var menu = Menu(c);
        menu.Enable(Enumerable.Range(0, CardMenu.MaxLines).ToArray());
        return menu.Run(nothing);
    }

    private const int EventKindsResolvedQuest = Core.Events.EventKinds.ResolvedQuest;
}
