namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Balti01": aboard ship on the eastern Baltic. The party picks a port (Stralsund, Stettin,
/// Danzig: hours to land, card 0x5A) or sails back west (card 269). Sweden, Finland and Poland
/// are shown but cannot be chosen.
/// </summary>
// orig: R111+0x0B28 (18EA:0008) matched; tests: CardSmokeTests
// orig: R111+0x102C (18EA:050C) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
// C: the loop calls card_menu without card_options or card_text (card_load draws only the picture), so the menu works on the option
// C: lines the screen last laid out (the previous card's last text) with this card's states and handlers (CardMenu.DrawsOptions = false).
// C: its pop-up branch calls (*item_fn[menu_slot])(): a pop-up line of that stale layout would run the pop-up handler installed here.
// C: the three ports all lead to card 0x5A; nothing here records which port was chosen.
public sealed class EasternBalticVoyage : Card
{
    public override int Number => 270;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.DrawsOptions = false;
        menu.Set(OptionState.Enabled, 0, 1, 2, 9);
        menu.Set(OptionState.Shown, 6, 7, 8);

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R111+0x0D36 (18EA:0216) matched; hint R111+0x0D54
            new MenuLine(_ => Land(c, c.Random(3) + 3)),
            // orig: R111+0x0D8C (18EA:026C) matched; hint R111+0x0DAA
            new MenuLine(_ => Land(c, c.Random(4) + 4)),
            // orig: R111+0x0DE2 (18EA:02C2) matched; hint R111+0x0E00
            new MenuLine(_ => Land(c, c.Random(3) + 3)),
            // orig: R111+0x0E38 (18EA:0318) matched (empty); hint R111+0x0E3A
            // DORMANT: lines 3..5 are never offered; their actions (R111+0x0E72, R111+0x0EAC) are empty.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            // orig: R111+0x0EE6 (18EA:03C6) matched; hint R111+0x0EFE
            // DORMANT: lines 6..8 are shown but cannot be chosen; they would only pass time at sea.
            new MenuLine(_ => c.PassHours(c.Random(6) + 20)),
            // orig: R111+0x0F36 (18EA:0416) matched; hint R111+0x0F4E
            new MenuLine(_ => c.PassHours(c.Random(8) + 30)),
            // orig: R111+0x0F86 (18EA:0466) matched; hint R111+0x0F9E
            new MenuLine(_ => c.PassHours(c.Random(4) + 16)),
            // orig: R111+0x0FD6 (18EA:04B6) matched; hint R111+0x0FF4
            new MenuLine(_ => { c.PassHours(c.Random(5) + 18); c.Next = 269; }));
    }

    private static void Land(CardContext c, int hours)
    {
        c.PassHours(hours);
        c.Next = 0x5A;
    }
}
