namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Storm00": a storm at sea. The party can hang on and hope (the storm blows the ship off
/// course for one to three days) or pray. The shipwreck the texts describe is never written:
/// a bad roll does nothing and the storm goes on.
/// </summary>
// orig: R113+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R113+0x039C (1838:039C) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: texts 2, 4 and 8 (the wreck) are never shown.
public sealed class StormAtSea : Card
{
    public override int Number => 276;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        // C: the prayer line is enabled without saints_usable(), so it is offered even when nobody knows a saint.
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1);
        c.SetCardSaints(0x2D, 0x63, 0x33, 0x12);
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R113+0x0232 (line 0); hint R113+0x029C (1838:029C) matched
            new MenuLine(_ => HangOnAndHope(c), () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R113+0x02D4 (line 1); hint R113+0x0344 (1838:0344) matched
            new MenuLine(_ => PrayForAid(c), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 2..9 are never offered; actions R113+0x037C.. and hints R113+0x037E.. are
            // C: empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    /// <summary>The storm drives the ship off course; then the voyage goes on (the card the party came from).</summary>
    // orig: R113+0x0232 (1838:0232) matched; tests: CardSmokeTests
    private static void HangOnAndHope(CardContext c)
    {
        var roll = c.Random(100);
        if (roll <= 30)
        {
            // orig: R113+0x0230 (1838:0230) matched (empty: the wreck)
            // DORMANT: the shipwreck is an empty function; the card simply asks again.
            return;
        }
        if (roll <= 45)
        {
            c.PassHours(c.Random(7) + 68);
        }
        else if (roll <= 70)
        {
            c.PassHours(c.Random(5) + 45);
        }
        else
        {
            c.PassHours(c.Random(3) + 22);
        }
        c.Screen.Text(1, TextFrame.BlankPage);
        c.Next = c.State.SavedCard;
    }

    /// <summary>Two saints calm the storm; the other two save the party in the water (but the ship sails on here); otherwise no answer.</summary>
    // orig: R113+0x02D4 (1838:02D4) matched; tests: CardSmokeTests
    // C: invoke_saint(var_chosen1 = menu_member, ...): the member is named before the prayer.
    private static void PrayForAid(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
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
                    c.Screen.Text(3, TextFrame.BlankPage);
                    c.Next = c.State.SavedCard;
                    break;
                case 2:
                case 3:
                    // The wreck-survival texts are shown, but the card does not move on.
                    c.Screen.Text(6, TextFrame.BlankPage);
                    c.Screen.Text(7);
                    break;
            }
            return;
        }
        c.Screen.Text(5, TextFrame.BlankPage);
    }
}
