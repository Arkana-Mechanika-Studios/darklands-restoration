namespace Darklands.Cards.Scripts;

/// <summary>
/// "$wqSta00": a decrepit manor by a misty lake, home of a magician-astrologer who guides the
/// quest against Satan's servants. The party can go in (her visions, card 293), pray for
/// insight, or pass by.
/// </summary>
// orig: R117+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R117+0x03AA (1838:03AA) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: text 7 (a repeated prayer) is never shown; four uncalled functions follow the pop-ups.
public sealed class AstrologersManor : Card
{
    public override int Number => 292;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x28);
        Show(c, 163);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.Variables.Chosen[0] = c.Leader;
        c.SetCardSaints(0x25, 0x35, 0x43, 0x5D);
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R117+0x024E (1838:024E) matched; hint R117+0x0290
            new MenuLine(_ => GoIn(c), () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R117+0x02C8 (1838:02C8) matched; hint R117+0x030A
            new MenuLine(_ => PrayForInsight(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R117+0x0342 (1838:0342) matched; hint R117+0x0356 (leaves without clearing cur_loc)
            new MenuLine(_ => { c.Screen.Text(1, TextFrame.BlankPage); c.Next = CardContext.MapCard; }, () => OptionHint.Say(NoRisk)),
            // DORMANT: lines 3..9 are never offered; actions R117+0x038E.. and hints R117+0x0390.. are empty
            // (UNCERTAIN: the hints return AX/DX; neutral hints are used).
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    /// <summary>The servant shows the party in (a welcome back on later visits); the visit is remembered forever.</summary>
    // orig: R117+0x024E (1838:024E) matched; tests: CardSmokeTests
    // C: event_here(0x5C, DS:270F) passes the address of DS:270F as the hours: 0x270F = 9999, forever.
    private static void GoIn(CardContext c)
    {
        c.PassHours(1);
        c.Screen.Text(c.EventAtHere(0x5C) ? 5 : 4);
        c.Next = 293;
        c.EventHere(0x5C, 9999);
    }

    /// <summary>An answered prayer shows the manor is unholy yet important (praying again is then not offered).</summary>
    // orig: R117+0x02C8 (1838:02C8) matched; tests: CardSmokeTests
    // C: this prayer does not make the praying member $ChosenOneName.
    private static void PrayForInsight(CardContext c, CardMenu menu)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            menu.Block = 2;
            menu[1] = OptionState.Hidden;
        }
        else
        {
            menu.Block = 3;
        }
        c.Next = 292;
    }
}
