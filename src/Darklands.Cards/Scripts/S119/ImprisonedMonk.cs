namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd03": in a dank cell of the witches' castle a mad old monk lies in chains. The party
/// may talk to him, test him with a prayer, attack him or try to free him; if they mention the
/// Lord he asks for confession, and a good confession earns the secret of the stag's horns.
/// Every way out of the cell goes back to the castle (−2/−3, battle mode 1).
/// </summary>
// orig: R119+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R119+0x023A; hint R119+0x0248
//   option 1: action R119+0x0280; hint R119+0x02A8
//   option 2: action R119+0x02E0; hint R119+0x031A
//   option 3: action R119+0x0352; hint R119+0x036C
//   option 4: action R119+0x03A4; hint R119+0x03B8
//   option 5: action R119+0x03F0; hint R119+0x040A
//   option 6: action R119+0x0442; hint R119+0x045C
//   option 7: action R119+0x0494; hint R119+0x04BE
//   option 8: action R119+0x04F6; hint R119+0x0526
//   option 9: action R119+0x0544; hint R119+0x055E
//   item pop-up: R119+0x0670, R119+0x06AC, R119+0x06E8,
//     R119+0x0724
//   saint pop-up: R119+0x0760, R119+0x07E8, R119+0x0870,
//     R119+0x08F8
//   other: R119+0x0596, R119+0x0638
// DORMANT (generated): option slots 10..19 keep the default handler R119+0x0638 ("screwed up.") and are never shown.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class ImprisonedMonk : Card
{
    public override int Number => 300;

    private const int Picture = 172;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        // orig: R119+0x0596 (1838:0596) matched — clears the option table, item and saint lists.
        // DORMANT: R119+0x0638 ("screwed up.") is the handler of every option slot before the menu
        //   fills slots 0..9; slots 10..19 keep it but are never shown (CardMenu offers lines 0..9 only).
        // DORMANT: the item pop-up handlers R119+0x0670 / _06ac / _06e8 / _0724 (R119+0x0670..0x0724:
        //   $ChosenOneName = the member in the box, "You can always encounter the unknown") need a usable item, but
        //   card_items stays all −1 here, so no line opens an item box. Kept (unwired) as ItemPopup below.
        c.SetCardItems();
        c.SetCardSaints(0x5D, 0x3D, 0x6A, 0x43);
        menu[2] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        // orig: R119+0x0760 / _07e8 / _0870 / _08f8 R119+0x0760..0x08F8 matched — saint pop-up chance text (c.SaintPopupChanceHint).
        // DORMANT: in text block 1 the option lines 0..4 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 7 the option lines 0..7 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            // orig: R119+0x023A / R119+0x0248 matched — leave the cell.
            new MenuLine(_ => Leave(c, -2), unknown),
            // orig: R119+0x0280 / R119+0x02A8 matched — talk: option text 1.
            new MenuLine(_ => menu.Offer(1, 5, 6, 7), unknown),
            // orig: R119+0x02E0 / R119+0x031A matched — call a saint.
            new MenuLine(_ => PrayOverHim(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R119+0x0352 / R119+0x036C matched — attack him.
            new MenuLine(_ => { c.Screen.Text(4); Leave(c, -3); }, unknown),
            // orig: R119+0x03A4 / R119+0x03B8 matched — try to free him (line shown, not choosable, afterwards).
            new MenuLine(_ => { menu[4] = OptionState.Shown; c.Screen.Text(5); }, unknown),
            // orig: R119+0x03F0 / R119+0x040A matched — "the one thing".
            new MenuLine(_ => { c.Screen.Text(6); Leave(c, -3); }, unknown),
            // orig: R119+0x0442 / R119+0x045C matched — persuade him.
            new MenuLine(_ => { c.Screen.Text(6); Leave(c, -3); }, unknown),
            // orig: R119+0x0494 / R119+0x04BE matched — talk about God: option text 7.
            new MenuLine(_ => menu.Offer(7, 8, 9), unknown),
            // orig: R119+0x04F6 / R119+0x0526 matched — confession.
            new MenuLine(chance => Confess(c, chance), () => ConfessionChance(c)),
            // orig: R119+0x0544 / R119+0x055E matched — press him for information.
            new MenuLine(_ => { c.Screen.Text(6); Leave(c, -3); }, unknown));
    }

    /// <summary>The card's item pop-up handlers (all four slots alike): the member in the box becomes $ChosenOneName.</summary>
    // orig: R119+0x0670 (1838:0670) matched; also _06ac, _06e8, _0724 (identical)
    // DORMANT: no item is ever usable on this card (card_items all −1).
    private static Func<OptionHint> ItemPopup(CardContext c) => () => c.MemberPopupHint(CardTexts.Unknown);

    private static void Leave(CardContext c, int next)
    {
        c.BattleMode = 1;
        c.Next = next;
    }

    /// <summary>A saint's light shows him a friend (text 2) or only agitates him (text 3).</summary>
    // orig: R119+0x02E0 (1838:02E0) matched
    private static void PrayOverHim(CardContext c)
    {
        // C: menu_member / menu_slot are the globals the pop-up box set last; chosen from the option line itself the
        //   prayer uses whatever they still hold (the same here: c.MenuMember / c.MenuSlot).
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        // C: the handler falls off its end without a value after either text; the card ignores it.
        c.Screen.Text(result != 0 ? 2 : 3);
    }

    /// <summary>The best Religion in the party hears his confession.</summary>
    // orig: R119+0x0526 (1838:0526) matched
    private static OptionHint ConfessionChance(CardContext c)
    {
        // C: party_max_skill sets best_member, which the handler then reads (truncated to unsigned char).
        var chance = c.PartyMaxSkill((int)Core.Characters.Skill.Religion);
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R119+0x04F6 (1838:04F6) matched
    private static void Confess(CardContext c, int chance)
    {
        c.Screen.Text(c.Random(100) <= chance ? 8 : 9);
        Leave(c, -3);
    }
}
