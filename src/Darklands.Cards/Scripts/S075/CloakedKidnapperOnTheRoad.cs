namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf10": a cloaked alchemist and her mercenaries block the road and demand one of the
/// party. Surrender the member, pray to a saint, or refuse and draw weapons. The scene is
/// unfinished in the original: surrendering changes nothing, and refusing ends with a text
/// and no battle. The follow-up lines of text 1 (give up the captive, or track the kidnappers
/// to $McGuf11) are never offered.
/// </summary>
// orig: R075+0x0A28 (18DA:0008) matched; tests: CardSmokeTests
// orig: R075+0x0E1A (18DA:03FA) matched; R075+0x0E7C (18DA:045C) matched; R075+0x0EB4 (18DA:0494) asm; R075+0x0EB6 (18DA:0496) asm; R075+0x0EB8 (18DA:0498) asm; R075+0x0EBA (18DA:049A) asm; tests: CardSmokeTests
// DORMANT: lines 5..9 have empty actions and empty (value-less) hint functions
// (below), and lines 10..19 the "screwed up." default: none is offered.
// orig: R075+0x0E06 (18DA:03E6) matched; R075+0x0E08 (18DA:03E8) matched; R075+0x0E0A (18DA:03EA) matched; R075+0x0E0C (18DA:03EC) matched; R075+0x0E0E (18DA:03EE) matched; R075+0x0E10 (18DA:03F0) matched; R075+0x0E12 (18DA:03F2) matched; R075+0x0E14 (18DA:03F4) matched; R075+0x0E16 (18DA:03F6) matched; R075+0x0E18 (18DA:03F8) matched; tests: CardSmokeTests
public sealed class CloakedKidnapperOnTheRoad : Card
{
    public const int CardNumber = 0x7D;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        c.SetCardSaints(0x36, 0x3D);
        var menu = Menu(c);
        menu.Enable(0, 2);
        menu[1] = c.SaintsUsable();
        // C: the loop calls card_menu without card_options or card_text (card_load draws only the picture), so the menu works
        // C: on the option lines the screen last laid out (the previous card's last text) with this card's states and handlers.
        menu.DrawsOptions = false;

        return menu.Run(
            // orig: R075+0x0C76 (18DA:0256) matched; R075+0x0C7E (18DA:025E) matched; tests: CardSmokeTests
            // DORMANT: surrendering only stays on the card (the original never shows text 1).
            new MenuLine(() => c.Next = CardNumber, OptionHint.Saying(SuccessAssured)),
            // orig: R075+0x0CB6 (line 1); saint pop-ups R075+0x0EBC (18DA:049C) matched; R075+0x0F44 (18DA:0524) matched;
            // R075+0x0FCC (18DA:05AC) matched; R075+0x1054 (18DA:0634) matched; tests: CardSmokeTests
            // C: the slot-3 pop-up handler is an empty function (its result is garbage); an empty hint here.
            new MenuLine(() => PrayForAid(c), OptionHint.Saying(CardTexts.SelectSaint), () => c.MenuSlot < 3
                ? OptionHint.Say(c.ChanceText(c.SaintChanceLow(c.MenuMember, c.CardSaints[c.MenuSlot]), c.SaintChanceHigh(c.MenuMember, c.CardSaints[c.MenuSlot])), c.MenuMember)
                : OptionHint.Say("", c.MenuMember)),
            new MenuLine(() => Refuse(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R075+0x0D88 (18DA:0368) matched; R075+0x0D90 (18DA:0370) matched; tests: CardSmokeTests
            // DORMANT: lines 3 and 4 (text 1's follow-ups) are never enabled.
            new MenuLine(() => c.Next = 0x0C, OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => TrackTheKidnappers(c), () => OptionHint.ChanceOf(100, OptionHint.WholeParty)));
    }

    // orig: R075+0x0CB6 (18DA:0296) matched; R075+0x0D12 (18DA:02F2) matched; tests: CardSmokeTests
    private static void PrayForAid(CardContext c)
    {
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(2);
            c.PassHours(1);
            c.Next = 0x0C;
            return;
        }
        c.Screen.Text(3);
        c.Next = CardNumber;
    }

    // orig: R075+0x0D4A (18DA:032A) matched; R075+0x0C46 (18DA:0226) matched; R075+0x0D50 (18DA:0330) matched; tests: CardSmokeTests
    // DORMANT: no battle: refusing only tells text 4.
    private static void Refuse(CardContext c)
    {
        c.PassHours(1);
        c.Screen.Text(4);
        c.PracticeSkill(Core.Characters.Who.Everybody, 9, 7, 10, -1);
        c.Next = 0x0C;
    }

    // orig: R075+0x0DC8 (18DA:03A8) matched; R075+0x0DF4 (18DA:03D4) matched; tests: CardSmokeTests
    // C: the hint's chance is 100 and its member is left unset; the roll always succeeds (text 8 dormant).
    private static void TrackTheKidnappers(CardContext c)
    {
        if (c.Random(100) <= 100)
        {
            c.Next = 0x7E;
            return;
        }
        c.Screen.Text(8);
        c.Next = 0x0C;
    }
}
