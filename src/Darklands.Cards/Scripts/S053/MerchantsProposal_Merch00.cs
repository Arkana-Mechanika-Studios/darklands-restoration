namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Merch00": a merchant of the city makes the party a special proposition (a rare item as the
/// reward for a dangerous service). The card has no option lines in its text; whichever line is
/// chosen shows the merchant's speech and leads to card 0x62 (the "cannot be entered" card).
/// </summary>
// orig: R053+0x2EE4 (1B26:0004) matched; tests: CardSmokeTests
public sealed class MerchantsProposal_Merch00 : Card
{
    public const int CardNumber = 0x27;

    private const int Picture = 0;

    private const int CannotEnter = 0x62;

    public override int Number => CardNumber;

    // orig: R053+0x2EE4 (1B26:0004) matched (entry: all ten option lines enabled, items and saints, the menu loop)
    // orig: R053+0x34AA (1B26:05CA) matched (init helper: switches the option lines off and installs the default and pop-up handlers; the entry then enables lines 0..9 and refills card_items and card_saints)
    // DORMANT: R053+0x354C (1B26:066C) (the "screwed up." default hint for opt_fn[0..19]); lines 0..9 have their own hints and lines 10..19 stay off.
    // DORMANT: R053+0x3584 (1B26:06A4), R053+0x35C0 (1B26:06E0), R053+0x35FC (1B26:071C), R053+0x3638 (1B26:0758) (item pop-up handlers: success assured for the chosen member) and
    // R053+0x3674 (1B26:0794), R053+0x36FE (1B26:081E), R053+0x3788 (1B26:08A8), R053+0x3812 (1B26:0932) (saint pop-up handlers: chance text of the chosen saint): no option opens an item or saint pop-up.
    // UNCERTAIN: the MSG file has no option lines (block 0 is only the merchant's speech), so on the real screen no line can be chosen; the ten actions are ported all the same.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R053+0x3126 (1B26:0246) (option 0 action), hint R053+0x3144 (1B26:0264) (success assured)
            Proposal(c),
            // orig: R053+0x3180 (1B26:02A0) (option 1 action), hint R053+0x319E (1B26:02BE)
            Proposal(c),
            // orig: R053+0x31DA (1B26:02FA) (option 2 action), hint R053+0x31F8 (1B26:0318)
            Proposal(c),
            // orig: R053+0x3234 (1B26:0354) (option 3 action), hint R053+0x3252 (1B26:0372)
            Proposal(c),
            // orig: R053+0x328E (1B26:03AE) (option 4 action), hint R053+0x32AC (1B26:03CC)
            Proposal(c),
            // orig: R053+0x32E8 (1B26:0408) (option 5 action), hint R053+0x3306 (1B26:0426)
            Proposal(c),
            // orig: R053+0x3342 (1B26:0462) (option 6 action), hint R053+0x3360 (1B26:0480)
            Proposal(c),
            // orig: R053+0x339C (1B26:04BC) (option 7 action), hint R053+0x33BA (1B26:04DA)
            Proposal(c),
            // orig: R053+0x33F6 (1B26:0516) (option 8 action), hint R053+0x3414 (1B26:0534)
            Proposal(c),
            // orig: R053+0x3450 (1B26:0570) (option 9 action), hint R053+0x346E (1B26:058E)
            Proposal(c));
    }

    /// <summary>
    /// Every option line does the same: the merchant speaks, no time passes and the next card is 0x62.
    /// C: the hints first set the member to the leader and then overwrite it with "whole party": success assured for everybody.
    /// </summary>
    // orig: R053+0x3126 (1B26:0246) matched (the ten actions are identical)
    private static MenuLine Proposal(CardContext c) =>
        new(() =>
        {
            c.Screen.Text(0);
            c.PassHours(0);
            c.Next = CannotEnter;
        });
}
