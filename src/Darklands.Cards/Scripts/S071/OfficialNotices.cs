namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Offic00": the official notices posted on doors and walls. A party that cannot read well
/// (best Read/Write skill 10 or less) has them explained by a friendly citizen in the daytime, and
/// goes away frustrated at night; one that can read sees the notices themselves. Which notices:
/// the city's state (besieged, under special precautions), the politics quest, and the curfew.
/// Then the party goes back to the card that sent it (saved_card2); the card has no menu.
/// </summary>
// orig: R071+0x0D82 (1910:0002) matched; tests: CardSmokeTests
// orig: handler setup R071+0x11B2 (called, but nothing below it is ever offered: the card has no menu loop)
public sealed class OfficialNotices : Card
{
    private const int NoticePicture = 192;
    private const int NightPicture = 193;
    private const int ReadWrite = 12;
    private const int PoliticsQuest = 2;

    public override int Number => 0x6D;

    // C: the original function is declared void; the dispatcher gets AX, which at the end holds saved_card2 just stored in next_card.
    public override int Run(CardContext c)
    {
        Show(c, NoticePicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;

        // C: party_max_skill is called again in the second test (no side effects beyond "best member found").
        if (c.PartyMaxSkill(ReadWrite) <= 10 && c.IsDaylight)
        {
            c.Screen.Text(4);
            ShowTheNotices(c);
        }
        else if (c.PartyMaxSkill(ReadWrite) <= 10)
        {
            ShowPicture(c, NightPicture);
            c.Screen.Text(5);
        }
        else
        {
            ShowTheNotices(c);
        }
        c.Next = c.State.SavedCard2;
        return c.Next;
        // DORMANT: below the card's code the file holds a menu that nothing runs: ten option lines whose actions
        // R071+0x0F16, 01d4, 020e, 0248, 0282 (lines 0..4) do nothing and R071+0x103C,
        // 0306, 0350, 039a, 03e4 (lines 5..9) let no time pass and go to card 0x62; their hints (0198 .. 03f6)
        // all say "Success is assured." and the handler setup 0432 lists the usual items and saints.
    }

    /// <summary>The city's state first (besieged or under precautions), the politics quest, then the curfew or the city lord's order.</summary>
    private static void ShowTheNotices(CardContext c)
    {
        var state = c.HereQuery(0x20);
        if (state == 1)
        {
            c.Screen.Text(1);
        }
        else if (state == 2)
        {
            c.Screen.Text(2);
        }
        if (c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            c.Screen.Text(3);
        }
        // C: % of a negative value keeps its sign; any odd (non-zero) remainder shows the first text.
        if (c.HereQuery(0x21) % 2 != 0)
        {
            c.Screen.Text(0);
        }
        else
        {
            c.Screen.Text(6);
        }
    }
}
