namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetS02": the tidal flood again (card 283) — a second list of saints to pray to. The
/// party can go back to the first list or pray; the line for "yet additional saints" leads
/// back to this same card. Card 283 never offers its way here.
/// </summary>
// orig: R115+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R115+0x0392 (1838:0392) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: unreachable — card 283's lines to cards 284/285 are never offered. Texts 1 and 3 are never shown.
// DORMANT: R115+0x042C, R115+0x042E, R115+0x0430, R115+0x0432 are never called.
public sealed class FloodSaintsSecondList : Card
{
    public override int Number => 284;

    public override int Run(CardContext c)
    {
        Show(c, 253);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardSaints(0x70, 0x3C, 0x3F, 0x50);
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R115+0x0238 (1838:0238) matched; hint R115+0x0240
            new MenuLine(_ => c.Next = 283, () => OptionHint.Say(NoRisk)),
            // orig: R115+0x0278 (1838:0278) matched; hint R115+0x02FE
            new MenuLine(_ => Pray(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R115+0x0336 (1838:0336) matched; hint R115+0x033E
            // C: next_card = 0x11C, this card itself (not 0x11D, the third list): the line does nothing.
            new MenuLine(_ => c.Next = 284, () => OptionHint.Say(NoRisk)),
            // DORMANT: lines 3..9 are never offered; actions R115+0x0376.. and hints R115+0x0378.. are empty
            // C: the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    /// <summary>Answered, the water stops rising (the last two saints after a day's wading); otherwise back to card 283.</summary>
    // orig: R115+0x0278 (1838:0278) matched; tests: CardSmokeTests
    // C: invoke_saint(var_chosen1 = menu_member, ...): the member is named before the prayer. Both answers show text 2.
    private static void Pray(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
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
                    c.PassHours(1);
                    c.Screen.Text(2, TextFrame.BlankPage);
                    c.Next = CardContext.MapCard;
                    break;
                case 2:
                case 3:
                    c.Screen.Text(2, TextFrame.BlankPage);
                    c.PassHours(c.Random(4) + 23);
                    c.Next = CardContext.MapCard;
                    break;
            }
            return;
        }
        c.Next = 283;
    }
}
