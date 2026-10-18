using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineR11": the kobolds' hall, ruled by a human. The party may fight him, hear his one
/// condition (once), or back away (battle −2). Killing him brings the dwarfs, who leave the mine
/// to the humans; losing ends the quest.
/// </summary>
// orig: R103+0x0B00 (18E8:0000) matched; tests: CardSmokeTests
public sealed class KoboldsHumanLeader_MineR11 : Card
{
    private const int Picture = 202;
    private const int MineQuestTag = 0x0C;

    public override int Number => 0xEF;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        // orig: R103+0x0E56 (18E8:0356) matched
        c.SetCardSaints();
        c.SetCardItems();
        return menu.Run(
            // orig: R103+0x0D80 (18E8:0280) matched
            new MenuLine(() => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R103+0x0DBE (18E8:02BE) matched
            new MenuLine(() => { menu[1] = OptionState.Shown; c.Screen.Text(5); c.Next = 0xEF; }),
            // orig: R103+0x0E10 (18E8:0310) matched
            new MenuLine(() => { c.BattleMode = 1; c.Next = -2; }));
    }

    // orig: R103+0x0C38 (18E8:0138) matched
    private void Fight(CardContext c)
    {
        var result = c.BattleResult = c.RunBattle(0x6D, c.MineDetail(), 3, 1, c.Random(5) + 3, 0, 2, 1, -1, 0, 0);
        switch (result)
        {
            case 0:
                ShowPicture(c, 203);
                c.Screen.Text(1);
                ShowPicture(c, 201);
                c.Screen.Text(2);
                ShowPicture(c, 202);
                c.Screen.Text(3);
                c.Next = 0x2E;
                break;
            case 1 or 2 or 3 or 4:
                c.DropAway();
                ShowPicture(c, 212);
                c.Screen.Text(4);
                c.LoseItems(Who.Everybody);
                c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, MineQuestTag, 0, 0, 0x63, 0, MineQuestTag);
                c.Next = 0x2E;
                break;
        }
    }
}
