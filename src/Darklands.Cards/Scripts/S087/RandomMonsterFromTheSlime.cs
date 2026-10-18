using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Rando00": a placeholder encounter written by the game's authors ("some horrible monster
/// dredged from the slime churning in Arnold and Sandy's depraved synapses"). The party may
/// continue on, fight, or talk to it; every line simply leaves the place (the fight and talk
/// lines go on to the encounter card, the others back to the map). On arrival the card makes
/// sure the Wild Hunt quest exists.
/// </summary>
// orig: R087+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RandomMonsterFromTheSlime : Card
{
    public const int CardNumber = 0xAC;

    private const int Picture = 0;

    /// <summary>The encounter card the fight and talk lines lead to.</summary>
    private const int EncounterCard = 0x62;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // Lines 0..2 are offered; lines 3..9 are placeholders (option flags 0).
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        // orig: R087+0x0620 (1838:0620) matched; R087+0x06C2 (1838:06C2) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 10..19 (never offered).
        c.SetCardItems();
        c.SetCardSaints();
        // orig: R087+0x06FA (1838:06FA) matched; R087+0x0736 (1838:0736) matched; R087+0x0772 (1838:0772) matched; R087+0x07AE (1838:07AE) matched; tests: CardSmokeTests
        // DORMANT: no item is set and no line opens the pop-up box, so the item line hints never run.
        menu.Popup = () => ItemLineHint(c, c.MenuMember);
        // orig: R087+0x07EA (1838:07EA) matched; R087+0x0874 (1838:0874) matched; R087+0x08FE (1838:08FE) matched; R087+0x0988 (1838:0988) matched; tests: CardSmokeTests
        // DORMANT: no saint is set either (the saint pop-up handler is c.SaintPopupHint).

        if (!c.Events.QuestExists(7))
        {
            StartTheWildHuntQuest(c);
        }
        // C: the start-up cannot leave this card, so the menu loop test is always true.

        return menu.Run(
            // orig: R087+0x029A (1838:029A) matched; R087+0x02A8 (1838:02A8) matched; tests: CardSmokeTests
            new MenuLine(() => LeaveTheMap(c, hours: false), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x02E4 (1838:02E4) matched; R087+0x02FC (1838:02FC) matched; tests: CardSmokeTests
            new MenuLine(() => LeaveTheMap(c, hours: true), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0334 (1838:0334) matched; R087+0x034C (1838:034C) matched; tests: CardSmokeTests
            new MenuLine(() => LeaveTheMap(c, hours: true), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0384 (1838:0384) matched; R087+0x0428 (1838:0428) matched; tests: CardSmokeTests
            // DORMANT: line 3 ("Start up the Wild Hunt Quest?") is never offered (flag 0); the quest is started on arrival.
            new MenuLine(() =>
            {
                StartTheWildHuntQuest(c);
                LeaveTheMap(c, hours: true);
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0460 (1838:0460) matched; R087+0x0472 (1838:0472) matched; tests: CardSmokeTests
            // DORMANT: lines 4..9 are not offered (flags 0); they would go to the encounter card.
            new MenuLine(() => GoToTheEncounter(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x04AA (1838:04AA) matched; R087+0x04BC (1838:04BC) matched; tests: CardSmokeTests
            new MenuLine(() => GoToTheEncounter(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x04F4 (1838:04F4) matched; R087+0x0506 (1838:0506) matched; tests: CardSmokeTests
            new MenuLine(() => GoToTheEncounter(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x053E (1838:053E) matched; R087+0x0550 (1838:0550) matched; tests: CardSmokeTests
            new MenuLine(() => GoToTheEncounter(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0588 (1838:0588) matched; R087+0x059A (1838:059A) matched; tests: CardSmokeTests
            new MenuLine(() => GoToTheEncounter(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x05D2 (1838:05D2) matched; R087+0x05E4 (1838:05E4) matched; tests: CardSmokeTests
            new MenuLine(() => GoToTheEncounter(c), OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>Starts the Wild Hunt quest (quest 7) here, forever, with a random saint and a random item.</summary>
    private static void StartTheWildHuntQuest(CardContext c)
    {
        // C: two separate statements: the item is drawn first, then the saint.
        var item = c.Random(0x20) + 0x8B;
        var saint = c.Random(0x88);
        var slot = c.Events.Create(-2, c.Here, 0x5F, 8, 0, 0, 0, EventLog.Forever, 0, 7);
        // DIVERGENCE: with all 300 event slots in use the original writes through a stale slot; here nothing is written.
        if (c.Events[slot] is { } quest)
        {
            // C: the quest's data words hold the saint, the item and 0 (the quest started by the town cards puts 2 and the item in the last two).
            quest.Data1 = saint;
            quest.Data2 = item;
            quest.Data3 = 0;
        }
        c.Events.Create(-2, c.Here, 0x5F, 0x1C, 5, 0, 0, EventLog.Forever, 0, 7);
    }

    private static void LeaveTheMap(CardContext c, bool hours)
    {
        if (hours)
        {
            c.PassHours(0);
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    private static void GoToTheEncounter(CardContext c)
    {
        c.PassHours(0);
        c.Next = EncounterCard;
    }

    private static OptionHint ItemLineHint(CardContext c, int member)
    {
        c.Variables.Chosen[0] = member;
        return OptionHint.Say(SuccessAssured, member);
    }
}
