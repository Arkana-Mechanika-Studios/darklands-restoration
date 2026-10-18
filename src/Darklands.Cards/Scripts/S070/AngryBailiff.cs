namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CESde00": an angry bailiff takes the foreign party for bandits and sets his guards on it.
/// Apologise, offer all the gold, flee, throw an alchemical bomb, pray, or fight. An unfinished
/// card: all ten lines let no time pass and lead to the "option isn't working yet" card.
/// </summary>
// orig: R070+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: lines 0..9: R070+0x0230, 027e, 02c8, 0312, 035c, 03a6, 03f0, 043a, 0484, 04ce (hints 0242 .. 04e0);
// handler setup R070+0x051C; unused-line handler 05be ("screwed up."); standard item/saint pop-ups
public sealed class AngryBailiff : Card
{
    /// <summary>The card's picture is named in the card itself ("smars2"); it has no picture number.</summary>
    private const string BailiffPicture = "smars2";

    private const int NotWorkingYet = 0x62;

    public override int Number => 0x68;

    public override int Run(CardContext c)
    {
        c.Screen.Show(Name, BailiffPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The items and saints are listed, but no line is tied to the pop-ups.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        // C: the hint handlers of lines 0 and 9 also assign the leader to r.a, then overwrite it: all ten say "assured" for the party.
        return menu.Run(Enumerable.Range(0, 10)
            .Select(_ => new MenuLine(__ => Leave(c), OptionHint.Saying(SuccessAssured)))
            .ToArray());
    }

    private static void Leave(CardContext c)
    {
        c.PassHours(0);
        c.Next = NotWorkingYet;
    }
}
