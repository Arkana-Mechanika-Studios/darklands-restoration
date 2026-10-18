using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd21": a blighted stretch of wilderness, twisted trees and black beasts, where witches
/// hold their rites. Searching (Perception + Woodwise) finds signs of witchcraft; searching again
/// brings a pack of witch-familiar wolves. A saint may reveal the next sabbat, cleanse the land
/// or send a dream of the cult's seals. Leaving goes to the map (card 0x0C).
/// </summary>
// orig: R123+0x09B6 (18D3:0006) matched; tests: CardSmokeTests
//   option 0: action R123+0x0C62; hint R123+0x0C6A
//   option 1: action R123+0x0CA2; hint R123+0x0CEC
//   option 2: action R123+0x0D16; hint R123+0x0E32
//   option 3: action R123+0x0E6A; hint -
//   option 4: action R123+0x0EA4; hint R123+0x0EC2
//   option 5: action R123+0x0EFE; hint R123+0x0F1C
//   option 6: action R123+0x0F58; hint R123+0x0F76
//   option 7: action R123+0x0FB2; hint R123+0x0FD0
//   option 8: action R123+0x100C; hint R123+0x102A
//   option 9: action R123+0x1066; hint R123+0x1084
//   item pop-up: R123+0x119A, R123+0x11D6, R123+0x1212,
//     R123+0x124E
//   saint pop-up: R123+0x128A, R123+0x1312, R123+0x139A,
//     R123+0x1422
//   other: R123+0x0BEE, R123+0x10C0, R123+0x1162
// DORMANT (generated): option slots 10..19 keep the default handler R123+0x1162 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 3, 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class BlightedWitchRitualSite : Card
{
    public override int Number => 317;

    private const int BlightedWoodPicture = 139;
    private const int DreamPicture = 90;

    private bool _searched;

    public override int Run(CardContext c)
    {
        Show(c, BlightedWoodPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.SetCardItems();
        c.SetCardSaints(3, 0x0F, 0x2B, 0x44);
        menu[2] = c.SaintsUsable();

        // The original keeps "the site has been searched" in options_block; the option text is always block 0.
        _searched = false;
        var assured = OptionHint.Saying(SuccessAssured);
        var showAgain = new MenuLine(_ => ShowTheWoodAgain(c), assured);
        return menu.Run(
            // orig: R123+0x0C62 / R123+0x0C6A matched — leave for the map.
            new MenuLine(_ => c.Next = 0x0C, assured),
            new MenuLine(chance => Investigate(c, chance), () => SearchChance(c)),
            new MenuLine(_ => CallASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R123+0x0E6A / R123+0x0E6C matched
            // DORMANT: line 3 is never enabled; its handler is empty (hint "Select a person and a saint.").
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.SelectSaint)),
            showAgain, showAgain, showAgain, showAgain, showAgain, showAgain);
    }

    /// <summary>The best Perception plus the best Woodwise; the whole party acts.</summary>
    // orig: R123+0x0CEC matched
    // C: the order of the two calls only changes best_member, which this hint does not read (r.b = −2).
    private static OptionHint SearchChance(CardContext c)
    {
        var perception = c.PartyMaxAttribute((int)Attribute.Perception);
        var woodwise = c.PartyMaxSkill((int)Skill.Woodwise);
        return OptionHint.ChanceOf(perception + woodwise, OptionHint.WholeParty);
    }

    // orig: R123+0x0CA2 matched
    private void Investigate(CardContext c, int chance)
    {
        c.PassHours(1);
        if (_searched)
        {
            c.Screen.Text(1);
            FightTheWolves(c);
            return;
        }
        _searched = true;
        c.Screen.Text(c.Random(100) < chance ? 4 : 5);
    }

    // orig: R123+0x0BEE matched
    // C: run_battle's only random argument is the count (rand_mod(5) + 3). A result outside 0..4 (the switch's default
    //   and its −64..−1 cases) returns at once and the card stays (next_card unchanged).
    private static void FightTheWolves(CardContext c)
    {
        var wolves = c.Random(5) + 3;
        switch (c.RunBattle(0x45, -1, 0x2A, 1, wolves, -1, 0, 0, -1, 0, 0))
        {
            case 0:
            case 1:
                c.Screen.Text(2);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 2:
            case 3:
            case 4:
                c.RejoinAway();
                c.LoseItems(Who.Everybody);
                c.Screen.Text(3);
                c.Next = 0x0C;
                break;
        }
    }

    /// <summary>
    /// Saint 0: the white rabbit names the next sabbat; 1 and 2: the land is cleansed (the leader
    /// practises Virtue); 3: a dream of the seals. After any prayer the saint line is greyed out.
    /// </summary>
    // orig: R123+0x0D16 matched
    // C: an int function that falls off its end without a value (except `return 0` after a cancelled prayer); the
    //   menu ignores it. A failed prayer loads +0x0C73, the card's own picture, then pic_buf again (the same one).
    private void CallASaint(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(6);
                    break;
                case 1:
                    c.PracticeSkill(c.Leader, (int)Skill.Virtue, 1, 10, 0);
                    c.Screen.Text(7);
                    break;
                case 2:
                    c.PracticeSkill(c.Leader, (int)Skill.Virtue, 1, 10, 0);
                    c.Screen.Text(8);
                    break;
                case 3:
                    ShowPicture(c, DreamPicture);
                    c.Screen.Text(9);
                    ShowPicture(c, BlightedWoodPicture);
                    break;
            }
            c.PassHours(1);
            _searched = true;
        }
        else
        {
            ShowPicture(c, BlightedWoodPicture);
            c.Screen.Text(10);
            ShowPicture(c, BlightedWoodPicture);
        }
        menu[2] = OptionState.Shown;
    }

    // orig: R123+0x0EA4 / _054e / _05a8 / _0602 / _065c / _06b6 matched
    private static void ShowTheWoodAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
