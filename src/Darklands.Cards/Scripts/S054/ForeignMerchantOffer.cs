namespace Darklands.Cards.Scripts;

/// <summary>
/// "$forei00": a foreign merchant's special offer (an item, a relic, armor or a horse in return for a
/// dangerous service). In the game's data this card is an unfinished stub: all ten option lines are
/// enabled and every one of them shows the first text, passes no time and leads to card 0x62
/// ("$NoGo00"), while the card's text file holds no option lines at all.
/// </summary>
// orig: R054+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class ForeignMerchantOffer : Card
{
    public const int CardNumber = 0x28;

    private const int Picture = 0;
    private const int NoGo = 0x62;

    public override int Number => CardNumber;

    // orig: R054+0x0000 (1838:0000) matched (entry: card set-up, ten enabled option lines and the menu loop)
    // orig: R054+0x05C6 (1838:05C6) matched (init helper: clears the option flags, installs the "screwed up." default handler on all 20 lines, the pop-up handlers on all 5 member rows, and empties the item and saint slots)
    // DORMANT: R054+0x0668 (1838:0668): the "screwed up." default handler of lines 10..19; the card text has no line for them and the menu loop copies only flags 0..9, so it never runs.
    // DORMANT: R054+0x06A0 (1838:06A0), R054+0x06DC (1838:06DC), R054+0x0718 (1838:0718) and R054+0x0754 (1838:0754): the item pop-up handlers (slots 0..3): they name the member of the pop-up ($ChosenOneName) and say "Success is assured.". The card fills the item slots (0x5F, 0x60, 0x65, 0x61) but none of its options asks usable-item-list(), so the item pop-up never opens.
    // DORMANT: R054+0x0790 (1838:0790), R054+0x081A (1838:081A), R054+0x08A4 (1838:08A4) and R054+0x092E (1838:092E): the saint pop-up handlers (slots 0..3, identical): they name the member and show its chance with the saint. The saint slots are filled (0, 0x59, 0x61, 0x80) but no option asks saints-available(), so the saint pop-up never opens.
    // C: the card's text file has no option lines, so what the original screen offers for the ten enabled options is unknown (probably nothing selectable); the C# offers all ten as the code enables them.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var options = Enumerable.Range(0, 10).Select(_ => LeaveOption(c)).ToArray();
        c.Next = Number;
        return menu.Run(options);
    }

    // orig: R054+0x0242 (1838:0242), R054+0x029C (1838:029C), R054+0x02F6 (1838:02F6), R054+0x0350 (1838:0350), R054+0x03AA (1838:03AA), R054+0x0404 (1838:0404), R054+0x045E (1838:045E), R054+0x04B8 (1838:04B8), R054+0x0512 (1838:0512), R054+0x056C (1838:056C) matched (option 0..9 actions, all the same)
    // orig: R054+0x0260 (1838:0260), R054+0x02BA (1838:02BA), R054+0x0314 (1838:0314), R054+0x036E (1838:036E), R054+0x03C8 (1838:03C8), R054+0x0422 (1838:0422), R054+0x047C (1838:047C), R054+0x04D6 (1838:04D6), R054+0x0530 (1838:0530), R054+0x058A (1838:058A) matched (option 0..9 hints, all the same)
    // C: each hint first names the leader (r.b = leader) and then overwrites it with -2, so the hint is for the whole party.
    private static MenuLine LeaveOption(CardContext c) =>
        new(() =>
            {
                c.Screen.Text(0);
                c.PassHours(0);
                c.Next = NoGo;
            },
            OptionHint.Saying(SuccessAssured));
}
