using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineR10": the giant dwarf slavemaster in the dwarfs' hall. The party may fight him, try
/// to reason with him (he loses patience the second time and fights), or back away (battle −2).
/// Beating him frees the mine; losing ends the quest.
/// </summary>
// orig: R103+0x0688 (18A0:0008) matched; tests: CardSmokeTests
public sealed class GiantSlavemaster_MineR10 : Card
{
    private const int Picture = 201;
    private const int KoboldsPicture = 213;
    private const int SlavemasterPicture = 202;
    private const int MineQuestTag = 0x0C;

    public override int Number => 0xEE;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.OptionsBlock = 0;
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        // orig: R103+0x0A90 (18A0:0410) matched
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        // C: the menu always shows block 0; OptionsBlock only remembers that he was spoken to.
        return menu.Run(
            // orig: R103+0x0938 (18A0:02B8) matched
            new MenuLine(() => { c.Screen.Text(1); Fight(c); }, OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R103+0x0982 (18A0:0302) matched
            new MenuLine(() =>
                {
                    ShowPicture(c, SlavemasterPicture);
                    if (c.OptionsBlock != 0)
                    {
                        c.Screen.Text(9);
                        Fight(c);
                        return;
                    }
                    c.Screen.Text(8);
                    ShowPicture(c, Picture);
                    c.OptionsBlock = 1;
                    c.Next = 0xEE;
                }),
            // orig: R103+0x0A4A (18A0:03CA) matched
            new MenuLine(() => { c.BattleMode = 1; c.Next = -2; }));
    }

    // orig: R103+0x07C0 (18A0:0140) matched
    private void Fight(CardContext c)
    {
        var result = c.BattleResult = c.RunBattle(0x6C, c.MineDetail(), 3, 1, c.Random(5) + 3, 0, 2, 1, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.Screen.Text(2);
                ShowPicture(c, KoboldsPicture);
                c.Screen.Text(3);
                ShowPicture(c, Picture);
                c.Screen.Text(4);
                ShowPicture(c, SlavemasterPicture);
                c.Screen.Text(5);
                c.Next = 0x2E;
                break;
            case 1 or 2 or 3:
                c.DropAway();
                ShowPicture(c, SlavemasterPicture);
                c.Screen.Text(6);
                TheMineIsLost(c);
                break;
            case 4:
                ShowPicture(c, SlavemasterPicture);
                c.Screen.Text(7);
                c.LoseItems(Who.Everybody);
                TheMineIsLost(c);
                break;
        }
    }

    private static void TheMineIsLost(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, MineQuestTag, 0, 0, 0x63, 0, MineQuestTag);
        c.Next = 0x2E;
    }
}
