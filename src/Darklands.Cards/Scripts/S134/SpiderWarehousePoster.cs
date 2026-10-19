using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Time;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$DPost00": a poster offers a few florins for a "trivial task". The odd man behind it pays in
/// advance to have his warehouse cleared of spiders, which turn out to be wolf-sized. The party
/// may clean it out by day or at once (sometimes the giant spiders attack), or keep the money
/// and leave (Virtue and standing suffer). The streets follow (card 9 by day, 10 at night).
/// </summary>
// orig: R134+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R134+0x0256; hint R134+0x025E
//   option 1: action R134+0x0296; hint R134+0x02E8
//   option 2: action R134+0x0320; hint R134+0x034A
//   option 3: action R134+0x038A; hint R134+0x039E
//   option 4: action R134+0x03D6; hint R134+0x0400
//   option 5: action R134+0x0438; hint R134+0x048E
//   option 6: action R134+0x04C6; hint R134+0x0518
//   option 7: action R134+0x0550; hint R134+0x065A
//   option 8: action R134+0x0692; hint R134+0x0788
//   option 9: action R134+0x07C0; hint R134+0x07F2
//   item pop-up: R134+0x0904, R134+0x0940, R134+0x097C,
//     R134+0x09B8
//   saint pop-up: R134+0x09F4, R134+0x0A7E, R134+0x0B08,
//     R134+0x0B92
//   other: R134+0x082A, R134+0x08CC
// DORMANT (generated): option slots 10..19 keep the default handler R134+0x08CC ("screwed up.") and are never shown.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
// C: clamp_range(lo, hi, v) is Math.Clamp(v, lo, hi) here; every call has lo <= hi.
public sealed class SpiderWarehousePoster : Card
{
    public override int Number => 360;

