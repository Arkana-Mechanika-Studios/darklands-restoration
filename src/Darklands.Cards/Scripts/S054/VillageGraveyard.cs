namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Villa08": the village graveyard, where the quest item (a $McGuffin) rests on a grave guarded
/// by a ghost. Only the first menu is used: the party either gives up on the item (the quest is
/// turned into a resolved one) or just leaves; both go back to the card the party came from. The
/// ghost scenes of the text file are not reached from this card.
/// </summary>
// orig: R054+0x11A4 (1952:0004) matched; tests: CardSmokeTests
public sealed class VillageGraveyard : Card
{
    public const int CardNumber = 0x2A;

    private const int Picture = 182;
    private const int NoGo = 0x62;

    /// <summary>The quest event kind and the tag of the graveyard quest.</summary>
    private const int QuestKind = 8, GraveyardTag = 0x0A;

    public override int Number => CardNumber;

    // orig: R054+0x11A4 (1952:0004) matched (entry: card set-up, the quest item, two enabled option lines and the menu loop)
    // orig: R054+0x1686 (1952:04E6) matched (init helper: clears the option flags, installs the "screwed up." default handler on all 20 lines and the pop-up handlers on all 5 member rows, empties the item and saint slots)
    // DORMANT: R054+0x1728 (1952:0588): the "screwed up." default handler of lines 10..19; nothing selects them.
    // DORMANT: R054+0x1760 (1952:05C0), R054+0x179C (1952:05FC), R054+0x17D8 (1952:0638) and R054+0x1814 (1952:0674): the item pop-up handlers (slots 0..3): they name the member of the pop-up and say "Success is assured."; the card fills the item slots but no option asks usable-item-list() (card-option-flag-3 is never set here), so the pop-up never opens.
    // DORMANT: R054+0x1850 (1952:06B0), R054+0x18DA (1952:073A), R054+0x1964 (1952:07C4) and R054+0x19EE (1952:084E): the saint pop-up handlers (slots 0..3, identical): member name and chance with the saint; saints-available() is never asked either.
    // C: the C sets only card-option-flag-0 and card-option-flag-1; the flags 2..9 it copies into the option table are stale values left by the previous card (DS:EE7A..EE88). The card's first text has two option lines, so options 2..9 cannot be picked; they are kept hidden here with the actions the C has for them.
    // UNCERTAIN: the hint handlers of the ten options return a struct they never fill in (only the text "Success is assured." is set), so the hint shows that text for the whole party here.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        // The quest's first word is the item it is about. DIVERGENCE: with no such quest the original reads
        // event slot -1 (garbage); the C# keeps the item as it was.
        var quest = c.Events.Find(QuestKind, -1, -1, c.Here, GraveyardTag, -1);
        if (quest != -1)
        {
            c.Variables.Item = c.EventType(quest);
        }
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        var options = new MenuLine[10];
        options[0] = new(() => GiveUpTheItem(c), SuccessText);
        options[1] = new(() => Leave(c), SuccessText);
        for (var i = 2; i < options.Length; i++)
        {
            options[i] = new(() => TurnAway(c), SuccessText);
        }
        c.Next = Number;
        return menu.Run(options);
    }

    private static OptionHint SuccessText() => OptionHint.Say(SuccessAssured);

    // orig: R054+0x13F8 (1952:0258) matched (option 0 action); hint handler R054+0x1418 (1952:0278)
    private static void GiveUpTheItem(CardContext c)
    {
        c.PassHours(0);
        c.ResolveQuest(GraveyardTag, c.Here);
        c.Next = c.State.PreviousCard;
    }

    // orig: R054+0x1446 (1952:02A6) matched (option 1 action); hint handler R054+0x1458 (1952:02B8)
    private static void Leave(CardContext c)
    {
        c.PassHours(0);
        c.Next = c.State.PreviousCard;
    }

    // orig: R054+0x1486 (1952:02E6), R054+0x14C6 (1952:0326), R054+0x1506 (1952:0366), R054+0x1546 (1952:03A6), R054+0x1586 (1952:03E6), R054+0x15C6 (1952:0426), R054+0x1606 (1952:0466), R054+0x1646 (1952:04A6) matched (option 2..9 actions, all alike)
    // orig: R054+0x1498 (1952:02F8), R054+0x14D8 (1952:0338), R054+0x1518 (1952:0378), R054+0x1558 (1952:03B8), R054+0x1598 (1952:03F8), R054+0x15D8 (1952:0438), R054+0x1618 (1952:0478), R054+0x1658 (1952:04B8) matched (option 2..9 hints)
    // DORMANT: options 2..9 are not enabled by the C (their flags are stale), see the UNCERTAIN note above; their actions end the visit at card 0x62.
    private static void TurnAway(CardContext c)
    {
        c.PassHours(0);
        c.Next = NoGo;
    }
}
