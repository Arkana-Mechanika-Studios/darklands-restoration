namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubi00": a patron (bishop, city lord, Schulz, merchant ...) asks the party to put an end
/// to a robber knight. A village Schulz or a Hanseatic merchant may offer to come along when
/// the party has room.
/// </summary>
// orig: R080+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R080+0x031C (1838:031C) matched; R080+0x0354 (1838:0354) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered).
public sealed class RobberKnightTaskOffered : Card
{
    public const int CardNumber = 0x90;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 94);
        var patron = c.State.SavedCard;
        var quest = c.Events.Find(8, -1, -1, c.State.SavedCard2, 3, -1);
        c.Variables.Location = c.State.SavedCard2;
        // C: nearest_city takes the castle's position word pair (+4, +6) as one long.
        var castle = c.State.Locations[c.Variables.Location];
        c.Variables.Location2 = c.NearestCity(castle.X, castle.Y);
        // DIVERGENCE: without the quest (slot -1) the original reads through a stray pointer; 0 here.
        c.RandomName(c.EventType(quest), 0, 0);
        c.RandomName(c.Variables.Location + 0x44C, 0, 1);
        // Only the Schulz (0x86) and the Hanse merchant (0x47) can join, if there is room.
        c.Next = (patron == 0x86 || patron == 0x47) && c.PresentCount() <= 4 ? CardNumber : patron;
        switch (patron)
        {
            case 0x15:
            case 0x16:
                c.Screen.Text(c.EventOwner(quest) == 4 ? 4 : 3);
                break;
            case 0x32:
            case 0x33:
                c.Screen.Text(0);
                break;
            case 0x43:
                c.Screen.Text(6);
                break;
            case 0x45:
                c.Screen.Text(8);
                break;
            case 0x47:
                c.Screen.Text(7);
                break;
            case 0x59:
                c.Screen.Text(5);
                break;
            case 0x86:
                c.Screen.Text(2);
                break;
            default:
                c.Screen.Text(1);
                break;
        }
        c.Screen.Text(0x0E);
        // C: the script keeps loc_reputation(cur_loc) in cur_rep; nothing reads it.
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        // DORMANT: for any other patron the script would not show an option list (it never gets here).
        var menu = Menu(c, patron == 0x86 ? 0x0A : 0x0D);
        menu.Enable(0, 1);
        return menu.Run(
            // orig: R080+0x024A (1838:024A) matched; R080+0x02A4 (1838:02A4) matched; tests: CardSmokeTests
            new MenuLine(() => TakeTheGuideAlong(c, patron), OptionHint.Saying(NoRisk)),
            // orig: R080+0x02DC (1838:02DC) matched; R080+0x02E4 (1838:02E4) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = patron, OptionHint.Saying(NoRisk)));
    }

    private static void TakeTheGuideAlong(CardContext c, int patron)
    {
        c.RandomName(c.Here, 0, 0);
        // The Schulz joins for 90 days, the merchant for a year (shield picture "F01").
        if (patron == 0x86)
        {
            c.AddMember(c.Variables.Named[0], "Schulz", "F01", 0x870, 9);
        }
        else
        {
            c.AddMember(c.Variables.Named[0], "Hanse", "F01", 0x21C0, 7);
        }
        c.Next = patron;
    }
}
