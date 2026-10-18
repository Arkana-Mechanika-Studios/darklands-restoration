using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Count05": in a corrupt monastery, a rich lover steals to $ChosenOneName's bedside by night
/// with an invitation. The member may accept, decline for virtue's sake, or pray. No card of the
/// game leads here, and the scene is unfinished in the original: accepting does nothing, a
/// prayer only prays, and the texts telling of the sabbat are never shown.
/// </summary>
// orig: R105+0x27AA (1AB2:000A) matched; tests: CardSmokeTests
// orig: R105+0x2CAC (1AB2:050C) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: no card sets next_card to 0xF7, so the whole card is unreachable in the original.
public sealed class NightVisitorInTheCell : Card
{
    public override int Number => 247;

    public override int Run(CardContext c)
    {
        // C: the original names the card "$Count05.msg" (the engine appends ".msg" itself); its picture is picture 0.
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, VisitorBlock(c));
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardSaints(0x11, 0x35, 0x44, 0x7E);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R105+0x29E2 (1AB2:0242) matched (empty: accepting does nothing and the card asks again); hint R105+0x29E4
            new MenuLine(_ => { }, () => Unknown(c)),
            // orig: R105+0x2A1C (1AB2:027C) matched; hint R105+0x2A44
            new MenuLine(_ => KeepVirtue(c), () => Unknown(c)),
            // orig: R105+0x2A7C (1AB2:02DC) matched (only prays: the answer is not used); hint R105+0x2A98
            new MenuLine(_ => c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]), () => OptionHint.Say("Select a person and a saint.", c.Variables.Chosen[0])),
            // DORMANT: lines 3..9 are never offered; each lets no time pass (R105+0x2AD0..04c8).
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured));
    }

    private static OptionHint Unknown(CardContext c) => OptionHint.Say("You can always encounter the unknown", c.Variables.Chosen[0]);

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>
    /// The original offers the option lines of an uninitialised local (card_options(l_2)); block
    /// 0 is the visit to a woman, block 1 to a man, so the block is chosen by $ChosenOneName here.
    /// </summary>
    // C: l_2 is never written (checked against [bp-2] is only read).
    // DIVERGENCE: the original's block is stack garbage; here it follows $ChosenOneName's sex.
    private static int VisitorBlock(CardContext c) => c.IsFemale(c.Variables.Chosen[0]) ? 0 : 1;

    /// <summary>Declining is a lesson in Virtue; the party goes back to the card it came from.</summary>
    // orig: R105+0x2A1C (1AB2:027C) matched; tests: CardSmokeTests
    // C: $ChosenOneName (var_chosen1) is whatever an earlier card left there; practice mode 7.
    private static void KeepVirtue(CardContext c)
    {
        c.Screen.Text(5);
        c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Virtue, 7, 20, 0);
        c.Next = c.State.SavedCard;
    }
}
