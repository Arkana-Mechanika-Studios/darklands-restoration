using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd17": at a blood-spattered altar three men ordain a witch-priest for the next sabbat.
/// The party may stay hidden to hear more (Stealth), attack, or slip away (Stealth); if they stay
/// unseen until the men leave, a saint may purify the altar (Virtue for all). Being noticed means
/// an ambush (−3).
/// </summary>
// orig: R122+0x0B2E (18EA:000E) matched; tests: CardSmokeTests
//   option 0: action R122+0x0D60; hint R122+0x0DD6
//   option 1: action R122+0x0DF6; hint R122+0x0E08
//   option 2: action R122+0x0E40; hint R122+0x0EA4
//   option 3: action R122+0x0EC4; hint R122+0x0ED2
//   option 4: action R122+0x0F0A; hint R122+0x0FBA
//   option 5: action R122+0x0FF2; hint R122+0x1010
//   option 6: action R122+0x104C; hint R122+0x106A
//   option 7: action R122+0x10A6; hint R122+0x10C4
//   option 8: action R122+0x1100; hint R122+0x111E
//   option 9: action R122+0x115A; hint R122+0x1178
//   item pop-up: R122+0x128E, R122+0x12CA, R122+0x1306,
//     R122+0x1342
//   saint pop-up: R122+0x137E, R122+0x1406, R122+0x148E,
//     R122+0x1516
//   other: R122+0x11B4, R122+0x1256
// DORMANT (generated): option slots 10..19 keep the default handler R122+0x1256 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class WitchPriestOrdination : Card
{
    public override int Number => 313;

    private const int AltarPicture = 175;
    private const int AmbushPicture = 167;
    private const int PurifiedAltarPicture = 176;

    public override int Run(CardContext c)
    {
        Show(c, AltarPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.SetCardItems();
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        var showAgain = new MenuLine(_ => ShowTheAltarAgain(c), unknown);
        // DORMANT: in text block 2 the option lines 0..2 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(chance => WaitToHearMore(c, menu, chance), () => StealthChance(c)),
            new MenuLine(_ => { c.CombatFlags |= 8; c.BattleMode = 1; c.Next = -3; }, unknown),
            new MenuLine(chance => SlipAway(c, chance), () => StealthChance(c)),
            new MenuLine(_ => { c.BattleMode = 2; c.Next = -2; }, unknown),
            new MenuLine(_ => PurifyTheAltar(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            showAgain, showAgain, showAgain, showAgain, showAgain);
    }

    /// <summary>The worst Stealth in the party, doubled, decides whether they stay unseen.</summary>
    // orig: R122+0x0DD6 / R122+0x0EA4 matched (identical hints of lines 0 and 2)
    // C: party_min_skill sets best_member, read afterwards as unsigned char; the shift cannot overflow (at most 198).
    private static OptionHint StealthChance(CardContext c)
    {
        var chance = c.PartyMinSkill((int)Skill.Stealth) << 1;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R122+0x0D60 matched
    // C: success is rand_mod(100) < chance (strict), unlike most cards' <=.
    private void WaitToHearMore(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) < chance)
        {
            menu.Offer(2, 3);
            menu[4] = c.SaintsUsable();
            return;
        }
        Noticed(c);
    }

    // orig: R122+0x0E40 matched
    private void SlipAway(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.BattleMode = 2;
            c.Next = -2;
            return;
        }
        Noticed(c);
    }

    /// <summary>A noise gives them away: the three men throw off their cloaks and attack.</summary>
    // C: the C reloads the whole card (card_load, not card_load_near) with the ambush picture; Show does the same.
    private void Noticed(CardContext c)
    {
        Show(c, AmbushPicture);
        c.Screen.Text(3);
        c.CombatFlags |= 8;
        c.BattleMode = 1;
        c.Next = -3;
    }

    // orig: R122+0x0F0A matched
    private void PurifyTheAltar(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        Show(c, PurifiedAltarPicture);
        if (result != 0)
        {
            c.Screen.Text(4);
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, 2);
        }
        else
        {
            c.Screen.Text(5);
        }
        c.BattleMode = 1;
        c.Next = -3;
    }

    // orig: R122+0x0FF2 / _052c / _0586 / _05e0 / _063a matched (lines 5..9, never enabled)
    private static void ShowTheAltarAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
