using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Count01": admitted to the keep, the party is the lord's guest for the night. It can try
/// to trade, learn about the place, pray for insight into the lord, rebuke him for his rule,
/// or wait for night to search the dungeons. A cruel lord (by the party's seed and the place,
/// as at the gate) has goods to trade and throws out those who rebuke him; a just one shames them.
/// </summary>
// orig: R104+0x1FD0 (1A35:0000) matched; tests: CardSmokeTests
// orig: R104+0x278E (1A35:07BE) matched (registration; its saint pop-ups do not name the member)
public sealed class GuestOfTheCastleLord : Card
{
    private const int HallPicture = 260;

    public override int Number => 243;

    public override int Run(CardContext c)
    {
        Show(c, HallPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 7);
        c.SetCardSaints(0x29, 0x2E, 0x40, 0x25);
        menu[2] = c.SaintsUsable();
        if (c.HereQuery(0) < 0)
        {
            menu[0] = OptionState.Shown;
        }
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R104+0x221C (line 0); hint R104+0x2290 (1A35:02C0) matched
            new MenuLine(_ => Trade(c), () => ConversationHint(c)),
            // orig: R104+0x238C (1A35:03BC) matched (hint)
            new MenuLine(p => LearnAboutThePlace(c, menu, p), () => ConversationHint(c)),
            new MenuLine(_ => PrayForInsight(c), () => OptionHint.Say("Select a person and a saint.")),
            new MenuLine(_ => RebukeTheLord(c), () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R104+0x2626 (1A35:0656) matched (empty action); hint R104+0x2628 is an empty function too
            // DORMANT: line 4 is never offered (opt_flag_4 = 0). UNCERTAIN: its hint returns whatever is in AX/DX; a neutral hint is used.
            new MenuLine(_ => { }, Assured),
            // DORMANT: lines 5, 6, 8, 9 are never offered; each only lets no time pass.
            // orig: R104+0x262A (line 5)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x266E (line 6)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x26B2 (1A35:06E2) matched (leaves at dawn without clearing cur_loc)
            new MenuLine(_ => { c.PassHours(c.HoursUntil(5) + 1); c.Next = CardContext.MapCard; }, Assured),
            // orig: R104+0x2706 (line 8)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x274A (line 9)
            new MenuLine(_ => c.PassHours(0), Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>The same parity as at the castle gate (card 242) decides the lord's nature.</summary>
    // C: party_seed + cur_loc & 1 (no char cast here; the parity is the same as at the gate).
    private static bool LordIsCruel(CardContext c) => ((c.State.Party.Seed + c.Here) & 1) != 0;

    /// <summary>The leader's speech or twice the charm, whichever is better.</summary>
    private static OptionHint ConversationHint(CardContext c)
    {
        var talk = Math.Max(c.Skill(Who.Leader, (int)Skill.SpeakCommon), c.Attribute(Who.Leader, (int)Attribute.Charisma) * 2);
        return OptionHint.ChanceOf(Math.Clamp(talk, 1, 99), c.Leader);
    }

    /// <summary>Only a cruel lord's household has things to trade; the chance is not rolled.</summary>
    // orig: R104+0x221C (1A35:024C) matched; tests: CardSmokeTests
    private static void Trade(CardContext c)
    {
        if (LordIsCruel(c))
        {
            c.OpenShop(-1, "castle", 0x202D, 0);
        }
        else
        {
            c.PassHours(1);
            ShowPicture(c, 293);
            c.Screen.Text(9);
            ShowPicture(c, HallPicture);
        }
        c.Next = 243;
    }

    // orig: R104+0x22E6 (1A35:0316) matched; tests: CardSmokeTests
    private static void LearnAboutThePlace(CardContext c, CardMenu menu, int chance)
    {
        c.PassHours(2);
        if (c.Random(100) <= chance)
        {
            if (LordIsCruel(c))
            {
                ShowPicture(c, 26);
                c.Screen.Text(10);
            }
            else
            {
                ShowPicture(c, 356);
                c.Screen.Text(4);
            }
        }
        else
        {
            c.Screen.Text(15);
        }
        menu[1] = OptionState.Shown;
        ShowPicture(c, HallPicture);
        c.Next = 243;
    }

    // orig: R104+0x23E2 (1A35:0412) matched; tests: CardSmokeTests
    private static void PrayForInsight(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.PassHours(2);
        if (answer == 0)
        {
            ShowPicture(c, 16);
            c.Screen.Text(7);
        }
        else if (LordIsCruel(c))
        {
            ShowPicture(c, 26);
            c.Screen.Text(8);
        }
        else
        {
            ShowPicture(c, 290);
            c.Screen.Text(6);
        }
        ShowPicture(c, HallPicture);
        c.Next = 243;
    }

    /// <summary>
    /// A cruel lord has the party escorted to the woods (on to the night outside, card 244); a
    /// just one keeps them in their room till dawn and sends them away. Either way the leader
    /// practises Virtue, and the original gives everybody 20 Divine Favour for the false accusation.
    /// </summary>
    // orig: R104+0x2500 (1A35:0530) matched; tests: CardSmokeTests
    private static void RebukeTheLord(CardContext c)
    {
        if (LordIsCruel(c))
        {
            ShowPicture(c, 100);
            c.Screen.Text(13);
            c.PracticeSkill(c.Leader, (int)Skill.Virtue, 1, 20, 2);
            c.PassHours(c.IsDaylight ? c.HoursUntil(18) : 1);
            c.Next = 244;
            return;
        }
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 20);
        c.PracticeSkill(c.Leader, (int)Skill.Virtue, 1, 20, -1);
        ShowPicture(c, 286);
        c.Screen.Text(14);
        c.PassHours(c.HoursUntil(5));
        c.Next = CardContext.MapCard;
    }
}
