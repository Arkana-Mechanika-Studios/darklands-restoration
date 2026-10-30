namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf11": the party has tracked the kidnappers to their camp at night. Sneak up and free
/// the captive, pray to a saint, use potions as a distraction, have one member draw them
/// off, or give up. Like $McGuf10 the scene is unfinished in the original: sneaking up does
/// nothing, the distractions only tell a text, and the follow-up lines are never offered.
/// </summary>
// orig: R075+0x1056 (193D:0006) matched; tests: CardSmokeTests
// orig: R075+0x150C (193D:04BC) matched; R075+0x15A2 (193D:0552) matched; R075+0x1862 (193D:0812) asm; tests: CardSmokeTests
// DORMANT: lines 7..9 have empty actions and hints (below); lines 10..19 the default.
// orig: R075+0x1500 (193D:04B0) matched; R075+0x1502 (193D:04B2) matched; R075+0x1504 (193D:04B4) matched; R075+0x1506 (193D:04B6) matched; R075+0x1508 (193D:04B8) matched; R075+0x150A (193D:04BA) matched; tests: CardSmokeTests
public sealed class KidnappersCampAtNight : Card
{
    public const int CardNumber = 0x7E;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        c.SetCardItems(0x5F, 0x65, 0x61, 0x62);
        c.SetCardSaints(0x23, 0x6C, 0x62);
        // orig: R075+0x15DA (193D:058A) matched; R075+0x1616 (193D:05C6) matched; R075+0x1652 (193D:0602) matched; R075+0x168E (193D:063E) matched; tests: CardSmokeTests
        // (the item pop-up hint is c.MemberPopupHint on the potion line)
        // orig: R075+0x16CA (193D:067A) matched; R075+0x1752 (193D:0702) matched; R075+0x17DA (193D:078A) matched; tests: CardSmokeTests
        // C: slot 3 has no handler of its own (a stale one; this card has no fourth saint).
        // (the saint pop-up hint is c.SaintPopupChanceHint on the saint line)
        var menu = Menu(c);
        menu.Enable(0, 3, 4);
        menu[2] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();

        // C: card-option-lines(local-minus-2) reads an uninitialised local.
        // UNCERTAIN: the first option list (block 0) is assumed.
        return menu.Run(
            // orig: R075+0x12E2 (193D:0292) matched; R075+0x12E4 (193D:0294) matched; tests: CardSmokeTests
            // DORMANT: sneaking up does nothing (the menu returns).
            new MenuLine(() => { }, () => OptionHint.ChanceOf(100, OptionHint.WholeParty)),
            new MenuLine(() => ImploreASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R075+0x1396 (193D:0346) matched; R075+0x13A4 (193D:0354) matched; tests: CardSmokeTests
            new MenuLine(() => c.Screen.Text(5), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint()),
            // orig: R075+0x13DC (193D:038C) matched; R075+0x13EA (193D:039A) matched; tests: CardSmokeTests
            // C: the hint leaves its member unset; the whole party is shown.
            new MenuLine(() => c.Screen.Text(6), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R075+0x141C (193D:03CC) matched; R075+0x1424 (193D:03D4) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x0C, OptionHint.Saying(NoRisk)),
            // orig: R075+0x145C (193D:040C) matched; R075+0x146A (193D:041A) matched; tests: CardSmokeTests
            // DORMANT: lines 5 and 6 (block 1's follow-ups) are never enabled.
            new MenuLine(() => c.Screen.Text(7), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => StealAway(c), OptionHint.Saying(SuccessAssured)));
    }

    // orig: R075+0x12FA (193D:02AA) matched; R075+0x135E (193D:030E) matched; tests: CardSmokeTests
    private static void ImploreASaint(CardContext c)
    {
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(3);
            c.PracticeSkill(Core.Characters.Who.Everybody, 9, 7, 10, -1);
            c.Next = 0x0C;
            return;
        }
        c.Screen.Text(4);
        c.Next = CardNumber;
    }

    // orig: R075+0x14A2 (193D:0452) matched; R075+0x14C8 (193D:0478) matched; tests: CardSmokeTests
    private static void StealAway(CardContext c)
    {
        c.Screen.Text(0x0B);
        c.PracticeSkill(Core.Characters.Who.Everybody, 9, 7, 10, -1);
        c.Next = 0x0C;
    }

    // orig: R075+0x12B2 (193D:0262) matched; tests: CardSmokeTests
    // DORMANT: never called by the original (no line leads here): an hour, text 8, practice, to the map.
    internal static void LeaveTheCamp(CardContext c)
    {
        c.PassHours(1);
        c.Screen.Text(8);
        c.PracticeSkill(Core.Characters.Who.Everybody, 9, 7, 10, -1);
        c.Next = 0x0C;
    }
}
