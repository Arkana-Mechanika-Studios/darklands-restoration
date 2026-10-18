namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineB02": the party meets the king of the mine's dwarfs. It may hear him out (0xE3) or
/// provoke him into a fight (battle −3).
/// </summary>
// orig: R101+0x0622 (189A:0002) matched; tests: CardSmokeTests
public sealed class DwarfKing_MineB02 : Card
{
    private const int Picture = 200;

    public override int Number => 0xE5;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        // orig: R101+0x07E0 (189A:01C0) matched
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1);
        return menu.Run(
            // orig: R101+0x0738 (189A:0118) matched
            new MenuLine(() => c.Next = 0xE3, () => OptionHint.Say(NoRisk, c.Leader)),
            // orig: R101+0x077A (189A:015A) matched
            new MenuLine(() =>
                {
                    c.Screen.Text(1);
                    c.BattleMode = 1;
                    c.BattleEnemy = 0x0B;
                    c.BattleSeed = c.Random(1000);
                    c.Next = -3;
                }, OptionHint.Saying(CardTexts.Unknown)));
    }
}
