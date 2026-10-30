using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd33": the finished altar, an hour before the ceremony; the priest and the stonemason
/// step away. The party may sabotage the altar (unless already done), wait, or pray to purify it
/// (unless a saint already has). Everything goes on to the summoning (0x14A), or to 0x14E after
/// a lost fight.
/// </summary>
// orig: R126+0x0B40 (18EC:0000) matched; tests: CardSmokeTests
//   option 0: action R126+0x0E34; hint R126+0x0EE0
//   option 1: action R126+0x0EF6; hint R126+0x0EFE
//   option 2: action R126+0x0F36; hint R126+0x0FB8
//   option 3: action R126+0x0FF0; hint R126+0x100E
//   option 4: action R126+0x104A; hint R126+0x1068
//   option 5: action R126+0x10A4; hint R126+0x10C2
//   option 6: action R126+0x10FE; hint R126+0x111C
//   option 7: action R126+0x1158; hint R126+0x1176
//   option 8: action R126+0x11B2; hint R126+0x11D0
//   option 9: action R126+0x120C; hint R126+0x122A
//   item pop-up: R126+0x1340, R126+0x137C, R126+0x13B8,
//     R126+0x13F4
//   saint pop-up: R126+0x1430, R126+0x14B8, R126+0x1540,
//     R126+0x15C8
//   other: R126+0x0DD0, R126+0x1266, R126+0x1308
// DORMANT (generated): option slots 10..19 keep the default handler R126+0x1308 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 3, 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card-item-lines stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SabbatFinishedAltar : Card
{
    public override int Number => 329;

    private const int Picture = 148;
    private const int AltarPicture = 146;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 1);
        menu.Enable(0, 1, 2);
        c.SetCardItems();
        if (Known(c, AltarSabotaged))
        {
            menu[0] = OptionState.Shown;
        }
        if (Known(c, AltarPurified))
        {
            menu[2] = OptionState.Shown;
        }
        c.SetCardSaints(0x0F, 0x6F);
        if (menu[2] == OptionState.Enabled)
        {
            menu[2] = c.SaintsUsable();
        }
        c.Screen.Text(0);
        ShowPicture(c, AltarPicture);

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: lines 3..9 R126+0x0FF0, R126+0x104A, R126+0x10A4, R126+0x10FE, R126+0x1158, R126+0x11B2, R126+0x120C
        //   (hints R126+0x100E, R126+0x1068, R126+0x10C2, R126+0x111C, R126+0x1176, R126+0x11D0, R126+0x122A) matched
        // DORMANT: lines 3..9 are never enabled; each handler shows text 0, passes 0 hours and goes to 0x62.
        var noLine = new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured);
        return menu.Run(
            new MenuLine(chance => SabotageTheAltar(c, chance), () => SabotageChance(c)),
            new MenuLine(_ => c.Next = 0x14A, assured),
            new MenuLine(_ => PrayToPurifyTheAltar(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            noLine, noLine, noLine, noLine, noLine, noLine, noLine);
    }

    // orig: R126+0x0EE0 matched
    // C: the hint sets only r.a / r.b (text-bank-buffer keeps its last text); the average Stealth is the chance.
    private static OptionHint SabotageChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Stealth), OptionHint.WholeParty);

    // orig: R126+0x0E34 matched
    private static void SabotageTheAltar(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
            Add(c, Suspicion, 1);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            Note(c, AltarSabotaged, 72);
            c.Next = 0x14A;
            return;
        }
        ShowPicture(c, Picture);
        c.Screen.Text(5);
        ShowPicture(c, AltarPicture);
        FightTheGuards(c);
    }

    /// <summary>The witches catch the saboteurs: a win or a flight (0, 1) raises suspicion, a defeat drops the party away.</summary>
    // orig: R126+0x0DD0 matched
    // C: any other battle result (−0x40..−1 and the cases past 4) returns without changing following-card; the menu loops.
    private static void FightTheGuards(CardContext c)
    {
        switch (c.RunBattle(0x4D, -1, 0x1C, 4, 2, 0x29, 3, 6, -1, 0, 0))
        {
            case 0 or 1:
                c.Screen.Text(6);
                Add(c, Suspicion, 2);
                c.Next = 0x14A;
                break;
            case 2 or 3 or 4:
                c.DropAway();
                c.Next = 0x14E;
                break;
        }
    }

    /// <summary>A saint purifies the altar (text 2); an unanswered prayer only draws scowls (text 3).</summary>
    // orig: R126+0x0F36 matched
    // C: returns 0 after a cancelled box, otherwise falls off its end without a value (ignored).
    private static void PrayToPurifyTheAltar(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            c.Screen.Text(2);
            Note(c, AltarPurified, 48);
            Add(c, Suspicion, 2);
        }
        else
        {
            c.Screen.Text(3);
            Add(c, Suspicion, 3);
        }
        c.Next = 0x14A;
    }
}
