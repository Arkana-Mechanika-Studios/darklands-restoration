namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetA02": a large army on the road. The party can make a small or a wide detour, pray
/// for safe passage, or hide for days until it has passed.
/// </summary>
// orig: R110+0x159C (1991:000C) matched; tests: CardSmokeTests
// orig: R110+0x1990 (1991:0400) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: R110+0x1A2A, R110+0x1A2C, R110+0x1A2E, R110+0x1A30 are never called; text 2 is never shown.
public sealed class ArmyOnTheMarch : Card
{
    public override int Number => 266;

    public override int Run(CardContext c)
    {
        Show(c, 286);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3);
        c.Variables.Chosen[0] = c.Leader;
        c.SetCardSaints(0x6F, 0x34, 0x82, 0x83);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R110+0x17D8 (1991:0248) matched; hint R110+0x17E0
            new MenuLine(_ => c.Next = CardContext.MapCard, () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R110+0x1818 (1991:0288) matched; hint R110+0x1820
            new MenuLine(_ => c.Next = CardContext.MapCard, () => OptionHint.Say(SuccessAssured)),
            // orig: R110+0x1858 (line 2); hint R110+0x18AA (1991:031A) matched
            new MenuLine(_ => PrayForSafePassage(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R110+0x18E2 (1991:0352) matched; hint R110+0x1900
            new MenuLine(_ => { c.Screen.Text(3); c.PassHours(0x5F); c.Next = CardContext.MapCard; }, () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R110+0x1938 (1991:03A8) matched; hint R110+0x1944
            // DORMANT: line 4 is never offered; it lets no time pass.
            new MenuLine(_ => c.PassHours(0), () => OptionHint.Say(SuccessAssured)),
            // DORMANT: lines 5..9 are never offered; actions R110+0x197C.. and hints R110+0x197E.. are
            // C: empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    /// <summary>An answered prayer lets the party go on; otherwise the original shows the hiding text (3), not the failure text (2).</summary>
    // orig: R110+0x1858 (1991:02C8) matched; tests: CardSmokeTests
    private static void PrayForSafePassage(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(1);
            c.Next = CardContext.MapCard;
            return;
        }
        c.Screen.Text(3);
        c.Next = 266;
    }
}
