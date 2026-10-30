using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd02": a black oak writing desk in the Templars' fortress, with a silver warding
/// symbol, silver papers and ink. Taking the silver or burning the desk calls the guardians
/// (the card returns −3); copying the symbol may teach the best scholar three formulas.
/// </summary>
// orig: R118+0x1F3C (1A2B:000C) matched; tests: CardSmokeTests
// orig: R118+0x2674 (1A2B:0744) matched (registration: item pop-ups name the member, saint pop-ups do not)
// DORMANT: texts 2 and 8 (after the guardians' battle) are never shown by this card.
public sealed class TemplarWritingDesk : Card
{
    private const int FormulaCount = 0x42;

    public override int Number => 299;

    public override int Run(CardContext c)
    {
        Show(c, 166);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        c.SetCardSaints(0x00, 0x59, 0x61, 0x80);
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R118+0x2174 (1A2B:0244) matched; hint R118+0x21A4
            new MenuLine(_ => TakeTheSilver(c), Unknown),
            // orig: R118+0x21DC (1A2B:02AC) matched; hint R118+0x2384
            new MenuLine(p => CopyTheSymbol(c, menu, p),
                () => OptionHint.ChanceOf(c.PartyMaxSkillSum((int)Skill.ReadWrite, (int)Skill.SpeakLatin), c.BestMember)),
            // orig: R118+0x23A4 (1A2B:0474) matched; hint R118+0x23C2
            new MenuLine(_ => DestroyTheDesk(c), Unknown),
            // orig: R118+0x23FA (1A2B:04CA) matched; hint R118+0x2434
            new MenuLine(_ => CallOnASaint(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R118+0x246C (1A2B:053C) matched; hint R118+0x247A
            // C: "turn and leave" starts a battle (mode 1, -2) with the enemy and seed left from before: R118+0x246C writes only DS:A766 = 1 and DS:E48A = -2;
            //   CardBattle is the persistent BattleOrders, which keep the enemy, seed and the rest.
            new MenuLine(_ => { c.CardBattle.Mode = 1; c.Next = -2; }, Unknown),
            // DORMANT: lines 5..9 are never enabled. Each (R118+0x24B2, R118+0x250C, R118+0x2566,
            // R118+0x25C0, R118+0x261A) would show text 0, pass no time and close the card (0x62);
            // hints 05a0..0708 say "unknown" for no one.
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The silver is worth a florin, but its guardians come (flag 8, battle mode 1, −3).</summary>
    // orig: R118+0x2174 (1A2B:0244) matched; tests: CardSmokeTests
    private static void TakeTheSilver(CardContext c)
    {
        c.Screen.Text(1);
        c.State.Party.Purse = c.State.Party.Purse.Add(1, 0, 0);
        c.CardBattle.Flags |= 8;
        c.CardBattle.Mode = 1;
        c.Next = -3;
    }

    // orig: R118+0x23A4 (1A2B:0474) matched; tests: CardSmokeTests
    private static void DestroyTheDesk(CardContext c)
    {
        c.Screen.Text(7);
        c.CardBattle.Flags |= 8;
        c.CardBattle.Mode = 1;
        c.Next = -3;
    }

    /// <summary>A successful copy teaches the best Read &amp; Write + Latin member three formulas.</summary>
    // orig: R118+0x21DC (1A2B:02AC) matched; tests: CardSmokeTests
    // C: party-ability-top-sum is called again (its result unused) to set the best member before the roll.
    private static void CopyTheSymbol(CardContext c, CardMenu menu, int chance)
    {
        menu[1] = OptionState.Shown;
        c.PartyMaxSkillSum((int)Skill.ReadWrite, (int)Skill.SpeakLatin);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(3);
            var scholar = c.State.Party.Slot(c.BestMember);
            LearnAFormula(c, scholar);
            LearnAFormula(c, scholar);
            LearnAFormula(c, scholar);
            return;
        }
        c.Screen.Text(4);
    }

    /// <summary>
    /// A random formula 0..65, moved on (wrapping) past any whose byte of the formula table
    /// already holds a known formula, then learnt.
    /// </summary>
    // BUG: the search tests known-recipes[m][n / 3] && 1 << n % 3 (logical, not bitwise): it skips any n
    // whose group of three has any formula known, not just n itself. If all groups do, the first n is learnt.
    private static void LearnAFormula(CardContext c, Character scholar)
    {
        var first = c.Random(FormulaCount);
        var n = first;
        while (GroupHasAFormula(scholar, n / 3))
        {
            if (++n >= FormulaCount)
            {
                n = 0;
            }
            if (n == first)
            {
                break;
            }
        }
        scholar.Formulas.Add(n);
    }

    private static bool GroupHasAFormula(Character scholar, int group) =>
        scholar.Formulas.Contains(group * 3) || scholar.Formulas.Contains(group * 3 + 1) || scholar.Formulas.Contains(group * 3 + 2);

    // orig: R118+0x23FA (1A2B:04CA) matched; tests: CardSmokeTests
    private static void CallOnASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.Screen.Text(answer != 0 ? 5 : 6);
    }

    // orig: R118+0x24B2 (1A2B:0582) matched (dormant; twins 05dc, 0636, 0690, 06ea)
    private static void CloseTheCard(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CardContext.ClosedCard;
    }
}
