namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineS00": the miners' shacks at the troubled mine. The head miner (a random name) asks the
/// party to clear the mine of knockers; the party can travel on, hear the story again (only while
/// the quest is on), enter the mine (which starts a battle) or buy from the mine's gift shop.
/// </summary>
// orig: R055+0x08FC (18C7:000C) matched; tests: CardSmokeTests
public sealed class MineVillage : Card
{
    public const int CardNumber = 0x2D;

    private const int Picture = 209, SecondPicture = 208;

    /// <summary>The mine quest's tag, the event that marks the mine as being fought for, and the card the party came from at a quest hand-over.</summary>
    private const int MineQuest = 0x0C, MineBattleEvent = 0x64, QuestHandOver = 0x43;

    private const int OverlandMap = 0x0C;

    public override int Number => CardNumber;

    // orig: R055+0x08FC (18C7:000C) matched (entry: head miner's name, the quest texts, the option flags and the menu loop)
    // orig: R055+0x0C98 (18C7:03A8) matched (init helper: clears the option flags, installs the "screwed up." default handler on all 20 lines, empties the item and saint slots)
    // DORMANT: R055+0x0CD0 (18C7:03E0): the "screwed up." default handler of lines 4..19; the card text has four lines.
    // DORMANT: the card has no item or saint pop-up handlers (the init helper installs none) and fills no item or saint slot.
    // C: the flags are copied from the module's option flags 0..3 on every round; flag 1 is cleared before the loop, so "refresh my memory" is never offered.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.RandomName(c.Here + 8, 0, 0); // random_name: the head miner's name (the EXE's name tables via PersonalNames)
        if (c.State.PreviousCard == QuestHandOver)
        {
            c.Screen.Text(0);
            return c.State.PreviousCard;
        }
        var mineOpen = OptionState.Hidden;
        if (c.Events.QuestAt(MineQuest, c.Here))
        {
            TellTheStory(c);
            mineOpen = OptionState.Enabled;
        }
        var menu = Menu(c, 5);
        menu.Enable(0, 2, 3);
        menu[2] = mineOpen;
        var options = new MenuLine[]
        {
            // orig: R055+0x0ADA (18C7:01EA) matched (option 0 action: travel on to the overland map); hint handler R055+0x0AE2 (18C7:01F2)
            new MenuLine(() => c.Next = OverlandMap, OptionHint.Saying(SuccessAssured)),
            // orig: R055+0x0B1A (18C7:022A) matched (option 1 action: the story again, see TellTheStory); hint handler R055+0x0B70 (18C7:0280)
            // DORMANT: option 1 is switched off by the card (flag 1 is cleared after the quest check), so it is never offered.
            new MenuLine(() =>
                {
                    TellTheStory(c);
                    c.Next = CardNumber;
                },
                OptionHint.Saying(SuccessAssured)),
            // orig: R055+0x0BA8 (18C7:02B8) matched (option 2 action, see EnterTheMine); hint handler R055+0x0C14 (18C7:0324)
            new MenuLine(() => EnterTheMine(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R055+0x0C4C (18C7:035C) matched (option 3 action: the mine's gift shop, items of category 0x400); hint handler R055+0x0C60 (18C7:0370)
            new MenuLine(() => c.Barter(-1, 0x400L, "Mine Giftshop"), OptionHint.Saying(SuccessAssured)),
        };
        c.Next = Number;
        return menu.Run(options);
    }

    /// <summary>The head miner's account: the first part on the card's picture, the knockers on a second picture.</summary>
    private static void TellTheStory(CardContext c)
    {
        c.Screen.Text(3);
        ShowPicture(c, SecondPicture);
        c.Screen.Text(4, TextFrame.OnPicture);
        ShowPicture(c, Picture);
    }

    /// <summary>
    /// Enters the mine: a battle against the knockers whose kind depends on the mine's sort (and,
    /// for the other sorts, on a draw of its detail); the battle is started by returning -2.
    /// </summary>
    private static void EnterTheMine(CardContext c)
    {
        c.BattleSetup.Mode = 0;
        c.EventHere(MineBattleEvent, 1);
        switch (c.MineSort())
        {
            case 0:
                c.BattleSetup.Enemy = 0x0B;
                break;
            case 2:
                c.BattleSetup.Enemy = 0x0D;
                break;
            default:
                // C: the remainder keeps the sign of the detail, so a negative detail falls to the default.
                switch (c.MineDetail() % 3)
                {
                    case 0:
                        c.BattleSetup.Enemy = 0x0E;
                        break;
                    case 2:
                        c.BattleSetup.Enemy = 0x0F;
                        break;
                    default:
                        c.BattleSetup.Enemy = 0x10;
                        break;
                }
                break;
        }
        // C: the seed is a 16-bit sum.
        c.BattleSetup.Seed = (short)(c.State.Party.Seed + c.Here);
        c.Next = -2;
    }
}
