using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetG01": on the road a Dominican friar preaches and sells pardons. Buy one, pray to a
/// saint, or sneer and go on, which earns his curse of the wasting disease: then pray, scare
/// him with a potion, attack him, or leave while he chants.
/// </summary>
// orig: R075+0x1864 (19BE:0004) matched; tests: CardSmokeTests
// orig: R075+0x20AC (19BE:084C) matched; R075+0x214E (19BE:08EE) matched; R075+0x20A0 (19BE:0840) matched; R075+0x20A2 (19BE:0842) matched; R075+0x20A4 (19BE:0844) matched; R075+0x20A6 (19BE:0846) matched; R075+0x20A8 (19BE:0848) matched; R075+0x20AA (19BE:084A) matched; tests: CardSmokeTests
// DORMANT: lines 7..9 have empty actions and hints; lines 10..19 the "screwed up." default.
public sealed class FriarSellingPardons : Card
{
    public const int CardNumber = 0x7F;

    private const int Picture = 155;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        var purse = c.State.Party.Purse.TotalPfennigs;
        // C: money_pfennigs is a long; the pardon costs (unsigned)(pfennigs) / 60 + 3 per slot.
        if (purse <= 0)
        {
            // C: the price ($Money1) then keeps an earlier card's value; the line is hidden.
            menu[0] = OptionState.Hidden;
        }
        else if (purse <= 12)
        {
            c.Variables.Money[0] = c.State.Party.Purse;
        }
        else
        {
            c.Variables.Money[0] = Money.FromPfennigs((ushort)purse / 60 + c.PartySize * 3);
        }
        c.SetCardSaints(0x3D, 0x4E, 0x24);
        c.SetCardItems(0x65, 0x66);
        // orig: R075+0x2186 (19BE:0926) matched; R075+0x21C2 (19BE:0962) matched; R075+0x21FE (19BE:099E) matched; R075+0x223A (19BE:09DA) matched; tests: CardSmokeTests
        // orig: R075+0x2276 (19BE:0A16) matched; R075+0x22FE (19BE:0A9E) matched; R075+0x2386 (19BE:0B26) matched; R075+0x240E (19BE:0BAE) matched; tests: CardSmokeTests
        menu[1] = c.SaintsUsable();

        // DORMANT: in text block 2 the option lines 0..2 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 3 the option line 1 is never offered (an Offer for this block lists other lines); it can be offered in other blocks only.
        return menu.Run(
            new MenuLine(() => BuyAPardon(c), OptionHint.Saying(SuccessAssured)),
            // orig: R075+0x1D06 (19BE:04A6) matched; R075+0x1DD2 (19BE:0572) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                if (PrayForAid(c) == PrayerUnanswered)
                {
                    menu.Offer(3, 0, 2);
                    menu[1] = OptionState.Shown;
                }
            }, OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R075+0x1E0A (19BE:05AA) matched; R075+0x1E62 (19BE:0602) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                menu.Offer(2, 3, 4, 5, 6);
                c.SetCardSaints(0x3D, 0x4E, 0x73, 0x75);
                menu[3] = c.SaintsUsable();
                menu[4] = c.ItemsUsable();
            }, OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(() => PrayAgainstTheCurse(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(() => FrightenHim(c), OptionHint.Saying(CardTexts.Unknown), () => c.MemberPopupHint()),
            new MenuLine(() => Attack(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => LeaveCursed(c), OptionHint.Saying(CardTexts.Unknown)));
    }

    private const int PrayerUnanswered = 0;

    /// <summary>The party moves on into the wilderness.</summary>
    private static void GoOn(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    // orig: R075+0x1CA6 (19BE:0446) matched; R075+0x1CCE (19BE:046E) matched; tests: CardSmokeTests
    private static void BuyAPardon(CardContext c)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.Screen.Text(1);
        GoOn(c);
    }

    /// <summary>A saint's help before the curse; returns the prayer's answer.</summary>
    private static int PrayForAid(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return -1;
        }
        if (answer == 1)
        {
            Blessed(c, c.MenuSlot <= 1 ? 5 : 4, c.MenuSlot <= 1 ? 8 : 9);
            GoOn(c);
            return 1;
        }
        return PrayerUnanswered;
    }

    // orig: R075+0x1E9A (19BE:063A) matched; R075+0x1F44 (19BE:06E4) matched; tests: CardSmokeTests
    private static void PrayAgainstTheCurse(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            Blessed(c, c.MenuSlot <= 1 ? 5 : 6, c.MenuSlot <= 1 ? 8 : 6);
        }
        else
        {
            c.Screen.Text(0x0C);
            TheWastingCurse(c);
        }
        GoOn(c);
    }

    // DORMANT: a slot beyond 3 (impossible: four saint slots) would show nothing and still go on.
    /// <summary>The saint answers (slots 0..3 only): the party practises virtue (skill 9) and gains divine favour.</summary>
    private static void Blessed(CardContext c, int text, int favour)
    {
        if (c.MenuSlot is < 0 or > 3)
        {
            return;
        }
        c.Screen.Text(text);
        c.PracticeSkill(Who.Everybody, 9, 7, 15, 0);
        c.AddAttribute(Who.Everybody, 6, favour);
    }

    // orig: R075+0x1F7C (19BE:071C) matched; R075+0x1FD4 (19BE:0774) matched; tests: CardSmokeTests
    private static void FrightenHim(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        ShowPicture(c, 189);
        c.Screen.Text(7);
        GoOn(c);
    }

    // orig: R075+0x204A (19BE:07EA) matched; R075+0x2068 (19BE:0808) matched; tests: CardSmokeTests
    private static void LeaveCursed(CardContext c)
    {
        c.Screen.Text(8);
        TheWastingCurse(c);
        GoOn(c);
    }

    // orig: R075+0x1B14 (19BE:02B4) matched; tests: CardSmokeTests
    // C: (100 - skill) / 14 + 1 is the C's -(skill - 100) / 14 + 1 (division truncates toward 0;
    // skills are at most 99, so it is positive); the loop runs over every slot below party_size.
    /// <summary>
    /// The curse wastes each member's endurance and strength (and their maxima) and divine
    /// favour, the less the better the member's virtue (skill 9).
    /// </summary>
    private static void TheWastingCurse(CardContext c)
    {
        for (var i = 0; i < c.PartySize; i++)
        {
            var severity = (100 - c.Skill(i, 9)) / 14 + 1;
            c.AddAttribute(i, 1, -severity);
            c.AddAttribute(i, 0, -severity);
            c.AddAttribute(i, 6, -(severity * 4));
            c.AddAttributeMaximum(i, 1, -(severity / 2));
            c.AddAttributeMaximum(i, 0, -(severity / 2));
        }
    }

    // orig: R075+0x1B9E (19BE:033E) matched; R075+0x200C (19BE:07AC) matched; R075+0x2012 (19BE:07B2) matched; tests: CardSmokeTests
    private static void Attack(CardContext c)
    {
        switch (c.RunBattle(0x2E, -1, 0x0D, 1, 7, 0x20, 1, 1, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 286);
                c.Screen.Text(9);
                break;
            case 1:
            case 2:
                c.RejoinAway();
                c.PassHours(1);
                TheWastingCurse(c);
                c.Screen.Text(8);
                break;
            case 3:
                c.PassHours(1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(0x0B);
                break;
            case 4:
                c.PassHours(1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(0x0A);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results: straight on to the penance below.
                break;
        }
        // Attacking a friar weighs on everyone's conscience.
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddSkill(i, 9, -2);
                c.AddAttribute(i, 6, -30);
            }
        }
        GoOn(c);
    }
}