    private const int Picture = 0;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.Variables.Numbers[0] = c.Random(5) + 2;
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0x00, 0x59, 0x61, 0x80);

        var assured = OptionHint.Saying(SuccessAssured);
        // DORMANT: card_items / card_saints are filled but items_usable / saints_usable are never asked and no line of
        //   the card's texts is a saint or item line, so the pop-up handlers (R134+0x0904 .. _09b8: $ChosenOneName
        //   = the member in the box, "Success is assured."; saints R134+0x09F4 .. _0b92) never run. Should a box
        //   open, the loop calls item_fn by the slot for every line, as the popup below does.
        var popup = () => c.MemberPopupHint(SuccessAssured);
        // C: cur_loc is the town the poster hangs in; loc_reputation_adj(cur_loc, …) is called without a check.
        return menu.Run(
            // orig: R134+0x0256 / R134+0x025E matched
            new MenuLine(_ => c.Next = c.State.SavedCard, assured, popup),
            // orig: R134+0x0296 / R134+0x02E8 matched
            // C: declared int, falls off its end without a value (ignored).
            new MenuLine(_ =>
            {
                c.Screen.Text(c.IsDaylight ? 1 : 2);
                c.Screen.Text(5);
                menu.Offer(6, 3, 4);
            }, assured, popup),
            // orig: R134+0x0320 / R134+0x034A matched
            new MenuLine(chance => AskTheLocals(c, menu, chance), () => LocalsChance(c), popup),
            // orig: R134+0x038A / R134+0x039E matched
            new MenuLine(_ => ToTheStreets(c), assured, popup),
            // orig: R134+0x03D6 / R134+0x0400 matched
            // C: declared int, falls off its end without a value (ignored).
            new MenuLine(_ => menu.Offer(7, 5, 6), assured, popup),
            // orig: R134+0x0438 / R134+0x048E matched — take the job (block 8: lines 7 by night only, 8, 9).
            new MenuLine(_ =>
            {
                TakeTheFee(c);
                menu.Offer(8, 8, 9);
                menu[7] = c.IsDaylight ? OptionState.Hidden : OptionState.Enabled;
            }, assured, popup),
            // orig: R134+0x04C6 / R134+0x0518 matched — keep the money and leave.
            new MenuLine(_ =>
            {
                TakeTheFee(c);
                c.AdjustReputation(c.Here, -20, -40);
                c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
                ToTheStreets(c);
            }, assured, popup),
            // orig: R134+0x0550 / R134+0x065A matched — wait for daylight (until 5 o'clock, then 2 hours).
            new MenuLine(_ =>
            {
                c.PassHours(TimeOfDay.HoursUntil(c.State.Date.Hour, 5) + 2);
                EnterTheWarehouse(c, menu, c.Random(4) != 0, 3);
            }, assured, popup),
            // orig: R134+0x0692 / R134+0x0788 matched — go in now (giant spiders one time in two).
            new MenuLine(_ => EnterTheWarehouse(c, menu, c.Random(2) != 0, 5), assured, popup),
            // orig: R134+0x07C0 / R134+0x07F2 matched
            new MenuLine(_ =>
            {
                c.AdjustReputation(c.Here, -2, -4);
                c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
                ToTheStreets(c);
            }, assured, popup));
    }

    /// <summary>The leader's Speak Common, doubled, 10 less by day, 1..99.</summary>
    // orig: R134+0x034A matched
    private static OptionHint LocalsChance(CardContext c)
    {
        var chance = c.Skill(Who.Leader, (int)Skill.SpeakCommon) * 2;
        if (c.IsDaylight)
        {
            chance -= 10;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Leader);
    }

    // orig: R134+0x0320 matched
    private static void AskTheLocals(CardContext c, CardMenu menu, int chance)
    {
        menu[2] = OptionState.Shown;
        c.Screen.Text(c.Random(100) <= chance ? 3 : 4);
    }

    /// <summary>The job is taken (a lasting mark here) and the fee paid in advance.</summary>
    // orig: the start repeated inline in R134+0x0438 / _04c6 matched
    private static void TakeTheFee(CardContext c)
    {
        c.EventHere(0x65, EventLog.Forever);
        c.State.Party.Purse = c.State.Party.Purse.Add(c.Variables.Numbers[0], 0, 0);
    }

    /// <summary>Ordinary spiders (a few bites) or the giant ones attack.</summary>
    // orig: the body shared by R134+0x0550 (1838:0550) / R134+0x0692 (1838:0692) matched (they differ in the first roll and the enemy level)
    // C: the "which spiders" roll is drawn first (by the caller), then the Endurance roll or, for the fight, the
    //   count rand_mod(5) + 1 after text 10. A lost battle (3, 4) moves to block 13 and the card stays.
    private static void EnterTheWarehouse(CardContext c, CardMenu menu, bool ordinarySpiders, int quality)
    {
        if (ordinarySpiders)
        {
            // BUG: the original adds rand(10) Endurance although text 9 says many were bitten; kept as it is (no
            //   OriginalBehaviour flag for it).
            c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, c.Random(10));
            c.Screen.Text(9);
            ToTheStreets(c);
            return;
        }
        c.Screen.Text(10);
        var count = c.Random(5) + 1;
        switch (c.RunBattle(0x13, -2, 0x39, quality, count, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.AdjustReputation(c.Here, 4, 4);
                c.Screen.Text(11);
                ToTheStreets(c);
                break;
            case 1:
            case 2:
                c.DropAway();
                c.Screen.Text(12);
                c.PassHours(TimeOfDay.HoursUntil(c.State.Date.Hour, 5));
                c.AdjustReputation(c.Here, -2, -2);
                ToTheStreets(c);
                break;
            case 3:
            case 4:
                c.AdjustReputation(c.Here, -4, -4);
                menu.Block = 13;
                break;
        }
    }

    // orig: the "is_daylight() ? 9 : 0xa" ending repeated in R134+0x038A .. _07c0 matched
    private static void ToTheStreets(CardContext c) => c.Next = c.IsDaylight ? 9 : 10;
}
