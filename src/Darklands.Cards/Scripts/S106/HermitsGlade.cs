namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf00": a quiet glade with a spring and a hermit's lean-to. The party can watch the
/// clearing, search it, or pray for insight. If an item quest (tag 10) leads here, the item
/// lies hidden in the lean-to.
/// </summary>
// orig: R106+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R106+0x051A (1838:051A) matched (registration: saint pop-ups only, they do not name the member)
public sealed class HermitsGlade : Card
{
    private const int GladePicture = 100;

    public override int Number => 248;

    public override int Run(CardContext c)
    {
        Show(c, GladePicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 9);
        if (c.Events.QuestAt(10, c.Here))
        {
            // The original looks the quest up but then names the item of the last event
            // created or visited, not of the event it found.
            // C: event_find does not set last_event; event_f2e_of(last_event) reads the stale global.
            _ = c.Events.Find(8, -1, c.Here, -1, 10, -1);
            c.NameToSlot(0, c.EventDetail(c.LastEvent), 0);
        }
        c.SetCardSaints(0x11, 0x35, 0x44, 0x45);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R106+0x0276 (1838:0276) matched; hint R106+0x0294
            new MenuLine(_ => { c.PassHours(3); c.Screen.Text(1); c.Next = 248; }, Unknown),
            // orig: R106+0x02CC (line 1); hint R106+0x035E (1838:035E) matched
            new MenuLine(_ => SearchTheLeanTo(c, menu), Unknown),
            // orig: R106+0x0396 (line 2); hint R106+0x0432 (1838:0432) matched
            new MenuLine(_ => PrayForGuidance(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R106+0x046A (1838:046A) matched; hint R106+0x048A
            // DORMANT: line 3 (rest eight hours, +1 Divine Favour, leave) is never offered (opt_flag_3 = 0).
            new MenuLine(_ => { c.PassHours(8); c.AddAttribute(Core.Characters.Who.Everybody, (int)Core.Characters.Attribute.DivineFavor, 1); c.Next = CardContext.MapCard; }, Assured),
            // DORMANT: lines 4..8 are never offered; their actions and hints (R106+0x04C6..04d8) are empty functions.
            // C: those hints return whatever is in AX/DX; a neutral hint is used.
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            new MenuLine(_ => { }, Assured),
            // orig: R106+0x04DA (1838:04DA) matched; hint R106+0x04E2 (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard, () => OptionHint.Say(NoRisk)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>The quest's item is found under the leaves (the quest is done); otherwise nothing.</summary>
    // orig: R106+0x02CC (1838:02CC) matched; tests: CardSmokeTests
    private static void SearchTheLeanTo(CardContext c, CardMenu menu)
    {
        menu[1] = OptionState.Shown;
        c.PassHours(1);
        if (c.Events.QuestAt(10, c.Here))
        {
            c.FinishQuest10(c.Here);
            ShowPicture(c, 331);
            c.Screen.Text(5);
            ShowPicture(c, GladePicture);
        }
        else
        {
            c.PassHours(1);
            c.Screen.Text(4);
        }
        c.Next = 248;
    }

    // orig: R106+0x0396 (1838:0396) matched; tests: CardSmokeTests
    private static void PrayForGuidance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 1)
        {
            c.Screen.Text(2);
        }
        else if (c.Events.QuestAt(10, c.Here))
        {
            c.Screen.Text(6);
        }
        else
        {
            ShowPicture(c, 155);
            c.Screen.Text(3);
            ShowPicture(c, GladePicture);
        }
        c.Next = 248;
    }
}
