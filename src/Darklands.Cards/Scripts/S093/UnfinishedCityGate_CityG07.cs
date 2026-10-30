namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityG07": an unfinished card. Its text is a single "x" without option lines; each of its
/// ten options shows that text and ends the game (0x62). It lists potions 0x5F, 0x60, 0x65,
/// 0x61 and saints 0, 0x59, 0x61, 0x80 for pop-ups its text never offers.
/// </summary>
// orig: R093+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class UnfinishedCityGate_CityG07 : Card
{
    private const int Picture = 95;

    public override int Number => 0xC4;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // the card sets the engine word DS:EE70 to 15 (other cards 0), meaning unknown; not modelled.
        // orig: R093+0x05CA (1838:05CA) matched (option and pop-up reset)
        c.SetCardSaints();
        c.SetCardItems();
        // C: card-item-lines[3] is stored between the saints, in the order 0x5F, 0x60, 0x65, saints 0
        // and 0x59, item 0x61, saints 0x61 and 0x80; the result is the same lists.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        // orig: R093+0x0246 (1838:0246) and the nine handlers like it, matched
        // DORMANT: the item pop-up hints (06A4..0758) would also name the chooser as $ChosenOneName;
        // the text has no pop-up line, so they never run.
        var end = new MenuLine(() => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; });
        return menu.Run(end, end, end, end, end, end, end, end, end, end);
    }
}
