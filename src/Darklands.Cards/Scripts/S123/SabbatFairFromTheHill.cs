using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd22": from a hill the party sees a sabbat gathering like a country fair, with pilgrims
/// coming up the path. They may meet a group of travellers (wait for them or ambush them), walk
/// into the "fair" alone (card 321) or leave (card 0x0C). Talked to, the four pilgrims flee,
/// take the party along (card 320) or, by a saint's help, greet them as brothers; beaten in
/// an ambush they leave their cloaks behind. The sabbat fact (tag 1, type 7) counts how much
/// the cult has been alarmed.
/// </summary>
// orig: R123+0x14AA (1982:000A) matched; tests: CardSmokeTests
//   option 0: action R123+0x1724; hint R123+0x1794
//   option 1: action R123+0x17CC; hint R123+0x17D4
//   option 2: action R123+0x180C; hint R123+0x1814
//   option 3: action R123+0x184C; hint R123+0x18A4
//   option 4: action R123+0x18DC; hint R123+0x18E4
//   option 5: action R123+0x191C; hint R123+0x19BC
//   option 6: action R123+0x19F4; hint R123+0x1A1E
//   option 7: action R123+0x1A56; hint R123+0x1A6A
//   option 8: action R123+0x1AA2; hint R123+0x1B80
//   option 9: action R123+0x1BB8; hint R123+0x1C44
//   item pop-up: R123+0x1D56, R123+0x1D6E, R123+0x1DA6,
//     R123+0x1DDE
//   saint pop-up: R123+0x1E16, R123+0x1E9E, R123+0x1F26,
//     R123+0x1FAE
//   other: R123+0x1C7C, R123+0x1D1E
// DORMANT (generated): option slots 10..19 keep the default handler R123+0x1D1E ("screwed up.") and are never shown.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SabbatFairFromTheHill : Card
{
    public override int Number => 318;

    private const int FairPicture = 144;
    private const int PathPicture = 140;
    private const int HeavenlyLightPicture = 90;

    /// <summary>Location facts (tag 1): 7 the sabbat's alarm, 0x15 the travellers were met.</summary>
    private const int FactTag = 1;
    private const int AlarmFact = 7;
    private const int TravellersMetFact = 0x15;

    public override int Run(CardContext c)
    {
        Show(c, FairPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        if (!c.Events.FactAnywhere(FactTag, AlarmFact))
        {
            c.Events.Create(0, c.Here, 0, 0x1C, AlarmFact, 0, 0, 72, 0, FactTag);
        }
        if (c.Events.FactAnywhere(FactTag, TravellersMetFact))
        {
            menu[0] = OptionState.Shown;
        }
        c.SetCardItems();
        c.SetCardSaints(5, 0x2E, 0x41, 0x81);
        // C: line 9 (the saint) is switched on in block 0 too, as in the original.
        menu[9] = c.SaintsUsable();

        var assured = OptionHint.Saying(SuccessAssured);
        // DORMANT: in text block 1 the option lines 0..2 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 2 the option lines 0..4 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => GoDownToThePath(c, menu), assured),
            // orig: R123+0x17CC / R123+0x17D4 matched — into the fair alone.
            new MenuLine(_ => c.Next = 321, assured),
            // orig: R123+0x180C / R123+0x1814 matched — leave for the map.
            new MenuLine(_ => c.Next = 0x0C, assured),
            new MenuLine(_ => WaitForTheTravellers(c, menu), assured),
            // orig: R123+0x18DC / R123+0x18E4 matched — ambush them on the path.
            new MenuLine(_ => c.Next = 319, assured),
            new MenuLine(_ => TheyFlee(c, menu), assured),
            // orig: R123+0x19F4 / R123+0x1A1E matched — they take the party along, a little alarmed.
            new MenuLine(_ => { c.Screen.Text(4); RaiseTheAlarm(c, 1); c.Next = 320; }, assured),
            // orig: R123+0x1A56 / R123+0x1A6A matched — they take the party along.
            new MenuLine(_ => { c.Screen.Text(5); c.Next = 320; }, assured),
            new MenuLine(_ => AttackTheTravellers(c, menu), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(_ => CallASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint));
    }

    // orig: the event_update(−1, −1, 0x1C, 7, n, 1, add) of R123+0x191C / _0554 / _0602 / _0718 matched
    private static void RaiseTheAlarm(CardContext c, int amount) =>
        c.Events.Update(-1, -1, 0x1C, AlarmFact, amount, FactTag, EventUpdate.Add);

    /// <summary>Down the hill ahead of a group of pilgrims (remembered for a day).</summary>
    // orig: R123+0x1724 matched
    // C: it copies the path picture into pic_buf itself (the card's own picture from then on).
    private static void GoDownToThePath(CardContext c, CardMenu menu)
    {
        menu.Offer(1, 3, 4);
        ShowPicture(c, PathPicture);
        c.Events.Create(0, c.Here, 0, 0x1C, TravellersMetFact, 0, 0, 24, 0, FactTag);
    }

    // orig: R123+0x184C matched
    // C: an int function that falls off its end without a value; ignored. It also sets pic_buf to the path picture.
    private static void WaitForTheTravellers(CardContext c, CardMenu menu)
    {
        menu.Offer(2, 5, 6, 7, 8);
        menu[9] = c.SaintsUsable();
        ShowPicture(c, PathPicture);
    }

    /// <summary>Back on the hill; the travellers can no longer be met.</summary>
    // orig: the common tail of R123+0x191C / _0602 matched (pic_buf back to the fair picture)
    private static void BackToTheHill(CardContext c, CardMenu menu)
    {
        menu.Offer(0, 1, 2);
        menu[0] = OptionState.Shown;
        ShowPicture(c, FairPicture);
    }

    // orig: R123+0x191C matched
    // C: an int function that falls off its end without a value; ignored.
    private static void TheyFlee(CardContext c, CardMenu menu)
    {
        ShowPicture(c, PathPicture);
        c.Screen.Text(3);
        RaiseTheAlarm(c, 2);
        BackToTheHill(c, menu);
    }

    /// <summary>Four pilgrims fight; won, their cloaks are taken (fact 6 for 72 hours); lost, they raise the alarm.</summary>
    // orig: R123+0x1AA2 matched
    // C: an int function that falls off its end without a value; ignored. A battle result outside 0..4 skips both
    //   cases and still goes back to the hill. No random argument.
    private static void AttackTheTravellers(CardContext c, CardMenu menu)
    {
        switch (c.RunBattle(0x46, -1, 3, 1, 4, -1, 0, 0, -1, 0, 0))
        {
            case 0:
            case 1:
                c.Screen.Text(6);
                c.PassHours(1);
                c.Events.Create(0, 0, 0x5F, 0x1C, 6, 0, 0, 72, 0, FactTag);
                break;
            case 2:
            case 3:
            case 4:
                c.RejoinAway();
                c.Screen.Text(7);
                RaiseTheAlarm(c, 3);
                break;
        }
        BackToTheHill(c, menu);
    }

    // orig: R123+0x1BB8 matched
    // C: an int function that falls off its end without a value (except `return 0` after a cancelled prayer); ignored.
    private static void CallASaint(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            ShowPicture(c, HeavenlyLightPicture);
            c.Screen.Text(8);
            c.Next = 320;
        }
        else
        {
            c.Screen.Text(9);
            RaiseTheAlarm(c, 2);
        }
        menu[9] = OptionState.Shown;
    }

    /// <summary>
    /// The card's item pop-up handlers. Unlike the usual ones they do not set $ChosenOneName: slot 0 leaves the
    /// message buffer as it was, slots 1..3 say "Success is assured."; all point at the member in the box.
    /// </summary>
    // orig: R123+0x1D56 / _08ce / _0906 / _093e matched
    // DORMANT: card_items stays all −1, so no line opens an item pop-up; kept unwired.
    // C: slot 0 shows whatever msg_buf still holds; "Success is assured." stands in for it here.
    private static OptionHint ItemPopup(CardContext c, int slot) =>
        OptionHint.Say(SuccessAssured, c.MenuMember);
}
