using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Alche01": the alchemist offers four formulas for sale (which four depends on the city),
/// each priced by the city's size. A party member chosen in the item pop-up buys the formula
/// for the potion shown; the other lines let the party talk about other things or take its leave.
/// </summary>
// orig: R064+0x2D38 (1B0B:0008) matched; tests: CardSmokeTests
// orig: handler setup R064+0x3580; unused-line handler R064+0x35E2 ("screwed up.");
// item pop-ups R064+0x361A/0926/0962/099e (standard); no saints are listed
public sealed class AlchemistFormulaSeller : Card
{
    private const int AlchemistPicture = 74;
    private const int ListedFormulas = 4;
    private const int FormulaGroups = 22;
    private const int CityEntryAfterTalk = 0x62;

    public override int Number => 0x53;

    public override int Run(CardContext c)
    {
        Show(c, AlchemistPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // Lines 1, 2 and 5..9 are switched off (flags 0): the text lists only the purchase and the two ways to leave.
        var menu = Menu(c, 0);
        menu.Enable(0, 3, 4);

        // The four formulas on offer: potion item, name and price in $Text1..4 and $Money1..4.
        for (var slot = 0; slot < ListedFormulas; slot++)
        {
            c.CardItems[slot] = FormulaOnOffer(c, slot) / 3 + Core.Items.Potions.FirstPotion;
        }
        for (var slot = 0; slot < ListedFormulas; slot++)
        {
            c.Variables.Texts[slot] = FormulaName(c, FormulaOnOffer(c, slot));
        }
        for (var slot = 0; slot < ListedFormulas; slot++)
        {
            c.Variables.Money[slot] = FormulaPrice(c, FormulaOnOffer(c, slot));
        }
        // C: DS:5204 = 1 for the menu loop (back to 0 when it ends): the item pop-up lists every formula for every member.
        c.PopupListsAll = true;
        var next = menu.Run(
            // The purchase is made through the item pop-up (the plain line only says "assured").
            new MenuLine(chance => BuyFormula(c), OptionHint.Saying(SuccessAssured), () => c.MemberPopupHint()),
            // DORMANT: lines 1 and 2 (opt_flag_1 = opt_flag_2 = 0): empty actions, empty hint handlers.
            // orig: R064+0x331E/05f2 R064+0x331E/0x3322, hint handlers 05f0/05f4 (empty: their result is stale in the original)
            // C: the hint handlers are empty void functions: their struct res would be whatever was left in DX:AX.
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            // orig: R064+0x3326, hint 0608: politely take leave of the alchemist
            new MenuLine(_ => { c.PassHours(1); c.Next = c.PreviousCard; }, () => new OptionHint(-1, SuccessAssured, c.Leader)),
            // orig: R064+0x3372, hint 0654: talk about other things (back to where the party was)
            new MenuLine(_ => { c.PassHours(1); c.Next = c.SavedCard; }, () => new OptionHint(-1, SuccessAssured, c.Leader)),
            // DORMANT: lines 5..9 (flags 0): text 0 and the card 0x62. orig: R064+0x33BE, 06e8, 0742, 079c, 07f6; hints 06ac, 0706, 0760, 07ba, 0814
            new MenuLine(_ => TalkAndGo(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndGo(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndGo(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndGo(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TalkAndGo(c), OptionHint.Saying(SuccessAssured)));
        c.PopupListsAll = false;
        return next;
    }

    /// <summary>
    /// The formula number (0..21) offered in a slot: the city's own number (a different one per city) plus
    /// 100, 103, 106 or 109, modulo 22. The potion is the formula divided by three.
    /// </summary>
    // C: cur_loc_query returns unsigned: the sum and the modulo are unsigned 16-bit.
    private static int FormulaOnOffer(CardContext c, int slot) => (ushort)((ushort)c.HereQuery(0x21) + slot * 3 + 100) % FormulaGroups;

    // C: R136+0xF066 (virtue_names) is filled with the full formula names of DARKLAND.LST by world_load (see CardContext.NameToSlot), so the LST names are the table.
    private static string FormulaName(CardContext c, int formula) =>
        formula < c.World.Items.Formulae.Count ? c.World.Items.Formulae[formula].FullName : "";

    /// <summary>10 florins for the first grade of a potion, 20 and 30 for the others, divided by the city's size.</summary>
    // C: unsigned 16-bit division, as cur_loc_query returns unsigned (a size of 0 would trap in the original).
    private static Money FormulaPrice(CardContext c, int formula) =>
        Money.From((ushort)((formula % 3 + 1) * 10) / (ushort)c.HereQuery(2), 0, 0);

    /// <summary>The chosen member buys the formula of the potion in the chosen slot, if rich enough and not knowing it yet.</summary>
    // orig: R064+0x31DC
    private static void BuyFormula(CardContext c)
    {
        var member = c.MenuMember;
        var formula = FormulaOnOffer(c, c.MenuSlot);
        var price = FormulaPrice(c, formula);
        c.Variables.Money[0] = price;
        var purse = c.State.Party.Purse;
        if (purse.CompareTo(price) < 0)
        {
            c.Screen.Text(1);
            return;
        }
        var known = c.State.Party.Slot(member).Formulas;
        if (known.Contains(formula))
        {
            c.Screen.Text(2);
            return;
        }
        known.Add(formula);
        c.State.Party.Purse = purse.Subtract(price);
        c.Next = c.PreviousCard;
    }

    private static void TalkAndGo(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CityEntryAfterTalk;
    }
}
