using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using Darklands.Core.Economy;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC11": behind a gate of the last castle waits Famine, the starved horseman with the
/// balance. A saint hints that the rich fear famine least. Offering souls costs Virtue and
/// divine favour; offering lives costs a member each time until fewer than three are left, and
/// then he gives up the balance; offering all money and goods wins it at once. Every way out
/// returns to the courtyard (−2, the castle's battle 0x57).
/// </summary>
// orig: R133+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R133+0x01A8; hint R133+0x01F4
//   option 1: action R133+0x022E; hint R133+0x029C
//   option 2: action R133+0x02D4; hint R133+0x033C
//   option 3: action R133+0x0374; hint R133+0x0424
//   option 4: action R133+0x049C; hint R133+0x050E
//   option 5: action R133+0x0546; hint R133+0x056E
//   saint pop-up: R133+0x0636, R133+0x06C0
//   other: R133+0x05A6, R133+0x05FE
// DORMANT (generated): option slots 10..19 keep the default handler R133+0x05FE ("screwed up.") and are never shown.
// C (generated): opt_flag_6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class FamineHorseman : Card
{
    public override int Number => 356;

    private const int Picture = 300;
    private const int Despair = 301;
    private const int Horseman = 348;
    private const int Balance = 349;

    /// <summary>The fifth Key of the Apocalypse, the balance.</summary>
    private const int TheBalance = 0x90;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var background = Picture;
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 5);
        c.SetCardItems();
        c.SetCardSaints(0x0A, 0x47);
        menu[1] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        // C: every handler of this card redraws with card_load (the whole card, Show), not card_load_near.
        return menu.Run(
            // orig: R133+0x01A8 / R133+0x01F4 matched — approach him: block 3 (line 5 keeps its flag 1).
            // C: the handler returns the value of var_chosen1 = leader; the menu ignores it.
            new MenuLine(_ =>
            {
                menu.Offer(3, 2, 3, 4, 5);
                background = Horseman;
                Show(c, Horseman);
                c.Variables.Chosen[0] = c.Leader;
            }, () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R133+0x022E / R133+0x029C matched; pop-up R133+0x0636 / _06c0 matched
            new MenuLine(_ => CallOnASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), () => SaintHint(c)),
            // orig: R133+0x02D4 / R133+0x033C matched — offer souls.
            new MenuLine(_ =>
            {
                Show(c, Despair);
                c.Screen.Text(4);
                Show(c, background);
                c.AddSkill(Who.Everybody, (int)Skill.Virtue, -99);
                c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -99);
                c.Next = Number;
            }, unknown),
            // orig: R133+0x0374 / R133+0x0424 matched
            new MenuLine(_ => OfferALife(c), () => PickAVictim(c)),
            // orig: R133+0x049C / R133+0x050E matched — offer all money and goods.
            // C: declared int, falls off its end without a value (ignored).
            new MenuLine(_ =>
            {
                Show(c, Balance);
                c.Screen.Text(7);
                c.State.Party.Purse = Money.Zero;
                // C: DS:9072 = 0 clears only the florins word; DS:9074/DS:9076 (groschen, pfennigs) stay.
                c.State.Party.BankNotes = c.State.Party.BankNotes with { Florins = 0 };
                c.AddItem(Who.Leader, TheBalance);
                BackToTheCourtyard(c);
            }, unknown),
            // orig: R133+0x0546 / R133+0x056E matched
            new MenuLine(_ => BackToTheCourtyard(c), OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The saint's whisper (text 2) leaves a fact the ending looks for.</summary>
    // orig: R133+0x022E matched
    // C: returns 0 after a cancelled box (the line stays choosable), otherwise returns without a value (ignored).
    private void CallOnASaint(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        menu[1] = OptionState.Shown;
        if (result != 0)
        {
            c.Screen.Text(2);
            c.Events.Create(-2, c.Here, 0, 0x1C, 0x19, 0, 0, EventLog.Forever, 0, 1);
        }
        else
        {
            c.Screen.Text(1);
        }
        c.Next = Number;
    }

    /// <summary>The saint pop-up: the member chosen in the box becomes $ChosenOneName.</summary>
    // orig: R133+0x0636 matched (also _06c0)
    private static OptionHint SaintHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var low = c.SaintChanceLow(c.MenuMember, c.CardSaints[c.MenuSlot]);
        var high = c.SaintChanceHigh(c.MenuMember, c.CardSaints[c.MenuSlot]);
        return OptionHint.Say(c.ChanceText(low, high));
    }

    /// <summary>The horseman picks whose life he takes: a random slot, then the next occupied one.</summary>
    // orig: R133+0x0424 matched
    // C: checked against the asm: var_chosen1 is a 16-bit int; the slot test reads party[var_chosen1 % 5]'s status
    //   byte (0 = empty) and the hint stores var_chosen1 %= 5. The loop ends because a present member exists.
    private static OptionHint PickAVictim(CardContext c)
    {
        var victim = c.Random(c.PartySize);
        while (c.Status(victim % 5) == CharacterStatus.Empty)
        {
            victim++;
        }
        victim %= 5;
        c.Variables.Chosen[0] = victim;
        return OptionHint.Say(CardTexts.Unknown, victim);
    }

    /// <summary>A member dies while more than two are present; then the horseman gives up.</summary>
    // orig: R133+0x0374 matched
    // C: party[var_chosen1] (set by the hint just before) is written without a check; it is always 0..4 here.
    //   Declared int, the handler returns without a value (ignored).
    private void OfferALife(CardContext c)
    {
        if (c.PresentCount() > 2)
        {
            c.Screen.Text(5);
            var victim = c.Variables.Chosen[0];
            c.SetStatus(victim, CharacterStatus.Empty);
            c.DeleteMember(victim);
            c.Variables.Chosen[0] = c.Leader;
            c.PassHours(0);
            c.Next = Number;
            return;
        }
        Show(c, Balance);
        c.Screen.Text(6);
        c.AddItem(Who.Leader, TheBalance);
        BackToTheCourtyard(c);
    }

    // orig: the ending repeated inline in R133+0x0374 / _049c / _0546 matched
    private static void BackToTheCourtyard(CardContext c)
    {
        c.LastCastleFlags |= 0x1010;
        c.BattleMode = 0;
        c.BattleEnemy = 0x57;
        c.BattleSeed = c.Random(1000);
        c.Next = -2;
    }
}
