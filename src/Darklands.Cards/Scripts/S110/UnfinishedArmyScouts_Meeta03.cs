using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// Card 267 ("$Meeta03"): soldiers — scouts of an army — watch the road from the brush. Like
/// card 265 the script is an unfinished skeleton: it never draws an option list, most lines
/// do nothing, and every chance is 100.
/// </summary>
// orig: R110+0x1C52 (19FD:0002) matched; tests: CardSmokeTests
// orig: R110+0x21AC (19FD:055C) matched (registration: item pop-ups R110+0x226E/0638 give 100 and name the member; saint pop-ups do not)
// C: the loop calls card_menu without card_options or card_text (card_load draws only the picture), so the menu works on the option
// C: lines the screen last laid out (the previous card's last text) with this card's states and handlers (CardMenu.DrawsOptions = false).
// DORMANT: R110+0x1E90 (text 11, then the map; hidden in db bytes),
// R110+0x1E8E, R110+0x22A2 and R110+0x22A4 are never called.
public sealed class UnfinishedArmyScouts_Meeta03 : Card
{
    public override int Number => 267;

    public override int Run(CardContext c)
    {
        Show(c, 286);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.DrawsOptions = false;
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5, 6, 7);
        c.SetCardItems(0x65, 0x66);
        c.SetCardSaints(0x36, 0x3D, 0x83);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R110+0x1EA4 (19FD:0254) matched (empty); hint R110+0x1EA6
            new MenuLine(_ => { }, () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R110+0x1EDE (19FD:028E) matched (empty); hint R110+0x1EE0
            new MenuLine(_ => { }, () => OptionHint.ChanceOf(100, c.Variables.Chosen[0])),
            // orig: R110+0x1EF8 (19FD:02A8) matched; hint R110+0x1F28
            new MenuLine(p => ThrowAPotion(c, p), () => OptionHint.Say("Of course the potion will work!"), () => FullChanceForTheMember(c)),
            // orig: R110+0x1F60 (19FD:0310) matched; hint R110+0x1FCC
            new MenuLine(_ => Pray(c), () => OptionHint.Say("XXX Saint % success msg XXX.")),
            // orig: R110+0x2004 (19FD:03B4) matched; hint R110+0x2022
            new MenuLine(_ => { c.PassHours(1); c.Screen.Text(10); c.Next = CardContext.MapCard; }, () => OptionHint.Say(SuccessAssured)),
            // orig: R110+0x205A (19FD:040A) matched; hint R110+0x20A8
            new MenuLine(p => SneakPast(c, p), () => OptionHint.ChanceOf(100, OptionHint.WholeParty)),
            // orig: R110+0x20BE (19FD:046E) matched (empty: no battle); hint R110+0x20C0
            new MenuLine(_ => { }, () => OptionHint.Say("Enter Combat")),
            // orig: R110+0x20F8 (19FD:04A8) matched; hint R110+0x212A
            new MenuLine(p => { c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, c.Random(100) <= p ? 7 : 0, 10, 0); c.Next = 267; },
                () => OptionHint.ChanceOf(100, OptionHint.WholeParty)),
            // orig: R110+0x2140 (19FD:04F0) matched; hint R110+0x2192
            // DORMANT: line 8 (run and hide) is never offered (opt_flag_8 = 0).
            new MenuLine(p => RunAndHide(c, p), () => OptionHint.ChanceOf(100, OptionHint.WholeParty)),
            // orig: R110+0x21A8 (19FD:0558) matched (empty); hint R110+0x21AA (empty)
            // DORMANT: line 9 is never offered. UNCERTAIN: its hint returns AX/DX; a neutral hint is used.
            new MenuLine(_ => { }));
    }

    // orig: R110+0x226E (19FD:061E) matched (slot 1 R110+0x2288)
    private static OptionHint FullChanceForTheMember(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.ChanceOf(100, c.MenuMember);
    }

    /// <summary>The potion is not taken; from the option line (no pop-up) the chance is −1, so it fails.</summary>
    // orig: R110+0x1EF8 (19FD:02A8) matched; tests: CardSmokeTests
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

    // orig: R110+0x1F60 (19FD:0310) matched; tests: CardSmokeTests
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
        c.Next = 267;
    }

    // orig: R110+0x205A (19FD:040A) matched; tests: CardSmokeTests
    private static void SneakPast(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(12);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.Next = CardContext.MapCard;
            return;
        }
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        c.Next = 267;
    }

    // orig: R110+0x2140 (19FD:04F0) matched; tests: CardSmokeTests
    private static void RunAndHide(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(12);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.Next = CardContext.MapCard;
            return;
        }
        c.Screen.Text(16);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
    }
}
