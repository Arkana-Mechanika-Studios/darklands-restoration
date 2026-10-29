using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// Card 265 ("$MeetB03"): an unfinished encounter that random travel can lead to. Its script is
/// a bandit-encounter skeleton (potion, saints, surrender, sneak) but it loads the blizzard texts
/// of "$MeetB03" (card 279) and never draws an option list. Most lines do nothing; a few leave.
/// </summary>
// orig: R110+0x0D8E (1910:000E) matched; tests: CardSmokeTests
// orig: R110+0x1282 (1910:0502) matched (registration: item pop-ups R110+0x1344/05de give 100 and name the member; saint pop-ups do not)
// C: the loop calls card_menu without card_options or card_text (card_load draws only the picture), so the menu works on the option
// C: lines the screen last laid out (the previous card's last text) with this card's states and handlers (CardMenu.DrawsOptions = false).
// DORMANT: R110+0x0FCA (text 11, then the map), R110+0x1378 and R110+0x137A are never called.
public sealed class UnfinishedBanditEncounter_MeetB03 : Card
{
    public override int Number => 265;

    public override int Run(CardContext c)
    {
        Show(c, 286);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.DrawsOptions = false;
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5, 7);
        c.SetCardItems(0x65, 0x66);
        c.SetCardSaints(0x36, 0x3D, 0x83);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R110+0x0FDE (1910:025E) matched (empty); hint R110+0x0FE0
            new MenuLine(_ => { }, () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R110+0x1018 (1910:0298) matched (empty); hint R110+0x101A
            new MenuLine(_ => { }, () => OptionHint.ChanceOf(100, c.Variables.Chosen[0])),
            // orig: R110+0x1032 (1910:02B2) matched; hint R110+0x1062
            new MenuLine(p => ThrowAPotion(c, p), () => OptionHint.Say("Of course the potion will work!"), () => FullChanceForTheMember(c)),
            // orig: R110+0x109A (1910:031A) matched; hint R110+0x1106
            new MenuLine(_ => Pray(c), () => OptionHint.Say("XXX Saint % success msg XXX.")),
            // orig: R110+0x113E (1910:03BE) matched; hint R110+0x115C
            new MenuLine(_ => { c.PassHours(1); c.Screen.Text(10); c.Next = CardContext.MapCard; }, () => OptionHint.Say(SuccessAssured)),
            // orig: R110+0x1194 (1910:0414) matched; hint R110+0x11E2
            new MenuLine(p => SneakAway(c, p), () => OptionHint.ChanceOf(100, OptionHint.WholeParty)),
            // orig: R110+0x11F8 (1910:0478) matched (empty); hint R110+0x11FA
            // DORMANT: line 6 is never offered.
            new MenuLine(_ => { }, () => OptionHint.Say("Enter Combat")),
            // orig: R110+0x1232 (1910:04B2) matched; hint R110+0x1264
            new MenuLine(p => Scout(c, p), () => OptionHint.ChanceOf(100, OptionHint.WholeParty)),
            // DORMANT: lines 8 and 9 are never offered; actions R110+0x127A/04fe and hints R110+0x127C/0500
            // C: are empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    // orig: R110+0x1344 (1910:05C4) matched (slot 1 R110+0x135E)
    private static OptionHint FullChanceForTheMember(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.ChanceOf(100, c.MenuMember);
    }

    /// <summary>The potion is not taken; from the option line (no pop-up) the chance is −1, so it fails.</summary>
    // orig: R110+0x1032 (1910:02B2) matched; tests: CardSmokeTests
    private static void ThrowAPotion(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
            c.Next = CardContext.MapCard;
            return;
        }
        c.Screen.Text(5);
    }

    // orig: R110+0x109A (1910:031A) matched; tests: CardSmokeTests
    private static void Pray(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    c.Screen.Text(7, TextFrame.BlankPage);
                    break;
                case 2:
                    c.Screen.Text(8, TextFrame.BlankPage);
                    break;
                case 3:
                    c.Screen.Text(9, TextFrame.BlankPage);
                    break;
            }
            c.Next = CardContext.MapCard;
            return;
        }
        c.Screen.Text(6);
        c.Next = 265;
    }

    // orig: R110+0x1194 (1910:0414) matched; tests: CardSmokeTests
    private static void SneakAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(12);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.Next = CardContext.MapCard;
            return;
        }
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        c.Next = 265;
    }

    // orig: R110+0x1232 (1910:04B2) matched; tests: CardSmokeTests
    private static void Scout(CardContext c, int chance)
    {
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, c.Random(100) <= chance ? 7 : 0, 10, 0);
        c.Next = 265;
    }
}
