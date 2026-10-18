using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineR07": a kobold steps up and asks for help against the dwarfs. Offering help (fact 6)
/// or refusing it (fact 5) is remembered for a month; either way, and on the other two lines,
/// a fight follows.
/// </summary>
// orig: R102+0x1202 (1958:0002) matched; tests: CardSmokeTests
public sealed class KoboldEnvoy_MineR07 : Card
{
    private const int Picture = 201;
    private const int KoboldPicture = 202;
    private const int MineQuestTag = 0x0C;

    public override int Number => 0xEB;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        // orig: R102+0x1570 (1958:0370) matched
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3);
        return menu.Run(
            // orig: R102+0x135C (1958:015C) matched
            new MenuLine(() => Answer(c, 1, 2, fact: 6, fight: -3), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R102+0x1400 (1958:0200) matched
            new MenuLine(() => Answer(c, 3, 4, fact: 5, fight: -3), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R102+0x14A4 (1958:02A4) matched
            new MenuLine(() => Answer(c, 5, 2, fact: null, fight: -4), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R102+0x152A (1958:032A) matched
            new MenuLine(() => { c.BattleMode = 1; c.Next = -2; }, OptionHint.Saying(NoRisk)));
    }

    private void Answer(CardContext c, int reply, int more, int? fact, int fight)
    {
        c.Screen.Text(reply);
        ShowPicture(c, KoboldPicture);
        c.Screen.Text(more);
        if (fact is { } type)
        {
            c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, type, 0, 0, 0x2D0, 0, MineQuestTag);
        }
        c.BattleMode = 1;
        c.Next = fight;
    }
}
