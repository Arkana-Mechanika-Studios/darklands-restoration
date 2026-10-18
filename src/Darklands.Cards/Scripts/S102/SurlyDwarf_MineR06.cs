namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineR06": an aggressive dwarf glowers at the party. Whatever is said, he answers in
/// rhyme; asking him one thing (line 1) leaves the party where it is, the rest end in a fight.
/// </summary>
// orig: R102+0x0E56 (191D:0006) matched; tests: CardSmokeTests
public sealed class SurlyDwarf_MineR06 : Card
{
    private const int Picture = 200;
    private const int DwarfPicture = 207;

    public override int Number => 0xEA;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        // orig: R102+0x1192 (191D:0342) matched
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3);
        return menu.Run(
            // orig: R102+0x0FB0 (191D:0160) matched
            new MenuLine(() => Answer(c, 1, -3), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R102+0x102A (191D:01DA) matched
            new MenuLine(() => { ShowPicture(c, DwarfPicture); c.Screen.Text(2); c.Next = 0xEA; }, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R102+0x109E (191D:024E) matched
            new MenuLine(() => Answer(c, 3, -4), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R102+0x1118 (191D:02C8) matched
            new MenuLine(() => Answer(c, 4, -2), OptionHint.Saying(NoRisk)));
    }

    private static void Answer(CardContext c, int text, int fight)
    {
        ShowPicture(c, DwarfPicture);
        c.Screen.Text(text);
        c.BattleMode = 1;
        c.Next = fight;
    }
}
