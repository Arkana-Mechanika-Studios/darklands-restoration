namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetS03": the tidal flood again (card 283) — a third list of saints. The party can go
/// back to the first or the second list, or pray. No card ever leads here.
/// </summary>
// orig: R115+0x0654 (189D:0004) matched; tests: CardSmokeTests
// orig: R115+0x09D8 (189D:0388) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: unreachable (card 283 never offers its line here, card 284's line loops to itself). Texts 1, 3 and 4 are never shown.
// DORMANT: R115+0x0A72, R115+0x0A74, R115+0x0A76, R115+0x0A78 are never called.
public sealed class FloodSaintsThirdList : Card
{
    public override int Number => 285;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardSaints(0x12, 0x15, 0x59, 0x61);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R115+0x088C (189D:023C) matched; hint R115+0x0894
            new MenuLine(_ => c.Next = 283, () => OptionHint.Say(NoRisk)),
            // orig: R115+0x08CC (189D:027C) matched; hint R115+0x08D4
            new MenuLine(_ => c.Next = 284, () => OptionHint.Say(NoRisk)),
            // orig: R115+0x090C (189D:02BC) matched; hint R115+0x0984
            new MenuLine(_ => Pray(c), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 3..9 are never offered; actions R115+0x09BC.. and hints R115+0x09BE.. are empty
            // C: the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    // orig: R115+0x090C (189D:02BC) matched; tests: CardSmokeTests
    private static void Pray(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            if (c.MenuSlot == 0)
            {
                c.PassHours(1);
                c.Screen.Text(2, TextFrame.BlankPage);
            }
            else
            {
                c.Screen.Text(2, TextFrame.BlankPage);
                c.PassHours(2);
            }
            c.Next = CardContext.MapCard;
            return;
        }
        c.Next = 283;
    }
}
