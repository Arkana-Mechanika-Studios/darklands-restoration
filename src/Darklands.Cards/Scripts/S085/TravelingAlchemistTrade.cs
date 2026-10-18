namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetA00": the pavilion of a friendly traveling alchemist. He offers one of his potions
/// in exchange for a potion of the party's; after a trade he is ready for another.
/// </summary>
// orig: R085+0x0FE2 (1936:0002) matched; tests: CardSmokeTests
// orig: R085+0x12AA (1936:02CA) matched; R085+0x1302 (1936:0322) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered).
// DORMANT: text block 1 ("another trade?") is never shown by this card: the menu always uses block 0.
public sealed class TravelingAlchemistTrade : Card
{
    public const int CardNumber = 0xA6;

    private const int Picture = 105;

    /// <summary>
    /// The potion the alchemist offers. The original keeps it in a module static (it is set on
    /// every visit before use, and kept between visits).
    /// </summary>
    private int _offered;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // The potion on offer: one of 14 (13 listed, the 14th falls to the default).
        _offered = c.Random(14) switch
        {
            0 => 0x5F,
            1 => 0x60,
            2 => 0x65,
            3 => 0x61,
            4 => 0x68,
            5 => 0x62,
            6 => 0x69,
            7 => 0x6C,
            8 => 0x6F,
            9 => 0x70,
            10 => 0x71,
            11 => 0x72,
            12 => 0x73,
            _ => 0x74,
        };
        c.NameToSlot(0, _offered, 0);
        // The two potions of the party he asks for, one of four each (drawn in this order).
        var firstAsked = c.Random(4) switch
        {
            0 => 0x66,
            1 => 0x63,
            2 => 0x67,
            _ => 0x64,
        };
        var secondAsked = c.Random(4) switch
        {
            0 => 0x6A,
            1 => 0x6B,
            2 => 0x6D,
            _ => 0x6E,
        };
        c.SetCardSaints();
        c.SetCardItems(firstAsked, secondAsked);
        var menu = Menu(c);
        menu.Enable(1);
        menu[0] = c.ItemsUsable();
        if (c.Next != CardNumber)
        {
            // DORMANT: the start-up never leaves this card.
            return c.Next;
        }
        return menu.Run(
            // orig: R085+0x133A (1936:035A) matched; R085+0x1376 (1936:0396) matched; tests: CardSmokeTests
            // orig: R085+0x11F6 (1936:0216) matched; R085+0x122C (1936:024C) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
                c.AddItem(c.MenuMember, _offered);
                menu[0] = c.ItemsUsable();
                c.Next = CardNumber;
            }, OptionHint.Saying(NoRisk), () => c.MemberPopupHint(NoRisk)),
            // orig: R085+0x1264 (1936:0284) matched; R085+0x1272 (1936:0292) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
            }, OptionHint.Saying(SuccessAssured)));
    }
}
