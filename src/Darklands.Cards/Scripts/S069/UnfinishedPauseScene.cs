namespace Darklands.Cards.Scripts;

/// <summary>
/// The scene the game's unfinished city "rest" cards share ($CityR00 and $CityR01): the plain
/// "common" picture and ten option lines (continue, pause an hour, pause a bell, wait for the
/// night or the morning, look for a room ...); none of them is written yet, so each lets no time
/// pass and sends the party to the "option isn't working yet" card.
/// </summary>
// orig: R069+0x0000 (1838:0000) and R069+0x0928 (18CA:0008), and R069 seg 18CA matched; the two cards are the same code
public abstract class UnfinishedPauseScene : Card
{
    private const int CommonPicture = 0;
    private const int NotWorkingYet = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, CommonPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The items and saints are listed, but no line is tied to the pop-ups.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        // The hints: 100% for the leader on lines 0, 2, 6..9; the leader alone on lines 1 and 4; the party on 3 and 5.
        return menu.Run(
            Line(c, () => new OptionHint(100, SuccessAssured, c.Leader)),
            Line(c, () => new OptionHint(-1, SuccessAssured, c.Leader)),
            Line(c, () => new OptionHint(100, SuccessAssured, c.Leader)),
            Line(c, () => OptionHint.Say(SuccessAssured)),
            Line(c, () => new OptionHint(-1, SuccessAssured, c.Leader)),
            Line(c, () => OptionHint.Say(SuccessAssured)),
            Line(c, () => new OptionHint(100, SuccessAssured, c.Leader)),
            Line(c, () => new OptionHint(100, SuccessAssured, c.Leader)),
            Line(c, () => new OptionHint(100, SuccessAssured, c.Leader)),
            Line(c, () => new OptionHint(100, SuccessAssured, c.Leader)));
    }

    private static MenuLine Line(CardContext c, Func<OptionHint> hint) => new(_ =>
    {
        c.PassHours(0);
        c.Next = NotWorkingYet;
    }, hint);
}
