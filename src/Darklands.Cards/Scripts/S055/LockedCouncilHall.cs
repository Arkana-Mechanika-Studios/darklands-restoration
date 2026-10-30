namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Counc01": the locked council hall. The party wants to reach the dungeon, but the prisoners
/// have been moved to the fortress. Of the nine option lines of the text (bribes, talking, a saint,
/// sneaking, a potion, the city square, the main street, a side street) the code only enables
/// three, which lead to the city square, the main street and a side street.
/// </summary>
// orig: R055+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class LockedCouncilHall : Card
{
    public const int CardNumber = 0x2C;

    private const int Picture = 95, DungeonCellPicture = 347;

    private const int CityOutskirts = 0x04, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09,
        SideStreetAtNight = 0x0A, CitySquare = 0x12, CitySquareAtNight = 0x19;

    public override int Number => CardNumber;

    // orig: R055+0x0000 (1838:0000) matched (entry: card set-up, the option flags (0..6 off, 7..9 on) and the menu loop)
    // orig: R055+0x050A (1838:050A) matched (init helper: enables all 20 lines, installs the "screwed up." default handler on them and the pop-up handlers on all 5 member rows, empties the item and saint slots; the card then clears the flags of lines 0..6)
    // DORMANT: R055+0x05AC (1838:05AC): the "screwed up." default handler of lines 10..19; the init loop leaves them enabled but the card text has no line for them.
    // DORMANT: R055+0x05E4 (1838:05E4), R055+0x0620 (1838:0620), R055+0x065C (1838:065C) and R055+0x0698 (1838:0698): the item pop-up handlers (slots 0..3): they name the member and say "Success is assured."; the card fills no item slot and never asks usable-item-list(), so the pop-up never opens.
    // DORMANT: R055+0x06D4 (1838:06D4), R055+0x075E (1838:075E), R055+0x07E8 (1838:07E8) and R055+0x0872 (1838:0872): the saint pop-up handlers (slots 0..3, identical): member name and chance with the saint; no saint slot is filled and saints-available() is never asked.
    // C: the flags enable options 7, 8 and 9, one position later than the text's lines ("return to the city square" is line 6, "leave by the main street" line 7, "leave by a side street" line 8, and there is no line 9), so the actions of 7, 8 and 9 are the square, the main street and a side street while the lines 7 and 8 read main street and side street. Kept as coded.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        // DORMANT: options 0..6 are switched off by the card (flags 0); their hints and actions are kept.
        var menu = Menu(c, 0);
        menu.Enable(7, 8, 9);
        var options = new MenuLine[]
        {
            // orig: R055+0x021A (1838:021A) matched (option 0 action); hint handler R055+0x022E (1838:022E): a 100 % chance for the leader
            new MenuLine(() => c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight,
                () => OptionHint.ChanceOf(0x64, c.Leader)),
            // orig: R055+0x0268 (1838:0268) matched (option 1 action); hint handler R055+0x02AE (1838:02AE)
            new MenuLine(() =>
                {
                    ShowPicture(c, DungeonCellPicture);
                    c.Screen.Text(1);
                    c.PassHours(0);
                    c.Next = CityOutskirts;
                },
                Assured),
            // orig: R055+0x02E6 (1838:02E6) matched (option 2 action); hint handler R055+0x02EE (1838:02EE)
            new MenuLine(() => c.Next = CityOutskirts, Assured),
            // orig: R055+0x0326 (1838:0326) matched (option 3 action); hint handler R055+0x032E (1838:032E)
            new MenuLine(() => c.Next = CityOutskirts, Assured),
            // orig: R055+0x0366 (1838:0366) matched (option 4 action); hint handler R055+0x036E (1838:036E)
            new MenuLine(() => c.Next = CityOutskirts, Assured),
            // orig: R055+0x03A6 (1838:03A6) matched (option 5 action); hint handler R055+0x03AE (1838:03AE)
            new MenuLine(() => c.Next = CityOutskirts, Assured),
            // orig: R055+0x03E6 (1838:03E6) matched (option 6 action); hint handler R055+0x03EE (1838:03EE)
            new MenuLine(() => c.Next = CityOutskirts, Assured),
            // orig: R055+0x0426 (1838:0426) matched (option 7 action); hint handler R055+0x043A (1838:043A)
            new MenuLine(() => c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight, Assured),
            // orig: R055+0x0472 (1838:0472) matched (option 8 action); hint handler R055+0x0486 (1838:0486)
            new MenuLine(() => c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight, Assured),
            // orig: R055+0x04BE (1838:04BE) matched (option 9 action); hint handler R055+0x04D2 (1838:04D2)
            new MenuLine(() => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight, Assured),
        };
        c.Next = Number;
        return menu.Run(options);
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);
}
