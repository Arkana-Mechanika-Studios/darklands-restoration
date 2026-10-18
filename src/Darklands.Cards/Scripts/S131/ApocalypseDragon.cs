using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC07": the seven-headed, ten-horned dragon bids the party kneel and worship it. They
/// may kneel (it then demands the most virtuous member, $ChosenOneName), attack it (enemy
/// 0x6E) or leave. Refusing its demand brings back the first choices; agreeing costs that
/// member's life and everyone's virtue, and the party finds itself far away in the wilderness.
/// </summary>
// orig: R131+0x0DC0 (1914:0000) matched; tests: CardSmokeTests
//   option 0: action R131+0x0F40; hint R131+0x0F88
//   option 1: action R131+0x0FC0; hint R131+0x1016
//   option 2: action R131+0x104E; hint R131+0x105C
//   option 3: action R131+0x1094; hint R131+0x10DE
//   option 4: action R131+0x1100; hint R131+0x1188
//   other: R131+0x11AA, R131+0x11E2
// DORMANT (generated): option slots 10..19 keep the default handler R131+0x11E2 ("screwed up.") and are never shown.
// C (generated): opt_flag_5/6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// DIVERGENCE (generated): party_panel_rows(0x100) (root 0000:1716, a party-panel redraw) is left out; the screen redraws itself.
public sealed class ApocalypseDragon : Card
{
    public override int Number => 351;

    private static readonly int Virtue = (int)Skill.Virtue;

    public override int Run(CardContext c)
    {
        Show(c, 315);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints();
        c.PartyMaxSkill(Virtue);
        // C: best_member read as unsigned char.
        c.Variables.Chosen[0] = c.BestMember;

        var mostVirtuous = () => MostVirtuous(c);
        // C: every handler of this card redraws with card_load (the whole card, Show), not card_load_near.
        return menu.Run(
            // orig: R131+0x0F40 / R131+0x0F88 matched
            new MenuLine(_ => Kneel(c, menu), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R131+0x0FC0 / R131+0x1016 matched
            new MenuLine(_ => Attack(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R131+0x104E / R131+0x105C matched
            new MenuLine(_ => LastCastle.TurnBack(c), OptionHint.Saying(CardTexts.SuccessAssured)),
            // orig: R131+0x1094 / R131+0x10DE matched
            new MenuLine(_ => Refuse(c, menu), mostVirtuous),
            // orig: R131+0x1100 / R131+0x1188 matched
            new MenuLine(_ => GiveUpTheChosen(c), mostVirtuous));
    }

    /// <summary>The highest Virtue in the party; that member becomes $ChosenOneName.</summary>
    // orig: R131+0x10DE matched (also _03c8)
    // C: the hint itself sets var_chosen1 to best_member (unsigned char); line 4's victim is the member it names.
    private static OptionHint MostVirtuous(CardContext c)
    {
        var chance = c.PartyMaxSkill(Virtue);
        c.Variables.Chosen[0] = c.BestMember;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    /// <summary>The dragon gloats and asks for $ChosenOneName (text 1: refuse or agree).</summary>
    // orig: R131+0x0F40 matched
    private void Kneel(CardContext c, CardMenu menu)
    {
        menu.Offer(1, 3, 4);
        Show(c, 319);
        c.Next = Number;
    }

    // orig: R131+0x0FC0 matched
    private void Attack(CardContext c)
    {
        Show(c, 318);
        c.Screen.Text(4);
        LastCastle.Fight(c, 0x6E);
    }

    /// <summary>The dragon roars for worship (text 3): kneel, attack or leave again.</summary>
    // orig: R131+0x1094 matched
    private void Refuse(CardContext c, CardMenu menu)
    {
        menu.Offer(3, 0, 1, 2);
        Show(c, 320);
        c.Next = Number;
    }

    /// <summary>The dragon devours $ChosenOneName; everyone loses 99 Virtue and the party is sent far away.</summary>
    // orig: R131+0x1100 matched
    // C: party[var_chosen1] is indexed without a check; var_chosen1 always holds a member here (the hint just set it).
    private void GiveUpTheChosen(CardContext c)
    {
        var victim = c.Variables.Chosen[0];
        c.SetStatus(victim, CharacterStatus.Empty);
        c.DeleteMember(victim);
        // DIVERGENCE: party_panel_rows(0x100) (party-panel redraw) is left out; the screen redraws itself.
        c.PassHours(0);
        c.AddSkill(-2, Virtue, -99);
        Show(c, 314);
        c.Screen.Text(2);
        c.Variables.Chosen[0] = c.Leader;
        c.PassHours(0);
        c.Next = 12;
    }
}
