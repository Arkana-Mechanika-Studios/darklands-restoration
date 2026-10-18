using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Initi00", card 0: the developers' test card ("Initial Card for Testing Only"). The party is
/// given 200 florins and an unknown standing here; the lines enter the city in peace, preparing
/// for war or at war, go to the map, camp outside or stay at the inn, or set the local standing
/// to −99, 0 or +99. Nothing in play leads here: the main loop runs it only when a screen returns
/// card 0.
/// </summary>
// orig: R044+0x0000 (1838:0000) matched; R136+0xD090[0]; tests: InitialTestCardTests, CardSmokeTests
// orig: the reset R044+0x05AC (1838:05AC) matched: opt_on[0..19] = 1, opt_fn[0..19] = the "screwed up." handler
//   R044+0x064E, item_fn[k][0..3] = R044+0x0686/06c2/06fe/073a ("Success is assured.", var_chosen1 = r.b = menu_member) and
//   saint_fn[k][0..3] = R044+0x0776/0800/088a/0914 (chance_text(saint_chance_lo, saint_chance_hi) of menu_member's saint in slot menu_slot),
//   then card_items and card_saints cleared to -1.
// DORMANT: the pop-up handlers: the card's text has no item or saint line, so no box opens; lines 10..19 (the "screwed up." handler) and
//   line 9 (R044+0x055E: advance_hours(0), next_card = 0x62, the end of the game; hint R044+0x0570) have no text line to choose.
// DIVERGENCE: DS:EE70 = 0xF and DS:EE72 = 7 (the pop-up boxes' drawing flags) are not kept; no box opens on this card.
public sealed class InitialTestCard : Card
{
    public const int CardNumber = 0x00;

    private const int Picture = 2;   // pic_names+0x0A4F
    private const int CityEntry = 0x04, CityPreparingForWar = 0x73, OverlandMap = 0x0C, EndOfGame = 0x62;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        var state = c.State;
        Show(c, Picture);
        // C: next_card = 0 (the card's own number: the menu runs while it stays 0), prev_screen = 0.
        c.Next = Number;
        state.PreviousCard = 0;
        // C: loc_set(cur_loc, 0, 0) first, then money_set(party_money, 0xC8, 0, 0), var_money1 = party_money,
        //   cur_rep = loc_query(cur_loc, 0), var_number1 = cur_rep, var_number2 = 0.
        c.LocationSet(c.Here, 0, 0);
        state.Party.Purse = Money.From(0xC8, 0, 0);
        c.Variables.Money[0] = state.Party.Purse;
        c.StandingSeen = c.LocationQuery(c.Here, 0);
        c.Variables.Numbers[0] = c.StandingSeen;
        c.Variables.Numbers[1] = 0;
        // C: random_name(*(int far *)(city_rec + 0x56) + 0xC, 0, 0): a man's name with a surname into $Named1, from the city's seed word.
        c.RandomName(c.City.Word(0x56) + 0x0C, 0, 0);
        // C: card_items 0x5F, 0x60, 0x65, 0x61 and card_saints 0, 0x59, 0x61, 0x80 after the reset.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        // C: opt_flag_0, 1, 3, 5..9 = 1, opt_flag_2 = 2 (shown, not chosen), opt_flag_4 = 0; the text is block 0.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 3, 5, 6, 7, 8, 9);
        menu[2] = OptionState.Shown;
        return menu.Run(
            // orig: R044+0x02A6 (action: advance_hours(0), next_card = 4), hint R044+0x02B8 (r.a = leader, then -1: says only)
            new MenuLine(() => { c.PassHours(0); c.Next = CityEntry; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x02F4 (advance_hours(0), next_card = 0x73), hint R044+0x0306
            new MenuLine(() => { c.PassHours(0); c.Next = CityPreparingForWar; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x033E (advance_hours(0), next_card = 0: the card again), hint R044+0x0350
            new MenuLine(() => { c.PassHours(0); c.Next = Number; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x0388 (advance_hours(0), next_card = 0xC), hint R044+0x039A
            new MenuLine(() => { c.PassHours(0); c.Next = OverlandMap; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x03D2 (camp, R029+0x222A(1); next_card = 0; advance_hours(0)), hint R044+0x03EC
            // DORMANT: opt_flag_4 = 0: "Camp outside the city" is never offered.
            new MenuLine(() => { c.Camp(1); c.Next = Number; c.PassHours(0); }, OptionHint.Saying("Try it...you will like it!")),
            // orig: R044+0x0416 (the inn, R029+0x222A(0); next_card = 0; advance_hours(0)), hint R044+0x0430
            new MenuLine(() => { c.Camp(0); c.Next = Number; c.PassHours(0); }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x0468 (cur_rep = -0x63, loc_set(cur_loc, 0, -0x63), var_number1 = cur_rep), hint R044+0x0484
            new MenuLine(() => SetStanding(c, -0x63), OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x04BC (cur_rep = 0, var_number1 = 0, loc_set(cur_loc, 0, 0)), hint R044+0x04D4
            new MenuLine(() => SetStanding(c, 0), OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x050C (cur_rep = 0x63, var_number1 = 0x63, loc_set(cur_loc, 0, 0x63)), hint R044+0x0526
            new MenuLine(() => SetStanding(c, 0x63), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => { c.PassHours(0); c.Next = EndOfGame; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The standing here as the card sees it ($Number1) and as the location keeps it; the card stays.</summary>
    private static void SetStanding(CardContext c, int standing)
    {
        c.StandingSeen = standing;
        c.LocationSet(c.Here, 0, standing);
        c.Variables.Numbers[0] = standing;
    }
}
