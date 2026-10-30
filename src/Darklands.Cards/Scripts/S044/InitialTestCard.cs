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
// orig: the reset R044+0x05AC (1838:05AC) matched: card-option-enabled[0..19] = 1, card-option-handler[0..19] = the "screwed up." handler
//   R044+0x064E, item-handler-table[k][0..3] = R044+0x0686/06c2/06fe/073a ("Success is assured.", text-slot-chosen-one = r.b = menu-owner-member) and
//   saint-handler-table[k][0..3] = R044+0x0776/0800/088a/0914 (chance-line(saint-odds-low, saint-odds-high) of menu-owner-member's saint in slot menu-choice-index),
//   then card-item-lines and card-saint-lines cleared to -1.
// DORMANT: the pop-up handlers: the card's text has no item or saint line, so no box opens; lines 10..19 (the "screwed up." handler) and
//   line 9 (R044+0x055E: pass-hours(0), following-card = 0x62, the end of the game; hint R044+0x0570) have no text line to choose.
// DIVERGENCE: DS:EE70 = 0xF and DS:EE72 = 7 (the pop-up boxes' drawing flags) are not kept; no box opens on this card. DS:EE70 has no reader in the EXE and DS:EE72 is only sign-tested (R043+0x2BFC), so the stores change nothing.
public sealed class InitialTestCard : Card
{
    public const int CardNumber = 0x00;

    private const int Picture = 2;   // image-file-names+0x0A4F
    private const int CityEntry = 0x04, CityPreparingForWar = 0x73, OverlandMap = 0x0C, EndOfGame = 0x62;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        var state = c.State;
        Show(c, Picture);
        // C: following-card = 0 (the card's own number: the menu runs while it stays 0), earlier-screen = 0.
        c.Next = Number;
        state.PreviousCard = 0;
        // C: place-assign(location-now, 0, 0) first, then purse-assign(party-purse, 0xC8, 0, 0), text-slot-amount-one = party-purse,
        //   reputation-now = place-query(location-now, 0), text-slot-number-one = reputation-now, text-slot-number-two = 0.
        c.LocationSet(c.Here, 0, 0);
        state.Party.Purse = Money.From(0xC8, 0, 0);
        c.Variables.Money[0] = state.Party.Purse;
        c.StandingSeen = c.LocationQuery(c.Here, 0);
        c.Variables.Numbers[0] = c.StandingSeen;
        c.Variables.Numbers[1] = 0;
        // C: made-up-name(*(int far *)(city-record + 0x56) + 0xC, 0, 0): a man's name with a surname into $Named1, from the city's seed word.
        c.RandomName(c.City.Word(0x56) + 0x0C, 0, 0);
        // C: card-item-lines 0x5F, 0x60, 0x65, 0x61 and card-saint-lines 0, 0x59, 0x61, 0x80 after the reset.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        // C: card-option-flag-0, 1, 3, 5..9 = 1, card-option-flag-2 = 2 (shown, not chosen), card-option-flag-4 = 0; the text is block 0.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 3, 5, 6, 7, 8, 9);
        menu[2] = OptionState.Shown;
        return menu.Run(
            // orig: R044+0x02A6 (action: pass-hours(0), following-card = 4), hint R044+0x02B8 (r.a = leader, then -1: says only)
            new MenuLine(() => { c.PassHours(0); c.Next = CityEntry; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x02F4 (pass-hours(0), following-card = 0x73), hint R044+0x0306
            new MenuLine(() => { c.PassHours(0); c.Next = CityPreparingForWar; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x033E (pass-hours(0), following-card = 0: the card again), hint R044+0x0350
            new MenuLine(() => { c.PassHours(0); c.Next = Number; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x0388 (pass-hours(0), following-card = 0xC), hint R044+0x039A
            new MenuLine(() => { c.PassHours(0); c.Next = OverlandMap; }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x03D2 (camp, R029+0x222A(1); following-card = 0; pass-hours(0)), hint R044+0x03EC
            // DORMANT: card-option-flag-4 = 0: "Camp outside the city" is never offered.
            new MenuLine(() => { c.Camp(1); c.Next = Number; c.PassHours(0); }, OptionHint.Saying("Try it...you will like it!")),
            // orig: R044+0x0416 (the inn, R029+0x222A(0); following-card = 0; pass-hours(0)), hint R044+0x0430
            new MenuLine(() => { c.Camp(0); c.Next = Number; c.PassHours(0); }, OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x0468 (reputation-now = -0x63, place-assign(location-now, 0, -0x63), text-slot-number-one = reputation-now), hint R044+0x0484
            new MenuLine(() => SetStanding(c, -0x63), OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x04BC (reputation-now = 0, text-slot-number-one = 0, place-assign(location-now, 0, 0)), hint R044+0x04D4
            new MenuLine(() => SetStanding(c, 0), OptionHint.Saying(SuccessAssured)),
            // orig: R044+0x050C (reputation-now = 0x63, text-slot-number-one = 0x63, place-assign(location-now, 0, 0x63)), hint R044+0x0526
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
