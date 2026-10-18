namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Pharm00": a merchant offers a special reward (a masterwork item, a relic, a suit of
/// armour, a white stallion, or only fame) for a dangerous service the merchants of the town
/// need done. The card is a stub: all ten option lines are enabled, and each shows the text
/// and ends at the encounter card.
/// </summary>
// orig: R087+0x0A12 (18D9:0002) matched; tests: CardSmokeTests
public sealed class MerchantsSpecialReward : Card
{
    public const int CardNumber = 0xAD;

    private const int Picture = 347;

    /// <summary>The card every line leads to.</summary>
    private const int NextCard = 0x62;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // All ten lines are enabled (option flags 1).
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        // orig: R087+0x0FDC (18D9:05CC) matched; R087+0x107E (18D9:066E) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 10..19 (never offered).
        // orig: R087+0x10B6 (18D9:06A6) matched; R087+0x10F2 (18D9:06E2) matched; R087+0x112E (18D9:071E) matched; R087+0x116A (18D9:075A) matched; tests: CardSmokeTests
        // DORMANT: no line opens the pop-up box (none is an item or saint line), so these hints never run.
        menu.Popup = () => ItemLineHint(c, c.MenuMember);
        // orig: R087+0x11A6 (18D9:0796) matched; R087+0x1230 (18D9:0820) matched; R087+0x12BA (18D9:08AA) matched; R087+0x1344 (18D9:0934) matched; tests: CardSmokeTests
        // DORMANT: the saint pop-up handler (c.SaintPopupHint) is not carried by any line either.
        // The start-up lists the potions and saints the pop-up box would offer, though no line uses them.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        // C: the start-up cannot leave this card, so the menu loop test is always true.
        // Each line shows text block 0 again and leaves; the hints say "success is assured".
        return menu.Run(
            // orig: R087+0x0C58 (18D9:0248) matched; R087+0x0C76 (18D9:0266) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0CB2 (18D9:02A2) matched; R087+0x0CD0 (18D9:02C0) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0D0C (18D9:02FC) matched; R087+0x0D2A (18D9:031A) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0D66 (18D9:0356) matched; R087+0x0D84 (18D9:0374) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0DC0 (18D9:03B0) matched; R087+0x0DDE (18D9:03CE) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0E1A (18D9:040A) matched; R087+0x0E38 (18D9:0428) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0E74 (18D9:0464) matched; R087+0x0E92 (18D9:0482) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0ECE (18D9:04BE) matched; R087+0x0EEC (18D9:04DC) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0F28 (18D9:0518) matched; R087+0x0F46 (18D9:0536) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x0F82 (18D9:0572) matched; R087+0x0FA0 (18D9:0590) matched; tests: CardSmokeTests
            new MenuLine(() => Leave(c), OptionHint.Saying(SuccessAssured)));
    }

    private static void Leave(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = NextCard;
    }

    private static OptionHint ItemLineHint(CardContext c, int member)
    {
        c.Variables.Chosen[0] = member;
        return OptionHint.Say(SuccessAssured, member);
    }
}
